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

        internal Rules(ManualLogSource logger)
        {
            CurrentRules = this;

            Logger = logger;

            Injury = new BoolRule(false)
            {
                ID = "injury",
                Description = "When your body takes lethal damage while not already injured, you survive and become injured until you sleep. Vulture masks prevent being killed by headshots, and king vulture masks prevent being stunned from headshots."
            };

            Imbalanced = new BoolRule(false) { ID = "karmically_imbalanced", Description = "Karma flowers don't spawn." };

            Corpulent = new FloatRule(1, 0, 4) { ID = "corpulent", Description = "How much food is needed to hibernate. A value greater than 1 means more, and a value less than 1 means less." };

            Insatiable = new FloatRule(1, 0, 4) { ID = "insatiable", Description = "Food gives x times more food pips." };

            Dislodge = new BoolRule(false) { ID = "dislodge_spears", Description = "You can dislodge stuck spears if you are standing nearby or hanging from them." };

            StableSpears = new BoolRule(false) { ID = "stable_spears", Description = "When you stick a spear in a wall, it won't fall off on its own." };

            Persistence = new EnumRule<PersistenceEnum>(PersistenceEnum.None) { ID = "persistence", Description = "Determines when objects (excluding ephemeral items) should live through a cycle. 'All' means they don't despawn. 'Wet' means they don't despawn unless they're under open sky. 'Dry' means they don't despawn unless the room rains or floods. 'None' means they follow vanilla behavior." };

            SaveShelterPositions = new BoolRule(false) { ID = "save_shelter_positions", Description = "Creatures' positions aren't reset when you wake up in a shelter." };

            CycleLength = new FloatRule(1) { ID = "cycle_length", Description = "Multiplier for cycle length." };

            SleepAnywhere = new BoolRule(false) { ID = "sleep_anywhere", Description = "Lets you sleep anywhere by holding crouch on a solid, flat surface. May not work correctly if 'save_shelter_positions' is false." };

            new Injury(this);
            new Imbalanced(this);
            new Corpulent(this);
            new Insatiable(this);
            new Dislodge(this);
            new Persistence(this);
            new CycleLength(this);
            new SaveShelterPositions(this);
            new SleepAnywhere(this);
        }
    }
}
