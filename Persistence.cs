using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.IO;
using UnityEngine;

namespace GameruleSet
{
    public class Persistence
    {
        private readonly Rules rules;

        public Persistence(Rules rules)
        {
            this.rules = rules;

            IL.AbstractCreature.RealizeInRoom += AbstractCreature_RealizeInRoom;
            IL.RegionState.AdaptWorldToRegionState += RegionState_AdaptWorldToRegionState;
            On.RegionState.CreatureToStringInDenPos += RegionState_CreatureToStringInDenPos;

            On.Spear.ChangeMode += Spear_ChangeMode;
            On.RegionState.AdaptRegionStateToWorld += RegionState_AdaptRegionStateToWorld;
        }

        private void AbstractCreature_RealizeInRoom(ILContext il)
        {
            try
            {
                var cursor = new ILCursor(il);

                // Find end of shelter check
                if (!cursor.TryGotoNext(i => i.MatchCall<WorldCoordinate>("get_NodeDefined")))
                {
                    rules.Logger.LogError("RealizeInRoom: Missing instruction");
                    return;
                }

                var brTo = cursor.Instrs[cursor.Index - 2];

                // Go back to start of method
                cursor.Index = 0;

                // Emit skip
                cursor.EmitDelegate<Func<bool>>(SkipVanillaCheck);
                cursor.Emit(OpCodes.Brtrue, brTo);

                static bool SkipVanillaCheck()
                {
                    return Rules.CurrentRules?.SaveShelterPositions?.Value ?? false;
                }
            }
            catch (Exception e)
            {
                rules.Logger.LogError(e);
            }
        }

