using MonoMod.RuntimeDetour;
using RWCustom;
using StaticTables;
using System;
using UnityEngine;

namespace GameruleSet
{
    public partial class Injury
    {
        private const int maxPainTime = 60;

        struct InjuryData : IWeakData<PlayerState>
        {
            public float injury;
            public float damageBlockedWithMask;
            public int painTime;
            public float lastAerobicLevel;
            public float woundDir;
            public bool shouldPain;

            public bool InPain => painTime > 0;
            public bool Injured => injury > 0;

            void IWeakData<PlayerState>.Construct(PlayerState key) { }
            void IWeakData<PlayerState>.Destruct() { }
        }

        struct SaveStateData : IWeakData<SaveState>
        {
            public float[] injuries;

            void IWeakData<SaveState>.Construct(SaveState owner)
            {
                injuries = new float[0];
            }
            void IWeakData<SaveState>.Destruct() { }
        }

        private readonly Rules rules;

        private Action? replaceDeath;

        public Injury(Rules rules)
        {
            this.rules = rules;

            new InjurySave(rules);
            
            On.PlayerGraphics.Update += PlayerGraphics_Update;
            On.Player.Die += Player_Die;
            On.Player.TerrainImpact += Player_TerrainImpact;
            On.Creature.Grab += Creature_Grab;
            On.Creature.Violence += Creature_Violence;
            On.Creature.SpearStick += Creature_SpearStick;
            On.Player.AerobicIncrease += Player_AerobicIncrease;
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

                if (average > 0)
                {
                    average /= count;
                    return orig(self) * Mathf.Lerp(1f, 0.4f, average / maxPainTime);
                }
            }
            return orig(self);
        }

