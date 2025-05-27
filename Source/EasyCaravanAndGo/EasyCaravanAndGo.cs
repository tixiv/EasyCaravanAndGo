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
using System.Runtime.ConstrainedExecution;
using static UnityEngine.GraphicsBuffer;
using static Verse.HediffCompProperties_RandomizeSeverityPhases;

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

            harmony.Patch(AccessTools.Method(typeof(LordJob_FormAndSendCaravan), "SendCaravan"),
                prefix: new HarmonyMethod(typeof(EasyCaravanAndGo), nameof(EasyCaravanAndGo.SendCaravan_Prefix)));

            harmony.Patch(AccessTools.Method(typeof(GiveToPackAnimalUtility), nameof(GiveToPackAnimalUtility.UsablePackAnimalWithTheMostFreeSpace)),
                postfix: new HarmonyMethod(typeof(EasyCaravanAndGo), nameof(EasyCaravanAndGo.UsablePackAnimalWithTheMostFreeSpace_Postfix)));

            Patch_GatherDownedPawns.Patch(harmony);

            harmony.PatchAll();

            toilsToPatch = new Dictionary<LordJob_FormAndSendCaravan, ToilsToPatch>();
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

        public static readonly Texture2D FormCaravanIcon = ContentFinder<Texture2D>.Get("UI/Commands/FormCaravan", true);
        public static readonly Texture2D SetPackingSpotIcon = ContentFinder<Texture2D>.Get("UI/EasyCaravanAndGo/SetPackingSpot", true);
        public static readonly Texture2D SetExitSpotIcon = ContentFinder<Texture2D>.Get("UI/EasyCaravanAndGo/SetExitSpot", true);
        public static readonly Texture2D CaravanLeaveIcon = ContentFinder<Texture2D>.Get("UI/EasyCaravanAndGo/CaravanLeave", true);

        public static void GetGizmos_Postfix(Pawn pawn, ref IEnumerable<Gizmo> __result)
        {
            List<Gizmo> newGizmos = new List<Gizmo>(__result);

            var lord = pawn.GetLord();

            if (lord != null && lord.LordJob is LordJob_FormAndSendCaravan lordJob)
            {
                newGizmos.Add(new Command_Action
                {
                    defaultLabel = "ECAG_SetHitchingSpot".Translate(),
                    defaultDesc = "ECAG_SetHitchingSpot_desc".Translate(),
                    icon = SetPackingSpotIcon,
                    action = () =>
                    {
                        TargetingParameters targetParams = CaravanPackingTargetParams();

                        void action(LocalTargetInfo target)
                        {
                            PatchPackingSpot(lordJob, target.Cell);
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
                    defaultLabel = "ECAG_SetExitCell".Translate(),
                    defaultDesc = "ECAG_SetExitCell_desc".Translate(),
                    icon = SetExitSpotIcon,
                    action = () =>
                    {
                        TargetingParameters targetParams = CaravanExitTargetParams();

                        void targeterAction(LocalTargetInfo target)
                        {
                            PatchExitSpot(lordJob, target.Cell);
                        }

                        Find.Targeter.BeginTargeting(
                            targetParams,
                            targeterAction,
                            null,
                            null,
                            TexCommand.Attack, // or null for default
                            true
                        );
                    }
                });

                newGizmos.Add(new Command_Action
                {
                    defaultLabel = "ECAG_CaravanLeave".Translate(),
                    defaultDesc = "ECAG_CaravanLeave_desc".Translate(),
                    icon = CaravanLeaveIcon,
                    action = () =>
                    {
                        lord.ReceiveMemo("CaravanLeaveNow");
                        Messages.Message("ECAG_CravanLeaveForced".Translate(), MessageTypeDefOf.CautionInput);
                    }
                });

                __result = newGizmos;
            }
            else if (!pawn.NonHumanlikeOrWildMan() && !CaravanFormingUtility.IsFormingCaravanOrDownedPawnToBeTakenByCaravan(pawn))
            {
                var formCaravanCommand = new Command_Action
                {
                    defaultLabel = "ECAG_FormCaravan".Translate(),
                    defaultDesc = "ECAG_FormCaravan_desc".Translate(),
                    icon = FormCaravanIcon,
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

                        // TODO: Use this instead, depending on closest map edge
                        // Maybe even patch it later if exit spot is changed
                        // int directionTile = CaravanExitMapUtility.FindRandomStartingTileBasedOnExitDir(exitFromTile, dir);

                        var exitSpot = TryFindExitSpot(pawn.Map, pawns, startingTile);
                        if (exitSpot.HasValue)
                        {
                            CaravanFormingUtility.StartFormingCaravan(pawns, new List<Pawn>(), Faction.OfPlayer, new List<TransferableOneWay>(), pawn.Position, exitSpot.Value, startingTile, neighborTile);
                            Messages.Message("CaravanFormationProcessStarted".Translate(), pawns[0], MessageTypeDefOf.PositiveEvent, false);
                        }
                    }
                };

                if (pawn.Downed)
                {
                    formCaravanCommand.Disable("IsIncapped".Translate(pawn.LabelShort, pawn));
                }
                if (pawn.Deathresting)
                {
                    formCaravanCommand.Disable("IsDeathresting".Translate(pawn.Named("PAWN")));
                }

                newGizmos.Add(formCaravanCommand);

                __result = newGizmos;
            }
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

        // One set of toils to patch per 'LordJob_FormAndSendCaravan'.
        public static Dictionary<LordJob_FormAndSendCaravan, ToilsToPatch> toilsToPatch;

        public static void PatchSafe(object obj, Type type, string fieldName, IntVec3 value)
        {

            if (obj != null)
            {
                var field = AccessTools.Field(type, fieldName);
                if (field != null)
                {
                    field.SetValue(obj, value);
                    // Log.Message($"Patched {type}.{fieldName} to {value}");
                }
                else
                    Log.Warning($"Field '{fieldName}' not found in {type}");
            }
            else
                Log.Warning($"{type} instance is null");
        }

        public static void PatchPackingSpot(LordJob_FormAndSendCaravan lordJob, IntVec3 packingSpot)
        {
            var lord = lordJob.lord;

            if (lord != null && toilsToPatch.ContainsKey(lordJob))
            {
                ToilsToPatch ttp = toilsToPatch[lordJob];

                // Patch all the packing spots
                PatchSafe(lordJob, typeof(LordJob_FormAndSendCaravan), "meetingPoint", packingSpot);
                PatchSafe(ttp.gatherAnimals, typeof(LordToil_PrepareCaravan_GatherAnimals), "destinationPoint", packingSpot);
                PatchSafe(ttp.gatherItems, typeof(LordToil_PrepareCaravan_GatherItems), "meetingPoint", packingSpot);
                PatchSafe(ttp.gatherDownedPawns, typeof(LordToil_PrepareCaravan_GatherDownedPawns), "meetingPoint", packingSpot);
                PatchSafe(ttp.wait, typeof(LordToil_PrepareCaravan_Wait), "meetingPoint", packingSpot);

                if (lord.CurLordToil is LordToil_PrepareCaravan_GatherAnimals)
                {
                    lord.CurLordToil.UpdateAllDuties();
                    TransitionAction_EndJobs.DoAction(lord);
                }
                else
                {
                    lord.ReceiveMemo("CaravanBackToGatherAnimals");
                }

                Messages.Message("ECAG_HitchingSpotUpdated".Translate(), MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Log.Warning("Patching packing spot failed: No lord, or toilsToPatch does not contain this LoardJob");
            }
        }

        public static void PatchExitSpot(LordJob_FormAndSendCaravan lordJob, IntVec3 exitSpot)
        {
            var lord = lordJob.lord;

            if (lord != null && toilsToPatch.ContainsKey(lordJob))
            {
                ToilsToPatch ttp = toilsToPatch[lordJob];

                // Patch all the exit spots
                PatchSafe(lordJob, typeof(LordJob_FormAndSendCaravan), "exitSpot", exitSpot);
                PatchSafe(ttp.gatherDownedPawns, typeof(LordToil_PrepareCaravan_GatherDownedPawns), "exitSpot", exitSpot);
                PatchSafe(ttp.collectAnimals, typeof(LordToil_PrepareCaravan_CollectAnimals), "destinationPoint", exitSpot);
                PatchSafe(ttp.leave, typeof(LordToil_PrepareCaravan_Leave), "exitSpot", exitSpot);

                lordJob.lord?.ReceiveMemo("CaravanBackToGatherDownedPawns");

                Messages.Message("ECAG_CravanExitUpdated".Translate(), MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Log.Warning("Patching caravan exit failed: No lord or toilsToPatch does not contain this LoardJob");
            }
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
            LordToil_PrepareCaravan_GatherAnimals gatherAnimals = __result.lordToils.OfType<LordToil_PrepareCaravan_GatherAnimals>().FirstOrDefault();
            LordToil_PrepareCaravan_GatherItems gatherItems = __result.lordToils.OfType<LordToil_PrepareCaravan_GatherItems>().FirstOrDefault();
            LordToil_PrepareCaravan_GatherDownedPawns gatherDownedPawns = __result.lordToils.OfType<LordToil_PrepareCaravan_GatherDownedPawns>().FirstOrDefault();
            LordToil_PrepareCaravan_Wait wait = __result.lordToils.OfType<LordToil_PrepareCaravan_Wait>().FirstOrDefault();
            LordToil_PrepareCaravan_CollectAnimals collectAnimals = __result.lordToils.OfType<LordToil_PrepareCaravan_CollectAnimals>().FirstOrDefault();
            LordToil_PrepareCaravan_Leave leave = __result.lordToils.OfType<LordToil_PrepareCaravan_Leave>().FirstOrDefault();

            if (toilsToPatch.ContainsKey(__instance))
            {
                Log.Warning("toilsToPatch already contained this LordJob when creating stategraph");
                toilsToPatch.Remove(__instance);
            }

            ToilsToPatch ttp;

            ttp.gatherAnimals = gatherAnimals;
            ttp.gatherItems = gatherItems;
            ttp.gatherDownedPawns = gatherDownedPawns;
            ttp.wait = wait;
            ttp.collectAnimals = collectAnimals;
            ttp.leave = leave;

            toilsToPatch.Add(__instance, ttp);

            string trigger;

            void addTransistion(LordToil from, LordToil to)
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

        public static bool SendCaravan_Prefix(LordJob_FormAndSendCaravan __instance)
        {
            // Clear our cached toils so they can be garbage collected
            toilsToPatch.Remove(__instance);
            return true;
        }

        public static void UsablePackAnimalWithTheMostFreeSpace_Postfix(Pawn pawn, ref Pawn __result)
        {
            // This fixes a  - bug I guess I would call it - in Rimworld where
            // when you "load onto caravan" things manually and you have pack animals, it
            // will never consider loading stuff onto the pawn itself. The result is that
            // You can load up your animals just fine, but you can't utilise the pawns
            // carrying capacity.
            // This attempts to work around this in a slightly dirty way: we check whether the
            // animal is "full" (less than 2 weight units left) and the pawn itself could carry more.
            // In that case we return null here so 'MenuMakerMap::AddHumalikeOrders()' will
            // choose the pawn itself because there "are no suitable carrier animals".

            if (__result != null && pawn != null)
            {

                float pawnCapacity = MassUtility.FreeSpace(pawn);
                float animalCapacity = MassUtility.FreeSpace(__result);

                if (animalCapacity < 2.0f && animalCapacity < pawnCapacity)
                { 
                    __result = null;
                }
            }
        }
    }


    // Static class to track the float menu options we want to modify
    public static class FloatMenuOptionTracker
    {
        public static List<FloatMenuOption> lastLoadIntoCaravan;
        public static List<FloatMenuOption> lastLoadIntoCaravanAll;
        public static List<FloatMenuOption> lastLoadIntoCaravanSome;

        public static void TrackLoadIntoCaravan(FloatMenuOption opt) { lastLoadIntoCaravan.Add(opt); }
        public static void TrackLoadIntoCaravanAll(FloatMenuOption opt) { lastLoadIntoCaravanAll.Add(opt); }
        public static void TrackLoadIntoCaravanSome(FloatMenuOption opt) { lastLoadIntoCaravanSome.Add(opt); }

        public static void Clear()
        {
            lastLoadIntoCaravan = new List<FloatMenuOption>();
            lastLoadIntoCaravanAll = new List<FloatMenuOption>();
            lastLoadIntoCaravanSome = new List<FloatMenuOption>();
        }
    }

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
                            // Log.Message($"Transpiler phase {phase} success");
                            phase++;
                        }
                        break;

                    case 1:
                        if (instruction.Calls(AccessTools.Method(typeof(FloatMenuUtility), nameof(FloatMenuUtility.DecoratePrioritizedTask))))
                        {
                            // Log.Message($"Transpiler phase {phase} success");
                            phase++;
                            yield return new CodeInstruction(OpCodes.Dup); // Duplicate the FloatMenuOption on stack
                            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FloatMenuOptionTracker), nameof(FloatMenuOptionTracker.TrackLoadIntoCaravan)));
                        }
                        break;

                    case 2:
                        if (instruction.Calls(AccessTools.Method(typeof(FloatMenuUtility), nameof(FloatMenuUtility.DecoratePrioritizedTask))))
                        {
                            // Log.Message($"Transpiler phase {phase} success");
                            phase++;
                            yield return new CodeInstruction(OpCodes.Dup); // Duplicate the FloatMenuOption on stack
                            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FloatMenuOptionTracker), nameof(FloatMenuOptionTracker.TrackLoadIntoCaravanAll)));
                        }
                        break;

                    case 3:
                        if (instruction.Calls(AccessTools.Method(typeof(FloatMenuUtility), nameof(FloatMenuUtility.DecoratePrioritizedTask))))
                        {
                            // Log.Message($"Transpiler phase {phase} success");
                            phase++;
                            yield return new CodeInstruction(OpCodes.Dup); // Duplicate the FloatMenuOption on stack
                            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FloatMenuOptionTracker), nameof(FloatMenuOptionTracker.TrackLoadIntoCaravanSome)));
                        }
                        break;

                    case 4:
                        break;
                }
            }

            if (phase != 4)
            {
                Log.Warning("Transpiler failed to patch FloatMenuMakerMap::AddHumanlikeOrders() correctly.");
            }
        }

        [HarmonyPrefix]
        public static bool Prefix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts)
        {
            FloatMenuOptionTracker.Clear();
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

            FloatMenuOptionTracker.Clear();
        }
    }

    // This patch changes the behaviour of the gather items lord toil so that the caravan forming
    // will remain in the state "gather items" as long as any caravan pawns still have one of these
    // two jobs, which are assigned to them when you load stuff onto an existing caravan through the float menu.
    // This is very helpfull if the caravan has pack animals, because it stops the caravan forming from progressing
    // to "collect animals" while you are still manually loading stuff on them.

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
