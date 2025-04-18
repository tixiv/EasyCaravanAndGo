using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using RimWorld.Planet;
using RimWorld;
using Verse;
using Verse.AI.Group;
using Verse.AI;
using System.Reflection.Emit;
using System.Reflection;
using System.Collections;
using System.Diagnostics;

namespace EasyCaravanAndGo
{
    [StaticConstructorOnStartup]
    public static class EasyCaravanAndGo
    {
        static EasyCaravanAndGo()
        {
            var harmony = new Harmony("com.Tixiv.EasyCaravanAndGo");

            harmony.Patch(AccessTools.Method(typeof(CaravanFormingUtility), nameof(CaravanFormingUtility.GetGizmos)),
                postfix: new HarmonyMethod(typeof(EasyCaravanAndGo), nameof(EasyCaravanAndGo.GetGizmos_Postfix)));

            harmony.Patch(AccessTools.Method(typeof(LordJob_FormAndSendCaravan), nameof(LordJob_FormAndSendCaravan.CreateGraph)),
                postfix: new HarmonyMethod(typeof(EasyCaravanAndGo), nameof(EasyCaravanAndGo.CreateGraph_Postfix)));

            harmony.PatchAll();
        }

        public static IntVec3? TryFindExitSpot(Map map, List<Pawn> pawns, int startingTile)
        {
            // We use the appropriate method from the normal carvan forming dialog

            // We instantiate the dialog here because the method we need is an instance method
            Dialog_FormCaravan fakeDialog = new Dialog_FormCaravan(map);

            // set the private 'startingTile'
            AccessTools.Field(typeof(Dialog_FormCaravan), "startingTile").SetValue(fakeDialog, startingTile);

            var method = AccessTools.Method(typeof(Dialog_FormCaravan), "TryFindExitSpot",
                new Type[] { typeof(List<Pawn>), typeof(bool), typeof(IntVec3).MakeByRefType() });

            object[] parameters = new object[] { pawns, true, null }; // null for the out parameter

            if (!(bool)method.Invoke(fakeDialog, parameters))
                return null;

            return (IntVec3)parameters[2]; // out parameter is modified in-place
        }

        public static TargetingParameters CaravanExitTargetParams()
        {
            return new TargetingParameters
            {
                canTargetLocations = true,
                validator = (TargetInfo target) =>
                {
                    Map map = target.Map;
                    IntVec3 cell = target.Cell;

                    if (!cell.IsValid || !cell.InBounds(map) || !cell.Walkable(map) || cell.Fogged(map) || !cell.CloseToEdge(map, 4))
                        return false;

                    return true;
                }
            };
        }
        public static TargetingParameters CaravanPackingTargetParams()
        {
            return new TargetingParameters
            {
                canTargetLocations = true,
                validator = (TargetInfo target) =>
                {
                    Map map = target.Map;
                    IntVec3 cell = target.Cell;

                    if (!cell.IsValid || !cell.InBounds(map) || !cell.Walkable(map) || cell.Fogged(map))
                        return false;

                    return true;
                }
            };
        }

