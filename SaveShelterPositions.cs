// TODO port this to another mod
// TODO port "Sleep Anywhere" to another mod

using Gamerules;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RWCustom;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace GameruleSet
{
    public class SaveShelterPositions
    {
        private readonly Rules rules;

        private static readonly Dictionary<IntTuple, PlayerDenData> data = new();

        class PlayerDenData
        {
            public PlayerDenData() { pos = new IntVector2[0]; }

            public IntVector2[] pos;
        }

        struct IntTuple
        {
            public int a;
            public int b;

            public override int GetHashCode() => unchecked((2118541809 * -1521134295 + a) * -1521134295 + b);
        }

        private PlayerDenData GetFor(SaveState self)
        {
            var key = new IntTuple { a = rules.RW.options.saveSlot, b = self.saveStateNumber };
            if (!data.TryGetValue(key, out var denData))
            {
                data[key] = denData = new();
            }
            return denData;
        }

        public SaveShelterPositions(Rules rules)
        {
            data.Clear();

            this.rules = rules;

            On.SaveState.AbstractCreatureFromString += SaveState_AbstractCreatureFromString;
            On.SaveState.AbstractCreatureToString_AbstractCreature_WorldCoordinate += SaveState_AbstractCreatureToString_AbstractCreature_WorldCoordinate;
            On.SaveState.BringUpToDate += SaveState_BringUpToDate;
            On.SaveState.SaveToString += SaveState_SaveToString;
            On.SaveState.LoadGame += SaveState_LoadGame;
            On.PlayerState.ctor += PlayerState_ctor;
        }

        private AbstractCreature SaveState_AbstractCreatureFromString(On.SaveState.orig_AbstractCreatureFromString orig, World world, string creatureString, bool onlyInCurrentRegion)
        {
            try
            {
                var origRet = orig(world, creatureString, onlyInCurrentRegion);
                try
                {
                    var data = creatureString.Split(new[] { "<cPOS>" }, StringSplitOptions.None);
                    if (data.Length >= 4 && rules.Persistence.Value != PersistenceEnum.None)
                    {
                        if (int.TryParse(data[1], out int x) && int.TryParse(data[2], out int y))
                        {
                            origRet.pos.x = x;
                            origRet.pos.y = y;
                            origRet.InDen = data[3] == "1";
                        }
                    }
                }
                catch (Exception e)
                {
                    rules.Logger.LogError(creatureString + ": " + e);
                }
                return origRet;
            }
            catch (Exception e)
            {
                rules.Logger.LogError(creatureString + ": " + e);
                throw;
            }
        }

        private string SaveState_AbstractCreatureToString_AbstractCreature_WorldCoordinate(On.SaveState.orig_AbstractCreatureToString_AbstractCreature_WorldCoordinate orig, AbstractCreature critter, WorldCoordinate pos)
        {
            return $"{orig(critter, pos)}<cC><cPOS>{pos.x}<cPOS>{pos.y}<cPOS>{(critter.InDen ? 1 : 0)}<cPOS>";
        }

        private void SaveState_BringUpToDate(On.SaveState.orig_BringUpToDate orig, SaveState self, RainWorldGame game)
        {
            try
            {
                orig(self, game);

                var denData = GetFor(self);
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
                var pos = GetFor(self).pos;
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

                var denData = GetFor(self);
                var split = str.Split(new[] { "<DENTILE>" }, StringSplitOptions.None);

                if (split.Length == 3)
                {
                    var players = split[1].Split(';');

                    denData.pos = new IntVector2[players.Length - 1];

                    for (int i = 0; i < players.Length; i++)
                    {
                        if (players[i] == null)
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
                var denData = GetFor(sess.saveState);
                if (playerNumber < denData.pos.Length)
                {
                    crit.pos.Tile = denData.pos[playerNumber];
                }
            }
        }
    }
}
