using RWCustom;
using System;
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
            for (int i = 0; i < self.world.NumberOfRooms; i++)
            {
                AbstractRoom abstractRoom = self.world.GetAbstractRoom(self.world.firstRoomIndex + i);
                for (int j = 0; j < abstractRoom.entities.Count; j++)
                {
                    if (abstractRoom.entities[j] is AbstractPhysicalObject o && o.type != AbstractPhysicalObject.AbstractObjectType.Creature && o.type != AbstractPhysicalObject.AbstractObjectType.KarmaFlower && o.type != AbstractPhysicalObject.AbstractObjectType.Rock && o.type != AbstractPhysicalObject.AbstractObjectType.Spear && !abstractRoom.shelter)
                    {
                        self.savedObjects.Add(abstractRoom.entities[j].ToString());
                    }
                }
            }
        }

        private void Spear_ChangeMode(On.Spear.orig_ChangeMode orig, Spear self, Weapon.Mode newMode)
        {
            orig(self, newMode);
            if (rules.SpearPersist.Value && newMode == Weapon.Mode.StuckInWall)
            {
                if (self.abstractSpear.stuckInWallCycles > 0)
                    self.abstractSpear.stuckInWallCycles = int.MaxValue;
                else
                    self.abstractSpear.stuckInWallCycles = int.MinValue;
            }
        }
    }
}
