using StaticTables;
using System;

namespace GameruleSet
{
    internal struct PlayerData : IWeakData<PlayerState>
    {
        public int injuryCooldown;
        public bool injured;
        public float damageBlockedWithMask;
        public float danger;
        public double hunger;
        public SlugcatStats cachedSlugcatStats;
        public int painTime;
        public float lastAerobicLevel;
        public float woundDir;

        void IDisposable.Dispose() { }
        void IWeakData<PlayerState>.Initialize(PlayerState owner, object? state) { }
    }
}
