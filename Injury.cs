using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RWCustom;
using StaticTables;
using System;
using System.Linq;
using UnityEngine;

namespace GameruleSet
{
    public class Injury
    {
        struct InjuryData : IWeakData<PlayerState>
        {
            public bool usedPainkiller;
            public int injuryCooldown;
            public bool injured;
            public float damageBlockedWithMask;
            public float danger;
            public int painTime;
            public float lastAerobicLevel;
            public float woundDir;

            void IDisposable.Dispose() { }
            void IWeakData<PlayerState>.Initialize(PlayerState owner, object? state) { }
        }

        private readonly Rules rules;

        private Action? replaceDeath;

        public Injury(Rules rules)
        {
            this.rules = rules;

            On.PlayerGraphics.Update += PlayerGraphics_Update;
            On.Player.Die += Player_Die;
            On.Player.TerrainImpact += Player_TerrainImpact;
            On.Creature.Grab += Creature_Grab;
            On.Creature.Violence += Creature_Violence;
            On.Creature.SpearStick += Creature_SpearStick;
            On.Player.Update += Player_Update;
            On.Player.Stun += Player_Stun;

            new Hook(typeof(VirtualMicrophone).GetMethod("get_InWorldSoundsVolumeGoal"), (Func<Func<VirtualMicrophone, float>, VirtualMicrophone, float>)GetterInWorldSoundsVolumeGoal)
                    .Apply();
        }

        private float GetterInWorldSoundsVolumeGoal(Func<VirtualMicrophone, float> orig, VirtualMicrophone self)
        {
            if (rules.Injury)
            {
                var average = 0f;
                var count = 0;

                foreach (var item in self.room.abstractRoom.entities)
                {
                    if (item is AbstractCreature c && c.realizedCreature is Player p && p.State.alive)
                    {
                        average += p.playerState.Data().Get<InjuryData>().painTime;
                        count++;
                    }
                }

                average /= count;

                if (average > 1)
                {
                    return orig(self) * Mathf.Lerp(1, 0.5f, 1f - 1f / Mathf.Sqrt(average));
                }
            }
            return orig(self);
        }

