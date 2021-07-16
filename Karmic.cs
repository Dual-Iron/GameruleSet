using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;

namespace GameruleSet
{
    public class Karmic
    {
        private readonly Rules rules;

        public Karmic(Rules rules)
        {
            this.rules = rules;

            IL.Room.Loaded += Room_Loaded;
            On.KarmaFlower.PlaceInRoom += KarmaFlower_PlaceInRoom;
        }

        private void Room_Loaded(ILContext il)
        {
            var cursor = new ILCursor(il);

            if (!cursor.TryGotoNext(i => i.MatchCallvirt<RainWorldGame>("get_StoryCharacter")) || 
                !cursor.TryGotoNext(i => i.MatchCallvirt<RainWorldGame>("get_StoryCharacter")) ||
                !cursor.TryGotoNext(i => i.MatchCallvirt<RainWorldGame>("get_StoryCharacter")))
            {
                rules.Logger.LogError("Missing storycharacter get");
                return;
            }

            var jmp = cursor.Next.Next.Next.Next;

            if (jmp == null)
            {
                rules.Logger.LogError("Missing jmp");
                return;
            }

            cursor.Index--;

            cursor.EmitDelegate<Func<Room, bool>>(PlaceKarmaFlower);
            cursor.Emit(OpCodes.Brtrue, jmp);
            cursor.Emit(OpCodes.Ldarg_0);

            bool PlaceKarmaFlower(Room self)
            {
                return rules.Karmic.Value == KarmaRating.Attuned;
            }
        }

        private void KarmaFlower_PlaceInRoom(On.KarmaFlower.orig_PlaceInRoom orig, KarmaFlower self, Room placeRoom)
        {
            if (rules.Karmic.Value != KarmaRating.Imbalanced)
                orig(self, placeRoom);
        }
    }
}