        private void RegionState_AdaptWorldToRegionState(ILContext il)
        {
            try
            {
                var cursor = new ILCursor(il);
                if (!cursor.TryGotoNext(i => i.MatchCallvirt<AbstractRoom>("MoveEntityToDen")))
                {
                    rules.Logger.LogError("AdaptWorldToRegionState: Missing instruction 1");
                    return;
                }

                if (!cursor.TryGotoPrev(i => i.MatchCallvirt<AbstractRoom>("get_shelter")))
                {
                    rules.Logger.LogError("AdaptWorldToRegionState: Missing instruction 2");
                    return;
                }

                cursor.Index++;

                // Emit skip
                cursor.Emit(OpCodes.Ldloc_3);
                cursor.EmitDelegate<Func<AbstractCreature, bool>>(CanLoadPosition);
                cursor.Emit(OpCodes.Or);

                static bool CanLoadPosition(AbstractCreature c)
                {
                    if (c.creatureTemplate.type == CreatureTemplate.Type.PoleMimic || c.creatureTemplate.type == CreatureTemplate.Type.TentaclePlant)
                    {
                        return false;
                    }
                    if (c.InDen || c.Room.offScreenDen || c.pos.x == -1 || c.pos.y == -1)
                    {
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception e)
            {
                rules.Logger.LogError(e);
            }
        }

        private string RegionState_CreatureToStringInDenPos(On.RegionState.orig_CreatureToStringInDenPos orig, RegionState self, AbstractCreature critter, int validSaveShelter, int activeGate)
        {
            try
            {
                var pos = GetPos(self, critter, validSaveShelter, activeGate);
                if (pos == null || !self.world.IsRoomInRegion(pos.Value.room))
                {
                    return string.Empty;
                }
                return SaveState.AbstractCreatureToString(critter, pos.Value);
            }
            catch (Exception e)
            {
                rules.Logger.LogMessage($"Did not save creature {critter.creatureTemplate.name} {critter.ID} because: {e}");
                return string.Empty;
            }

            WorldCoordinate? GetPos(RegionState self, AbstractCreature critter, int validSaveShelter, int activeGate)
            {
                if (critter.creatureTemplate.type == CreatureTemplate.Type.Slugcat || critter.pos.room == activeGate)
                {
                    return null;
                }
                if (critter.state.dead || critter.creatureTemplate.offScreenSpeed == 0f)
                {
                    if (self.world.GetAbstractRoom(critter.pos).shelter)
                    {
                        return rules.SaveShelterPositions ? critter.pos : new(critter.pos.room, -1, -1, 0);
                    }
                    Room? room = null;
                    RoomRain? rain = null;
                    return PersistenceApplies(ref room, ref rain, self.world, critter.pos) ? critter.pos : null;
                }
                if (critter.pos.room == validSaveShelter)
                {
                    return rules.SaveShelterPositions ? critter.pos : new(critter.pos.room, -1, -1, 0);
                }
                return critter.abstractAI?.denPosition;
            }
        }

        private void RegionState_AdaptRegionStateToWorld(On.RegionState.orig_AdaptRegionStateToWorld orig, RegionState self, int playerShelter, int activeGate)
        {
            orig(self, playerShelter, activeGate);

            // Only worth checking if Dry or higher is chosen
            if (rules.Persistence == PersistenceEnum.None)
                return;

            for (int i = 0; i < self.world.NumberOfRooms; i++)
            {
                var abstractRoom = self.world.GetAbstractRoom(self.world.firstRoomIndex + i);
                if (abstractRoom.shelter)
                    continue;

                // Cache these values to maximize lazy loading
                Room? room = null;
                RoomRain? rain = null;

                for (int j = 0; j < abstractRoom.entities.Count; j++)
                {
                    if (abstractRoom.entities[j] is AbstractPhysicalObject o && o.type != AbstractPhysicalObject.AbstractObjectType.Creature && o.type != AbstractPhysicalObject.AbstractObjectType.KarmaFlower && o.type != AbstractPhysicalObject.AbstractObjectType.Rock && (o.type != AbstractPhysicalObject.AbstractObjectType.Spear || o is AbstractSpear s && s.explosive) && (o is not AbstractConsumable c || c.isConsumed))
                    {
                        if (PersistenceApplies(ref room, ref rain, self.world, o.pos))
                        {
                            self.savedObjects.Add(o.ToString());
                        }
                    }
                }
            }
        }

        public static bool PersistenceApplies(ref Room? room, ref RoomRain? roomRain, World world, WorldCoordinate coord)
        {
            var rules = Rules.CurrentRules;

            if (rules == null || rules.Persistence.Value == PersistenceEnum.None)
                return false;

            room ??= new Room(world.game, world, world.GetAbstractRoom(coord));

            // Forbid scav outputs/treasuries; they generate items
            foreach (var placedObject in room.roomSettings.placedObjects)
            {
                if (placedObject.type == PlacedObject.Type.ScavengerOutpost || placedObject.type == PlacedObject.Type.ScavengerTreasury)
                {
                    return false;
                }
            }

            // Don't need to calculate anything if it's All
            if (rules.Persistence.Value == PersistenceEnum.All)
                return true;

            // If dry, we're good
            if (room.roomSettings.DangerType == RoomRain.DangerType.None)
                return true;

            // If not, and the rule is set to dry, abandon ship
            if (rules.Persistence.Value == PersistenceEnum.Dry)
                return false;

            // Enum must == Wet. Check if rain reaches position and save if it doesn't
            if (roomRain == null)
            {
                room.LoadFromDataString(File.ReadAllLines(WorldLoader.FindRoomFileDirectory(room.abstractRoom.name, false) + ".txt"));
                roomRain = new(world.game.globalRain, room);
            }

            return roomRain.rainReach[coord.Tile.x] > coord.Tile.y;
        }

        private void Spear_ChangeMode(On.Spear.orig_ChangeMode orig, Spear self, Weapon.Mode newMode)
        {
            orig(self, newMode);
            if (rules.StableSpears.Value && newMode == Weapon.Mode.StuckInWall)
            {
                if (self.abstractSpear.stuckInWallCycles >= 0)
                    self.abstractSpear.stuckInWallCycles = int.MaxValue;
                else
                    self.abstractSpear.stuckInWallCycles = int.MinValue;
            }
        }
    }
}
