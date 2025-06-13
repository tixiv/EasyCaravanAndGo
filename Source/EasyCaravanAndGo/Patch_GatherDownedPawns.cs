using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using static EasyCaravanAndGo.EasyCaravanAndGo;
using static UnityEngine.Scripting.GarbageCollector;

namespace EasyCaravanAndGo
{
    public static class Patch_GatherDownedPawns
    {
        public static void Patch(Harmony harmony)
        {
            // Log.Message("Patch IsDownedPawnNearExitPoint");

            harmony.Patch(AccessTools.Method(typeof(JobGiver_PrepareCaravan_GatherDownedPawns), "IsDownedPawnNearExitPoint"),
                prefix: new HarmonyMethod(typeof(Patch_GatherDownedPawns), nameof(Patch_GatherDownedPawns.IsDownedPawnNearExitPoint)));

            // Log.Message("Patch FindDownedPawn");

            harmony.Patch(AccessTools.Method(typeof(JobGiver_PrepareCaravan_GatherDownedPawns), "FindDownedPawn"),
                transpiler: new HarmonyMethod(typeof(Patch_GatherDownedPawns), nameof(Patch_GatherDownedPawns.Transpiler_JobGiver)));

            // Log.Message("Patch CheckAllPawnsArrived");

            harmony.Patch(AccessTools.Method(typeof(LordToil_PrepareCaravan_GatherDownedPawns), "CheckAllPawnsArrived"),
                transpiler: new HarmonyMethod(typeof(Patch_GatherDownedPawns), nameof(Patch_GatherDownedPawns.Transpiler_LordToil)));

            // Log.Message("Patch ExitMapAndCreateCaravan");

#if RIMWORLD_1_5

            harmony.Patch(AccessTools.Method(typeof(CaravanExitMapUtility), nameof(CaravanExitMapUtility.ExitMapAndCreateCaravan),
                new Type[] {typeof(IEnumerable<Pawn>), typeof(Faction), typeof(int) /* exitFromTile */, typeof(int) /* directionTile */,
                    typeof(int) /* destinationTile */, typeof(bool) /* sendMessage */ }),
                prefix: new HarmonyMethod(typeof(Patch_GatherDownedPawns), nameof(ExitMapAndCreateCaravan_Prefix)));
#elif RIMWORLD_1_6

            harmony.Patch(AccessTools.Method(typeof(CaravanExitMapUtility), nameof(CaravanExitMapUtility.ExitMapAndCreateCaravan),
                new Type[] {typeof(IEnumerable<Pawn>), typeof(Faction), typeof(PlanetTile) /* exitFromTile */, typeof(PlanetTile) /* directionTile */,
                    typeof(PlanetTile) /* destinationTile */, typeof(bool) /* sendMessage */ }),
                prefix: new HarmonyMethod(typeof(Patch_GatherDownedPawns), nameof(ExitMapAndCreateCaravan_Prefix)));
#endif
        }


        // Increase distance form 7 to 8 so pawn is not sometimes "forgotten".
        // This version of the check is mainly relevant for 
        // LordToil_PrepareCaravan_GatherDownedPawns::CheckAllPawnsArrived().

        public static bool IsDownedPawnNearExitPoint(ref bool __result, Pawn downedPawn, IntVec3 exitPoint)
        {

            __result = downedPawn.PositionHeld.InHorDistOf(exitPoint, 8f);

            return false; // don't run original method
        }

        // Check whether pawn is at exit point or is being carried
        // The original check is replaced with this one for the JobGiver anf the LordToil,
        // so that no one tries to carry a pawn that is already being carried,
        // or the caravan process gets stuck in this lord toil because the pawn is not at the
        // exit spot.

        public static bool IsDownedPawnNearExitPoint_OrBeingCarried(Pawn downedPawn, IntVec3 exitPoint)
        {
            return downedPawn.PositionHeld.InHorDistOf(exitPoint, 8f) || downedPawn.holdingOwner?.Owner != null;
        }

        // We use this transpiler for the following two patches
        // It exchhanges the call to 'IsDownedPawnNearExitPoint()' with our own 'IsDownedPawnNearExitPoint_OrBeingCarried()' method.
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, string methodName)
        {
            bool found = false;

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(AccessTools.Method(
                    typeof(JobGiver_PrepareCaravan_GatherDownedPawns),
                    nameof(JobGiver_PrepareCaravan_GatherDownedPawns.IsDownedPawnNearExitPoint))))
                {
                    // Log.Message($"Transpiler {methodName} success");

                    found = true;

                    yield return new CodeInstruction(
                        OpCodes.Call, AccessTools.Method(typeof(Patch_GatherDownedPawns), nameof(IsDownedPawnNearExitPoint_OrBeingCarried)));
                }
                else
                {
                    yield return instruction;
                }
            }

            if (!found)
            {
                Log.Warning($"Transpiler failed patching {methodName}");
            }
        }

        // Aplly the transpiler here so the jobGiver doesn't give jobs to carry a pawn that is already being carried.
        public static IEnumerable<CodeInstruction> Transpiler_JobGiver(IEnumerable<CodeInstruction> instructions)
        {
            return Transpiler(instructions, "JobGiver_PrepareCaravan_GatherDownedPawns::FindDownedPawn()");
        }

        // Apply the transpiler here so the LordToil can finish without the carried pawn having reached the exit spot.
        public static IEnumerable<CodeInstruction> Transpiler_LordToil(IEnumerable<CodeInstruction> instructions)
        {
            return Transpiler(instructions, "LordToil_PrepareCaravan_GatherDownedPawns::CheckAllPawnsArrived()");
        }


        // Drop the carried pawn if someone is still carrying it at the exit spot
        public static bool ExitMapAndCreateCaravan_Prefix(IEnumerable<Pawn> pawns)
        {
            foreach (Pawn pawn in pawns)
            {
                var carryTracker = pawn.holdingOwner?.Owner as Pawn_CarryTracker;
                Pawn carrier = carryTracker?.pawn;

                if (carrier != null) {
                    // Drop it at the carrier's current position
                    carryTracker.innerContainer.TryDrop(pawn, carrier.Position, carrier.MapHeld, ThingPlaceMode.Near, 1, out Thing droppedThing);
                }
            }
            return true;
        }
    }
}
