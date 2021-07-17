namespace GameruleSet
{
    public class ShareGrasps
    {
        private readonly Rules rules;

        public ShareGrasps(Rules rules)
        {
            this.rules = rules;

            On.Creature.Grab += Creature_Grab;
            On.Creature.Grasp.ShareabilityConflict += Grasp_ShareabilityConflict;
        }

        private int chunkGrabbed;

        private bool Creature_Grab(On.Creature.orig_Grab orig, Creature self, PhysicalObject obj, int graspUsed, int chunkGrabbed, Creature.Grasp.Shareability shareability, float dominance, bool overrideEquallyDominant, bool pacifying)
        {
            this.chunkGrabbed = chunkGrabbed;
            return orig(self, obj, graspUsed, chunkGrabbed, shareability, dominance, overrideEquallyDominant, pacifying);
        }

        private bool Grasp_ShareabilityConflict(On.Creature.Grasp.orig_ShareabilityConflict orig, Creature.Grasp self, Creature.Grasp.Shareability other)
        {
            return (self.grabber is not Player || !rules.ShareGrasps || self.chunkGrabbed == chunkGrabbed) && orig(self, other);
        }
    }
}