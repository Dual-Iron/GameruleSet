using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using Music;
using RWCustom;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace GameruleSet
{
    public class Corpulent
    {
        private readonly Rules rules;

        public Corpulent(Rules rules)
        {
            this.rules = rules;

            On.SlugcatStats.SlugcatFoodMeter += SlugcatStats_SlugcatFoodMeter;
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
