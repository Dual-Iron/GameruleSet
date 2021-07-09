using Mono.Cecil.Cil;
using MonoMod.Cil;
using RWCustom;
using StaticTables;
using System;
using System.Linq;
using UnityEngine;
using static Mono.Cecil.Cil.OpCodes;

namespace GameruleSet
{
    public partial class RideableLizards
    {
        private static bool IsRiding(Creature self, out RideStick stick)
        {
            stick = null!;
            foreach (var stuck in self.abstractCreature.stuckObjects)
            {
                if (stuck is RideStick s && s.Lizard.realizedObject is Lizard)
                {
                    stick = s;
                    return true;
                }
            }
            return false;
        }

        private static Lizard? GetPotentialRideable(Player self, PhysicalObject other)
        {
            return other is Lizard l && l.State.alive && l.AI.LikeOfPlayer(l.AI.tracker.RepresentationForCreature(self.abstractCreature, false)) >= 0.5f ? l : null;
        }

        private static void StopRiding(Player self, RideStick stick)
        {
            var l = (stick.Lizard.realizedObject as Lizard)!;

            foreach (var item in self.bodyChunks)
            {
                item.terrainSqueeze = 1f;
            }

            self.CollideWithObjects = true;
            self.wantToJump = 0;
            self.animation = Player.AnimationIndex.Flip;
            self.room.PlaySound(SoundID.Slugcat_Flip_Jump, self.bodyChunks[1]);
            stick.Deactivate();

            self.graphicsModule.ReleaseSpecificInternallyContainedObjectSprites(l);
        }

        private static void StartRiding(Player p, Lizard l)
        {
            new RideStick(p.abstractCreature, l.abstractCreature);
            p.animation = EnumExt_GameruleSet.RidingLizard;
            p.wantToPickUp = 0;
            p.dontGrabStuff = 5;
            p.noGrabCounter = 5;

            p.graphicsModule.AddObjectToInternalContainer(l.graphicsModule, 0);
        }

        private readonly Rules rules;

        public RideableLizards(Rules rules)
        {
            this.rules = rules;

            On.LizardAI.Update += LizardAI_Update;
            On.Lizard.Bite += Lizard_Bite;
            On.Lizard.AttemptBite += Lizard_AttemptBite;
            On.Weapon.HitThisObject += Weapon_HitThisObject;
            On.Player.Update += Player_Update;
            On.Player.UpdateAnimation += Player_UpdateAnimation;
            On.Player.Collide += Player_Collide;

            GraphicalHooks();
        }

        private void LizardAI_Update(On.LizardAI.orig_Update orig, LizardAI self)
        {
            orig(self);

            if (IsRiding(self.lizard, out var stick) && stick.Player.realizedObject is Player p)
            {
                var like = self.LikeOfPlayer(self.tracker.RepresentationForCreature(p.abstractCreature, false));
                if (self.utilityComparer.highestUtilityTracker.module == self.friendTracker ||
                    self.utilityComparer.highestUtilityTracker.module.Utility() < like)
                {
                    var rIndex = self.lizard.room.abstractRoom.index;
                    var rPos = self.lizard.room.MiddleOfTile(self.creature.pos);

                    var dir = p.input[0].analogueDir != default ? p.input[0].analogueDir : new(p.input[0].x, p.input[0].y);

                    var huntPos = rPos + 100 * dir;

                    var tilePos = SharedPhysics.RayTraceTilesForTerrainReturnFirstSolid(p.room, rPos, huntPos);
                    if (tilePos == null || 
                        !self.pathFinder.CoordinateReachable(self.pathFinder.FindReachableNeighbourIfPossible(new(rIndex, tilePos.Value.x, tilePos.Value.y, -1))))
                    {
                        tilePos = null;
                        var tilePosTop = p.room.GetTilePosition(huntPos);
                        for (int y = tilePosTop.y; y > 0; y--)
                        {
                            var coord = self.pathFinder.FindReachableNeighbourIfPossible(new(rIndex, tilePosTop.x, y, -1));
                            if (self.pathFinder.CoordinateReachable(coord))
                            {
                                tilePos = new(tilePosTop.x, y);
                                break;
                            }
                        }
                    }

                    if (tilePos != null)
                    {
                        stick.lastSuccess = tilePos.Value;
                    }
                    else if (stick.lastSuccess != default)
                    {
                        tilePos = stick.lastSuccess;
                    }

                    if (tilePos != null)
                    {
                        var coord = new WorldCoordinate(rIndex, tilePos.Value.x, tilePos.Value.y, -1);
                        self.pathFinder.nextDestination = coord;
                        self.creature.abstractAI.SetDestination(coord);
                        self.runSpeed = Mathf.Clamp01(self.runSpeed + like);
                    }
                }
                else
                {
                    stick.lastSuccess = default;
                }
            }
        }

