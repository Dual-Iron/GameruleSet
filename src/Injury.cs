using MonoMod.RuntimeDetour;
using RWCustom;
using WeakTables;
using System;
using UnityEngine;

namespace GameruleSet
{
    public partial class Injury
    {
        private const int maxPainTime = 60;

        static readonly WeakTable<PlayerState, InjuryData> injuryData = new(_ => new());
        static readonly WeakTable<SaveState, SaveStateData> saveStateData = new(_ => new());

        sealed class InjuryData
        {
            public float injury;
            public float damageBlockedWithMask;
            public int painTime;
            public float lastAerobicLevel;
            public float woundDir;
            public bool shouldPain;

            public bool InPain => painTime > 0;
            public bool Injured => injury > 0;
        }

        sealed class SaveStateData
        {
            public float[] injuries = new float[0];
        }

        private readonly Rules rules;

        private Action replaceDeath;

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

            new Hook(typeof(VirtualMicrophone).GetMethod("get_InWorldSoundsVolumeGoal"), GetterInWorldSoundsVolumeGoal)
                    .Apply();
        }

        private float GetterInWorldSoundsVolumeGoal(Func<VirtualMicrophone, float> orig, VirtualMicrophone self)
        {
            if (rules.Injury && self.room.game.IsStorySession)
            {
                foreach (var plr in self.room.game.Players)
                {
                    if (plr?.realizedObject is Player p && p.State.alive)
                    {
                        return orig(self) * Mathf.Lerp(1f, 0.5f, injuryData[p.playerState].painTime / (float)maxPainTime);
                    }
                }
            }
            return orig(self);
        }

