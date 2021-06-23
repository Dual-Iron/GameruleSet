using RWCustom;
using UnityEngine;

namespace GameruleSet
{
    public class Corpulent
    {
        private readonly Rules rules;

        public Corpulent(Rules rules)
        {
            this.rules = rules;

            On.SaveState.SaveToString += SaveState_SaveToString;
            On.SlugcatStats.SlugcatFoodMeter += SlugcatStats_SlugcatFoodMeter;
        }

        private string SaveState_SaveToString(On.SaveState.orig_SaveToString orig, SaveState self)
        {
            var tempFood = self.food;
            if (self.progression.rainWorld.processManager.currentMainLoop is RainWorldGame g && g.Players.Count > 0 && g.Players[0].realizedCreature is Player p)
            {
                self.food = Mathf.Clamp(p.playerState.foodInStomach - p.slugcatStats.foodToHibernate, 0, p.slugcatStats.maxFood);
            }
            var ret = orig(self);
            self.food = tempFood;
            return ret;
        }

        private IntVector2 SlugcatStats_SlugcatFoodMeter(On.SlugcatStats.orig_SlugcatFoodMeter orig, int slugcatNum)
        {
            var ret = orig(slugcatNum);
            var ratio = ret.y / (float)ret.x;
            ratio = Mathf.Clamp((float)(ratio * rules.Corpulent), 0, 1);
            ret.y = (int)(ret.x * ratio);
            return ret;
        }
    }
}
