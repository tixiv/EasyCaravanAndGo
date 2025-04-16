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
                postfix: new HarmonyMethod(typeof(EasyCaravanAndGo), nameof(EasyCaravanAndGo.CreateGraph_Postfix))
            );

            harmony.PatchAll();
        }

        public static IntVec3? TryFindExitSpot(Map map, List<Pawn> pawns, int startingTile)
        {
            // We use the appropriate method from the normal carvan forming dialog

            // We instantiate the dialog here because the method we need is an instance method
            Dialog_FormCaravan fakeDialog = new Dialog_FormCaravan(map);
            
            // set the private 'startingTile'
            AccessTools.Field(typeof(Dialog_FormCaravan), "startingTile").SetValue(fakeDialog, startingTile);

            var method = AccessTools.Method(
                typeof(Dialog_FormCaravan),
                "TryFindExitSpot",
                new Type[] { typeof(List<Pawn>), typeof(bool), typeof(IntVec3).MakeByRefType() }
            );

            object[] parameters = new object[] { pawns, true, null }; // null for the out parameter

            bool result = (bool)method.Invoke(fakeDialog, parameters);

            if (!result)
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

                    if (!cell.IsValid || !cell.InBounds(map)|| !cell.Walkable(map) || cell.Fogged(map)|| !cell.OnEdge(map))
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

            if (lord != null && lord.LordJob is LordJob_FormAndSendCaravan lj)
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
                    icon = TexCommand.Attack, // You can customize this
                    action = () =>
                    {
                        TargetingParameters targetParams = CaravanExitTargetParams();

                        void action(LocalTargetInfo target)
                        {
                            IntVec3 exitSpot = target.Cell;

                            Messages.Message("Caravan exit set to " + exitSpot, MessageTypeDefOf.PositiveEvent);

                            // Patch all the exit spots
                            AccessTools.Field(typeof(LordJob_FormAndSendCaravan), "exitSpot").SetValue(lj, exitSpot);
                            PatchToils(exitSpot);
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
            public LordToil_PrepareCaravan_GatherDownedPawns gatherDownedPawns;
            public LordToil_PrepareCaravan_CollectAnimals collectAnimals;
            public LordToil_PrepareCaravan_Leave leave;
        }

        // I might want to make this a Dictionary if multiple caravans are being formed, which is actually a usecase, ChatGpt, thanks for reminding me!
        public static ToilsToPatch toilsToPatch;

        public static void PatchToils(IntVec3 exitSpot)
        {
            if (toilsToPatch.gatherDownedPawns != null)
            {
                var field = AccessTools.Field(typeof(LordToil_PrepareCaravan_GatherDownedPawns), "exitSpot");
                if (field != null)
                {
                    field.SetValue(toilsToPatch.gatherDownedPawns, exitSpot);
                    Log.Message($"Patched 'gatherDownedPawns.exitSpot' to {exitSpot}");
                }
                else
                {
                    Log.Warning("Field 'exitSpot' not found in LordToil_PrepareCaravan_GatherDownedPawns");
                }
            }
            else
            {
                Log.Warning("toilsToPatch.gatherDownedPawns is null");
            }

            if (toilsToPatch.collectAnimals != null)
            {
                var field = AccessTools.Field(typeof(LordToil_PrepareCaravan_CollectAnimals), "destinationPoint");
                if (field != null)
                {
                    field.SetValue(toilsToPatch.collectAnimals, exitSpot);
                    Log.Message($"Patched 'collectAnimals.destinationPoint' to {exitSpot}");
                }
                else
                {
                    Log.Warning("Field 'destinationPoint' not found in LordToil_PrepareCaravan_CollectAnimals");
                }
            }
            else
            {
                Log.Warning("toilsToPatch.collectAnimals is null");
            }

            if (toilsToPatch.leave != null)
            {
                var field = AccessTools.Field(typeof(LordToil_PrepareCaravan_Leave), "exitSpot");
                if (field != null)
                {
                    field.SetValue(toilsToPatch.leave, exitSpot);
                    Log.Message($"Patched 'leave.exitSpot' to {exitSpot}");
                }
                else
                {
                    Log.Warning("Field 'exitSpot' not found in LordToil_PrepareCaravan_Leave");
                }
            }
            else
            {
                Log.Warning("toilsToPatch.leave is null");
            }
        }

        public static void CreateGraph_Postfix(StateGraph __result, LordJob_FormAndSendCaravan __instance)
        {
            // 🧠 Your logic here: add stuff to the graph
            // Example:
            Log.Message("Adding node to the StateGraph...");

            LordToil                                  gatherAnimals     = __result.lordToils.OfType<LordToil_PrepareCaravan_GatherAnimals>().FirstOrDefault();
            LordToil                                  gatherItems       = __result.lordToils.OfType<LordToil_PrepareCaravan_GatherItems>().FirstOrDefault();
            LordToil_PrepareCaravan_GatherDownedPawns gatherDownedPawns = __result.lordToils.OfType<LordToil_PrepareCaravan_GatherDownedPawns>().FirstOrDefault();
            LordToil_PrepareCaravan_CollectAnimals    collectAnimals    = __result.lordToils.OfType<LordToil_PrepareCaravan_CollectAnimals>().FirstOrDefault();
            LordToil_PrepareCaravan_Leave             leave             = __result.lordToils.OfType<LordToil_PrepareCaravan_Leave>().FirstOrDefault();


            if (collectAnimals != null)
            {
                if (gatherAnimals != null)
                {
                    Transition transition = new Transition(gatherAnimals, collectAnimals);
                    transition.AddTrigger(new Trigger_Memo("CaravanLeaveNow"));
                    __result.AddTransition(transition);
                }
                if (gatherItems != null)
                {
                    Transition transition = new Transition(gatherItems, collectAnimals);
                    transition.AddTrigger(new Trigger_Memo("CaravanLeaveNow"));
                    __result.AddTransition(transition);
                }
                if (gatherDownedPawns != null)
                {
                    Transition transition = new Transition(gatherDownedPawns, collectAnimals);
                    transition.AddTrigger(new Trigger_Memo("CaravanLeaveNow"));
                    __result.AddTransition(transition);
                }
            }

            if (leave != null && gatherItems != null)
            {
                {
                    Transition transition = new Transition(leave, gatherItems);
                    transition.AddTrigger(new Trigger_Memo("CaravanBackToPacking"));
                    __result.AddTransition(transition);
                }
            }

            if (gatherDownedPawns != null)
                toilsToPatch.gatherDownedPawns = gatherDownedPawns;

            if (collectAnimals != null)
                toilsToPatch.collectAnimals = collectAnimals;

            if (leave != null)
                toilsToPatch.leave = leave;

        }

        public static void daFloatMenuePatch(Pawn pawn, JobDef myJob, Pawn targetAnimal)
        {

            // MyDefOf.LoadOntoAnimal

            JobDriver_PrepareCaravan_GatherItems.IsUsableCarrier(targetAnimal, pawn, true);

            FloatMenuOption option = new FloatMenuOption("Load items onto pack animal", () =>
            {
                Job job = JobMaker.MakeJob(myJob, targetAnimal);
                pawn.jobs.TryTakeOrderedJob(job);
            });
        }


        public static readonly Texture2D AddToCaravanCommand = ContentFinder<Texture2D>.Get("UI/Commands/AddToCaravan", true);
    }

}