        public static void GetGizmos_Postfix(Pawn pawn, ref IEnumerable<Gizmo> __result)
        {
            if (pawn.NonHumanlikeOrWildMan())
                return;

            List<Gizmo> newGizmos = new List<Gizmo>(__result);

            var lord = pawn.GetLord();

            if (lord != null && lord.LordJob is LordJob_FormAndSendCaravan lordJob)
            {
                newGizmos.Add(new Command_Action
                {
                    defaultLabel = "Caravan Leave",
                    defaultDesc = "Force the caravan to leave now.",
                    icon = TexCommand.Attack,
                    action = () =>
                    {
                        lord.ReceiveMemo("CaravanLeaveNow");
                    }
                });

                newGizmos.Add(new Command_Action
                {
                    defaultLabel = "Select Exit Cell",
                    defaultDesc = "Choose a location on the edge of the map where the caravan should exit.",
                    icon = TexCommand.Attack,
                    action = () =>
                    {
                        TargetingParameters targetParams = CaravanExitTargetParams();

                        void action(LocalTargetInfo target)
                        {
                            IntVec3 exitSpot = target.Cell;

                            Messages.Message("Caravan exit set to " + exitSpot, MessageTypeDefOf.PositiveEvent);

                            // Patch all the exit spots
                            patchSafe(lordJob, typeof(LordJob_FormAndSendCaravan), "exitSpot", exitSpot);
                            patchSafe(toilsToPatch.gatherDownedPawns, typeof(LordToil_PrepareCaravan_GatherDownedPawns), "exitSpot", exitSpot);
                            patchSafe(toilsToPatch.collectAnimals, typeof(LordToil_PrepareCaravan_CollectAnimals), "destinationPoint", exitSpot);
                            patchSafe(toilsToPatch.leave, typeof(LordToil_PrepareCaravan_Leave), "exitSpot", exitSpot);

                            lord.ReceiveMemo("CaravanBackToGatherDownedPawns");
                        }

                        Find.Targeter.BeginTargeting(
                            targetParams,
                            action,
                            null,
                            null,
                            TexCommand.Attack, // or null for default
                            true
                        );
                    }
                });

                newGizmos.Add(new Command_Action
                {
                    defaultLabel = "Set Packing Spot",
                    defaultDesc = "Update the packing spot for this caravan.",
                    icon = TexCommand.Attack,
                    action = () =>
                    {
                        TargetingParameters targetParams = CaravanPackingTargetParams();

                        void action(LocalTargetInfo target)
                        {
                            IntVec3 packingSpot = target.Cell;

                            Messages.Message("Caravan packing spot set to " + packingSpot, MessageTypeDefOf.PositiveEvent);
                            Log.Message("Caravan packing spot set to " + packingSpot);

                            // Patch all the packing spots
                            patchSafe(lordJob, typeof(LordJob_FormAndSendCaravan), "meetingPoint", packingSpot);
                            patchSafe(toilsToPatch.gatherAnimals, typeof(LordToil_PrepareCaravan_GatherAnimals), "destinationPoint", packingSpot);
                            patchSafe(toilsToPatch.gatherItems, typeof(LordToil_PrepareCaravan_GatherItems), "meetingPoint", packingSpot);
                            patchSafe(toilsToPatch.gatherDownedPawns, typeof(LordToil_PrepareCaravan_GatherDownedPawns), "meetingPoint", packingSpot);
                            patchSafe(toilsToPatch.wait, typeof(LordToil_PrepareCaravan_Wait), "meetingPoint", packingSpot);

                            if (lord.CurLordToil is LordToil_PrepareCaravan_GatherAnimals)
                            {
                                lord.CurLordToil.UpdateAllDuties();
                                TransitionAction_EndJobs.DoAction(lord);
                            }
                            else
                                lord.ReceiveMemo("CaravanBackToGatherAnimals");
                        }

                        Find.Targeter.BeginTargeting(
                            targetParams,
                            action,
                            null,
                            null,
                            TexCommand.Attack, // or null for default
                            true
                        );
                    }
                });


            }
            else
            {

                newGizmos.Add(new Command_Action
                {
                    defaultLabel = "Form Caravan",
                    defaultDesc = "Form a caravan that only includes this pawn",
                    icon = AddToCaravanCommand,
                    action = () =>
                    {
                        List<Pawn> pawns = new List<Pawn>();
                        pawns.Add(pawn);

                        if (pawn == null || pawn.MapHeld == null || pawn.MapHeld.Parent == null)
                        {
                            Log.Error("pawn, pawn.MapHeld or pawn.Mapheld.Parent is null. Can't form Caravan");
                            return;
                        }

                        int startingTile = pawn.Map.Parent.Tile;

                        List<int> surroundingTiles = new List<int>();
                        Find.WorldGrid.GetTileNeighbors(startingTile, surroundingTiles);
                        int neighborTile = surroundingTiles.First();

                        var exitSpot = TryFindExitSpot(pawn.Map, pawns, startingTile);
                        if (exitSpot.HasValue)
                        {
                            CaravanFormingUtility.StartFormingCaravan(pawns, new List<Pawn>(), Faction.OfPlayer, new List<TransferableOneWay>(), pawn.Position, exitSpot.Value, startingTile, neighborTile);
                            Messages.Message("CaravanFormationProcessStarted".Translate(), pawns[0], MessageTypeDefOf.PositiveEvent, false);
                        }
                    }
                });
            }

            __result = newGizmos;
        }

        public struct ToilsToPatch
        {
            public LordToil_PrepareCaravan_GatherAnimals gatherAnimals;
            public LordToil_PrepareCaravan_GatherItems gatherItems;
            public LordToil_PrepareCaravan_GatherDownedPawns gatherDownedPawns;
            public LordToil_PrepareCaravan_Wait wait;
            public LordToil_PrepareCaravan_CollectAnimals collectAnimals;
            public LordToil_PrepareCaravan_Leave leave;
        }

