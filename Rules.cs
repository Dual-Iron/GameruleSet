using BepInEx.Logging;
using Gamerules.Rules;
using Gamerules.Rules.Builders;

namespace GameruleSet
{
    public enum KarmaRating
    {
        Default,
        Imbalanced,
        Attuned
    }

    public sealed class Rules
    {
        internal static Rules? CurrentRules { get; private set; }

        public ManualLogSource Logger { get; }

        public BoolRule Injury { get; }
        public EnumRule<KarmaRating> Karmic { get; }
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

            Injury = new BoolRuleBuilder()
                .Description("While healthy, if you suffer a critical injury, you survive and become injured until you sleep. Mushrooms work as painkillers. Headshots are still lethal unless you're holding a vulture mask, and king vulture masks are extra sturdy.")
                .Register("std/injury");

            Karmic = new EnumRuleBuilder<KarmaRating>().Description("'Default' follows vanilla behavior. 'Imbalanced' removes karma flowers. 'Attuned' makes karma flowers always spawn, even in Hunter mode.")
                .Register("std/karmic");

            Corpulent = new FloatRuleBuilder().Default(1).Min(0).Max(4).Description("Multiplier for the amount of food needed to hibernate.")
                .Register("std/corpulent");

            Insatiable = new FloatRuleBuilder().Default(1).Min(0).Max(4).Description("Multiplier for the amount of food pips that items give.")
                .Register("std/insatiable");

            Dislodge = new BoolRuleBuilder().Description("You can dislodge stuck spears if you are standing nearby or hanging from them.")
                .Register("std/dislodge_spears");
            
            StableSpears = new BoolRuleBuilder().Description("When you stick a spear in a wall, it won't fall off on its own.")
                .Register("std/stable_spears");

            Persistence = new EnumRuleBuilder<PersistenceEnum>()
                .Description("Determines when objects (excluding ephemeral items) should live through a cycle. 'All' means they don't despawn. 'Wet' means they don't despawn unless they're under open sky. 'Dry' means they don't despawn unless the room rains or floods. 'None' means they follow vanilla behavior.")
                .Register("std/persistence");

            SaveShelterPositions = new BoolRuleBuilder().Description("Creatures' positions aren't reset when you wake up in a shelter.")
                .Register("std/save_shelter_positions");

            CycleLength = new FloatRuleBuilder().Default(1).Min(0).Max(4).Description("Multiplier for cycle length.")
                .Register("std/cycle_length");

            SleepAnywhere = new BoolRuleBuilder()
                .Description("Lets you sleep anywhere by holding crouch on a solid, flat surface. May not work correctly if 'save_shelter_positions' is false.")
                .Register("std/sleep_anywhere");

            ShareGrasps = new BoolRuleBuilder()
                .Description("Lets creatures grab different parts of the same object. For example, two players can hold different parts of the same dead centipede.")
                .Register("std/share_grasps");

            HandHolding = new BoolRuleBuilder()
                .Description("Hold grab and map keys while approaching a scavenger, iterator, or slugcat to grab its hand.")
                .Register("std/hand_holding");

            new Injury(this);
            new Karmic(this);
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
