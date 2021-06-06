//TODO port this to another mod
// TODO port "Sleep Anywhere" to another mod

//using Gamerules;
//using Mono.Cecil.Cil;
//using MonoMod.Cil;
//using System;

//namespace GameruleSet
//{
//    public class SaveShelterPositions
//    {
//        private readonly Rules rules;

//        private static EnumRule<PersistenceEnum>? persistence;

//        public SaveShelterPositions(Rules rules)
//        {
//            this.rules = rules;

//            persistence = rules.Persistence;

//            // TODO make SaveState.AbstractCreatureFromString and whatever serializes creatures include position
//            IL.AbstractCreature.RealizeInRoom += AbstractCreature_RealizeInRoom;
//            IL.RegionState.AdaptWorldToRegionState += RegionState_AdaptWorldToRegionState;
//            On.RegionState.CreatureToStringInDenPos += RegionState_CreatureToStringInDenPos;
//        }

//        private string RegionState_CreatureToStringInDenPos(On.RegionState.orig_CreatureToStringInDenPos orig, RegionState self, AbstractCreature critter, int validSaveShelter, int activeGate)
//        {
//            try
//            {
//                var pos = GetPos(self, critter, validSaveShelter, activeGate);
//                if (pos == null || !self.world.IsRoomInRegion(pos.Value.room))
//                {
//                    return string.Empty;
//                }
//                return SaveState.AbstractCreatureToString(critter, pos.Value);
//            }
//            catch (Exception e)
//            {
//                Debug.Log($"Did not save creature {critter.creatureTemplate.name} {critter.ID} because: {e}");
//                return string.Empty;
//            }
//        }

//        private WorldCoordinate? GetPos(RegionState self, AbstractCreature critter, int validSaveShelter, int activeGate)
//        {
//            if (critter.creatureTemplate.type == CreatureTemplate.Type.Slugcat || critter.pos.room == activeGate)
//            {
//                return null;
//            }
//            if (critter.state.dead || critter.creatureTemplate.offScreenSpeed == 0f)
//            {
//                if (self.world.GetAbstractRoom(critter.pos).shelter)
//                {
//                    return rules.SaveShelterPositions ? critter.pos : new(critter.pos.room, -1, -1, 0);
//                }
//                Room? room = null;
//                RoomRain? rain = null;
//                return PersistenceApplies(ref room, ref rain, self.world, critter.pos) ? critter.pos : null;
//            }
//            if (critter.pos.room == validSaveShelter)
//            {
//                return rules.SaveShelterPositions ? critter.pos : new(critter.pos.room, -1, -1, 0);
//            }
//            return critter.abstractAI?.denPosition;
//        }

//        private static bool SkipVanillaCheck()
//        {
//            return persistence != null && persistence.Value != PersistenceEnum.None;
//        }

//        private void AbstractCreature_RealizeInRoom(ILContext il)
//        {
//            try
//            {
//                var cursor = new ILCursor(il);

//                // Find end of shelter check
//                if (!cursor.TryGotoNext(i => i.MatchCall<WorldCoordinate>("get_NodeDefined")))
//                {
//                    rules.Logger.LogError("RealizeInRoom: Missing instruction");
//                    return;
//                }

//                var brTo = cursor.Instrs[cursor.Index - 2];

//                // Go back to start of method
//                cursor.Index = 0;

//                // Emit skip
//                cursor.EmitDelegate<Func<bool>>(SkipVanillaCheck);
//                cursor.Emit(OpCodes.Brtrue, brTo);
//            }
//            catch (Exception e)
//            {
//                Console.WriteLine(e);
//            }
//        }

//        private void RegionState_AdaptWorldToRegionState(ILContext il)
//        {
//            try
//            {
//                var cursor = new ILCursor(il);
//                if (!cursor.TryGotoNext(i => i.MatchCallvirt<AbstractRoom>("MoveEntityToDen")))
//                {
//                    rules.Logger.LogError("AdaptWorldToRegionState: Missing instruction 1");
//                    return;
//                }

//                if (!cursor.TryGotoPrev(i => i.MatchLdarg(0)))
//                {
//                    rules.Logger.LogError("AdaptWorldToRegionState: Missing instruction 2");
//                    return;
//                }

//                // Set original instruction to no-op to interrupt branch instructions
//                cursor.Next.OpCode = OpCodes.Nop;
//                cursor.Next.Operand = null;

//                cursor.Index++;

//                // Emit skip
//                cursor.EmitDelegate<Func<bool>>(SkipVanillaCheck);
//                cursor.Emit(OpCodes.Brtrue, cursor.Instrs[cursor.Index + 5]);

//                // Re-add original instruction
//                cursor.Emit(OpCodes.Ldarg_0);
//            }
//            catch (Exception e)
//            {
//                Console.WriteLine(e);
//            }
//        }
//    }
//}