        private void PlayerGraphics_Update(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
        {
            if (!rules.Injury)
            {
                orig(self);
            }
            else
            {
                orig(self);

                var data = self.player.playerState.Data().Get<InjuryData>();
                if (data.Injured)
                {
                    if (self.malnourished < data.injury * 0.75f + 0.25f)
                        self.malnourished += 0.01f;

                    if (self.player.State.alive)
                    {
                        if (data.InPain)
                        {
                            if (self.markAlpha > 0)
                            {
                                self.markAlpha += (UnityEngine.Random.value - 0.5f) * data.painTime / maxPainTime;
                                self.markAlpha = Mathf.Clamp01(self.markAlpha);
                            }

                            self.objectLooker.lookAtPoint = null;
                            self.objectLooker.LookAtNothing();
                        }

                        if (!self.player.lungsExhausted && !self.player.exhausted)
                        {
                            self.breath += 1 / (70f - UnityEngine.Random.value * data.injury * 25f);
                            self.player.swimCycle += 0.05f * self.player.aerobicLevel;

                            if (self.player.stun <= 0)
                            {
                                self.head.vel.y += Mathf.Sin(self.player.swimCycle * Mathf.PI * 2) * 0.25f;
                                self.drawPositions[0, 0].y += Mathf.Sin(self.player.swimCycle * Mathf.PI * 2f) * 0.75f;
                            }
                        }
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
            if (rules.Injury)
            {
                var data = self.playerState.Data().Get<InjuryData>();

                st = (int)(st * (1 + data.injury));
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
            if (rules.Injury && obj is Player player && chunkGrabbed == 0)
            {
                var mask = GetGraspedMask(player);
                if (mask != null)
                {
                    mask.room.PlaySound(SoundID.Spear_Fragment_Bounce, mask.firstChunk.pos, 0.7f, 1.35f);
                    VulturePopEffect(obj.room, null, obj.bodyChunks[chunkGrabbed].pos, 0.1f, pacifying ? 45f : 0f);
                    mask.AllGraspsLetGoOfThisObject(true);
                    obj = mask;
                }
            }
            return orig(self, obj, graspUsed, chunkGrabbed, shareability, dominance, overrideEquallyDominant, pacifying);
        }

        private void Player_AerobicIncrease(On.Player.orig_AerobicIncrease orig, Player self, float f)
        {
            orig(self, f);

            ref var data = ref self.playerState.Data().Get<InjuryData>();
            if (rules.Injury && data.Injured && self.aerobicLevel >= 1)
            {
                data.shouldPain = true;
            }
        }

        private void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            if (!rules.Injury)
            {
                orig(self, eu);
                return;
            }

            ref var data = ref self.playerState.Data().Get<InjuryData>();

            if (data.Injured && self.State.alive)
            {
                InjuredUpdate(self, ref data);
            }

            data.lastAerobicLevel = self.aerobicLevel;

            orig(self, eu);

            if (self.State.dead)
                return;

            if (data.damageBlockedWithMask > 0)
            {
                self.Stun((int)(data.damageBlockedWithMask * 15));
                data.damageBlockedWithMask = 0;
            }
        }

        private static void InjuredUpdate(Player self, ref InjuryData data)
        {
            // Aerobic level decreases 20% slower
            if (self.aerobicLevel < data.lastAerobicLevel)
                self.aerobicLevel -= (self.aerobicLevel - data.lastAerobicLevel) * (data.injury / 2);

            // If exhausted, experience pain
            if (self.aerobicLevel >= 1 && data.painTime == 0 || data.shouldPain)
            {
                data.shouldPain = false;
                Hurt(self, ref data);
            }

            bool troubleStanding = false;

            // Agony
            if (data.InPain)
            {
                troubleStanding = true;

                self.slowMovementStun = Math.Max(self.slowMovementStun, (int)(10f * self.aerobicLevel));

                if (self.animation == Player.AnimationIndex.GetUpOnBeam)
                {
                    self.bodyChunks[1].vel *= 0.8f;
                }

                if (self.aerobicLevel < 0.4f || self.Adrenaline > 0)
                {
                    data.painTime -= 1;

                    if (UnityEngine.Random.value < 0.18)
                        self.Blink(5);
                }
                else self.Blink(5);
            }

            data.painTime = Math.Max(data.painTime, 0);

            if (self.Adrenaline > 0)
                return;

            self.slowMovementStun = Math.Max(self.slowMovementStun, (int)(6f * self.aerobicLevel));

            // Forewarn pain
            if (self.aerobicLevel >= 0.68f)
                self.Blink(2);

            // Twinge of pain
            if (self.aerobicLevel >= 0.85f)
                troubleStanding = true;

            if (troubleStanding && self.stun == 0 && self.animation != Player.AnimationIndex.HangFromBeam &&
                self.standing && UnityEngine.Random.value < 0.04f)
            {
                self.Stun(self.bodyMode == Player.BodyModeIndex.Stand ? 15 : 5);
            }
        }

        private static void Hurt(Player self, ref InjuryData data)
        {
            if (self.Adrenaline > 0)
            {
                return;
            }

            if (data.InPain)
            {
                self.Stun(30);
                data.painTime = maxPainTime;
                return;
            }

            self.aerobicLevel = Mathf.Min(1, self.aerobicLevel);
            data.painTime = maxPainTime;
            self.Stun(50 + UnityEngine.Random.Range(0, 21));

            // Visuals
            self.room.PlaySound(SoundID.Slugcat_Swallow_Item, self.mainBodyChunk.pos, 1.25f, 2.2f);

            for (int i = 0; i < 3; i++)
            {
                var dir = self.bodyChunks[1].Rotation.GetAngle() + data.woundDir - 90 + UnityEngine.Random.value * 30 - 15;
                var pos = self.bodyChunks[1].pos + Custom.DegToVec(dir) * self.bodyChunks[1].rad * 0.75f;
                var direction = Custom.DegToVec(dir);
                var speed = 4 + UnityEngine.Random.value * 8;
                self.room.AddObject(new WaterDrip(pos, direction * speed, false));
            }
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
            if (rules.Injury && self is Player player && player.State.alive)
            {
                Injure(self, source, directionAndMomentum, hitChunk, type, ref damage, ref stunBonus, player);
            }
            orig(self, source, directionAndMomentum, hitChunk, hitAppendage, type, damage, stunBonus);
        }

        private void Injure(Creature self, BodyChunk source, Vector2? directionAndMomentum, BodyChunk hitChunk, Creature.DamageType type, ref float damage, ref float stunBonus, Player player)
        {
            float actualDamage(float damage)
            {
                return damage / self.Template.baseDamageResistance / (self.Template.damageRestistances[(int)type, 0] > 0 ? self.Template.damageRestistances[(int)type, 0] : 1);
            }

            ref var data = ref player.playerState.Data().Get<InjuryData>();

            var wouldBeLethal = actualDamage(damage) >= self.Template.instantDeathDamageLimit;

            if (wouldBeLethal)
            {
                if (source?.owner is Lizard l)
                    damage = l.lizardParams.biteDamage * (0.8f + 0.4f * UnityEngine.Random.value);
                else if (type == Creature.DamageType.Blunt)
                    damage *= 0.3333f;
                else if (type == Creature.DamageType.Explosion)
                    damage *= 0.5f;
            }

            if (hitChunk.index == 0 && (type == Creature.DamageType.Stab || type == Creature.DamageType.Blunt))
            {
                if (source != null && GetGraspedMask(player) is VultureMask mask)
                {
                    VulturePopEffect(player.room, source.pos, hitChunk.pos, damage, stunBonus);

                    if (!mask.King)
                        data.damageBlockedWithMask = damage + stunBonus / 45f;
                    else
                        data.damageBlockedWithMask = stunBonus / 90f;

                    mask.donned = 1f;
                    stunBonus = 0;
                    damage = 0f;

                    return;
                }
            }
            else
            {
                damage *= 0.5f;
            }

            if (wouldBeLethal)
            {
                data.lastAerobicLevel = 0;
                player.aerobicLevel = 0;

                // Mitigate instant death from chained damage
                data.woundDir = (directionAndMomentum?.GetAngle() ?? (UnityEngine.Random.value * 360)) - player.bodyChunks[1].Rotation.GetAngle();

                if (type == Creature.DamageType.Bite)
                {
                    stunBonus = 0;
                }
            }

            if (wouldBeLethal || data.Injured)
            {
                data.injury += actualDamage(damage);

                if (data.injury >= 1)
                {
                    data.injury = 1;

                    if (wouldBeLethal)
                    {
                        damage = self.Template.instantDeathDamageLimit;
                    }
                }
            }
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