        // I might want to make this a Dictionary if multiple caravans are being formed, which is actually a usecase, ChatGpt, thanks for reminding me!
        public static ToilsToPatch toilsToPatch;


        public static void patchSafe(object obj, Type type, string fieldName, IntVec3 value)
        {

            if (obj != null)
            {
                var field = AccessTools.Field(type, fieldName);
                if (field != null)
                {
                    field.SetValue(obj, value);
                    Log.Message($"Patched {type}.{fieldName} to {value}");
                }
                else
                    Log.Warning($"Field '{fieldName}' not found in {type}");
            }
            else
                Log.Warning($"{type} instance is null");
        }

        public class TransitionAction_EndJobs : TransitionAction
        {
            public override void DoAction(Transition trans)
            {
                DoAction(trans.target.lord);
            }

            public static void DoAction(Lord lord)
            {
                foreach (Pawn p in lord.ownedPawns)
                {
                    Job job = p.jobs?.curJob;
                    if (job != null && job.def != JobDefOf.TakeInventory && job.def != JobDefOf.GiveToPackAnimal)
                    {
                        p.jobs.EndCurrentJob(JobCondition.InterruptForced, true, false);
                    }
                }
            }
        }

        public static void CreateGraph_Postfix(StateGraph __result, LordJob_FormAndSendCaravan __instance)
        {
            Log.Message("Adding transisiont to the StateGraph...");

            LordToil_PrepareCaravan_GatherAnimals gatherAnimals = __result.lordToils.OfType<LordToil_PrepareCaravan_GatherAnimals>().FirstOrDefault();
            LordToil_PrepareCaravan_GatherItems gatherItems = __result.lordToils.OfType<LordToil_PrepareCaravan_GatherItems>().FirstOrDefault();
            LordToil_PrepareCaravan_GatherDownedPawns gatherDownedPawns = __result.lordToils.OfType<LordToil_PrepareCaravan_GatherDownedPawns>().FirstOrDefault();
            LordToil_PrepareCaravan_Wait wait = __result.lordToils.OfType<LordToil_PrepareCaravan_Wait>().FirstOrDefault();
            LordToil_PrepareCaravan_CollectAnimals collectAnimals = __result.lordToils.OfType<LordToil_PrepareCaravan_CollectAnimals>().FirstOrDefault();
            LordToil_PrepareCaravan_Leave leave = __result.lordToils.OfType<LordToil_PrepareCaravan_Leave>().FirstOrDefault();

            toilsToPatch.gatherAnimals = gatherAnimals;
            toilsToPatch.gatherItems = gatherItems;
            toilsToPatch.gatherDownedPawns = gatherDownedPawns;
            toilsToPatch.wait = wait;
            toilsToPatch.collectAnimals = collectAnimals;
            toilsToPatch.leave = leave;

            string trigger;

            void addTransistion(LordToil from, LordToil to, bool endAllJobs = true)
            {
                if (from != null && to != null)
                {
                    Transition transition = new Transition(from, to);
                    transition.AddTrigger(new Trigger_Memo(trigger));
                    transition.AddPostAction(new TransitionAction_EndJobs());
                    __result.AddTransition(transition);
                }
            }

            trigger = "CaravanLeaveNow";
            addTransistion(gatherAnimals, collectAnimals);
            addTransistion(gatherItems, collectAnimals);
            addTransistion(gatherDownedPawns, collectAnimals);

            trigger = "CaravanBackToGatherAnimals";
            addTransistion(gatherItems, gatherAnimals);
            addTransistion(gatherDownedPawns, gatherAnimals);
            addTransistion(wait, gatherAnimals);
            addTransistion(collectAnimals, gatherAnimals);
            addTransistion(leave, gatherAnimals);

            trigger = "CaravanBackToGatherItems";
            addTransistion(gatherDownedPawns, gatherItems);
            addTransistion(wait, gatherItems);
            addTransistion(collectAnimals, gatherItems);
            addTransistion(leave, gatherItems);

            trigger = "CaravanBackToGatherDownedPawns";
            addTransistion(wait, gatherDownedPawns);
            addTransistion(collectAnimals, gatherDownedPawns);
            addTransistion(leave, gatherDownedPawns);


        }

        public static readonly Texture2D AddToCaravanCommand = ContentFinder<Texture2D>.Get("UI/Commands/AddToCaravan", true);
    }


    // Static class to track the float menu option we want to modify
    public static class FloatMenuOptionTracker
    {
        public static List<FloatMenuOption> lastLoadIntoCaravan;
        public static List<FloatMenuOption> lastLoadIntoCaravanAll;
        public static List<FloatMenuOption> lastLoadIntoCaravanSome;

