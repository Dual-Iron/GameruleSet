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
        const int yank = 20;
        const int stunDuration = 10;
        const int maxTime = 40;

        struct DislodgeAnim : IWeakData<Player>
        {
            public WeakRef<Spear> spear;
            public float time;
            public int graspUsed;

            void IDisposable.Dispose() { }
            void IWeakData<Player>.Initialize(Player owner, object? state)
            {
                spear = new();
            }
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

        private bool IsOnWall(Player self) => self.bodyChunks[1].ContactPoint.y >= 0 && self.bodyMode != ClimbIntoShortCut && self.bodyMode != CorridorClimb;

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

            try
            {
                ref var data = ref self.player.Data().Get<DislodgeAnim>();
                if (self.player.animation == EnumExt_GameruleSet.PullingSpear && data.spear.TryGetTarget(out var spear))
                {
                    var pos = self.player.room.MiddleOfTile(spear.abstractSpear.pos);

                    if (data.time < maxTime - 4)
                    {
                        self.LookAtPoint(self.player.firstChunk.pos + (self.player.firstChunk.pos - pos), 20);
                        self.blink = 5;
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
            catch (Exception e)
            {
                rules.Logger.LogError(e);
            }
        }

        private void Player_UpdateAnimation(On.Player.orig_UpdateAnimation orig, Player self)
        {
            ref var data = ref self.Data().Get<DislodgeAnim>();
            if (!data.spear.TryGetTarget(out var spear))
            {
                if (self.animation == EnumExt_GameruleSet.PullingSpear)
                    self.animation = Player.AnimationIndex.None;
                data.time = 0;
                orig(self);
            }
            else
            {
                self.animation = EnumExt_GameruleSet.PullingSpear;

                orig(self);

                var horizontal = Mathf.Abs(spear.rotation.x) > 0.01f;
                var target = self.room.MiddleOfTile(spear.abstractPhysicalObject.pos);

                // If too far from any of the spear's body chunks, plop off
                if (spear.bodyChunks.Min(b => (b.pos - self.firstChunk.pos).magnitude) > 35 * 35)
                {
                    self.room.PlaySound(SoundID.Big_Needle_Worm_Impale_Terrain, spear.firstChunk.pos, 1.1f, 0.9f);
                    self.animation = Player.AnimationIndex.None;
                    self.bodyMode = Stunned;
                    data.time = float.NegativeInfinity;
                    self.Stun(80);
                    self.SlugcatGrab(spear, data.graspUsed);
                }
                else
                {
                    if (IsOnWall(self))
                    {
                        self.bodyMode = ClimbingOnBeam;
                        self.standing = true;
                        self.firstChunk.pos.y = self.room.MiddleOfTile(self.firstChunk.pos).y + 4;
                        self.firstChunk.vel = Vector2.zero;

                        self.bodyChunks[0].vel -= (target - self.bodyChunks[0].pos - new Vector2(5 * -spear.rotation.x, 10)).normalized * 1.5f;
                        self.bodyChunks[1].vel += (target - self.bodyChunks[1].pos).normalized * 2f;

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
                    else
                    {
                        self.bodyChunks[0].vel.x -= Math.Sign(target.x - self.bodyChunks[1].pos.x);
                        self.bodyChunks[1].vel.x += Math.Sign(target.x - self.bodyChunks[1].pos.x);
                    }
                }

                self.aerobicLevel = Math.Max(self.aerobicLevel, 1f / Math.Max(1, data.time + 1f));

                if (data.time > 0)
                {
                    float distance = Math.Abs(horizontal ? target.y - self.firstChunk.pos.y : target.x - self.firstChunk.pos.x);
                    float delta = -1f / Mathf.Clamp(distance / 5f, 1f, 2f);

                    if (self.bodyMode == ClimbIntoShortCut || self.bodyMode == CorridorClimb)
                    {
                        delta /= 2;
                    }

                    if (data.time > yank && data.time + delta <= yank)
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
                    }

                    data.time += delta;
                }
                // Ignore special case of float.NegativeInfinity
                else if (data.time > float.NegativeInfinity)
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
                }

                if (self.animation != EnumExt_GameruleSet.PullingSpear)
                {
                    data.spear = new();
                }
            }
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
                    data.spear = new();
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
            if (obj is Spear s && s.mode == Weapon.Mode.StuckInWall && s.bodyChunks.Min(b => (b.pos - self.firstChunk.pos).magnitude) < 25)
            {
                // If climbing on a beam but not hanging on the spear, boot
                if (self.bodyMode == ClimbingOnBeam && 
                    (self.animation != Player.AnimationIndex.HangFromBeam || self.room.GetTilePosition(self.firstChunk.pos).y != s.abstractPhysicalObject.pos.y))
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
