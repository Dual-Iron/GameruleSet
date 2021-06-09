namespace GameruleSet
{
    public class Imbalanced
    {
        private readonly Rules rules;

        public Imbalanced(Rules rules)
        {
            this.rules = rules;

            On.KarmaFlower.PlaceInRoom += KarmaFlower_PlaceInRoom;
        }

        private void KarmaFlower_PlaceInRoom(On.KarmaFlower.orig_PlaceInRoom orig, KarmaFlower self, Room placeRoom)
        {
            if (!rules.Imbalanced)
                orig(self, placeRoom);
        }
    }
}
