using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Partiality;
using StaticTables;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace GameruleSet
{
    public class SleepAnywhere
    {
        const int startCurl = 120;
        const int startSleeping = 300;
        const int maxSleeping = startSleeping + 400;

        struct SleepData : IWeakData<Player>
        {
            public int sleepingFor;
            public int groggy;
            public bool wasSleeping;

            void IWeakData<Player>.Construct(Player key) { }
            void IWeakData<Player>.Destruct() { }
        }

        private static int GetGlobalSleepingFor(RainWorldGame game)
        {
            int sleepingAmount = int.MaxValue;

            foreach (var abstractPlayer in game.Players)
            {
                // Get the minimum sleepingFor value. If not all players are sleeping, don't sleep.
                if (abstractPlayer?.realizedCreature is Player player && player.Consious)
                {
                    var sleepData = player.Data().Get<SleepData>();
                    if (sleepingAmount > sleepData.sleepingFor)
                        sleepingAmount = sleepData.sleepingFor;
                }
            }

            return sleepingAmount != int.MaxValue ? sleepingAmount : 0;
        }

        private readonly Rules rules;
        private bool caughtMMF;

        public SleepAnywhere(Rules rules)
        {
            this.rules = rules;

            // Many More Fixes hooks MainLoopProcess_RawUpdate but doesn't call orig,
            // so here I forcefully ignore their implementation to allow mine to exist.
            On.RainWorld.Update += RainWorld_Update;

            void RainWorld_Update(On.RainWorld.orig_Update orig, RainWorld self)
            {
                orig(self);

                if (caughtMMF)
                    return;

                var mmf = PartialityManager.Instance.modManager.loadedMods.FirstOrDefault(pm => pm.ModID == "Many More Fixes");
                if (mmf == null)
                    return;

                caughtMMF = true;

                var method = mmf.GetType().Assembly
                    .GetType("ManyMoreFixes.MiscChangesHK")?
                    .GetMethod("MainLoopProcess_RawUpdate", BindingFlags.NonPublic | BindingFlags.Static);

                if (method == null)
                    return;

                try
                {
                    new ILHook(method, ExtraHook).Apply();
                }
                catch (Exception e)
                {
                    rules.Logger.LogError(e);
                }

                static void ExtraHook(ILContext il)
                {
                    var cursor = new ILCursor(il);
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldarg_1);
                    cursor.Emit(OpCodes.Ldarg_2);
                    cursor.Emit(OpCodes.Callvirt, typeof(On.MainLoopProcess).GetNestedType("orig_RawUpdate").GetMethod("Invoke"));
                    cursor.Emit(OpCodes.Ret);
                }
            }

            On.Player.Stun += Player_Stun;
            On.Player.Update += Player_Update;
            On.Player.checkInput += Player_checkInput;
            On.HUD.HUD.Update += HUD_Update;
            On.MainLoopProcess.RawUpdate += MainLoopProcess_RawUpdate;

            new Hook(typeof(VirtualMicrophone).GetMethod("get_InWorldSoundsVolumeGoal"), (Func<Func<VirtualMicrophone, float>, VirtualMicrophone, float>)GetterInWorldSoundsVolumeGoal)
                .Apply();
        }

        private float GetterInWorldSoundsVolumeGoal(Func<VirtualMicrophone, float> orig, VirtualMicrophone self)
        {
            if (rules.SleepAnywhere)
            {
                int sleepingAmount = GetGlobalSleepingFor(self.room.game);
                if (sleepingAmount != int.MaxValue)
                {
                    return orig(self) * Mathf.Lerp(1, 0.25f, sleepingAmount / (float)maxSleeping);
                }
            }
            return orig(self);
        }

        private void Player_Stun(On.Player.orig_Stun orig, Player self, int st)
        {
            if (self.Data().Get<SleepData>().sleepingFor >= startSleeping)
            {
                st += 60;
            }
            orig(self, st);
        }

        private void MainLoopProcess_RawUpdate(On.MainLoopProcess.orig_RawUpdate orig, MainLoopProcess self, float dt)
        {
            try
            {
                orig(self, dt);

                if (!rules.SleepAnywhere || self is not RainWorldGame game || game.pauseMenu != null || !game.processActive)
                {
                    return;
                }

                int sleepingAmount = GetGlobalSleepingFor(game);
                if (sleepingAmount > startSleeping)
                {
                    if (dt > 1 / 40f)
                        dt = 1 / 40f;

                    var extraFps = sleepingAmount - startSleeping;

                    self.myTimeStacker += extraFps * dt;
                    while (self.myTimeStacker >= 1f)
                    {
                        self.Update();
                        self.myTimeStacker -= 1f;
                    }
                    self.GrafUpdate(self.myTimeStacker);
                }
            }
            catch (Exception e)
            {
                rules.Logger.LogError($"Main loop process {self.GetType()} threw an uncaught exception.\n" + e);
            }
        }

        private void HUD_Update(On.HUD.HUD.orig_Update orig, HUD.HUD self)
        {
            orig(self);

            if (rules.SleepAnywhere && self.owner is Player player && player.Data().Get<SleepData>().sleepingFor >= startSleeping)
            {
                self.showKarmaFoodRain = true;
            }
        }

        private void Player_checkInput(On.Player.orig_checkInput orig, Player self)
        {
            orig(self);

            if (rules.SleepAnywhere && self.Data().Get<SleepData>().sleepingFor >= startCurl)
            {
                self.input[0] = new Player.InputPackage(self.room.game.rainWorld.options.controls[self.playerState.playerNumber].gamePad, 0, self.input[0].y, false, false, false, false, false);
                if (self.input[0].y > 0)
                    self.input[0].y = 0;
            }
        }

        private void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);

            if (!rules.SleepAnywhere || !self.abstractCreature.world.game.IsStorySession)
            {
                return;
            }

            ref var sleepData = ref self.Data().Get<SleepData>();

            bool canSleep = self.Consious && self.airInLungs >= 0.95f && self.grabbedBy.Count == 0 && self.abstractCreature.Room?.shelter == false &&
                (self.bodyChunks[0].pos - self.bodyChunks[0].lastPos).sqrMagnitude < 1 &&
                (self.bodyChunks[1].pos - self.bodyChunks[1].lastPos).sqrMagnitude < 1;

            if (canSleep && self.bodyMode == Player.BodyModeIndex.Crawl && self.animation == Player.AnimationIndex.None && self.input[0].x == 0 && self.input[0].y < 0)
            {
                if (sleepData.sleepingFor < maxSleeping)
                    sleepData.sleepingFor++;
            }
            else
            {
                sleepData.sleepingFor -= canSleep ? 2 : 10;
                if (sleepData.sleepingFor < 0)
                    sleepData.sleepingFor = 0;
            }

            if (!canSleep)
            {
                if (sleepData.wasSleeping)
                {
                    sleepData.wasSleeping = false;
                    self.sleepCurlUp = 0;
                    self.forceSleepCounter = 0;
                }
                return;
            }

            if (sleepData.sleepingFor > startCurl)
            {
                self.forceSleepCounter = sleepData.sleepingFor - startCurl;
                sleepData.wasSleeping = true;
            }

            const int maxGroggy = 600;

            if (sleepData.sleepingFor >= startSleeping)
            {
                self.aerobicLevel *= 0.5f;
                if (sleepData.groggy < maxGroggy)
                    sleepData.groggy++;
            }
            else
            {
                if (sleepData.groggy > 0)
                    sleepData.groggy--;
            }

            if (sleepData.sleepingFor >= startCurl)
            {
                self.Blink(5);
            }
            else if (sleepData.groggy > 0)
            {
                self.slowMovementStun = (int)(10 * Mathf.InverseLerp(0, maxGroggy, sleepData.groggy));
                if (sleepData.sleepingFor > 0 || UnityEngine.Random.value < 0.15f)
                {
                    self.Blink(12);
                }
            }

            var world = self.abstractCreature.world;
            int global = GetGlobalSleepingFor(world.game);
            if (global >= maxSleeping && world.game.Players[0] == self.abstractCreature && !self.Malnourished &&
                world.rainCycle.TimeUntilRain < -1200 && world.game.globalRain.Intensity >= 1 && self.abstractCreature.Room?.shelter == false)
            {
                var roomRain = self.room?.roomRain;
                if (roomRain == null || roomRain.dangerType == RoomRain.DangerType.None || roomRain.dangerType == RoomRain.DangerType.Rain)
                {
                    // If sleeping outside a shelter while nourished and safe, sleep.
                    world.game.Win(self.playerState.foodInStomach < self.slugcatStats.foodToHibernate);
                }
            }
        }
    }
}