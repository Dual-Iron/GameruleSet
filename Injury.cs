using Mono.Cecil.Cil;
using MonoMod.Cil;
using RWCustom;
using StaticTables;
using System;
using UnityEngine;

namespace GameruleSet
{
    public class Injury
    {
        private readonly Rules rules;

        private Action? replaceDeath;

        public Injury(Rules rules)
        {
            this.rules = rules;

            On.PlayerGraphics.Update += PlayerGraphics_Update; ;
            On.Player.Die += Player_Die;
            On.Player.TerrainImpact += Player_TerrainImpact;
            On.Creature.Grab += Creature_Grab;
            On.Creature.Violence += Creature_Violence;
            On.Creature.SpearStick += Creature_SpearStick;
            On.Player.Update += Player_Update;
            On.Player.Stun += Player_Stun;
        }

        private void PlayerGraphics_Update(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
        {
            orig(self);
            if (rules.Injury)
            {
                var data = self.player.playerState.Data().Get<PlayerData>();
                if (data.injured)
                {
                    if (self.malnourished < 1)
                        self.malnourished += 0.01f;
                    self.breath += 1 / 50f;
                }
            }
        }

        private void Player_Die(On.Player.orig_Die orig, Player self)
        {
            if (replaceDeath != null)
            {
                Action a = replaceDeath;
                replaceDeath = null;
                a();
            }
            else
            {
                orig(self);
            }
        }

        private void Player_TerrainImpact(On.Player.orig_TerrainImpact orig, Player self, int chunk, IntVector2 direction, float speed, bool firstContact)
        {
            if (!rules.Injury)
            {
                orig(self, chunk, direction, speed, firstContact);
                return;
            }

            void ApplyDamageFromFalling()
            {
                int stun = (int)Custom.LerpMap(speed, 35f, 60f, 40f, 140f, 2.5f);
                float damage = self.Template.instantDeathDamageLimit * speed / 60;
                self.Violence(null, null, self.bodyChunks[chunk], null, Creature.DamageType.Blunt, damage, stun);
            }

            replaceDeath = ApplyDamageFromFalling;
            orig(self, chunk, direction, speed, firstContact);
            replaceDeath = null;
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

            if (data.injured && self.State.alive)
            {
                const int maxPainTime = 120;

                // Aerobic level decreases 33% slower
                if (self.aerobicLevel < data.lastAerobicLevel)
                    self.aerobicLevel -= (self.aerobicLevel - data.lastAerobicLevel) / 3f;

                data.lastAerobicLevel = self.aerobicLevel;

                // If exhausted, experience pain
                if (self.aerobicLevel >= 1 || self.aerobicLevel >= 0.9 && self.Malnourished)
                {
                    data.painTime = maxPainTime;
                    self.Stun(80);

                    // Visuals
                    self.room.PlaySound(SoundID.Slugcat_Swallow_Item, self.mainBodyChunk.pos, 1.25f, 2.5f);

                    for (int i = 0; i < 4 + (int)(5 * data.woundIntensity); i++)
                    {
                        var dir = -data.woundDir + UnityEngine.Random.value * 30 - 15 + self.bodyChunks[1].Rotation.GetAngle();
                        var pos = self.bodyChunks[1].pos + Custom.DegToVec(dir) * self.bodyChunks[1].rad * UnityEngine.Random.value;
                        var direction = Custom.DegToVec(dir);
                        var speed = 7 + UnityEngine.Random.value * data.woundIntensity * 7;
                        self.room.AddObject(new WaterDrip(pos, direction * speed, false));
                    }
                }

                // Forewarn pain
                if (self.aerobicLevel >= 0.68f)
                    self.Blink(2);

                // Twinge of pain
                if (self.aerobicLevel >= 0.85f)
                    self.standing = false;

                // Agony
                if (data.painTime > 0)
                {
                    self.standing = false;
                    self.slowMovementStun = Math.Max(self.slowMovementStun, 2 + (int)(6f * data.painTime / maxPainTime));

                    if (self.aerobicLevel < 0.4f)
                    {
                        data.painTime--;

                        if (UnityEngine.Random.value < 0.18)
                            self.Blink(5);
                    }
                    else self.Blink(5);
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
            if (rules.Injury && self is Player player && damage >= self.Template.instantDeathDamageLimit)
            {
                ref var data = ref player.playerState.Data().Get<PlayerData>();
                if (hitChunk.index == 0 && (type == Creature.DamageType.Stab || type == Creature.DamageType.Blunt))
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
                        data.woundIntensity = Math.Min(1, (damage - self.Template.instantDeathDamageLimit) / self.Template.instantDeathDamageLimit);
                        data.woundDir = (directionAndMomentum?.GetAngle() ?? (UnityEngine.Random.value * 360)) - player.bodyChunks[1].Rotation.GetAngle();
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
