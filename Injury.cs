using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GameruleSet
{
    public class Injury
    {
        private readonly Rules rules;

        public Injury(Rules rules)
        {
            this.rules = rules;
        }

        internal void Initialize()
        {
            On.ProcessManager.SwitchMainProcess += ProcessManager_SwitchMainProcess;
            On.Creature.Violence += Creature_Violence;
            On.Player.Update += Player_Update;
            On.Creature.SpearStick += Creature_SpearStick;
        }

        private void ProcessManager_SwitchMainProcess(On.ProcessManager.orig_SwitchMainProcess orig, ProcessManager self, ProcessManager.ProcessID ID)
        {
            if (self.currentMainLoop is RainWorldGame game)
                for (int i = 0; i < game.session.Players.Count; i++)
                {
                    rules.GetData(game.session.Players[i].ID).injured = false;
                }
            orig(self, ID);
        }

        private void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);
            if (!rules.Injury.Value)
                return;
            var data = rules.GetData(self.abstractCreature.ID);
            if (data.slugcatStats?.Target != self.slugcatStats && self.slugcatStats != null)
            {
                data.slugcatStats = new(self.slugcatStats);
                if (data.injured)
                {
                    self.slugcatStats.bodyWeightFac *= 0.9f;
                    self.slugcatStats.runspeedFac *= 0.875f;
                    self.slugcatStats.throwingSkill = 0;
                    self.slugcatStats.poleClimbSpeedFac *= 0.8f;
                    self.slugcatStats.corridorClimbSpeedFac *= 0.86f;
                    self.slugcatStats.malnourished = true;

                    self.bodyChunks[0].mass = 0.35f * self.slugcatStats.bodyWeightFac;
                    self.bodyChunks[1].mass = 0.35f * self.slugcatStats.bodyWeightFac;
                }
            }
            if (data.injured)
                data.injuryCooldown--;
            else data.injuryCooldown = 0;
        }

        private bool Creature_SpearStick(On.Creature.orig_SpearStick orig, Creature self, Weapon source, float dmg, BodyChunk chunk, PhysicalObject.Appendage.Pos appPos, UnityEngine.Vector2 direction)
        {
            if (rules.Injury.Value && self is Player player && chunk.index == 0 && player.grasps.Any(g => g?.grabbed is VultureMask))
                return false;
            return orig(self, source, dmg, chunk, appPos, direction);
        }

        private void Creature_Violence(On.Creature.orig_Violence orig, Creature self, BodyChunk source, UnityEngine.Vector2? directionAndMomentum, BodyChunk hitChunk, PhysicalObject.Appendage.Pos hitAppendage, Creature.DamageType type, float damage, float stunBonus)
        {
            if (rules.Injury.Value && self is Player player && damage >= self.Template.instantDeathDamageLimit)
            {
                var data = this.rules.GetData(player.abstractCreature.ID);
                if (hitChunk.index == 0 && type != Creature.DamageType.Electric && type != Creature.DamageType.Explosion)
                {
                    var grasp = player.grasps.FirstOrDefault(g => g?.grabbed is VultureMask);
                    if (grasp?.grabbed is VultureMask mask)
                    {
                        mask.donned = 1f;
                        stunBonus = -30;
                        damage = 0;
                    }
                }
                else
                {
                    if (data.injuryCooldown > 0)
                    {
                        stunBonus += damage * 30;
                        damage = 0;
                    }
                    else if (!data.injured && !player.Malnourished)
                    {
                        data.injured = true;
                        data.injuryCooldown = 5;
                        stunBonus += damage * 30;
                        damage = 0;

                        player.abstractCreature.world.game.session.characterStats = new SlugcatStats(player.abstractCreature.world.game.StoryCharacter, false);
                    }
                }
            }
            orig(self, source, directionAndMomentum, hitChunk, hitAppendage, type, damage, stunBonus);
        }
    }
}
