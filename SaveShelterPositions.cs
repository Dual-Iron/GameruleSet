using Mono.Cecil.Cil;
using MonoMod.Cil;
using RWCustom;
using StaticTables;
using System;
using System.Text;

namespace GameruleSet
{
    public class SaveShelterPositions
    {
        struct SaveStateData : IWeakData<SaveState>
        {
            public IntVector2[] pos;

            void IWeakData<SaveState>.Construct(SaveState owner)
            {
                pos = new IntVector2[0];
            }
            void IWeakData<SaveState>.Destruct() { }
        }

        private readonly Rules rules;

        public SaveShelterPositions(Rules rules)
        {
            this.rules = rules;

            IL.AbstractCreature.RealizeInRoom += AbstractCreature_RealizeInRoom;
            On.SaveState.BringUpToDate += SaveState_BringUpToDate;
            On.SaveState.SaveToString += SaveState_SaveToString;
            On.SaveState.LoadGame += SaveState_LoadGame;
            On.PlayerState.ctor += PlayerState_ctor;
        }

        private void AbstractCreature_RealizeInRoom(ILContext il)
        {
            try
            {
                var cursor = new ILCursor(il);

                // Find end of shelter check
                if (!cursor.TryGotoNext(i => i.MatchCall<WorldCoordinate>("get_NodeDefined")))
                {
                    rules.Logger.LogError("RealizeInRoom: Missing instruction");
                    return;
                }

                var brTo = cursor.Instrs[cursor.Index - 2];

                // Go back to start of method
                cursor.Index = 0;

                // Emit skip
                cursor.EmitDelegate<Func<bool>>(SkipVanillaCheck);
                cursor.Emit(OpCodes.Brtrue, brTo);

                static bool SkipVanillaCheck()
                {
                    return Rules.CurrentRules?.SaveShelterPositions?.Value ?? false;
                }
            }
            catch (Exception e)
            {
                rules.Logger.LogError(e);
            }
        }

        private void SaveState_BringUpToDate(On.SaveState.orig_BringUpToDate orig, SaveState self, RainWorldGame game)
        {
            try
            {
                orig(self, game);

                ref var denData = ref self.Data().Get<SaveStateData>();
                denData.pos = new IntVector2[game.Players.Count];
                for (int i = 0; i < game.Players.Count; i++)
                {
                    denData.pos[i] = game.Players[i] != null ? game.Players[i].pos.Tile : new(-1, -1);
                }
            }
            catch (Exception e)
            {
                rules.Logger.LogError(e);
                throw;
            }
        }

        private string SaveState_SaveToString(On.SaveState.orig_SaveToString orig, SaveState self)
        {
            try
            {
                var pos = self.Data().Get<SaveStateData>().pos;
                var sb = new StringBuilder();
                for (int i = 0; i < pos.Length; i++)
                {
                    sb.Append($"{pos[i].x},{pos[i].y};");
                }
                return $"{orig(self)}<DENTILE>{sb}<DENTILE>";
            }
            catch (Exception e)
            {
                rules.Logger.LogError(e);
                throw;
            }
        }

        private void SaveState_LoadGame(On.SaveState.orig_LoadGame orig, SaveState self, string str, RainWorldGame game)
        {
            try
            {
                orig(self, str, game);

                ref var denData = ref self.Data().Get<SaveStateData>();
                var split = str.Split(new[] { "<DENTILE>" }, StringSplitOptions.None);

                if (split.Length == 3)
                {
                    var players = split[1].Split(';');

                    denData.pos = new IntVector2[players.Length - 1];

                    for (int i = 0; i < players.Length - 1; i++)
                    {
                        if (string.IsNullOrEmpty(players[i]))
                            continue;

                        var values = players[i].Split(',');

                        if (int.TryParse(values[0], out int x) && int.TryParse(values[1], out int y))
                        {
                            denData.pos[i] = new(x, y);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                rules.Logger.LogError(e);
                throw;
            }
        }

        private void PlayerState_ctor(On.PlayerState.orig_ctor orig, PlayerState self, AbstractCreature crit, int playerNumber, int slugcatCharacter, bool isGhost)
        {
            orig(self, crit, playerNumber, slugcatCharacter, isGhost);
            if (crit.world.game.session is StoryGameSession sess)
            {
                ref var denData = ref sess.saveState.Data().Get<SaveStateData>();
                if (playerNumber < denData.pos.Length)
                {
                    crit.pos.Tile = denData.pos[playerNumber];
                }
            }
        }
    }
}
