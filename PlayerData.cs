using StaticTables;
using System;

namespace GameruleSet
{
    internal struct PlayerData : IWeakData<AbstractCreature>
    {
        public int injuryCooldown;
        public bool injured;
        public float damageBlockedWithMask;
        public float danger;
        public double hunger;
        public ValueWeakRef<SlugcatStats> cachedSlugcatStats;

        void IDisposable.Dispose()
        {
            cachedSlugcatStats.Dispose();
        }

        void IWeakData<AbstractCreature>.Initialize(AbstractCreature owner, object? state) { }
    }
}
