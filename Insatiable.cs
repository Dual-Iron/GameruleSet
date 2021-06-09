using System;
using System.Net.NetworkInformation;
using UnityEngine;

namespace GameruleSet
{
    public class Insatiable
    {
        private readonly Rules rules;

        public Insatiable(Rules rules)
        {
            this.rules = rules;

            On.OverseerTutorialBehavior.TutorialText += OverseerTutorialBehavior_TutorialText;
            On.HUD.FoodMeter.ctor += FoodMeter_ctor;
            On.Player.AddFood += Player_AddFood;
            On.Player.AddQuarterFood += Player_AddQuarterFood;
            On.Player.FoodInRoom_Room_bool += Player_FoodInRoom_Room_bool;

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

        private void OverseerTutorialBehavior_TutorialText(On.OverseerTutorialBehavior.orig_TutorialText orig, OverseerTutorialBehavior self, string text, int wait, int time, bool hideHud)
        {
            if (text == "Three is enough to hibernate" || text == "Four is enough to hibernate" || text == "Additional food (above three) is kept for later" || text == "Additional food (above four) is kept for later")
            {
                int amount = (int)(self.player.slugcatStats.foodToHibernate / rules.Insatiable);
                string digit = amount switch
                {
                    < 1 => "Any amount",
                    1 => "One",
                    2 => "Two",
                    3 => "Three",
                    4 => "Four",
                    5 => "Five",
                    6 => "Six",
                    7 => "Seven",
                    8 => "Eight",
                    9 => "Nine",
                    > 9 => amount.ToString(),
                };

                if (!text.StartsWith("Three") && !text.StartsWith("Four"))
                    digit = digit.ToLower();

                text = text.Replace("Three", digit).Replace("three", digit).Replace("Four", digit).Replace("four", digit);
            }
            orig(self, text, wait, time, hideHud);
        }

        private int Player_FoodInRoom_Room_bool(On.Player.orig_FoodInRoom_Room_bool orig, Player self, Room checkRoom, bool eatAndDestroy)
        {
            int sum = orig(self, checkRoom, eatAndDestroy) - self.FoodInStomach;
            return (int)(sum * rules.Insatiable + self.FoodInStomach + self.playerState.quarterFoodPoints * 0.25);
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
                AddFood(self, 0.25 * rules.Insatiable);
            else
                orig(self);
        }

        private void Player_AddFood(On.Player.orig_AddFood orig, Player self, int add)
        {
            if (safe)
                AddFood(self, add * rules.Insatiable);
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
