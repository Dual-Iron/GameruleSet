using RWCustom;
using System.Linq;

namespace GameruleSet
{
    public partial class RideableLizards
    {
        private void GraphicalHooks()
        {
            On.SlugcatHand.Update += SlugcatHand_Update;
            On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;
            On.PlayerGraphics.AddToContainer += PlayerGraphics_AddToContainer;
            On.PlayerGraphics.InitiateSprites += PlayerGraphics_InitiateSprites;
            On.PlayerGraphics.ctor += PlayerGraphics_ctor;
        }

        private void SlugcatHand_Update(On.SlugcatHand.orig_Update orig, SlugcatHand self)
        {
            orig(self);

            if (self.owner.owner is Player p && IsRiding(p, out var stick) && stick.Lizard.realizedObject is Lizard l)
            {
                var rot = Custom.RotateAroundOrigo((l.bodyChunks[0].pos - l.bodyChunks[1].pos).normalized, self.limbNumber == 0 ? -90 : 90);
                var lizNeckPos = l.bodyChunks[0].pos + l.bodyChunks[0].rad * rot * 0.8f;
                self.absoluteHuntPos = lizNeckPos;
                self.mode = Limb.Mode.HuntAbsolutePosition;
                self.huntSpeed = 30;
                self.quickness = 1;
                self.retractCounter = 0;
            }
        }

        private void PlayerGraphics_DrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, UnityEngine.Vector2 camPos)
        {
            sLeaser.sprites[4].alpha = 1;

            orig(self, sLeaser, rCam, timeStacker, camPos);

            if (IsRiding(self.player, out _))
            {
                sLeaser.sprites[4].alpha = 0;
            }
        }

        private void PlayerGraphics_AddToContainer(On.PlayerGraphics.orig_AddToContainer orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContatiner)
        {
            orig(self, sLeaser, rCam, newContatiner);

            (newContatiner ?? rCam.ReturnFContainer("Midground")).AddChildAtIndex(sLeaser.containers[0], 0);
        }

        private void PlayerGraphics_InitiateSprites(On.PlayerGraphics.orig_InitiateSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            sLeaser.containers = new[] { new FContainer() };

            orig(self, sLeaser, rCam);
        }

        private void PlayerGraphics_ctor(On.PlayerGraphics.orig_ctor orig, PlayerGraphics self, PhysicalObject ow)
        {
            orig(self, ow);

            self.internalContainerObjects ??= new();
        }
    }
}