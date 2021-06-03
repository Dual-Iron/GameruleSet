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
        public BoolRule Survival { get; }
        public BoolRule Imbalanced { get; }
        public IntRule Corpulent { get; }
        public IntRule Insatiable { get; }
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

            Survival = new BoolRule(false) { ID = "survival", Description = "If you die without a karma shield, you get a Hunter-style game over. Karma flowers don't respawn." };
            Imbalanced = new BoolRule(false) { ID = "imbalanced", Description = "Karma flowers don't spawn at all." };

            Corpulent = new IntRule(1, 1, 4) { ID = "corpulent", Description = "You lose x times less hunger after sleeping." };

            Insatiable = new IntRule(1, 1, 4) { ID = "insatiable", Description = "Food gives x times less food pips." };

            Dislodge = new BoolRule(false) { ID = "dislodge_spears", Description = "You can dislodge a stuck spear if you're hanging from it or if you're standing on solid ground adjacent to it." };

            SpearPersist = new BoolRule(false) { ID = "rainswept/spear_persistence", Description = "Stuck spears don't despawn." };
            WetPersist = new BoolRule(false) { ID = "rainswept/dry_persistence", Description = "In areas that don't experience rain, items don't despawn, excluding rocks and spears." };
            DryPersist = new BoolRule(false) { ID = "rainswept/wet_persistence", Description = "In areas that don't directly contact rain, items don't despawn, excluding rocks and spears." };
            SoakedPersist = new BoolRule(false) { ID = "rainswept/soaked_persistence", Description = "In areas that directly contact rain, items don't despawn, excluding rocks and spears." };
        }

        internal void Initialize()
        {
            new Injury(this);
            new Survival(this);
        }

        public EntityData GetData(EntityID entity) => data.TryGetValue(entity, out var ret) ? ret : (data[entity] = new());
    }
}
