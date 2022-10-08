using RWCustom;
using System.Linq;
using System;
using UnityEngine;
using static LizardTongue;
using static Player.AnimationIndex;
using static Player.BodyModeIndex;
using WeakTables;
using System.Collections.Generic;

namespace GameruleSet
{
    sealed class DislodgeData
    {
        public WeakRef<Spear> Spear;
        public int Grasp;
        public float Time;
        public int NoThrowTime;

        public void Reset()
        {
            Grasp = default;
            NoThrowTime = default;
            Spear = default;
            Time = default;
        }

        public bool IsActive()
        {
            return Spear != null && Spear.TryGetTarget(out _);
        }

        public bool IsActive(out Spear spear)
        {
            if (Spear == null) {
                spear = null;
                return false;
            }
            return Spear.TryGetTarget(out spear);
        }
    }

    sealed class RoomData
    {
        public readonly Dictionary<BeamPos, BeamData> Beams = new();
    }

    readonly struct BeamPos
    {
        public readonly int X;
        public readonly int Y;

        public BeamPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public override bool Equals(object obj)
        {
            return obj is BeamPos pos && X == pos.X && Y == pos.Y;
        }

        public override int GetHashCode()
        {
            int hashCode = 1861411795;
            hashCode = hashCode * -1521134295 + X.GetHashCode();
            hashCode = hashCode * -1521134295 + Y.GetHashCode();
            return hashCode;
        }
    }

    readonly struct BeamData
    {
        public readonly bool Horizontal;
        public readonly bool Vertical;

        public BeamData(bool horizontal, bool vertical)
        {
            Horizontal = horizontal;
            Vertical = vertical;
        }
    }

    sealed class Dislodge
    {
        static readonly WeakTable<Player, DislodgeData> dislodgeData = new(_ => new());
        static readonly WeakTable<Room, RoomData> roomData = new(_ => new(), verify: false);

        const float DislodgeChance = 0.65f;
        const float DislodgeTime = 30;

        private static bool IsStuckVertically(Spear s)
        {
            return s.abstractSpear.stuckInWallCycles < 0;
        }

        private static bool IsDislodgingFromWall(Player self)
        {
            return self.bodyChunks[1].ContactPoint.y >= 0 && self.bodyMode is not ClimbIntoShortCut and not CorridorClimb;
        }

        private static Vector2 TileStuckIn(Spear s)
        {
            if (IsStuckVertically(s)) {
                return s.firstChunk.pos + 20 * (s.IsTileSolid(0, 0, 1) ? Vector2.up : -Vector2.up);
            }
            return s.firstChunk.pos + 20 * (s.IsTileSolid(0, 1, 0) ? Vector2.right : -Vector2.right);
        }

        public Dislodge()
        {
            // For graphics, see PlayerGraphicsSharedHooks.cs
            On.PlayerGraphics.Update += PlayerGraphics_Update;
            On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;

            On.Player.IsObjectThrowable += Player_IsObjectThrowable;
            On.Player.UpdateAnimation += AnimateDislodging;
            On.Player.checkInput += LockInput;
            On.Creature.Grab += StartDislodgingSpear;
            On.Player.CanIPickThisUp += FixPickup;
            On.Spear.ChangeMode += FixChangeMode;
        }

