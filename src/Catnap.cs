using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Partiality;
using WeakTables;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace GameruleSet
{
    public class Catnap
    {
        const int startCurl = 80;
        const int startSleeping = 160;
        const int maxSleeping = startSleeping + 400;

        static readonly WeakTable<Player, SleepData> sleepData = new(_ => new());

        sealed class SleepData
        {
            public int sleepDuration;
            public int groggy;
            public bool wasSleeping;
        }

        private static int GetMinSleepDuration(RainWorldGame game)
        {
            int duration = 0;

            foreach (var abstractPlayer in game.Players)
            {
                // Get the minimum sleepingFor value. If not all players are sleeping, don't sleep.
                if (abstractPlayer?.realizedCreature is Player player && player.Consious)
                {
                    var data = sleepData[player];
                    if (duration > data.sleepDuration)
                        duration = data.sleepDuration;
                }
            }

            return duration;
        }

        private readonly Rules rules;
        private bool caughtMMF;

        public Catnap(Rules rules)
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

            new Hook(typeof(VirtualMicrophone).GetMethod("get_InWorldSoundsVolumeGoal"), GetterInWorldSoundsVolumeGoal)
                .Apply();
        }

        private float GetterInWorldSoundsVolumeGoal(Func<VirtualMicrophone, float> orig, VirtualMicrophone self)
        {
            if (rules.SleepAnywhere)
            {
                int sleepingAmount = GetMinSleepDuration(self.room.game);
                if (sleepingAmount != int.MaxValue)
                {
                    return orig(self) * Mathf.Lerp(1, 0.25f, sleepingAmount / (float)maxSleeping);
                }
            }
            return orig(self);
        }

        private void Player_Stun(On.Player.orig_Stun orig, Player self, int st)
        {
            if (sleepData[self].sleepDuration >= startSleeping)
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

                int sleepingAmount = GetMinSleepDuration(game);
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

            if (rules.SleepAnywhere && self.owner is Player player && sleepData[player].sleepDuration >= startSleeping)
            {
                self.showKarmaFoodRain = true;
            }
        }

        private void Player_checkInput(On.Player.orig_checkInput orig, Player self)
        {
            orig(self);

            if (rules.SleepAnywhere && sleepData[self].sleepDuration >= startCurl)
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

            var data = sleepData[self];

            bool canSleep = self.Consious && self.airInLungs >= 0.95f && self.grabbedBy.Count == 0 && self.abstractCreature.Room?.shelter == false &&
                (self.bodyChunks[0].pos - self.bodyChunks[0].lastPos).sqrMagnitude < 1 &&
                (self.bodyChunks[1].pos - self.bodyChunks[1].lastPos).sqrMagnitude < 1;

            if (canSleep && self.bodyMode == Player.BodyModeIndex.Crawl && self.animation == Player.AnimationIndex.None && self.input[0].x == 0 && self.input[0].y < 0)
            {
                if (data.sleepDuration < maxSleeping)
                    data.sleepDuration++;
            }
            else
            {
                data.sleepDuration -= canSleep ? 2 : 10;
                if (data.sleepDuration < 0)
                    data.sleepDuration = 0;
            }

            if (!canSleep)
            {
                if (data.wasSleeping)
                {
                    data.wasSleeping = false;
                    self.sleepCurlUp = 0;
                    self.forceSleepCounter = 0;
                }
                return;
            }

            if (data.sleepDuration > startCurl)
            {
                self.forceSleepCounter = data.sleepDuration - startCurl;
                data.wasSleeping = true;
            }

            const int maxGroggy = 600;

            if (data.sleepDuration >= startSleeping)
            {
                self.aerobicLevel *= 0.5f;
                if (data.groggy < maxGroggy)
                    data.groggy++;
            }
            else
            {
                if (data.groggy > 0)
                    data.groggy--;
            }

            if (data.sleepDuration >= startCurl)
            {
                self.Blink(5);
            }
            else if (data.groggy > 0)
            {
                self.slowMovementStun = (int)(10 * Mathf.InverseLerp(0, maxGroggy, data.groggy));
                if (data.sleepDuration > 0 || UnityEngine.Random.value < 0.15f)
                {
                    self.Blink(12);
                }
            }
        }
    }
}