using BepInEx;
using System;

namespace GameruleSet
{
    [BepInPlugin("com.github.dual.gameruleset", "Gamerule Set", "1.5.1")]
    public sealed class GameruleSetPlugin : BaseUnityPlugin
    {
        public Rules? Gamerules { get; private set; }

        public void OnEnable()
        {
            try
            {
                Gamerules = new(Logger);
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }

        public void OnDisable()
        {
            Gamerules = null;
        }
    }
}
