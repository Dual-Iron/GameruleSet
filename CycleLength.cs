namespace GameruleSet
{
    public class CycleLength
    {
        private Rules rules;

        public CycleLength(Rules rules)
        {
            this.rules = rules;

            On.RainCycle.ctor += RainCycle_ctor;
        }

        private void RainCycle_ctor(On.RainCycle.orig_ctor orig, RainCycle self, World world, float minutes)
        {
            orig(self, world, (float)(minutes * rules.CycleLength));
        }
    }
}