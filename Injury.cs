using RWCustom;
using StaticTables;
using System;
using System.Linq;
using UnityEngine;

namespace GameruleSet
{
    public class Injury
    {
        struct PlayerData : IWeakData<AbstractCreature>
        {
            public int injuryCooldown;
            public bool injured;
            public float damageBlockedWithMask;
            public float danger;
            public double hunger;
            public UnmanagedWeakRef<SlugcatStats> slugcatStats;

            void IDisposable.Dispose()
            {
                slugcatStats.Dispose();
            }

            void IWeakData<AbstractCreature>.Initialize(AbstractCreature owner, object? state) { }
        }

        private readonly Rules rules;

        public Injury(Rules rules)
        {
            this.rules = rules;

            On.ProcessManager.SwitchMainProcess += (o, s, i) =>
            {
                if (s.currentMainLoop is RainWorldGame game)
                    ResetInjury(game);
                o(s, i);
            };
            On.ArenaGameSession.EndSession += (o, s) =>
            {
                ResetInjury(s.game);
                o(s);
            };
            On.Creature.Grab += Creature_Grab;
            On.Creature.Violence += Creature_Violence;
            On.Creature.SpearStick += Creature_SpearStick;
            On.Player.Update += Player_Update;
        }

        private void ResetInjury(RainWorldGame game)
        {
            foreach (var player in game.session.Players)
            {
                player.Data().Get<PlayerData>().injured = false;
            }
        }

        private VultureMask? GetGraspedMask(Player player)
        {
            foreach (var grasp in player.grasps)
            {
                if (grasp?.grabbed is VultureMask mask)
                    return mask;
            }
            return null;
        }

        private bool Creature_Grab(On.Creature.orig_Grab orig, Creature self, PhysicalObject obj, int graspUsed, int chunkGrabbed, Creature.Grasp.Shareability shareability, float dominance, bool overrideEquallyDominant, bool pacifying)
        {
            if (rules.Injury && obj is Player player)
            {
                if (player.abstractCreature.Data().Get<PlayerData>().injuryCooldown > 0)
                {
                    return false;
                }
                if (chunkGrabbed == 0)
                {
                    var mask = GetGraspedMask(player);
                    if (mask != null)
                    {
                        mask.AllGraspsLetGoOfThisObject(true);
                        obj = mask;
                    }
                }
            }
            return orig(self, obj, graspUsed, chunkGrabbed, shareability, dominance, overrideEquallyDominant, pacifying);
        }

        private void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);
            if (!rules.Injury)
                return;
            ref var data = ref self.abstractCreature.Data().Get<PlayerData>();
            if (data.slugcatStats != self.slugcatStats && self.slugcatStats != null)
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

            if (data.damageBlockedWithMask > 0)
            {
                self.Stun((int)(data.damageBlockedWithMask * 15));
                data.damageBlockedWithMask = 0;
            }

            if (data.injured)
            {
                data.injuryCooldown--;
            }
            else data.injuryCooldown = 0;

            var mask = GetGraspedMask(self);
            if (mask != null && self.graphicsModule is PlayerGraphics graphics && graphics.objectLooker.currentMostInteresting is Creature creature && !creature.dead)
            {
                CreatureTemplate.Relationship relationship = self.abstractCreature.creatureTemplate.CreatureRelationship(creature);
                if (relationship.type == CreatureTemplate.Relationship.Type.Afraid || relationship.type == CreatureTemplate.Relationship.Type.Attacks)
                {
                    data.danger += creature.abstractCreature.abstractAI.RealAI.CurrentPlayerAggression(self.abstractCreature) / 20f;
                    mask.donned = Mathf.Clamp(data.danger, mask.donned, 1);
                }
            }
            else data.danger = 0;
        }

        private bool Creature_SpearStick(On.Creature.orig_SpearStick orig, Creature self, Weapon source, float dmg, BodyChunk chunk, PhysicalObject.Appendage.Pos appPos, Vector2 direction)
        {
            if (rules.Injury && self is Player player && chunk.index == 0 && GetGraspedMask(player) != null)
            {
                return false;
            }

            return orig(self, source, dmg, chunk, appPos, direction);
        }

        private void Creature_Violence(On.Creature.orig_Violence orig, Creature self, BodyChunk source, Vector2? directionAndMomentum, BodyChunk hitChunk, PhysicalObject.Appendage.Pos hitAppendage, Creature.DamageType type, float damage, float stunBonus)
        {
            if (rules.Injury && self is Player player && damage >= self.Template.instantDeathDamageLimit)
            {
                var data = rules.GetData(player.abstractCreature.ID);
                if (hitChunk.index == 0 && type != Creature.DamageType.Electric && type != Creature.DamageType.Explosion)
                {
                    var mask = GetGraspedMask(player);
                    if (mask != null)
                    {
                        if (!mask.King)
                            data.damageBlockedWithMask = damage;
                        mask.donned = 1f;
                        stunBonus = 0;
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
                        data.injuryCooldown = 10;
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