        private void PlayerGraphics_Update(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
        {
            orig(self);

            var dislodge = dislodgeData[self.player];
            if (!dislodge.IsActive(out var spear)) {
                return;
            }

            if (dislodge.Time < DislodgeTime - 4) {
                self.LookAtPoint(self.player.firstChunk.pos + (self.player.firstChunk.pos - spear.firstChunk.pos), 60);
                self.blink = 10;
            }

            if (IsDislodgingFromWall(self.player)) {
                self.legsDirection = (self.player.bodyChunks[1].pos - self.player.bodyChunks[0].pos).normalized;
            }

            for (int i = 0; i < 2; i++) {
                self.hands[i].mode = Limb.Mode.HuntAbsolutePosition;
                self.hands[i].absoluteHuntPos = spear.firstChunk.pos - spear.rotation * 8 * i;
                self.hands[i].pos = self.hands[i].absoluteHuntPos;
            }
        }

        private void PlayerGraphics_DrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            orig(self, sLeaser, rCam, timeStacker, camPos);

            var dislodge = dislodgeData[self.player];
            if (!dislodge.IsActive()) {
                return;
            }

            for (int i = 0; i < 2; i++) {
                if (self.player.grasps[i] != null) {
                    continue;
                }

                ShowBeamHandSprite(self, sLeaser, timeStacker, camPos, i);
            }

            if (sLeaser.sprites[9].element.name.Length == 6 && dislodge.Time < 8) {
                sLeaser.sprites[9].element = Futile.atlasManager.GetElementWithName("FaceStunned");
            }

            sLeaser.sprites[4].isVisible = true;

            static void ShowBeamHandSprite(PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, float timeStacker, Vector2 camPos, int hand)
            {
                // Use HangFromBeam hands
                var vector7 = Vector2.Lerp(self.hands[hand].lastPos, self.hands[hand].pos, timeStacker);
                sLeaser.sprites[7 + hand].element = Futile.atlasManager.GetElementWithName("OnTopOfTerrainHand2");
                sLeaser.sprites[7 + hand].x = vector7.x - camPos.x;
                sLeaser.sprites[7 + hand].y = vector7.y - camPos.y;
                sLeaser.sprites[7 + hand].isVisible = true;

                sLeaser.sprites[5 + hand].isVisible = true;
            }
        }

        private static bool Player_IsObjectThrowable(On.Player.orig_IsObjectThrowable orig, Player self, PhysicalObject obj)
        {
            return dislodgeData[self].NoThrowTime == 0 && orig(self, obj);
        }

        private void AnimateDislodging(On.Player.orig_UpdateAnimation orig, Player self)
        {
            var dislodge = dislodgeData[self];

            if (dislodge.IsActive(out var spear) && !spear.slatedForDeletetion && self.Consious) {
                self.animation = (Player.AnimationIndex)(-59483);

                orig(self);

                DoDislodgeAnimation(self, dislodge, spear);
            }
            else {
                if (dislodge.NoThrowTime > 0) {
                    dislodge.NoThrowTime--;
                }
                else {
                    dislodge.Reset();
                }

                orig(self);
            }
        }