        public static void TrackLoadIntoCaravan(FloatMenuOption opt) { lastLoadIntoCaravan.Add(opt); }
        public static void TrackLoadIntoCaravanAll(FloatMenuOption opt) { lastLoadIntoCaravanAll.Add(opt); }
        public static void TrackLoadIntoCaravanSome(FloatMenuOption opt) { lastLoadIntoCaravanSome.Add(opt); }
    }

    // Patch the private static method
    [HarmonyPatch(typeof(FloatMenuMakerMap), "AddHumanlikeOrders")]
    public static class Patch_AddHumanlikeOrders
    {
        // Transpiler to intercept and track the FloatMenuOption
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int phase = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;

                switch (phase)
                {
                    case 0:
                        if (instruction.Calls(AccessTools.Method(typeof(CaravanFormingUtility), nameof(CaravanFormingUtility.CapacityLeft))))
                        {
                            Log.Message($"Transpiler phase {phase} success");
                            phase++;
                        }
                        break;

                    case 1:
                        if (instruction.Calls(AccessTools.Method(typeof(FloatMenuUtility), nameof(FloatMenuUtility.DecoratePrioritizedTask))))
                        {
                            Log.Message($"Transpiler phase {phase} success");
                            phase++;
                            yield return new CodeInstruction(OpCodes.Dup); // Duplicate the FloatMenuOption on stack
                            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FloatMenuOptionTracker), nameof(FloatMenuOptionTracker.TrackLoadIntoCaravan)));
                        }
                        break;

                    case 2:
                        if (instruction.Calls(AccessTools.Method(typeof(FloatMenuUtility), nameof(FloatMenuUtility.DecoratePrioritizedTask))))
                        {
                            Log.Message($"Transpiler phase {phase} success");
                            phase++;
                            yield return new CodeInstruction(OpCodes.Dup); // Duplicate the FloatMenuOption on stack
                            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FloatMenuOptionTracker), nameof(FloatMenuOptionTracker.TrackLoadIntoCaravanAll)));
                        }
                        break;

                    case 3:
                        if (instruction.Calls(AccessTools.Method(typeof(FloatMenuUtility), nameof(FloatMenuUtility.DecoratePrioritizedTask))))
                        {
                            Log.Message($"Transpiler phase {phase} success");
                            phase++;
                            yield return new CodeInstruction(OpCodes.Dup); // Duplicate the FloatMenuOption on stack
                            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FloatMenuOptionTracker), nameof(FloatMenuOptionTracker.TrackLoadIntoCaravanSome)));
                        }
                        break;

                    case 4:
                        break;
                }
            }
        }

        [HarmonyPrefix]
        public static bool Prefix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts)
        {
            FloatMenuOptionTracker.lastLoadIntoCaravan = new List<FloatMenuOption>();
            FloatMenuOptionTracker.lastLoadIntoCaravanAll = new List<FloatMenuOption>();
            FloatMenuOptionTracker.lastLoadIntoCaravanSome = new List<FloatMenuOption>();

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts)
        {
            void patchOption(FloatMenuOption option)
            {
                if (option.action != null)
                {
                    Action originalAction = option.action;

                    void newAction()
                    {

                        Log.Message("New Action !!");

                        originalAction();

                        pawn?.GetLord()?.ReceiveMemo("CaravanBackToGatherItems");
                    }

                    option.action = newAction;
                }
            }

            foreach (FloatMenuOption option in FloatMenuOptionTracker.lastLoadIntoCaravan)
                patchOption(option);

            foreach (FloatMenuOption option in FloatMenuOptionTracker.lastLoadIntoCaravanAll)
                patchOption(option);

            foreach (FloatMenuOption option in FloatMenuOptionTracker.lastLoadIntoCaravanSome)
                patchOption(option);
        }
    }

    [HarmonyPatch(typeof(LordToil_PrepareCaravan_GatherItems), nameof(LordToil_PrepareCaravan_GatherItems.LordToilTick))]
    public static class Patch_LordToil_GatherItems
    {
        [HarmonyPrefix]
        public static bool Prefix(LordToil_PrepareCaravan_GatherItems __instance)
        {
            if (Find.TickManager.TicksGame % 120 == 0)
            {
                foreach (Pawn p in __instance.lord.ownedPawns)
                {
                    Job job = p.CurJob;
                    if (job != null && (job.def == JobDefOf.TakeInventory || job.def == JobDefOf.GiveToPackAnimal))
                        return false;
                }
            }

            return true;
        }
    }
}
