﻿using Mono.Cecil.Cil;
using MonoMod.Cil;
using WeakTables;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameruleSet
{
    public class Persistence
    {
        static readonly WeakTable<AbstractCreature, AbstractCreatureData> critData = new(_ => new());
        static readonly WeakTable<RainWorldGame, GameData> gameData = new(_ => new(), false);

        sealed class AbstractCreatureData
        {
            public const int defaultDayDead = int.MinValue;
            public int dayDead = defaultDayDead;
        }

        sealed class GameData
        {
            public HashSet<EntityID> dontSave = new();
        }

        private readonly Rules rules;

        public Persistence(Rules rules)
        {
            this.rules = rules;

            On.Room.ReadyForAI += Room_ReadyForAI;
            On.Room.Loaded += Room_Loaded;
            On.AbstractCreature.Die += AbstractCreature_Die;
            On.SaveState.AbstractCreatureFromString += SaveState_AbstractCreatureFromString;
            On.SaveState.AbstractCreatureToString_AbstractCreature_WorldCoordinate += SaveState_AbstractCreatureToString_AbstractCreature_WorldCoordinate;
            IL.RegionState.AdaptWorldToRegionState += RegionState_AdaptWorldToRegionState;
            On.RegionState.CreatureToStringInDenPos += RegionState_CreatureToStringInDenPos;
            On.RegionState.AdaptRegionStateToWorld += RegionState_AdaptRegionStateToWorld;
        }

        private void Room_ReadyForAI(On.Room.orig_ReadyForAI orig, Room self)
        {
            int startIndex = self.abstractRoom?.entities?.Count ?? 0;

            orig(self);

            if (self.abstractRoom?.world?.game?.session is StoryGameSession && self.abstractRoom.entities != null)
            {
                var data = gameData[self.abstractRoom.world.game];
                for (int i = startIndex; i < self.abstractRoom.entities.Count; i++)
                {
                    if (self.abstractRoom.entities[i] is AbstractPhysicalObject o)
                    {
                        data.dontSave.Add(o.ID);
                    }
                }
            }
        }

        private void Room_Loaded(On.Room.orig_Loaded orig, Room self)
        {
            int startIndex = self.abstractRoom.entities.Count;

            orig(self);

            if (self.abstractRoom.world.game.IsStorySession)
            {
                var data = gameData[self.abstractRoom.world.game];
                for (int i = startIndex; i < self.abstractRoom.entities.Count; i++)
                {
                    if (self.abstractRoom.entities[i] is AbstractPhysicalObject o && 
                        (o is not AbstractConsumable c || c.placedObjectIndex < 0) &&
                        (o is not DataPearl.AbstractDataPearl d || d.dataPearlType != DataPearl.AbstractDataPearl.DataPearlType.Red_stomach) &&
                        o.type != AbstractPhysicalObject.AbstractObjectType.NSHSwarmer)
                    {                        
                        data.dontSave.Add(o.ID);
                    }
                }
            }
        }

        private void AbstractCreature_Die(On.AbstractCreature.orig_Die orig, AbstractCreature self)
        {
            if (self.state.alive && self.world.game.session is StoryGameSession sess)
            {
                critData[self].dayDead = sess.saveState.cycleNumber;
            }
            orig(self);
        }

        private AbstractCreature SaveState_AbstractCreatureFromString(On.SaveState.orig_AbstractCreatureFromString orig, World world, string creatureString, bool onlyInCurrentRegion)
        {
            try
            {
                var critter = orig(world, creatureString, onlyInCurrentRegion);
                if (critter == null)
                    return null;

                var data = creatureString.Split(new[] { "<cPOS>" }, StringSplitOptions.None);
                if (data.Length >= 3 && rules.Persistence.Value != PersistenceEnum.None && 
                    int.TryParse(data[1], out int x) && int.TryParse(data[2], out int y))
                {
                    critter.pos.x = x;
                    critter.pos.y = y;
                }

                if (critter.state.alive)
                {
                    return critter;
                }

                data = creatureString.Split(new[] { "<cDEATH>" }, StringSplitOptions.None);
                if (data.Length >= 3 && rules.Persistence.Value != PersistenceEnum.None &&
                    int.TryParse(data[1], out int dayDead))
                {
                    critData[critter].dayDead = dayDead;
                }

                return critter;
            }
            catch (Exception e)
            {
                rules.Logger.LogError(creatureString + ": " + e);
                throw;
            }
        }

        private string SaveState_AbstractCreatureToString_AbstractCreature_WorldCoordinate(On.SaveState.orig_AbstractCreatureToString_AbstractCreature_WorldCoordinate orig, AbstractCreature critter, WorldCoordinate pos)
        {
            string ret = $"{orig(critter, pos)}<cC><cPOS>{pos.x}<cPOS>{pos.y}<cPOS>";

            var dayDead = critData[critter].dayDead;
            if (dayDead != AbstractCreatureData.defaultDayDead)
                ret += $"<cDEATH>{dayDead}<cDEATH>";

            return ret;
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
                    return c.pos.x >= 0 && c.pos.y >= 0;
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
                if (rules.Persistence.Value == PersistenceEnum.None && !rules.SaveShelterPositions)
                {
                    return orig(self, critter, validSaveShelter, activeGate);
                }

                var pos = GetPos(self, critter, validSaveShelter, activeGate);
                if (pos == null || !self.world.IsRoomInRegion(pos.Value.room))
                {
                    UnityEngine.Debug.Log($"[GameruleSet] Did not save creature. type: {critter.creatureTemplate.type}, id: {critter.ID}, state: {critter.state}, pos: {critter.pos}, w: {self.world.firstRoomIndex}-{self.world.firstRoomIndex + self.world.NumberOfRooms}");
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
                // Don't save creatures in gates or slugcats
                if (critter.creatureTemplate.type == CreatureTemplate.Type.Slugcat || critter.pos.room == activeGate)
                {
                    return null;
                }

                // If it's dead or unmoving and in a shelter, save it...
                if ((critter.state.dead || critter.creatureTemplate.offScreenSpeed == 0f) && self.world.GetAbstractRoom(critter.pos).shelter)
                {
                    return rules.SaveShelterPositions ? critter.pos : new(critter.pos.room, -1, -1, 0);
                }

                // It should return if dead, not eaten, not in a den, and less than 3 days old
                if (critter.state.dead && critter.state.meatLeft > 0 && !critter.InDen && !critter.Room.offScreenDen &&
                    critData[critter].dayDead + 3 > self.saveState.cycleNumber)
                {
                    // Check if persistence applies. If so, save it!
                    Room room = null;
                    RoomRain rain = null;
                    return PersistenceApplies(ref room, ref rain, self.world, critter.pos) ? critter.pos : critter.abstractAI?.denPosition;
                }

                // If it's alive in the slugcat's shelter, save it accordingly
                if (critter.pos.room == validSaveShelter)
                {
                    return rules.SaveShelterPositions ? critter.pos : new(critter.pos.room, -1, -1, 0);
                }

                // Alive somewhere other than player's den, so put it back at its own den.
                var pos = critter.abstractAI?.denPosition;
                if (pos != null && self.world.IsRoomInRegion(pos.Value.room))
                    return pos;

                // Make sure it's in the region.
                if (self.world.IsRoomInRegion(critter.spawnDen.room))
                    return critter.spawnDen;

                return null;
            }
        }

        private void RegionState_AdaptRegionStateToWorld(On.RegionState.orig_AdaptRegionStateToWorld orig, RegionState self, int playerShelter, int activeGate)
        {
            orig(self, playerShelter, activeGate);

            // Only worth checking if Dry or higher is chosen
            if (rules.Persistence == PersistenceEnum.None)
                return;

            var data = gameData[self.world.game];

            for (int i = 0; i < self.world.NumberOfRooms; i++)
            {
                var abstractRoom = self.world.GetAbstractRoom(self.world.firstRoomIndex + i);
                if (abstractRoom.shelter || abstractRoom.offScreenDen)
                    continue;

                // Cache these values to maximize lazy loading
                Room room = null;
                RoomRain rain = null;

                for (int j = 0; j < abstractRoom.entities.Count; j++)
                {
                    if (abstractRoom.entities[j] is not AbstractPhysicalObject o)
                        continue;

                    if (o.pos.x >= 0 && o.pos.x <= abstractRoom.size.x &&
                        o.pos.y >= 0 && o.pos.y <= abstractRoom.size.y &&
                        o.type != AbstractPhysicalObject.AbstractObjectType.Creature &&
                        o.type != AbstractPhysicalObject.AbstractObjectType.KarmaFlower &&
                        o.type != AbstractPhysicalObject.AbstractObjectType.Rock &&
                        (o is not AbstractSpear s || o.GetType() != typeof(AbstractSpear) || s.explosive || !s.stuckInWall && o.stuckObjects.Any(s => s != null)) &&
                        (o is not AbstractConsumable c || c.isConsumed && AbstractConsumable.IsTypeConsumable(o.type)) &&
                        !data.dontSave.Contains(o.ID) &&
                        PersistenceApplies(ref room, ref rain, self.world, o.pos))
                    {
                        self.savedObjects.Add(o.ToString());
                    }

                    foreach (var stick in o.stuckObjects)
                    {
                        if (stick.A == o)
                        {
                            self.savedSticks.Add(stick.SaveToString(abstractRoom.index));
                        }
                    }
                }
            }
        }

        public static bool PersistenceApplies(ref Room room, ref RoomRain roomRain, World world, WorldCoordinate coord)
        {
            var rules = Rules.CurrentRules;

            if (rules == null || rules.Persistence.Value == PersistenceEnum.None)
                return false;

            // Don't need to calculate anything if it's All
            if (rules.Persistence.Value == PersistenceEnum.All)
                return true;

            room ??= new Room(world.game, world, world.GetAbstractRoom(coord));

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
    }
}
