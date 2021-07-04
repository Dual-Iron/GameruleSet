using BepInEx.Logging;
using Gamerules;

namespace GameruleSet
{
    public sealed class Rules
    {
        internal static Rules? CurrentRules { get; private set; }

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
        public BoolRule SaveShelterPositions { get; }
        public FloatRule CycleLength { get; }
        public BoolRule SleepAnywhere { get; }
        public BoolRule ShareGrasps { get; }
        public BoolRule HandHolding { get; }

        internal Rules(ManualLogSource logger)
        {
            CurrentRules = this;

            Logger = logger;

            Injury = new BoolRule(false)
            {
                ID = "injury",
                Description = "While healthy, if you suffer a critical injury, you survive and become injured until you sleep. Headshots are still lethal unless you're holding a vulture mask. King vulture masks are sturdy. Mushrooms let you push through the pain of injury."
            };

            Imbalanced = new BoolRule(false) { ID = "karmically_imbalanced", Description = "Karma flowers don't spawn." };

            Corpulent = new FloatRule(1, 0, 4) { ID = "corpulent", Description = "Multiplier for the amount of food needed to hibernate." };

            Insatiable = new FloatRule(1, 0, 4) { ID = "insatiable", Description = "Multiplier for the amount of food pips that items give." };

            Dislodge = new BoolRule(false) { ID = "dislodge_spears", Description = "You can dislodge stuck spears if you are standing nearby or hanging from them." };

            StableSpears = new BoolRule(false) { ID = "stable_spears", Description = "When you stick a spear in a wall, it won't fall off on its own." };

            Persistence = new EnumRule<PersistenceEnum>(PersistenceEnum.None) { ID = "persistence", Description = "Determines when objects (excluding ephemeral items) should live through a cycle. 'All' means they don't despawn. 'Wet' means they don't despawn unless they're under open sky. 'Dry' means they don't despawn unless the room rains or floods. 'None' means they follow vanilla behavior." };

            SaveShelterPositions = new BoolRule(false) { ID = "save_shelter_positions", Description = "Creatures' positions aren't reset when you wake up in a shelter." };

            CycleLength = new FloatRule(1) { ID = "cycle_length", Description = "Multiplier for cycle length." };

            SleepAnywhere = new BoolRule(false) { ID = "sleep_anywhere", Description = "Lets you sleep anywhere by holding crouch on a solid, flat surface. May not work correctly if 'save_shelter_positions' is false." };

            ShareGrasps = new BoolRule(false) { ID = "share_grasps", Description = "Lets creatures grab different parts of the same object. For example, two players can hold different parts of the same dead centipede." };

            HandHolding = new BoolRule(false) { ID = "hand_holding", Description = "Lets you hold the hand of scavengers, iterators, and slugcats by just grabbing them." };

            new Injury(this);
            new Imbalanced(this);
            new Corpulent(this);
            new Insatiable(this);
            new Dislodge(this);
            new Persistence(this);
            new CycleLength(this);
            new SaveShelterPositions(this);
            new SleepAnywhere(this);
            new ShareGrasps(this);
            new HandHolding(this);
        }
    }
}
