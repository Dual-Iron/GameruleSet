using StaticTables;
using System;
using System.Text;
using UnityEngine;

namespace GameruleSet
{
    public partial class Injury
    {
        internal class InjurySave
        {
            private readonly Rules rules;

            public InjurySave(Rules rules)
            {
                this.rules = rules;

                On.SaveState.BringUpToDate += SaveState_BringUpToDate;
                On.SaveState.SaveToString += SaveState_SaveToString;
                On.SaveState.LoadGame += SaveState_LoadGame;
                On.PlayerState.ctor += PlayerState_ctor;
            }

            private void SaveState_BringUpToDate(On.SaveState.orig_BringUpToDate orig, SaveState self, RainWorldGame game)
            {
                try
                {
                    orig(self, game);

                    ref var data = ref self.Data().Get<SaveStateData>();
                    data.injuries = new float[game.Players.Count];
                    for (int i = 0; i < game.Players.Count; i++)
                    {
                        if (game.Players[i]?.state is PlayerState s)
                            data.injuries[i] = Mathf.Clamp01(s.Data().Get<InjuryData>().injury - 0.25f);
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
                    var injuries = self.Data().Get<SaveStateData>().injuries;
                    var sb = new StringBuilder();
                    for (int i = 0; i < injuries.Length; i++)
                    {
                        sb.Append($"{injuries[i]};");
                    }
                    return $"{orig(self)}<INJURIES>{sb}<INJURIES>";
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

                    ref var data = ref self.Data().Get<SaveStateData>();
                    var split = str.Split(new[] { "<INJURIES>" }, StringSplitOptions.None);

                    if (split.Length == 3)
                    {
                        var players = split[1].Split(';');

                        data.injuries = new float[players.Length - 1];

                        for (int i = 0; i < players.Length - 1; i++)
                        {
                            if (float.TryParse(players[i], out var injury))
                            {
                                data.injuries[i] = injury;
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
                    ref var data = ref sess.saveState.Data().Get<SaveStateData>();
                    if (playerNumber < data.injuries.Length)
                    {
                        self.Data().Get<InjuryData>().injury = data.injuries[playerNumber];
                    }
                }
            }
        }
    }
}