        private void PlayerGraphics_Update(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
        {
            orig(self);

            if (!rules.Injury) {
                return;
            }

            var data = injuryData[self.player.playerState];
            if (!data.Injured) {
                return;
            }

            if (self.malnourished < data.injury * 0.75f + 0.25f) {
                self.malnourished += 0.01f;
            }

            if (self.player.State.alive) {
                if (data.InPain) {
                    if (self.markAlpha > 0) {
                        self.markAlpha += (UnityEngine.Random.value - 0.5f) * data.painTime / maxPainTime;
                        self.markAlpha = Mathf.Clamp01(self.markAlpha);
                    }

                    self.objectLooker.lookAtPoint = null;
                    self.objectLooker.LookAtNothing();
                }

                if (!self.player.lungsExhausted && !self.player.exhausted) {
                    self.breath += 1 / (70f - UnityEngine.Random.value * data.injury * 25f);
                    self.player.swimCycle += 0.05f * self.player.aerobicLevel;

                    if (self.player.stun <= 0) {
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
            if (rules.Injury)
            {
                var data = injuryData[self.playerState];

                st = (int)(st * (1 + data.injury));
            }
            orig(self, st);
        }

        private VultureMask GetGraspedMask(Player player)
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
            if (rules.Injury && obj is Player player && chunkGrabbed == 0 && GetGraspedMask(player) is VultureMask mask) {
                mask.room.PlaySound(SoundID.Spear_Fragment_Bounce, mask.firstChunk.pos, 0.7f, 1.35f);
                VulturePopEffect(obj.room, null, obj.bodyChunks[chunkGrabbed].pos, 0.1f, pacifying ? 45f : 0f);
                mask.AllGraspsLetGoOfThisObject(true);

                if (self is Lizard or Vulture or DropBug or BigSpider)
                    obj = mask;
                else
                    return false;
            }
            return orig(self, obj, graspUsed, chunkGrabbed, shareability, dominance, overrideEquallyDominant, pacifying);
        }

        private void Player_AerobicIncrease(On.Player.orig_AerobicIncrease orig, Player self, float f)
        {
            orig(self, f);

            var data = injuryData[self.playerState];
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

            var data = injuryData[self.playerState];
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

#if DEBUG
            if (Input.GetKeyDown(KeyCode.Alpha8)) {
                DropMaskDebug(self, true);
            }
            if (Input.GetKeyDown(KeyCode.Alpha7)) {
                DropMaskDebug(self, false);
            }
            static void DropMaskDebug(Player self, bool king)
            {
                var mask = new VultureMask.AbstractVultureMask(self.room.world, null, default, self.room.game.GetNewID(), 0, king) {
                    pos = self.abstractCreature.pos
                };
                self.room.abstractRoom.AddEntity(mask);
                mask.RealizeInRoom();
                mask.realizedObject.firstChunk.HardSetPosition(self.firstChunk.pos);
                mask.realizedObject.firstChunk.vel = default;
            }
#endif
        }

        private static void InjuredUpdate(Player self, ref InjuryData data)
        {
            // Aerobic level decreases slower
            if (self.aerobicLevel < data.lastAerobicLevel)
                self.aerobicLevel -= (self.aerobicLevel - data.lastAerobicLevel) * (data.injury * data.injury * 0.75f);

            // If exhausted, experience pain
            if (self.aerobicLevel >= 1 && data.painTime == 0 || data.shouldPain)
            {
                data.shouldPain = false;
                Hurt(self, ref data);
            }

            var troubleStanding = 0f;

            // Agony
            if (data.InPain)
            {
                troubleStanding = 0.04f;

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

            self.slowMovementStun = Math.Max(self.slowMovementStun, (int)(data.injury * 20f * (self.aerobicLevel - 0.5f)));

            // Forewarn pain
            if (self.aerobicLevel >= 0.75f)
                self.Blink(2);

            // Twinge of pain
            if (self.aerobicLevel >= 0.88f && troubleStanding == 0f)
                troubleStanding = 0.02f;

            if (self.stun == 0 && self.animation != Player.AnimationIndex.HangFromBeam &&
                self.standing && UnityEngine.Random.value < troubleStanding)
            {
                self.Stun(self.bodyMode == Player.BodyModeIndex.Stand ? 9 : 3);
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
            float ActualDamage(float damage)
            {
                return damage / self.Template.baseDamageResistance / (self.Template.damageRestistances[(int)type, 0] > 0 ? self.Template.damageRestistances[(int)type, 0] : 1);
            }

            var data = injuryData[player.playerState];

            bool wouldBeLethal = ActualDamage(damage) >= self.Template.instantDeathDamageLimit;

            if (wouldBeLethal)
            {
                // Damage from lizards is the same as non-player creatures with Injury enabled
                if (source?.owner is Lizard l) {
                    damage = l.lizardParams.biteDamage * (0.8f + 0.4f * UnityEngine.Random.value);
                }
                // Damage from explosions and blunt force is halved
                else if (type == Creature.DamageType.Explosion || type == Creature.DamageType.Blunt) {
                    damage *= 0.5f;
                }
            }

            // Headshots from items like spears, rocks, and darts have special conditions (vulture mask protection)
            if (hitChunk.index == 0 && (type == Creature.DamageType.Stab || type == Creature.DamageType.Blunt))
            {
                if (source != null && GetGraspedMask(player) is VultureMask mask && damage < (mask.King ? 6 : 3))
                {
                    if (source.owner is DartMaggot) {
                        source.vel = default;
                    }

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
            // Anything except headshots from spears/rocks is just half damage
            else
            {
                damage *= 0.5f;
            }

            // Mercy.
            if (wouldBeLethal)
            {
                data.lastAerobicLevel = 0;
                player.aerobicLevel = 0;

                // Mitigate instant death from chained damage
                data.woundDir = (directionAndMomentum?.GetAngle() ?? (UnityEngine.Random.value * 360)) - player.bodyChunks[1].Rotation.GetAngle();

                if (type == Creature.DamageType.Bite)
                {
                    stunBonus /= 5;
                }
            }

            // Only increase injury if the attack would've been lethal, or if already injured
            if (wouldBeLethal || data.Injured)
            {
                data.injury += ActualDamage(damage);
                data.injury = Mathf.Min(data.injury, 1);
            }

            // Kill player iff attack is lethal and injury ≥ 1
            if (wouldBeLethal && data.injury >= 1) {
                damage = self.Template.instantDeathDamageLimit;
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
