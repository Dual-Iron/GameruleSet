using StaticTables;
using System;
using UnityEngine;

namespace GameruleSet
{
    public class Injury
    {
        private readonly Rules rules;

        public Injury(Rules rules)
        {
            this.rules = rules;

            On.Creature.Grab += Creature_Grab;
            On.Creature.Violence += Creature_Violence;
            On.Creature.SpearStick += Creature_SpearStick;
            On.Player.Update += Player_Update;
            On.Player.Stun += Player_Stun;
        }

        private void Player_Stun(On.Player.orig_Stun orig, Player self, int st)
        {
            if (rules.Injury && self.playerState.Data().Get<PlayerData>().injured)
            {
                self.AerobicIncrease(st / 15f);
                st = (int)(st * 1.5f);
            }
            orig(self, st);
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
                if (player.playerState.Data().Get<PlayerData>().injuryCooldown > 0)
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
            if (!rules.Injury)
            {
                orig(self, eu);
                return;
            }

            ref var data = ref self.playerState.Data().Get<PlayerData>();

            if (data.injured)
            {
                const int maxPainTime = 120;

                // Aerobic level decreases 50% slower
                if (self.aerobicLevel < data.lastAerobicLevel)
                    self.aerobicLevel -= (self.aerobicLevel - data.lastAerobicLevel) / 3;

                data.lastAerobicLevel = self.aerobicLevel;

                // If exhausted, experience pain
                if (self.aerobicLevel >= 1)
                {
                    self.aerobicLevel = 1;
                    data.painTime = maxPainTime;
                    self.Stun(80);
                }

                // Tired
                if (self.aerobicLevel >= 0.85f)
                    self.standing = false;

                // Forewarn pain
                if (self.aerobicLevel >= 0.65f)
                    self.Blink(5);

                // Effects of pain
                if (data.painTime > 0)
                {
                    self.standing = false;
                    self.slowMovementStun = Math.Max(self.slowMovementStun, 2 + (int)(6f * data.painTime / maxPainTime));

                    self.Blink(5);

                    if (self.aerobicLevel < 0.4f)
                        data.painTime--;

                    if (self.graphicsModule is PlayerGraphics g)
                        g.breath += 0.02f;
                }

                self.slowMovementStun = Math.Max(self.slowMovementStun, (int)(6 * self.aerobicLevel));
            }

            orig(self, eu);

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
            Console.WriteLine(damage);
            if (rules.Injury && self is Player player && damage >= self.Template.instantDeathDamageLimit)
            {
                ref var data = ref player.playerState.Data().Get<PlayerData>();
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
                    // Adrenaline surge!
                    player.AerobicIncrease(-9);

                    // Oh shit
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
                    }
                }
            }
            orig(self, source, directionAndMomentum, hitChunk, hitAppendage, type, damage, stunBonus);
        }
    }
}