        private void DoDislodgeAnimation(Player plr, DislodgeData dislodge, Spear spear)
        {
            var horizontal = Mathf.Abs(spear.rotation.x) > 0.01f;
            var target = TileStuckIn(spear);

            // If too far from any of the spear's body chunks, plop off
            if ((spear.bodyChunks[0].pos - plr.firstChunk.pos).sqrMagnitude > 35 * 35) {
                dislodge.Reset();

                plr.animation = None;
                plr.bodyMode = Stunned;
                plr.Stun(40);
                return;
            }

            if (plr.dontGrabStuff < 10) {
                plr.dontGrabStuff = 10;
            }

            bool fromWall = IsDislodgingFromWall(plr);

            if (fromWall) {
                plr.bodyMode = ClimbingOnBeam;
                plr.standing = true;

                plr.firstChunk.pos.y = plr.room.MiddleOfTile(plr.firstChunk.pos).y + 4;
                plr.firstChunk.vel = Vector2.zero;

                if (IsStuckVertically(spear)) {
                    if (plr.bodyChunks[0].pos.y + 20 > target.y) {
                        plr.bodyChunks[0].pos.y -= 20;
                    }
                    plr.bodyChunks[0].vel.y -= 1.25f * plr.room.gravity;
                }
                else {
                    plr.bodyChunks[0].vel -= 1.25f * (target - plr.bodyChunks[0].pos - new Vector2(5 * -spear.rotation.x, 10)).normalized;
                }

                plr.bodyChunks[1].vel += 2f * (target - plr.bodyChunks[1].pos).normalized;

                var yDist = Mathf.Abs(plr.bodyChunks[1].pos.y - target.y);
                if (yDist < 1) {
                    plr.bodyChunks[1].pos.y = target.y;
                    plr.bodyChunks[1].vel.y *= 0.2f;
                }
                else if (yDist < 5) {
                    plr.bodyChunks[1].vel.y *= 0.5f;
                }
            }
            else if (horizontal || Math.Abs(plr.bodyChunks[0].pos.x - target.x) > 5) {
                var v = Math.Sign(target.x - plr.bodyChunks[0].pos.x);
                plr.bodyChunks[0].vel.x -= v;
                plr.bodyChunks[1].vel.x += v;
            }

            if (dislodge.Time > 0) {
                float distance = Math.Abs(horizontal ? target.y - plr.bodyChunks[1].pos.y : target.x - plr.bodyChunks[0].pos.x);
                float delta = -1f / Mathf.Clamp(distance / 15f, 1f, 2f);

                if (plr.bodyMode == Crawl || plr.bodyMode == ClimbingOnBeam) {
                    delta *= 0.85f;
                }
                else if (plr.bodyMode != Stand) {
                    delta *= 0.70f;
                }

                dislodge.Time += delta;

                if (dislodge.Time <= 0) {
                    if (UnityEngine.Random.value < DislodgeChance) {
                        SucceedDislodge(dislodge);
                    }
                    else {
                        FailDislodge();
                    }

                    dislodge.Time = DislodgeTime;
                }
            }

            void SucceedDislodge(DislodgeData dislodge)
            {
                int droplets = UnityEngine.Random.Range(1, 4);
                for (int i = 0; i < droplets; i++) {
                    var splashDir = -spear.rotation.normalized + Custom.DegToVec(UnityEngine.Random.value * 120 - 60);
                    var pos = target + splashDir * 4f;
                    var speed = 1 + UnityEngine.Random.value * 5;
                    plr.room.AddObject(new WaterDrip(pos, splashDir * speed, true));
                }

                if (fromWall) {
                    plr.room.PlaySound(SoundID.Slugcat_Super_Jump, plr.firstChunk);
                }

                plr.room.PlaySound(SoundID.Spear_Stick_In_Wall, spear.firstChunk.pos, 1f, 1.2f);

                plr.animation = Flip;
                plr.standing = false;

                var dir = (plr.bodyChunks[0].pos - plr.bodyChunks[1].pos).normalized;
                plr.flipDirection = plr.slideDirection = Math.Sign(dir.x);
                plr.bodyChunks[0].vel += dir * 8;
                plr.bodyChunks[1].vel += dir * 4;

                plr.SlugcatGrab(spear, dislodge.Grasp);

                plr.AerobicIncrease(0.5f);

                dislodge.Reset();
                dislodge.NoThrowTime = 15;
            }

            void FailDislodge()
            {
                plr.room.PlaySound(SoundID.Spear_Fragment_Bounce, spear.firstChunk.pos, 0.65f, 1.2f);

                if (fromWall) {
                    plr.room.PlaySound(SoundID.Slugcat_Normal_Jump, plr.mainBodyChunk);
                }

                int num = UnityEngine.Random.Range(0, 3);
                for (int i = 0; i < num; i++) {
                    var splashDir = -spear.rotation.normalized + Custom.DegToVec(UnityEngine.Random.value * 90 - 45);
                    var pos = target + splashDir * 3f;
                    var speed = UnityEngine.Random.value * 5;
                    plr.room.AddObject(new WaterDrip(pos, splashDir * speed, true));
                }

                var dir = (plr.bodyChunks[0].pos - plr.bodyChunks[1].pos).normalized;
                plr.bodyChunks[0].vel += dir * 6;
                plr.bodyChunks[1].vel -= dir * 3;

                plr.AerobicIncrease(0.5f);
            }
        }

        private void LockInput(On.Player.orig_checkInput orig, Player self)
        {
            var dislodge = dislodgeData[self];

            if (!dislodge.IsActive()) {
                orig(self);
                return;
            }

            var tempStandStill = self.standStillOnMapButton;
            self.standStillOnMapButton = false;
            orig(self);
            self.standStillOnMapButton = tempStandStill;

            if (self.input[0].jmp) {
                self.animation = Flip;
                dislodge.Reset();
                return;
            }

            self.input[0] = default;
        }

        private bool StartDislodgingSpear(On.Creature.orig_Grab orig, Creature self, PhysicalObject obj, int graspUsed, int c, Creature.Grasp.Shareability sh, float d, bool oed, bool p)
        {
            if (obj is Spear s && s.mode == Weapon.Mode.StuckInWall) {
                if (self is not Player player || !Rules.CurrentRules.Dislodge) {
                    return false;
                }

                var dislodge = dislodgeData[player];

                if (!dislodge.IsActive()) {
                    dislodge.Spear = new(s);
                    dislodge.Time = DislodgeTime;
                    dislodge.Grasp = graspUsed;
                    player.animation = None;
                    return false;
                }
            }
            return orig(self, obj, graspUsed, c, sh, d, oed, p);
        }

