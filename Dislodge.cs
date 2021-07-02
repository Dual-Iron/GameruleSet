using RWCustom;
using StaticTables;
using System;
using System.Linq;
using UnityEngine;
using static Player.BodyModeIndex;

namespace GameruleSet
{
    public class Dislodge
    {
        const float startingRipChance = 1 / 3f;
        const int stunDuration = 8;
        const int maxTime = 30;

        struct DislodgeAnim : IWeakData<Player>
        {
            public WeakRef<Spear> spear;
            public float time;
            public int graspUsed;
            public float ripChance;

            void IWeakData<Player>.Construct(Player owner)
            {
                spear = new();
            }
            void IWeakData<Player>.Destruct() { }
        }

        private readonly Rules rules;

        public Dislodge(Rules rules)
        {
            this.rules = rules;

            On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;
            On.PlayerGraphics.Update += PlayerGraphics_Update;
            On.Player.UpdateAnimation += Player_UpdateAnimation;
            On.Player.checkInput += Player_checkInput;
            On.Creature.Grab += Creature_Grab;
            On.Player.CanIPickThisUp += Player_CanIPickThisUp;
            On.Spear.ChangeMode += Spear_ChangeMode;
        }

        private bool IsOnWall(Player self)
        {
            return self.bodyChunks[1].ContactPoint.y >= 0 && self.bodyMode != ClimbIntoShortCut && self.bodyMode != CorridorClimb;
        }

        private void PlayerGraphics_DrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            orig(self, sLeaser, rCam, timeStacker, camPos);

            try
            {
                ref var data = ref self.player.Data().Get<DislodgeAnim>();

                if (self.player.animation == EnumExt_GameruleSet.PullingSpear && data.spear.TryGetTarget(out _))
                {
                    for (int i = 0; i < 2; i++)
                    {
                        // Use HangFromBeam hands
                        var vector7 = Vector2.Lerp(self.hands[i].lastPos, self.hands[i].pos, timeStacker);
                        sLeaser.sprites[7 + i].element = Futile.atlasManager.GetElementWithName("OnTopOfTerrainHand2");
                        sLeaser.sprites[7 + i].x = vector7.x - camPos.x;
                        sLeaser.sprites[7 + i].y = vector7.y - camPos.y;
                        sLeaser.sprites[7 + i].isVisible = true;

                        var vector = Vector2.Lerp(self.drawPositions[0, 1], self.drawPositions[0, 0], timeStacker);
                        var vector2 = Vector2.Lerp(self.drawPositions[1, 1], self.drawPositions[1, 0], timeStacker);

                        var num = 0.5f + 0.5f * Mathf.Sin(Mathf.Lerp(self.lastBreath, self.breath, timeStacker) * 3.1415927f * 2f);
                        if (self.player.aerobicLevel > 0.5f)
                            vector += Custom.DirVec(vector2, vector) * Mathf.Lerp(-1f, 1f, num) * Mathf.InverseLerp(0.5f, 1f, self.player.aerobicLevel) * 0.5f;

                        float num6 = 2.25f;
                        num6 *= Mathf.Abs(Mathf.Cos(Custom.AimFromOneVectorToAnother(vector2, vector) / 360f * 3.1415927f * 2f));
                        var vector8 = vector + Custom.RotateAroundOrigo(new Vector2((-1f + 2f * i) * num6, -3.5f), Custom.AimFromOneVectorToAnother(vector2, vector));
                        sLeaser.sprites[5 + i].element = Futile.atlasManager.GetElementWithName("PlayerArm" + Mathf.RoundToInt(Mathf.Clamp(Vector2.Distance(vector7, vector8) / 2f, 0f, 12f)));
                        sLeaser.sprites[5 + i].rotation = Custom.AimFromOneVectorToAnother(vector7, vector8) + 90f;
                        sLeaser.sprites[5 + i].scaleY = Mathf.Sign(Custom.DistanceToLine(vector7, vector, vector2));
                        sLeaser.sprites[5 + i].isVisible = true;
                    }

                    if (data.time < stunDuration && sLeaser.sprites[9].element?.name?.Length == 6)
                        sLeaser.sprites[9].element = Futile.atlasManager.GetElementWithName("FaceStunned");
                }
            }
            catch (Exception e)
            {
                rules.Logger.LogError(e);
            }
        }

