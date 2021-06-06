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

            On.Spear.ChangeMode += Spear_ChangeMode;
            On.RegionState.AdaptRegionStateToWorld += RegionState_AdaptRegionStateToWorld;
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

        public bool PersistenceApplies(ref Room? room, ref RoomRain? roomRain, World world, WorldCoordinate coord)
        {
            if (rules.Persistence.Value == PersistenceEnum.None)
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

            // Rule must be wet. Check if rain reaches position and save if it doesn't
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
