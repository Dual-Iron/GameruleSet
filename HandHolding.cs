using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Noise;
using RWCustom;
using StaticTables;
using System;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace GameruleSet
{
    public class HandHolding
    {
        struct GraspData : IWeakData<Creature.Grasp>
        {
            public Vector2 handPos;
            public Vector2 prevOffset;
            public float lax;

            void IWeakData<Creature.Grasp>.Construct(Creature.Grasp key) { }
            void IWeakData<Creature.Grasp>.Destruct() { }
        }

        private static bool CanHoldHand(PhysicalObject? o)
        {
            return Rules.CurrentRules!.HandHolding && 
                (o is Oracle || o is Player p && p.State.alive || o is Scavenger s && s.State.alive);
        }

        private static bool WannaHoldHands(Player player)
        {
            return player.input[0].pckp && player.input[0].mp;
        }

        private static bool RefuseHandHolding(ScavengerAI self, Player p)
        {
            return self.scared > 0.95f && self.threatTracker.Utility() > 0.95f || self.CurrentPlayerAggression(p.abstractCreature) > 0.05f;
        }

        private bool ignoreGrabGuard;
        private bool heavyCarryTrue;
        private readonly Rules rules;

        public HandHolding(Rules rules)
        {
            new Hook(typeof(BodyChunk).GetMethod("get_goThroughFloors"), (Func<Func<BodyChunk, bool>, BodyChunk, bool>)get_goThroughFloors)
                .Apply();

            static bool get_goThroughFloors(Func<BodyChunk, bool> orig, BodyChunk self)
            {
                return orig(self) && (self.privGoThroughFloors || self.owner.grabbedBy.Any(g => !CanHoldHand(g?.grabbed)));
            }

            IL.Scavenger.MidRangeUpdate += Scavenger_MidRangeUpdate;
            On.Weapon.HitThisObject += Weapon_HitThisObject;
            On.Creature.LoseAllGrasps += Creature_LoseAllGrasps;
            On.Scavenger.Update += Scavenger_Update;
            On.ScavengerAI.Update += ScavengerAI_Update;
            On.ScavengerGraphics.ScavengerHand.Update += ScavengerHand_Update;
            On.Creature.Grasp.Release += Grasp_Release;
            On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;
            On.SlugcatHand.Update += SlugcatHand_Update;
            On.SocialEventRecognizer.WeaponAttack += SocialEventRecognizer_WeaponAttack;
            On.SocialEventRecognizer.CreaturePutItemOnGround += SocialEventRecognizer_CreaturePutItemOnGround;
            On.PlayerGraphics.PlayerObjectLooker.HowInterestingIsThisObject += PlayerObjectLooker_HowInterestingIsThisObject;
            On.Player.GrabUpdate += Player_GrabUpdate;
            On.Player.CanIPickThisUp += Player_CanIPickThisUp;
            On.Player.IsObjectThrowable += Player_IsObjectThrowable;
            On.Player.SlugcatGrab += Player_SlugcatGrab;
            On.Creature.Grab += Creature_Grab;
            On.Player.TossObject += Player_TossObject;
            On.Player.GraphicsModuleUpdated += Player_GraphicsModuleUpdated;
            On.Player.HeavyCarry += Player_HeavyCarry;
            On.Player.Grabability += Player_Grabability;
            this.rules = rules;
        }

        private void Creature_LoseAllGrasps(On.Creature.orig_LoseAllGrasps orig, Creature self)
        {
            orig(self);

            if (CanHoldHand(self) && self is not Player)
                for (int i = self.grabbedBy.Count - 1; i >= 0; i--)
                    if (CanHoldHand(self.grabbedBy[i].grabber))
                        self.grabbedBy[i].Release();
        }

        private void Scavenger_MidRangeUpdate(ILContext il)
        {
            var cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(i => i.MatchCallvirt<Tracker>("GetRep")))
            {
                rules.Logger.LogError("Midrangeupdate missing instr 1");
                return;
            }

            if (!cursor.TryGotoNext(i => i.MatchLdarg(0)))
            {
                rules.Logger.LogError("Midrangeupdate missing instr 2");
                return;
            }

            var jumpTo = (cursor.Prev.Operand as ILLabel)?.Target;
            if (jumpTo == null)
            {
                rules.Logger.LogError("Prev instr is not label");
                return;
            }

            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldloc_S, il.Body.Variables[6]);
            cursor.EmitDelegate<Func<Scavenger, int, bool>>(OverlookTarget);
            cursor.Emit(OpCodes.Brtrue, jumpTo);

            static bool OverlookTarget(Scavenger self, int j)
            {
                return self.AI.tracker.GetRep(j).representedCreature.realizedCreature is Player p && self.grabbedBy.Any(g => g.grabber == p);
            }
        }

        private bool Weapon_HitThisObject(On.Weapon.orig_HitThisObject orig, Weapon self, PhysicalObject obj)
        {
            return orig(self, obj) && (self.thrownBy is not Scavenger s || obj is not Player || !s.grabbedBy.Any(g => g.grabber == obj));
        }

        private void Player_GrabUpdate(On.Player.orig_GrabUpdate orig, Player self, bool eu)
        {
            if (WannaHoldHands(self))
            {
                self.dontGrabStuff = 5;
                orig(self, eu);
                var pickup = self.PickupCandidate(0);
                if (pickup != null)
                    for (int i = 0; i < 2; i++)
                        if (self.grasps[i] == null)
                        {
                            self.room.PlaySound(SoundID.Slugcat_Pick_Up_Creature, pickup.firstChunk, false, 1f, 1f);
                            self.SlugcatGrab(pickup, i);
                            if (self.Grabability(pickup) < (Player.ObjectGrabability)5)
                            {
                                pickup.graphicsModule?.BringSpritesToFront();
                            }
                            break;
                        }
                self.pickUpCandidate = pickup;
            }
            else
                orig(self, eu);
        }

        private bool Player_CanIPickThisUp(On.Player.orig_CanIPickThisUp orig, Player self, PhysicalObject obj)
        {
            if (!orig(self, obj))
                return false;

            if (WannaHoldHands(self) && !CanHoldHand(obj))
                return false;

            // Don't grab objects that are being held by the person you're holding
            if (!ignoreGrabGuard)
                foreach (var g1 in obj.grabbedBy)
                {
                    if (CanHoldHand(g1.grabber) && self.grasps.Any(g => g?.grabbed == g1.grabber))
                        return false;
                }

            return true;
        }

        private float PlayerObjectLooker_HowInterestingIsThisObject(On.PlayerGraphics.PlayerObjectLooker.orig_HowInterestingIsThisObject orig, object self, PhysicalObject obj)
        {
            if (self is PlayerGraphics.PlayerObjectLooker l && WannaHoldHands(l.owner.player))
            {
                var p = l.owner.player;
                if (CanHoldHand(obj) && p.CanIPickThisUp(obj) && obj.room == p.room && obj.room.VisualContact(obj.firstChunk.pos, p.firstChunk.pos))
                {
                    var dist = Vector2.Distance(p.firstChunk.pos, obj.firstChunk.pos);
                    if (dist < 200)
                        return 1000 - dist;
                }
            }
            return orig(self, obj);
        }

        private void Scavenger_Update(On.Scavenger.orig_Update orig, Scavenger self, bool eu)
        {
            orig(self, eu);

            foreach (var grasp in self.grabbedBy)
            {
                if (CanHoldHand(grasp.grabber))
                {
                    self.grabbedAttackCounter = 0;
                    self.swingingForbidden = 10;
                    break;
                }
            }
        }

        private void ScavengerAI_Update(On.ScavengerAI.orig_Update orig, ScavengerAI self)
        {
            orig(self);

            for (int i = self.scavenger.grabbedBy.Count - 1; i >= 0; i--)
            {
                var grasp = self.scavenger.grabbedBy[i];
                if (CanHoldHand(grasp.grabber) && grasp.grabber is Player p)
                {
                    if (RefuseHandHolding(self, p))
                    {
                        grasp.Release();
                        continue;
                    }

                    const float speed = 0.001f;
                    const float calmSpeed = 0.05f;

                    var rel = self.creature.state.socialMemory.GetOrInitiateRelationship(p.abstractCreature.ID);
                    rel.InfluenceKnow(speed);

                    if (rel.tempLike < 0.5f)
                    {
                        rel.InfluenceTempLike(speed * 0.75f * self.creature.personality.sympathy);
                        rel.tempLike = Mathf.Min(rel.tempLike, 0.5f);
                    }

                    if (rel.like < 0.25f)
                    {
                        rel.InfluenceLike(speed * 0.25f * self.creature.personality.sympathy);
                        rel.like = Mathf.Min(rel.like, 0.25f);
                    }

                    self.agitation = Mathf.Clamp01(Mathf.Lerp(self.agitation, 0, calmSpeed * rel.like));

                    if (rel.like > 0.2f && rel.tempLike > 0.45f && 
                        UnityEngine.Random.value < 1 / 80f && self.threatTracker.Utility() > 1f - self.creature.personality.sympathy)
                    {
                        TryGiveWeapon(self, p);
                    }

                    if (Vector2.Distance(self.scavenger.firstChunk.pos, p.firstChunk.pos) < 40)
                    {
                        continue;
                    }

                    var offset = 50 * new Vector2(p.input[0].x, p.input[0].y).normalized;
                    if (offset != default)
                        grasp.Data().Get<GraspData>().prevOffset = offset;
                    else
                        offset = grasp.Data().Get<GraspData>().prevOffset;

                    var tileStanding = p.room.GetTile(p.bodyChunks[1].pos + p.bodyChunks[1].ContactPoint.ToVector2() * 20);

                    var pos = tileStanding.Solid || tileStanding.AnyBeam ? p.bodyChunks[1].pos : p.bodyChunks[0].pos;
                    var tileOffsetPos = SharedPhysics.RayTraceTilesForTerrainReturnFirstSolid(p.room, pos, pos + offset);
                    if (tileOffsetPos != null && !self.pathFinder.CoordinateReachable(p.room.GetWorldCoordinate(tileOffsetPos.Value)))
                        tileOffsetPos = p.room.GetTilePosition(pos);
                    var tilePos = tileOffsetPos ?? p.room.GetTilePosition(pos + offset);

                    var coord = new WorldCoordinate(p.room.abstractRoom.index, tilePos.x, tilePos.y, -1);

                    self.pathFinder.nextDestination = coord;
                    self.creature.abstractAI.SetDestination(coord);
                    self.runSpeedGoal = 1f;
                }
            }
        }

        private void TryGiveWeapon(ScavengerAI self, Player p)
        {
            var freeHand = p.FreeHand();
            if (freeHand == -1)
                return;

            var weapons = 0;
            foreach (var grasp in self.scavenger.grasps)
            {
                if (grasp?.grabbed != null && self.RealWeapon(grasp.grabbed))
                {
                    if (++weapons > 1)
                        break;
                }
            }

            int giveGrasp = -1;
            int minScore = int.MaxValue;

            for (int i = 0; i < self.scavenger.grasps.Length; i++)
            {
                var grasp = self.scavenger.grasps[i];
                if (grasp?.grabbed == null)
                    continue;

                if (weapons <= 1 && self.NeedAWeapon && self.RealWeapon(grasp.grabbed))
                    continue;

                ignoreGrabGuard = true;
                var canPickup = p.CanIPickThisUp(grasp.grabbed);
                ignoreGrabGuard = false;
                if (!canPickup)
                    continue;

                var score = self.WeaponScore(grasp.grabbed, false);
                if (score <= 1)
                    continue;

                if (minScore > score)
                {
                    minScore = score;
                    giveGrasp = i;
                }
            }

            if (giveGrasp != -1)
            {
                var obj = self.scavenger.grasps[giveGrasp].grabbed;
                self.scavenger.grasps[giveGrasp].Release();
                p.SlugcatGrab(obj, freeHand);
                p.room.PlaySound(SoundID.Slugcat_Pick_Up_Spear, p.firstChunk);
            }
        }

        private void ScavengerHand_Update(On.ScavengerGraphics.ScavengerHand.orig_Update orig, ScavengerGraphics.ScavengerHand self)
        {
            orig(self);

            if (self.limbNumber == 1)
                foreach (var grasp in self.owner.owner.grabbedBy)
                {
                    if (CanHoldHand(grasp.grabber) && grasp.grabber.graphicsModule is PlayerGraphics g)
                    {
                        self.spearPosAdd = default;
                        self.mode = Limb.Mode.HuntAbsolutePosition;
                        self.absoluteHuntPos = g.hands[grasp.graspUsed].pos;
                        self.huntSpeed = 100;
                        self.quickness = 1;
                        break;
                    }
                }
        }

        private void Grasp_Release(On.Creature.Grasp.orig_Release orig, Creature.Grasp self)
        {
            var grabber = self.grabber;
            var grabbed = self.grabbed;
            orig(self);
            if (grabber is Player && grabbed is Creature other)
                foreach (var otherGrasp in other.grasps)
                    if (otherGrasp?.grabbed == grabber)
                        otherGrasp.Release();
        }

        private void PlayerGraphics_DrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            orig(self, sLeaser, rCam, timeStacker, camPos);

            for (int i = 0; i < 2; i++)
            {
                if (CanHoldHand(self.player.grasps[i]?.grabbed))
                {
                    var otherPos = self.player.grasps[i].grabbedChunk.pos;
                    var xDist = otherPos.x - self.player.firstChunk.pos.x;
                    if (Mathf.Abs(xDist) > 5)
                        sLeaser.sprites[5 + i].scaleY = xDist < 0 ? -1 : 1;
                    else
                        sLeaser.sprites[5 + i].scaleY = otherPos.y > self.player.firstChunk.pos.y ? -1 : 1;
                }
            }
        }

        private void SocialEventRecognizer_WeaponAttack(On.SocialEventRecognizer.orig_WeaponAttack orig, SocialEventRecognizer self, PhysicalObject weapon, Creature thrower, Creature victim, bool hit)
        {
            if (victim is Scavenger s && thrower is Player p && s.grabbedBy.Any(g => g.grabber == p))
            {
                return;
            }
            orig(self, weapon, thrower, victim, hit);
        }

        private void SocialEventRecognizer_CreaturePutItemOnGround(On.SocialEventRecognizer.orig_CreaturePutItemOnGround orig, SocialEventRecognizer self, PhysicalObject item, Creature creature)
        {
            if (!CanHoldHand(item))
                orig(self, item, creature);
        }

        private void SlugcatHand_Update(On.SlugcatHand.orig_Update orig, SlugcatHand self)
        {
            orig(self);

            if (self.owner.owner is Player player)
            {
                if (self.limbNumber == player.FreeHand() && self.owner is PlayerGraphics g && WannaHoldHands(player) && CanHoldHand(g.objectLooker.currentMostInteresting))
                {
                    self.mode = Limb.Mode.HuntRelativePosition;
                    self.retractCounter = 0;
                    self.huntSpeed = 5;
                    self.quickness = 1;
                    self.relativeHuntPos = g.lookDirection * Mathf.Min(20, (self.pos - g.objectLooker.currentMostInteresting.firstChunk.pos).magnitude / 2);
                }
                else if (CanHoldHand(player.grasps[self.limbNumber]?.grabbed))
                {
                    var pos = player.grasps[self.limbNumber].Data().Get<GraspData>().handPos;
                    if (pos != default)
                    {
                        self.mode = Limb.Mode.HuntAbsolutePosition;
                        self.retractCounter = 0;
                        self.absoluteHuntPos = pos;
                        self.huntSpeed = 100f;
                        self.quickness = 1f;

                        if (player.grasps[self.limbNumber].grabbed is not Scavenger)
                            self.pos = self.absoluteHuntPos;
                    }
                }
            }
        }

        private bool Player_IsObjectThrowable(On.Player.orig_IsObjectThrowable orig, Player self, PhysicalObject obj)
        {
            return !CanHoldHand(obj) && orig(self, obj);
        }

        private void Player_SlugcatGrab(On.Player.orig_SlugcatGrab orig, Player self, PhysicalObject obj, int graspUsed)
        {
            if (CanHoldHand(obj) && obj is Player p)
            {
                int grasp = 1 - graspUsed;

                if (p.grasps[grasp] != null)
                    grasp = p.grasps.IndexOf(null);

                if (grasp < 0)
                    return;

                orig(self, obj, graspUsed);

                p.Grab(self, grasp, 0, Creature.Grasp.Shareability.NonExclusive, 0.5f, true, false);
            }
            else orig(self, obj, graspUsed);
        }

        private bool Creature_Grab(On.Creature.orig_Grab orig, Creature self, PhysicalObject obj, int graspUsed, int chunkGrabbed, Creature.Grasp.Shareability shareability, float dominance, bool overrideEquallyDominant, bool pacifying)
        {
            if (CanHoldHand(self) && CanHoldHand(obj))
            {
                chunkGrabbed = 0;
                pacifying = false;
                shareability = Creature.Grasp.Shareability.NonExclusive;
            }
            return orig(self, obj, graspUsed, chunkGrabbed, shareability, dominance, overrideEquallyDominant, pacifying);
        }

        private void Player_TossObject(On.Player.orig_TossObject orig, Player self, int grasp, bool eu)
        {
            heavyCarryTrue = true;
            try
            {
                orig(self, grasp, eu);
            }
            finally
            {
                heavyCarryTrue = false;
            }
        }

        private void Player_GraphicsModuleUpdated(On.Player.orig_GraphicsModuleUpdated orig, Player self, bool actuallyViewed, bool eu)
        {
            heavyCarryTrue = true;
            try
            {
                var swapHands = false;
                var grasps = new Creature.Grasp[self.grasps.Length];
                for (int i = 0; i < self.grasps.Length; i++)
                {
                    grasps[i] = self.grasps[i];
                    if (CanHoldHand(self.grasps[i]?.grabbed))
                    {
                        GraspUpdate(self, self.grasps[i], out swapHands);
                        self.grasps[i] = null;
                    }
                }

                orig(self, actuallyViewed, eu);

                grasps.CopyTo(self.grasps, 0);

                if (swapHands && self.grasps.Any(g => g?.grabbed != null && !CanHoldHand(g.grabbed)))
                {
                    self.SwitchGrasps(0, 1);
                }
            }
            finally
            {
                heavyCarryTrue = false;
            }
        }

        private void GraspUpdate(Player self, Creature.Grasp grasp, out bool swapHands)
        {
            self.switchHandsProcess = 1f;
            swapHands = false;

            BodyChunk otherChunk = grasp.grabbedChunk;

            Vector2 dir = (otherChunk.pos - self.mainBodyChunk.pos).normalized;
            float dist = (otherChunk.pos - self.mainBodyChunk.pos).magnitude;
            float edgeRad = grasp.grabbed is Scavenger ? 65 : 35;
            float asymmetry = self.enteringShortCut != null ? 0 : otherChunk.mass / (self.mainBodyChunk.mass + otherChunk.mass);

            if (dist > edgeRad)
            {
                self.mainBodyChunk.pos += dir * (dist - edgeRad) * asymmetry;
                self.mainBodyChunk.vel += dir * (dist - edgeRad) * asymmetry;
                otherChunk.pos -= dir * (dist - edgeRad) * (1f - asymmetry);
                otherChunk.vel -= dir * (dist - edgeRad) * (1f - asymmetry);
            }

            ref var data = ref grasp.Data().Get<GraspData>();

            var distY = Mathf.Abs(self.mainBodyChunk.pos.y - otherChunk.pos.y);
            if (distY < 1f && Mathf.Max(self.mainBodyChunk.vel.sqrMagnitude, otherChunk.vel.sqrMagnitude) < 3f)
            {
                data.lax += 0.03f * (1 - data.lax);
            }
            else
            {
                data.lax -= 0.05f;
            }
            data.lax = Mathf.Clamp01(data.lax);

            Vector2 midpoint = Vector2.Lerp(self.mainBodyChunk.pos, otherChunk.pos, 0.5f);

            data.handPos = midpoint - Vector2.up * data.lax * 12;

            var difference = otherChunk.pos.x - self.mainBodyChunk.pos.x;
            if ((difference < 0 ? 0 : 1) != grasp.graspUsed && Mathf.Abs(difference) > 15)
            {
                swapHands = true;
            }
        }

        private bool Player_HeavyCarry(On.Player.orig_HeavyCarry orig, Player self, PhysicalObject obj)
        {
            return CanHoldHand(obj) ? heavyCarryTrue : orig(self, obj);
        }

        private int Player_Grabability(On.Player.orig_Grabability orig, Player self, PhysicalObject obj)
        {
            if (self != obj && CanHoldHand(obj) && (obj is not Player p || p.grasps.Any(g => g == null)) && (obj is not Scavenger s || !RefuseHandHolding(s.AI, self)))
                return heavyCarryTrue ? (int)Player.ObjectGrabability.Drag : (int)Player.ObjectGrabability.OneHand;
            return orig(self, obj);
        }
    }
}