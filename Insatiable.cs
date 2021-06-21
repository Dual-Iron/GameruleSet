using StaticTables;
using System;

namespace GameruleSet
{
    public class Insatiable
    {
        struct InsatiableData : IWeakData<PlayerState>
        {
            public double hunger;

            void IWeakData<PlayerState>.Construct(PlayerState key) { }
            void IWeakData<PlayerState>.Destruct() { }
        }

        private readonly Rules rules;

        public Insatiable(Rules rules)
        {
            this.rules = rules;

            On.OverseerTutorialBehavior.TutorialText += OverseerTutorialBehavior_TutorialText;
            On.HUD.FoodMeter.ctor += FoodMeter_ctor;
            On.Player.AddFood += Player_AddFood;
            On.Player.AddQuarterFood += Player_AddQuarterFood;
            On.Player.FoodInRoom_bool += Player_FoodInRoom_bool;
            On.Player.FoodInRoom_Room_bool += Player_FoodInRoom_Room_bool;
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

        private int Player_FoodInRoom_bool(On.Player.orig_FoodInRoom_bool orig, Player self, bool eatAndDestroy)
        {
            return self.FoodInRoom(self.room, eatAndDestroy);
        }

        private int Player_FoodInRoom_Room_bool(On.Player.orig_FoodInRoom_Room_bool orig, Player self, Room checkRoom, bool eatAndDestroy)
        {
            if (!checkRoom.abstractRoom.shelter)
            {
                return self.FoodInStomach;
            }

            if (rules.Insatiable == 1)
            {
                return orig(self, checkRoom, eatAndDestroy);
            }

            var sum = orig(self, checkRoom, false) - self.FoodInStomach;
            var ret = (int)(sum * rules.Insatiable + self.FoodInStomach + self.playerState.quarterFoodPoints * 0.25f);

            // If you're not eating, just return the result.
            if (!eatAndDestroy)
            {
                return ret;
            }

            // Eat karma flowers.
            if (checkRoom.game.session is StoryGameSession s && !s.saveState.deathPersistentSaveData.reinforcedKarma)
            {
                foreach (var entities in checkRoom.abstractRoom.entities)
                {
                    if (entities is AbstractPhysicalObject o && o.realizedObject != null && o.type == AbstractPhysicalObject.AbstractObjectType.KarmaFlower)
                    {
                        s.saveState.deathPersistentSaveData.reinforcedKarma = true;
                        if (self.SessionRecord != null)
                        {
                            self.SessionRecord.AddEat(o.realizedObject);
                        }
                        break;
                    }
                }
            }

            // If you'd starve anyway, don't eat anything.
            if (ret < self.slugcatStats.foodToHibernate)
            {
                return ret;
            }

            var count = self.FoodInStomach + self.playerState.quarterFoodPoints * 0.25f;

            // Eat flies in hands first.
            for (int j = 0; j < self.grasps.Length; j++)
            {
                if (self.grasps[j]?.grabbed is Fly fly)
                {
                    count += fly.FoodPoints * rules.Insatiable;

                    var grabbed = self.grasps[j].grabbed;
                    foreach (var stick in self.abstractCreature.stuckObjects)
                    {
                        if (stick.A == self.abstractCreature && stick.B == grabbed.abstractPhysicalObject)
                        {
                            stick.Deactivate();
                        }
                    }
                    if (self.SessionRecord != null)
                    {
                        self.SessionRecord.AddEat(grabbed);
                    }
                    grabbed.Destroy();
                    checkRoom.RemoveObject(grabbed);
                    checkRoom.abstractRoom.RemoveEntity(grabbed.abstractPhysicalObject);
                    self.ReleaseGrasp(j);

                    if (count >= self.MaxFoodInStomach)
                    {
                        return self.MaxFoodInStomach;
                    }
                }
            }

            // If satisfied, stop eating.
            if (count >= self.slugcatStats.foodToHibernate)
            {
                return (int)count;
            }

            // Eat anything else in shelter.
            for (int l = checkRoom.abstractRoom.entities.Count - 1; l >= 0; l--)
            {
                if (checkRoom.abstractRoom.entities[l] is AbstractPhysicalObject o && o.realizedObject is IPlayerEdible i && self.ObjectCountsAsFood(o.realizedObject))
                {
                    count += i.FoodPoints * rules.Insatiable;

                    foreach (var stick in self.abstractCreature.stuckObjects)
                    {
                        if (stick.A == self.abstractCreature && stick.B == o.realizedObject.abstractPhysicalObject)
                        {
                            stick.Deactivate();
                        }
                    }
                    if (self.SessionRecord != null)
                    {
                        self.SessionRecord.AddEat(o.realizedObject);
                    }
                    o.realizedObject.Destroy();
                    checkRoom.RemoveObject(o.realizedObject);
                    checkRoom.abstractRoom.RemoveEntity(o.realizedObject.abstractPhysicalObject);

                    if (count >= self.slugcatStats.foodToHibernate)
                    {
                        return (int)count;
                    }
                }
            }

            return (int)count;
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
            ref var data = ref player.playerState.Data().Get<InsatiableData>();

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