        private void PlayerGraphics_Update(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
        {
            if (!rules.Injury || self.player.dead)
            {
                orig(self);
            }
            else
            {
                orig(self);

                var data = self.player.playerState.Data().Get<InjuryData>();
                if (data.injured)
                {
                    if (self.malnourished < 1)
                        self.malnourished += 0.01f;

                    if (data.painTime > 0 || self.player.aerobicLevel >= 0.68f)
                    {
                        self.LookAtNothing();
                    }

                    self.breath += 1 / 50f;
                    self.player.swimCycle += 0.05f * self.player.aerobicLevel;

                    if (!self.player.Stunned)
                    {
                        self.head.vel.y += Mathf.Sin(self.player.swimCycle * Mathf.PI * 2) * 0.25f;
                        self.drawPositions[0, 0].y += Mathf.Sin(self.player.swimCycle * Mathf.PI * 2f) * 0.75f;
                    }
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
                var stun = (int)Custom.LerpMap(speed, 35f, 60f, 40f, 140f, 2.5f);
                var damage = self.Template.instantDeathDamageLimit * speed / 60;
                self.Violence(null, direction.ToVector2().normalized, self.bodyChunks[chunk], null, Creature.DamageType.Blunt, damage, stun);
            }

            replaceDeath = ApplyDamageFromFalling;
            orig(self, chunk, direction, speed, firstContact);
            replaceDeath = null;
        }

        private void Player_Stun(On.Player.orig_Stun orig, Player self, int st)
        {
            if (rules.Injury && self.playerState.Data().Get<InjuryData>().injured)
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
                if (player.playerState.Data().Get<InjuryData>().injuryCooldown > 0)
                {
                    return false;
                }
                if (chunkGrabbed == 0)
                {
                    var mask = GetGraspedMask(player);
                    if (mask != null)
                    {
                        VulturePopEffect(obj.room, null, obj.bodyChunks[chunkGrabbed].pos, 0.1f, pacifying ? 45f : 0f);
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

            ref var data = ref self.playerState.Data().Get<InjuryData>();

            if (data.injured && self.State.alive)
            {
                const int maxPainTime = 120;

                // Aerobic level decreases 33% slower
                if (self.aerobicLevel < data.lastAerobicLevel)
                    self.aerobicLevel -= (self.aerobicLevel - data.lastAerobicLevel) / 3f;
                data.lastAerobicLevel = self.aerobicLevel;

                if (self.Adrenaline > 0)
                {
                    self.aerobicLevel = 0;
                    data.usedPainkiller = true;
                }
                else if (data.usedPainkiller)
                {
                    data.usedPainkiller = false;
                    self.aerobicLevel = 1;
                }
                else
                {
                    self.slowMovementStun = Math.Max(self.slowMovementStun, (int)(6 * self.aerobicLevel));
                }

                // If exhausted, experience pain
                if (self.aerobicLevel >= 1)
                {
                    data.painTime = maxPainTime;
                    self.Stun(80);

                    // Visuals
                    self.room.PlaySound(SoundID.Slugcat_Swallow_Item, self.mainBodyChunk.pos, 1.25f, 2.2f);

                    for (int i = 0; i < 4; i++)
                    {
                        var dir = self.bodyChunks[1].Rotation.GetAngle() + data.woundDir - 90 + UnityEngine.Random.value * 30 - 15;
                        var pos = self.bodyChunks[1].pos + Custom.DegToVec(dir) * self.bodyChunks[1].rad * 0.75f;
                        var direction = Custom.DegToVec(dir);
                        var speed = 5 + UnityEngine.Random.value * 7;
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
                        data.painTime -= self.Adrenaline > 0 ? 3 : 1;

                        if (UnityEngine.Random.value < 0.18)
                            self.Blink(5);
                    }
                    else self.Blink(5);
                }
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
            if (rules.Injury && self is Player player)
            {
                ref var data = ref player.playerState.Data().Get<InjuryData>();
                if (hitChunk.index == 0 && (type == Creature.DamageType.Stab || type == Creature.DamageType.Blunt))
                {
                    var mask = GetGraspedMask(player);
                    if (mask != null)
                    {
                        VulturePopEffect(player.room, source?.pos, hitChunk.pos, damage, stunBonus);
                        if (!mask.King)
                            data.damageBlockedWithMask = damage + stunBonus / 45f;
                        else
                            data.damageBlockedWithMask = stunBonus / 90f;
                        mask.donned = 1f;
                        stunBonus = 0;
                        damage = 0;
                    }
                }
                else if (damage >= self.Template.instantDeathDamageLimit)
                {
                    // Adrenaline surge!
                    player.aerobicLevel = 0;

                    // Oh shit
                    if (data.injuryCooldown > 0)
                    {
                        stunBonus += damage * 30;
                        damage = 0;
                    }
                    else if (!data.injured && !player.Malnourished)
                    {
                        data.woundDir = (directionAndMomentum?.GetAngle() ?? (UnityEngine.Random.value * 360)) - player.bodyChunks[1].Rotation.GetAngle();
                        data.injured = true;
                        data.injuryCooldown = 10;
                        stunBonus = 0;
                        damage = 0;
                    }
                }
            }
            orig(self, source, directionAndMomentum, hitChunk, hitAppendage, type, damage, stunBonus);
        }

        private void VulturePopEffect(Room room, Vector2? sourcePos, Vector2 hitPos, float damage, float stunBonus)
        {
            Vector2 pos = (sourcePos == null) ? hitPos : (hitPos + sourcePos.Value) / 2;
            if (damage > 0.1f || stunBonus > 30f)
            {
                room.AddObject(new StationaryEffect(pos, new Color(1f, 1f, 1f), null, StationaryEffect.EffectType.FlashingOrb));
                for (int i = 0; i < 3 + (int)Mathf.Min(damage * 3f, 9f); i++)
                {
                    room.AddObject(new Spark(pos, Custom.RNV() * UnityEngine.Random.value * 12f, new Color(1f, 1f, 1f), null, 6, 16));
                }
            }
        }
    }
}
