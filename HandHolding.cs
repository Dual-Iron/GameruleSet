using RWCustom;

namespace GameruleSet
{
    public class HandHolding
    {
        private static bool CanHoldHand(PhysicalObject? o) => o is Player || o is Scavenger || o is Oracle;

        private readonly Rules rules;
        private bool heavyCarryTrue;

        public HandHolding(Rules rules)
        {
            this.rules = rules;

            On.SlugcatHand.Update += SlugcatHand_Update;
            On.Creature.Grab += Creature_Grab;
            On.Player.TossObject += Player_TossObject;
            On.Player.GraphicsModuleUpdated += Player_GraphicsModuleUpdated;
            On.Player.HeavyCarry += Player_HeavyCarry;
            On.Player.Grabability += Player_Grabability;
        }

        private bool Creature_Grab(On.Creature.orig_Grab orig, Creature self, PhysicalObject obj, int graspUsed, int chunkGrabbed, Creature.Grasp.Shareability shareability, float dominance, bool overrideEquallyDominant, bool pacifying)
        {
            return orig(self, obj, graspUsed, chunkGrabbed, shareability, dominance, overrideEquallyDominant, !CanHoldHand(obj) && pacifying);
        }

        private void SlugcatHand_Update(On.SlugcatHand.orig_Update orig, SlugcatHand self)
        {
            orig(self);

            if (self.owner.owner is Player player && CanHoldHand(player.grasps[self.limbNumber]?.grabbed))
            {
                self.mode = Limb.Mode.HuntAbsolutePosition;
                self.retractCounter = 0;
                var grabbedChunk = player.grasps[self.limbNumber].grabbedChunk;
                var dir = Custom.PerpendicularVector((self.connection.pos - grabbedChunk.pos).normalized);
                self.absoluteHuntPos = grabbedChunk.pos + dir * grabbedChunk.rad * 0.8f * (self.limbNumber == 0 ? -1f : 1f);
                self.huntSpeed = 20f;
                self.quickness = 1f;
            }
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
                orig(self, actuallyViewed, eu);
            }
            finally
            {
                heavyCarryTrue = false;
            }
        }

        private bool Player_HeavyCarry(On.Player.orig_HeavyCarry orig, Player self, PhysicalObject obj)
        {
            return CanHoldHand(obj) ? heavyCarryTrue : orig(self, obj);
        }

        private int Player_Grabability(On.Player.orig_Grabability orig, Player self, PhysicalObject obj)
        {
            if (self != obj && CanHoldHand(obj))
            {
                return heavyCarryTrue ? (int)Player.ObjectGrabability.Drag : (int)Player.ObjectGrabability.OneHand;
            }
            return orig(self, obj);
        }
    }
}