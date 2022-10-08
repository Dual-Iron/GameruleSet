using System.Security.Permissions;
using System.Security;
using BepInEx;
using System;

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace GameruleSet
{
    [BepInPlugin("com.github.dual.gameruleset", "Gamerule Set", "1.5.2")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public Rules Gamerules { get; private set; }

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