        private void FixChangeMode(On.Spear.orig_ChangeMode orig, Spear spear, Weapon.Mode newMode)
        {
            if (newMode == Weapon.Mode.StuckInWall && spear.mode != newMode) {
                StoreBeamState();
            }
            else if (spear.mode == Weapon.Mode.StuckInWall && spear.mode != newMode) {
                UndoStuckInWall();
            }

            orig(spear, newMode);

            void StoreBeamState()
            {
                var roomData = Dislodge.roomData[spear.room];
                var wall = spear.stuckInWall!.Value;

                if (spear.throwDir.y != 0) {
                    for (int i = 0; i < 3; i++) {
                        Room.Tile tile = spear.room.GetTile(wall + new Vector2(0f, 20f * (i - 1)));

                        if (!roomData.Beams.ContainsKey(new(tile.X, tile.Y))) {
                            roomData.Beams[new(tile.X, tile.Y)] = new(tile.horizontalBeam, tile.verticalBeam);
                        }
                    }
                }
                else {
                    for (int i = 0; i < 3; i++) {
                        Room.Tile tile = spear.room.GetTile(wall + new Vector2(20f * (i - 1), 0f));

                        if (!roomData.Beams.ContainsKey(new(tile.X, tile.Y))) {
                            roomData.Beams[new(tile.X, tile.Y)] = new(tile.horizontalBeam, tile.verticalBeam);
                        }
                    }
                }
            }

            void UndoStuckInWall()
            {
                var roomData = Dislodge.roomData[spear.room];
                var wall = spear.stuckInWall!.Value;

                if (IsStuckVertically(spear)) {
                    for (int i = 0; i < 3; i++) {
                        Room.Tile tile = spear.room.GetTile(wall + new Vector2(0f, 20f * (i - 1)));

                        tile.verticalBeam = roomData.Beams[new(tile.X, tile.Y)].Vertical;
                    }
                }
                else {
                    for (int i = 0; i < 3; i++) {
                        Room.Tile tile = spear.room.GetTile(wall + new Vector2(20f * (i - 1), 0f));

                        tile.horizontalBeam = roomData.Beams[new(tile.X, tile.Y)].Horizontal;
                    }
                }

                spear.abstractSpear.stuckInWallCycles = 0;
            }
        }

        private bool FixPickup(On.Player.orig_CanIPickThisUp orig, Player self, PhysicalObject obj)
        {
            return !dislodgeData[self].IsActive() && (orig(self, obj) || obj is Spear s && CanDislodgeSpear(s));

            bool CanDislodgeSpear(Spear s)
            {
                if (!Rules.CurrentRules.Dislodge || self.bodyMode == Default || self.input[0].x != 0 || self.input[0].y != 0) {
                    return false;
                }

                if (self.bodyMode is Stand or Crawl && self.animation is not None) {
                    return false;
                }

                if (self.bodyMode == ClimbingOnBeam && self.animation is not HangFromBeam and not ClimbOnBeam) {
                    return false;
                }

                if (self.mainBodyChunk.vel.sqrMagnitude > 5f || self.mainBodyChunk.vel.y < -0.01f) {
                    return false;
                }

                // Spear must be in a wall and player must be within 30 units of it
                if (s.mode == Weapon.Mode.StuckInWall && (s.bodyChunks[0].pos - self.firstChunk.pos).magnitude < 25) {
                    // If climbing on a beam but not hanging on the spear, boot
                    if (self.animation == HangFromBeam) {
                        if (IsStuckVertically(s) || self.abstractPhysicalObject.pos.y != s.abstractPhysicalObject.pos.y) {
                            return false;
                        }
                    }

                    // If climbing on a beam but not climbing the spear, boot
                    // Also, can only do this with up-thrown spears
                    if (self.animation == ClimbOnBeam) {
                        if (!IsStuckVertically(s) || !s.IsTileSolid(0, 0, 1) || self.abstractPhysicalObject.pos.x != s.abstractPhysicalObject.pos.x) {
                            return false;
                        }
                    }

                    // Must have an open hand. This prevents dislodging spears directly into a backspear.
                    // Must also not be holding an incompatible item.
                    return self.grasps.Contains(null) && !self.grasps.Any(g => self.Grabability(g?.grabbed) > Player.ObjectGrabability.OneHand);
                }

                return false;
            }
        }
    }
}