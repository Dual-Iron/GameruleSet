﻿using BepInEx.Logging;
using Gamerules;
using System;
using System.Collections.Generic;

namespace GameruleSet
{
    public sealed class Rules
    {
        private readonly Dictionary<EntityID, EntityData> data = new();

        private RainWorld? rw;
        public RainWorld RW => rw ??= UnityEngine.Object.FindObjectOfType<RainWorld>();
        public RainWorldGame? RWGame => RW.processManager.currentMainLoop as RainWorldGame;

        public ManualLogSource Logger { get; }

        public BoolRule Injury { get; }
        public BoolRule Imbalanced { get; }
        public FloatRule Corpulent { get; }
        public FloatRule Insatiable { get; }
        public BoolRule Dislodge { get; }
        public BoolRule StableSpears { get; }
        public EnumRule<PersistenceEnum> Persistence { get; }

        internal Rules(ManualLogSource logger)
        {
            Logger = logger;

            Injury = new BoolRule(false)
            {
                ID = "injury",
                Description = "When your body takes lethal damage while not already injured, you survive and become injured until you sleep. Vulture masks prevent being killed by headshots, and king vulture masks prevent being stunned from headshots."
            };

            Imbalanced = new BoolRule(false) { ID = "imbalanced", Description = "Karma flowers don't spawn." };

            Corpulent = new FloatRule(1, 0, 4) { ID = "corpulent", Description = "How much food is needed to hibernate. A value greater than 1 means more, and a value less than 1 means less." };

            Insatiable = new FloatRule(1, 0, 4) { ID = "insatiable", Description = "Food gives x times more food pips." };

            Dislodge = new BoolRule(false) { ID = "dislodge_spears", Description = "You can dislodge stuck spears if you are standing nearby or hanging from them." };

            StableSpears = new BoolRule(false) { ID = "stable_spears", Description = "When you stick a spear in a wall, it won't fall off on its own." };

            Persistence = new EnumRule<PersistenceEnum>(PersistenceEnum.None) { ID = "persistence", Description = "Determines when objects (excluding ephemeral items) should live through a cycle. 'All' means they don't despawn. 'Wet' means they don't despawn unless they're under open sky. 'Dry' means they don't despawn unless the room rains or floods. 'None' means they follow vanilla behavior." };
        }

        internal void Initialize()
        {
            new Injury(this);
            new Imbalanced(this);
            new Corpulent(this);
            new Insatiable(this);
            new Dislodge(this);
            new Persistence(this);

            On.OverseerTutorialBehavior.TutorialText += OverseerTutorialBehavior_TutorialText;
        }

        private void OverseerTutorialBehavior_TutorialText(On.OverseerTutorialBehavior.orig_TutorialText orig, OverseerTutorialBehavior self, string text, int wait, int time, bool hideHud)
        {
            if (text == "Three is enough to hibernate" || text == "Four is enough to hibernate" || text == "Additional food (above three) is kept for later" || text == "Additional food (above four) is kept for later")
            {
                int amount = (int)(self.player.slugcatStats.foodToHibernate / Insatiable);
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
                text = text.Replace("Three", digit).Replace("three", digit).Replace("Four", digit).Replace("four", digit);
            }
            orig(self, text, wait, time, hideHud);
        }

        public EntityData GetData(EntityID entity) => data.TryGetValue(entity, out var ret) ? ret : (data[entity] = new());
    }
}
