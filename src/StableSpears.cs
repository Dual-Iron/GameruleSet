namespace GameruleSet
{
    public class StableSpears
    {
        private readonly Rules rules;

        public StableSpears(Rules rules)
        {
            this.rules = rules;

            On.AbstractSpear.StuckInWallTick += AbstractSpear_StuckInWallTick;
        }

        private void AbstractSpear_StuckInWallTick(On.AbstractSpear.orig_StuckInWallTick orig, AbstractSpear self, int ticks)
        {
            orig(self, rules.StableSpears ? 0 : ticks);
        }
    }
}