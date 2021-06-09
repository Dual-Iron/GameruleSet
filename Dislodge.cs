using UnityEngine;

namespace GameruleSet
{
    public class Dislodge
    {
        private readonly Rules rules;

        public Dislodge(Rules rules)
        {
            this.rules = rules;

            On.Player.CanIPickThisUp += Player_CanIPickThisUp;
            On.Spear.ChangeMode += Spear_ChangeMode;
        }

        private void Spear_ChangeMode(On.Spear.orig_ChangeMode orig, Spear self, Weapon.Mode newMode)
        {
            if (rules.Dislodge && self.mode == Weapon.Mode.StuckInWall)
            {
                if (self.abstractSpear.stuckInWallCycles >= 0)
                {
                    self.room.GetTile(self.stuckInWall!.Value).horizontalBeam = false;
                    for (int i = -1; i < 2; i += 2)
                    {
                        if (!self.room.GetTile(self.stuckInWall.Value + new Vector2(20f * i, 0f)).Solid)
                        {
                            self.room.GetTile(self.stuckInWall.Value + new Vector2(20f * i, 0f)).horizontalBeam = false;
                        }
                    }
                }
                else
                {
                    self.room.GetTile(self.stuckInWall!.Value).verticalBeam = false;
                    for (int j = -1; j < 2; j += 2)
                    {
                        if (!self.room.GetTile(self.stuckInWall.Value + new Vector2(0f, 20f * j)).Solid)
                        {
                            self.room.GetTile(self.stuckInWall.Value + new Vector2(0f, 20f * j)).verticalBeam = false;
                        }
                    }
                }
                self.abstractSpear.stuckInWallCycles = 0;
            }
            orig(self, newMode);
        }

        private bool Player_CanIPickThisUp(On.Player.orig_CanIPickThisUp orig, Player self, PhysicalObject obj)
        {
            if (orig(self, obj))
                return true;

            if (!rules.Dislodge || self.bodyMode == Player.BodyModeIndex.Default || self.bodyMode == Player.BodyModeIndex.ClimbingOnBeam && self.animation != Player.AnimationIndex.HangFromBeam || self.mainBodyChunk.vel.sqrMagnitude > 5f)
                return false;

            for (int i = 0; i < self.input.Length; i++)
            {
                if (self.input[i].x != 0 || self.input[i].y != 0)
                    return false;
            }

            if (obj is Spear spear && spear.mode == Weapon.Mode.StuckInWall && self.CanPutSpearToBack)
            {
                return true;
            }

            int num = (int)self.Grabability(obj);
            if (num == 2)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (self.grasps[i] != null && self.Grabability(self.grasps[i].grabbed) > Player.ObjectGrabability.OneHand)
                    {
                        return false;
                    }
                }
            }

            if (obj is Spear s && s.mode == Weapon.Mode.StuckInWall)
            {
                int num2 = 0;
                for (int j = 0; j < 2; j++)
                {
                    if (self.grasps[j] != null)
                    {
                        if (self.Grabability(self.grasps[j].grabbed) > Player.ObjectGrabability.OneHand)
                        {
                            num2++;
                        }
                    }
                }
                return num2 != 2 && (num2 <= 0 || num <= 2);
            }
            return false;
        }
    }
}