        private void PlayerGraphics_Update(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
        {
            orig(self);

            ref var data = ref self.player.Data().Get<DislodgeAnim>();
            if (IsPullingSpear(self.player, data, out var spear))
            {
                var pos = self.player.room.MiddleOfTile(spear!.abstractSpear.pos);

                if (data.time < maxTime - 4)
                {
                    self.LookAtPoint(self.player.firstChunk.pos + (self.player.firstChunk.pos - pos), 20);
                    self.blink = 10;
                }

                if (IsOnWall(self.player))
                {
                    self.legsDirection = (self.player.bodyChunks[1].pos - self.player.bodyChunks[0].pos).normalized;
                }

                for (int i = 0; i < 2; i++)
                {
                    self.hands[i].mode = Limb.Mode.HuntAbsolutePosition;
                    self.hands[i].absoluteHuntPos = pos - spear.rotation * 8 * i;
                    self.hands[i].pos = self.hands[i].absoluteHuntPos;
                }
            }
        }

        private void Player_UpdateAnimation(On.Player.orig_UpdateAnimation orig, Player self)
        {
            ref var data = ref self.Data().Get<DislodgeAnim>();;
            if (IsPullingSpear(self, data, out var spear))
            {
                self.animation = EnumExt_GameruleSet.PullingSpear;

                orig(self);

                var horizontal = Mathf.Abs(spear!.rotation.x) > 0.01f;
                var target = self.room.MiddleOfTile(spear.abstractPhysicalObject.pos);

                // If too far from any of the spear's body chunks, plop off
                if ((spear.bodyChunks[0].pos - self.firstChunk.pos).sqrMagnitude > 35 * 35)
                {
                    self.room.PlaySound(SoundID.Spear_Stick_In_Wall, spear.firstChunk.pos, 1.2f, 1.1f);
                    self.animation = Player.AnimationIndex.None;
                    self.bodyMode = Stunned;
                    self.SlugcatGrab(spear, data.graspUsed);
                    self.Stun(60);
                    StopPulling(ref data);
                }
                else
                {
                    if (IsOnWall(self))
                    {
                        self.bodyMode = ClimbingOnBeam;
                        self.standing = true;
                        self.firstChunk.pos.y = self.room.MiddleOfTile(self.firstChunk.pos).y + 4;
                        self.firstChunk.vel = Vector2.zero;

                        self.bodyChunks[0].vel -= 1.25f * (target - self.bodyChunks[0].pos - new Vector2(5 * -spear.rotation.x, 10)).normalized;
                        self.bodyChunks[1].vel += 2f * (target - self.bodyChunks[1].pos).normalized;

                        var yDist = Mathf.Abs(self.bodyChunks[1].pos.y - target.y);
                        if (yDist < 1)
                        {
                            self.bodyChunks[1].pos.y = target.y;
                            self.bodyChunks[1].vel.y *= 0.2f;
                        }
                        else if (yDist < 5)
                        {
                            self.bodyChunks[1].vel.y *= 0.5f;
                        }
                    }
                    else if (horizontal || Math.Abs(self.bodyChunks[0].pos.x - target.x) > 5)
                    {
                        var v = Math.Sign(target.x - self.bodyChunks[0].pos.x);
                        self.bodyChunks[0].vel.x -= v;
                        self.bodyChunks[1].vel.x += v;
                    }

                    if (data.time > 0)
                    {
                        float distance = Math.Abs(horizontal ? target.y - self.bodyChunks[1].pos.y : target.x - self.bodyChunks[0].pos.x);
                        float delta = -1f / Mathf.Clamp(distance / 15f, 1f, 2f);

                        if (self.bodyMode == Crawl || self.bodyMode == ClimbingOnBeam)
                        {
                            delta *= 0.85f;
                        }
                        else if (self.bodyMode != Stand)
                        {
                            delta *= 0.70f;
                        }

                        data.time += delta;

                        if (data.time <= 0)
                        {
                            if (UnityEngine.Random.value < data.ripChance)
                                RipSpear(self, ref data, spear, target);
                            else
                                FirstYank(self, spear, target);

                            data.time = maxTime;
                            data.ripChance += 0.1f;
                        }
                    }
                }
            }
            else
            {
                orig(self);
                StopPulling(ref data);
            }
        }

        private static void StopPulling(ref DislodgeAnim data)
        {
            if (data.spear.TryGetTarget(out _))
                data.spear = new();
            data.time = 0;
        }

        private bool IsPullingSpear(Player self, DislodgeAnim data, out Spear? spear)
        {
            spear = null;
            return rules.Dislodge && data.spear.TryGetTarget(out spear) && (self.bodyMode == CorridorClimb || self.bodyMode == ClimbIntoShortCut || self.animation == EnumExt_GameruleSet.PullingSpear);
        }

        private void FirstYank(Player self, Spear spear, Vector2 target)
        {
            self.room.PlaySound(SoundID.Spear_Fragment_Bounce, spear.firstChunk.pos, 0.65f, 1.2f);

            if (IsOnWall(self))
                self.room.PlaySound(SoundID.Slugcat_Normal_Jump, self.mainBodyChunk);

            int num = UnityEngine.Random.Range(1, 3);
            for (int i = 0; i < num; i++)
            {
                var splashDir = -spear.rotation.normalized + Custom.DegToVec(UnityEngine.Random.value * 90 - 45);
                var pos = target + splashDir * 3f;
                var speed = UnityEngine.Random.value * 4;
                self.room.AddObject(new WaterDrip(pos, splashDir * speed, true));
            }

            var dir = (self.bodyChunks[0].pos - self.bodyChunks[1].pos).normalized;
            self.bodyChunks[0].vel += dir * 6;
            self.bodyChunks[1].vel -= dir * 3;

            self.AerobicIncrease(1);
        }

        private void RipSpear(Player self, ref DislodgeAnim data, Spear spear, Vector2 target)
        {
            int num = UnityEngine.Random.Range(3, 6);
            for (int i = 0; i < num; i++)
            {
                var splashDir = -spear.rotation.normalized + Custom.DegToVec(UnityEngine.Random.value * 120 - 60);
                var pos = target + splashDir * 4f;
                var speed = 1 + UnityEngine.Random.value * 6;
                self.room.AddObject(new WaterDrip(pos, splashDir * speed, true));
            }

            if (IsOnWall(self))
                self.room.PlaySound(SoundID.Slugcat_Super_Jump, self.firstChunk);

            self.room.PlaySound(SoundID.Spear_Stick_In_Wall, spear.firstChunk.pos, 1f, 1.2f);

            self.animation = Player.AnimationIndex.Flip;
            self.standing = false;

            var dir = (self.bodyChunks[0].pos - self.bodyChunks[1].pos).normalized;
            self.flipDirection = Math.Sign(dir.x);
            self.bodyChunks[0].vel += dir * 8;
            self.bodyChunks[1].vel += dir * 4;

            self.SlugcatGrab(spear, data.graspUsed);

            self.AerobicIncrease(1);

            StopPulling(ref data);
        }

        private void Player_checkInput(On.Player.orig_checkInput orig, Player self)
        {
            ref var data = ref self.Data().Get<DislodgeAnim>();
            if (self.animation == EnumExt_GameruleSet.PullingSpear && data.spear.TryGetTarget(out _))
            {
                var tempStandStill = self.standStillOnMapButton;
                self.standStillOnMapButton = false;
                orig(self);
                self.standStillOnMapButton = tempStandStill;

                if (self.input[0].jmp)
                {
                    self.animation = Player.AnimationIndex.Flip;
                    return;
                }
                
                self.input[0] = new (self.room.game.rainWorld.options.controls[self.playerState.playerNumber].gamePad, 0, 0, false, false, false, false, false);
            }
            else
            {
                orig(self);
            }
        }

        private bool Creature_Grab(On.Creature.orig_Grab orig, Creature self, PhysicalObject obj, int graspUsed, int chunkGrabbed, Creature.Grasp.Shareability shareability, float dominance, bool overrideEquallyDominant, bool pacifying)
        {
            if (obj is Spear s && s.mode == Weapon.Mode.StuckInWall)
            {
                if (self is not Player player)
                    return false;

                ref var data = ref player.Data().Get<DislodgeAnim>();
                if (data.spear == null)
                {
                    data.spear = new(s);
                    data.ripChance = startingRipChance + player.slugcatStats.throwingSkill / 10f;
                    data.time = maxTime;
                    data.graspUsed = graspUsed;
                    player.animation = EnumExt_GameruleSet.PullingSpear;
                    return false;
                }
            }
            return orig(self, obj, graspUsed, chunkGrabbed, shareability, dominance, overrideEquallyDominant, pacifying);
        }

        private void Spear_ChangeMode(On.Spear.orig_ChangeMode orig, Spear self, Weapon.Mode newMode)
        {
            if (rules.Dislodge && self.mode == Weapon.Mode.StuckInWall)
            {
                if (self.abstractSpear.stuckInWallCycles >= 0)
                {
                    self.room.GetTile(self.stuckInWall!.Value).horizontalBeam = false;
                    for (int i = -1; i < 2; i += 2)
                    {
                        if (!self.room.GetTile(self.stuckInWall.Value + new Vector2(20f * i, 0f)).Solid)
                        {
                            self.room.GetTile(self.stuckInWall.Value + new Vector2(20f * i, 0f)).horizontalBeam = false;
                        }
                    }
                }
                else
                {
                    self.room.GetTile(self.stuckInWall!.Value).verticalBeam = false;
                    for (int j = -1; j < 2; j += 2)
                    {
                        if (!self.room.GetTile(self.stuckInWall.Value + new Vector2(0f, 20f * j)).Solid)
                        {
                            self.room.GetTile(self.stuckInWall.Value + new Vector2(0f, 20f * j)).verticalBeam = false;
                        }
                    }
                }
                self.abstractSpear.stuckInWallCycles = 0;
            }
            orig(self, newMode);
        }

        private bool Player_CanIPickThisUp(On.Player.orig_CanIPickThisUp orig, Player self, PhysicalObject obj)
        {
            if (self.Data().Get<DislodgeAnim>().time > 0)
                return false;

            if (orig(self, obj))
                return true;

            if (!rules.Dislodge || self.bodyMode == Default || self.bodyMode == ClimbingOnBeam && self.animation != Player.AnimationIndex.HangFromBeam || self.mainBodyChunk.vel.sqrMagnitude > 5f || self.mainBodyChunk.vel.y < -0.01f)
                return false;

            for (int i = 0; i < self.input.Length; i++)
            {
                if (self.input[i].x != 0 || self.input[i].y != 0)
                    return false;
            }

            // Spear must be in a wall and player must be within 30 units of it
            if (obj is Spear s && s.mode == Weapon.Mode.StuckInWall && (s.bodyChunks[0].pos - self.firstChunk.pos).magnitude < 25)
            {
                // If climbing on a beam but not hanging on the spear, boot
                if (self.bodyMode == ClimbingOnBeam && self.room.GetTilePosition(self.firstChunk.pos).y != s.abstractPhysicalObject.pos.y)
                {
                    return false;
                }
                // Must have an open hand. Backspear doesn't work for dislodging spears, duh.
                return !self.grasps.Any(v => v != null && self.Grabability(v.grabbed) > Player.ObjectGrabability.OneHand);
            }

            return false;
        }
    }
}
