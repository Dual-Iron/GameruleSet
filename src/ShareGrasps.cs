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

        private int cachedChunkGrabbed;
        private Creature cachedGrabber;

        private bool Creature_Grab(On.Creature.orig_Grab orig, Creature self, PhysicalObject obj, int graspUsed, int chunkGrabbed, Creature.Grasp.Shareability shareability, float dominance, bool overrideEquallyDominant, bool pacifying)
        {
            cachedChunkGrabbed = chunkGrabbed;
            cachedGrabber = self;
            var ret = orig(self, obj, graspUsed, chunkGrabbed, shareability, dominance, overrideEquallyDominant, pacifying);
            cachedChunkGrabbed = default;
            cachedGrabber = default;
            return ret;
        }

        private bool Grasp_ShareabilityConflict(On.Creature.Grasp.orig_ShareabilityConflict orig, Creature.Grasp self, Creature.Grasp.Shareability other)
        {
            if (rules.ShareGrasps && cachedGrabber is Player && self.grabber is Player && self.chunkGrabbed != cachedChunkGrabbed)
            {
                return false;
            }
            return orig(self, other);
        }
    }
}