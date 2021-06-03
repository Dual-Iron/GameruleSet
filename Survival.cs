using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GameruleSet
{
    public class Survival
    {
        private readonly Rules rules;

        public Survival(Rules rules)
        {
            this.rules = rules;

            new Hook(typeof(StoryGameSession).GetProperty(nameof(StoryGameSession.RedIsOutOfCycles)).GetGetMethod(), typeof(Survival).GetMethod(nameof(StoryGameSession_get_RedIsOutOfCycles)))
                .Apply();
        }

        private bool StoryGameSession_get_RedIsOutOfCycles(Func<StoryGameSession, bool> orig, StoryGameSession self)
        {
            Console.WriteLine($"Uh oh. " + rules.Survival.Value);
            return orig(self) || rules.Survival.Value && !self.Players.Any(a => a.realizedCreature is Player p && p.KarmaIsReinforced);
        }
    }
}
