using BepInEx.Logging;
using Gamerules;
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
        public BoolRule SpearPersist { get; }
        public BoolRule WetPersist { get; }
        public BoolRule DryPersist { get; }
        public BoolRule SoakedPersist { get; }

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

            Dislodge = new BoolRule(false) { ID = "dislodge_spears", Description = "You can dislodge stuck spears. To do so, you have to be standing on solid ground or hanging from a pole or spear, and you can't be moving." };

            SpearPersist = new BoolRule(false) { ID = "spear_persistence", Description = "When you stick a spear in a wall, it won't despawn." };
            WetPersist = new BoolRule(false) { ID = "dry_persistence", Description = "In areas that don't experience rain, items don't despawn, excluding rocks and spears." };
            DryPersist = new BoolRule(false) { ID = "wet_persistence", Description = "In areas that don't directly contact rain, items don't despawn, excluding rocks and spears." };
            SoakedPersist = new BoolRule(false) { ID = "soaked_persistence", Description = "In areas that directly contact rain, items don't despawn, excluding rocks and spears." };
        }

        internal void Initialize()
        {
            new Injury(this);
            new Imbalanced(this);
            new Corpulent(this);
            new Insatiable(this);
            new Dislodge(this);
            new Persistence(this);
        }

        public EntityData GetData(EntityID entity) => data.TryGetValue(entity, out var ret) ? ret : (data[entity] = new());
    }
}
