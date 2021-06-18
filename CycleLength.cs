namespace GameruleSet
{
    public class CycleLength
    {
        private readonly Rules rules;

        public CycleLength(Rules rules)
        {
            this.rules = rules;

            On.RainCycle.ctor += RainCycle_ctor;
        }

        private void RainCycle_ctor(On.RainCycle.orig_ctor orig, RainCycle self, World world, float minutes)
        {
            orig(self, world, UnityEngine.Mathf.Max(41f/40f, minutes * rules.CycleLength));
        }
    }
}