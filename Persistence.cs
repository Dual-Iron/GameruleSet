using RWCustom;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
                        room ??= new Room(self.world.game, self.world, abstractRoom);

                        bool cont = false;
                        foreach (var placedObject in room.roomSettings.placedObjects)
                        {
                            if (placedObject.type == PlacedObject.Type.ScavengerOutpost || placedObject.type == PlacedObject.Type.ScavengerTreasury)
                            {
                                cont = true;
                                break;
                            }
                        }

                        if (cont)
                            continue;

                        // Don't need to calculate anything if it's All
                        if (rules.Persistence.Value == PersistenceEnum.All)
                        {
                            self.savedObjects.Add(o.ToString());
                        }
                        else
                        {
                            // If (dry) or (wet, and wet rule is enabled), don't calculate anything further
                            if (room.roomSettings.DangerType == RoomRain.DangerType.None || rules.Persistence.Value == PersistenceEnum.Wet && room.roomSettings.DangerType == RoomRain.DangerType.Flood)
                            {
                                self.savedObjects.Add(o.ToString());
                            }
                            else
                            {
                                // Check if rain reaches position and save if it doesn't
                                if (rain == null)
                                {
                                    room.LoadFromDataString(File.ReadAllLines(WorldLoader.FindRoomFileDirectory(room.abstractRoom.name, false) + ".txt"));
                                    rain = new(self.world.game.globalRain, room);
                                }

                                var isInRain = rain.rainReach[o.pos.Tile.x] <= o.pos.Tile.y;
                                if (!isInRain)
                                    self.savedObjects.Add(o.ToString());
                            }
                        }
                    }
                }
            }
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
