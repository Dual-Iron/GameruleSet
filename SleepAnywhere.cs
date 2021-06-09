using MonoMod.RuntimeDetour;
using StaticTables;
using System;
using System.Linq;
using UnityEngine;
using static CreatureTemplate.Relationship.Type;

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

            void IDisposable.Dispose() { }
            void IWeakData<Player>.Initialize(Player owner, object? state) { }
        }

        private readonly Rules rules;

        public SleepAnywhere(Rules rules)
        {
            this.rules = rules;

            On.Player.ctor += Player_ctor;
            On.Player.Stun += Player_Stun;
            On.Player.Update += Player_Update;
            On.Player.checkInput += Player_checkInput;
            On.HUD.HUD.Update += HUD_Update;
            On.MainLoopProcess.RawUpdate += MainLoopProcess_RawUpdate;

            new Hook(typeof(VirtualMicrophone).GetMethod("get_InWorldSoundsVolumeGoal"), (Func<Func<VirtualMicrophone, float>, VirtualMicrophone, float>)GetterInWorldSoundsVolumeGoal)
                .Apply();
        }

        private void Player_ctor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world)
        {
            orig(self, abstractCreature, world);
            if (rules.SleepAnywhere && world.game.IsStorySession)
            {
                var abstractRoom = world.GetAbstractRoom(abstractCreature.pos.room);
                if (abstractRoom.creatures.Any(c =>
                {
                    var rel = c.creatureTemplate.CreatureRelationship(abstractCreature.creatureTemplate).type;
                    return rel == AgressiveRival || rel == Antagonizes || rel == Attacks || rel == Eats || rel == Uncomfortable;
                }))
                    self.sleepCounter = 0;
                else
                    self.sleepCounter = 100;
            }
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
                st *= 2;
                st += 160;
            }
            orig(self, st);
        }

        private void MainLoopProcess_RawUpdate(On.MainLoopProcess.orig_RawUpdate orig, MainLoopProcess self, float dt)
        {
            if (!rules.SleepAnywhere || self is not RainWorldGame game || game.pauseMenu != null || !game.processActive)
            {
                orig(self, dt);
                return;
            }

            int sleepingAmount = GetGlobalSleepingFor(game);
            if (sleepingAmount > startSleeping)
            {
                self.framesPerSecond += sleepingAmount - startSleeping;
                self.framesPerSecond = Mathf.Clamp(self.framesPerSecond, 1, 1000);

                self.myTimeStacker += dt * self.framesPerSecond;
                while (self.myTimeStacker >= 1f)
                {
                    self.Update();
                    self.myTimeStacker -= 1f;
                }
                self.GrafUpdate(self.myTimeStacker);
            }
            else
                orig(self, dt);
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

            if (!rules.SleepAnywhere || (self.abstractCreature.Room?.shelter ?? true) || !self.abstractCreature.world.game.IsStorySession)
            {
                return;
            }

            ref var sleepData = ref self.Data().Get<SleepData>();

            bool canSleep = self.Consious && !self.Malnourished && self.dangerGrasp == null;

            if (canSleep && self.bodyMode == Player.BodyModeIndex.Crawl && self.animation == Player.AnimationIndex.None && self.input[0].x == 0 && self.input[0].y < 0)
            {
                if (sleepData.sleepingFor < maxSleeping)
                    sleepData.sleepingFor++;
            }
            else
            {
                sleepData.sleepingFor -= canSleep ? 2 : 6;
                if (sleepData.sleepingFor < 0)
                    sleepData.sleepingFor = 0;
            }

            if (!canSleep)
            {
                self.sleepCurlUp = 0;
                self.forceSleepCounter = 0;
                return;
            }

            self.forceSleepCounter = Math.Max(0, sleepData.sleepingFor - startCurl);

            const int maxGroggy = 600;

            if (sleepData.sleepingFor >= startSleeping)
            {
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
            if (global >= maxSleeping && world.rainCycle.TimeUntilRain < -400)
            {
                int foodInStomach = world.game.Players.Max(a => a?.realizedCreature is Player p ? p.playerState.foodInStomach : 0);
                int foodToHibernate = world.game.Players.Max(a => a?.realizedCreature is Player p ? p.slugcatStats.foodToHibernate : 0);
                world.game.Win(foodInStomach < foodToHibernate);
            }
        }
    }
}