        private bool Weapon_HitThisObject(On.Weapon.orig_HitThisObject orig, Weapon self, PhysicalObject obj)
        {
            return orig(self, obj) && (self.thrownBy is not Player p || !IsRiding(p, out var stick) || stick.Lizard != obj.abstractPhysicalObject);
        }

        private void Lizard_Bite(On.Lizard.orig_Bite orig, Lizard self, BodyChunk chunk)
        {
            if (chunk.owner is Player p && IsRiding(p, out var stick))
                StopRiding(p, stick);
            orig(self, chunk);
        }

        private void Lizard_AttemptBite(On.Lizard.orig_AttemptBite orig, Lizard self, Creature creature)
        {
            if (creature is Player p && IsRiding(p, out var stick) && stick.Lizard == self.abstractCreature)
            {
                if (self.AI.casualAggressionTarget.representedCreature == p.abstractCreature)
                {
                    self.AI.casualAggressionTarget = null;
                }
                return;
            }
            orig(self, creature);
        }

        private void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);

            if (IsRiding(self, out var stick) && (!self.Consious || self.grabbedBy.Count > 0))
            {
                StopRiding(self, stick);
            }
        }

        private void Player_UpdateAnimation(On.Player.orig_UpdateAnimation orig, Player self)
        {
            if (IsRiding(self, out var stick) && stick.Lizard.realizedObject is Lizard l)
            {
                self.CollideWithObjects = false;
                if (self.input[0].jmp)
                {
                    StopRiding(self, stick);
                }
                else
                {
                    foreach (var item in self.bodyChunks)
                    {
                        item.terrainSqueeze = 0.05f;
                    }

                    self.standing = false;
                    self.animation = EnumExt_GameruleSet.RidingLizard;
                    self.bodyMode = Player.BodyModeIndex.Stunned;
                    self.noGrabCounter = 5;

                    var standChunk = l.bodyChunks[0];
                    var sitChunk = l.bodyChunks[1];

                    var horizontal = Mathf.Abs(Vector2.Dot((standChunk.pos - sitChunk.pos).normalized, Vector2.right));
                    var sitPos = sitChunk.pos + Vector2.up * (sitChunk.rad + self.bodyChunks[1].rad) * horizontal;
                    var asymmetry = Mathf.Lerp(sitChunk.mass / (self.bodyChunks[1].mass + sitChunk.mass), 1, 0.9f);

                    float threshold = 2;

                    var toSitPos = sitPos - self.bodyChunks[1].pos;
                    if (toSitPos.sqrMagnitude > threshold * threshold)
                    {
                        var toSitPosDir = toSitPos.normalized;
                        self.bodyChunks[1].pos += toSitPosDir * (toSitPos.magnitude - threshold) * asymmetry;
                        self.bodyChunks[1].vel += toSitPosDir * (toSitPos.magnitude - threshold) * asymmetry;
                        sitChunk.pos -= toSitPosDir * (toSitPos.magnitude - threshold) * (1 - asymmetry);
                        sitChunk.vel -= toSitPosDir * (toSitPos.magnitude - threshold) * (1 - asymmetry);
                    }

                    var standDir = (standChunk.pos - sitChunk.pos).normalized;
                    standDir = Custom.RotateAroundOrigo(standDir, (standDir.x < 0 ? 70 : -70) * horizontal);

                    self.bodyChunks[0].vel *= 0.9f;
                    self.bodyChunks[1].vel *= 0.9f;
                    self.bodyChunks[0].vel += standDir * 10;
                    self.bodyChunks[1].vel -= standDir * 10;
                }
            }
            orig(self);
        }

        private void Player_Collide(On.Player.orig_Collide orig, Player self, PhysicalObject otherObject, int myChunk, int otherChunk)
        {
            if (self.Consious && self.wantToPickUp > 0 && GetPotentialRideable(self, otherObject) is Lizard l && !IsRiding(self, out _) && self.standing)
            {
                if (!l.abstractCreature.stuckObjects.Any(o => o is RideStick))
                    StartRiding(self, l);
            }

            orig(self, otherObject, myChunk, otherChunk);
        }
    }

    public class RideStick : AbstractPhysicalObject.AbstractObjectStick
    {
        public RideStick(AbstractCreature player, AbstractCreature lizard) : base(player, lizard)
        {
        }

        public AbstractPhysicalObject Player => A;
        public AbstractPhysicalObject Lizard => B;

        public IntVector2 lastSuccess;
    }
}