using System;

namespace GameruleSet
{
    public class Insatiable
    {
        private readonly Rules rules;

        public Insatiable(Rules rules)
        {
            this.rules = rules;

            On.HUD.FoodMeter.ctor += FoodMeter_ctor;
            On.Player.AddFood += Player_AddFood;
            On.Player.AddQuarterFood += Player_AddQuarterFood;

            On.ProcessManager.SwitchMainProcess += (o, s, i) =>
            {
                if (s.currentMainLoop is RainWorldGame game)
                    ResetHunger(game);
                o(s, i);
            };
            On.ArenaGameSession.EndSession += (o, s) =>
            {
                ResetHunger(s.game);
                o(s);
            };
        }

        private void ResetHunger(RainWorldGame game)
        {
            for (int i = 0; i < game.session.Players.Count; i++)
            {
                rules.GetData(game.session.Players[i].ID).hunger = 0;
            }
        }

        private void FoodMeter_ctor(On.HUD.FoodMeter.orig_ctor orig, HUD.FoodMeter self, HUD.HUD hud, int maxFood, int survivalLimit)
        {
            orig(self, hud, maxFood, survivalLimit);
            self.quarterPipShower ??= new HUD.FoodMeter.QuarterPipShower(self);
        }

        bool safe = true;

        private void Player_AddQuarterFood(On.Player.orig_AddQuarterFood orig, Player self)
        {
            if (safe)
                AddFood(self, 0.25 * rules.Insatiable.Value);
            else
                orig(self);
        }

        private void Player_AddFood(On.Player.orig_AddFood orig, Player self, int add)
        {
            if (safe)
                AddFood(self, add * rules.Insatiable.Value);
            else
                orig(self, add);
        }

        private void AddFood(Player player, double amount)
        {
            var data = rules.GetData(player.abstractCreature.ID);

            amount += data.hunger;

            int amountFloored = (int)amount;

            amount -= amountFloored;
            safe = false;

            player.AddFood(amountFloored);

            while (amount >= 0.25)
            {
                amount -= 0.25;
                player.AddQuarterFood();
            }

            data.hunger = amount;
            safe = true;
        }
    }
}
