using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EasyCaravanAndGo
{
    class JobDriver_PackInventoryOnCaravanAnimal : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // return base.Map.lordManager.lords.Contains(this.job.lord) && (Pawn)TargetThingA != null && JobDriver_PrepareCaravan_GatherItems.IsUsableCarrier((Pawn)TargetThingA, pawn, false);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            //  this.FailOn(() => !base.Map.lordManager.lords.Contains(this.job.lord) || (Pawn)TargetThingA == null);// || !JobDriver_PrepareCaravan_GatherItems.IsUsableCarrier((Pawn)TargetThingA, pawn, false));

            // FailOnDespawnedOrNull(TargetIndex.A);
            // FailOnDead(TargetIndex.A);

            Log.Message($"Make new Toils called TargetThingA={TargetThingA}");

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch, false);
            yield return Toils_General.Wait(25, TargetIndex.None);
            yield return PutStuffOnAnimal(pawn, (Pawn)TargetThingA);
        }


        public void TransferItemsToCarrier(Pawn pawn, Pawn carrier)
        {
            Log.Message("TransferItemsToCarrier called");

            List<Thing> list = new List<Thing>();
            list.AddRange(pawn.inventory.innerContainer);
            foreach (Thing thing in list)
            {
                if (MassUtility.IsOverEncumbered(carrier))
                    break;

                pawn.inventory.innerContainer.TryTransferToContainer(thing, carrier.inventory.innerContainer, thing.stackCount, true);
            }
        }

        private Toil PutStuffOnAnimal(Pawn pawn, Pawn animal)
        {
            Toil toil = ToilMaker.MakeToil("PutStuffOnAnimal");
            toil.initAction = delegate ()
            {
                TransferItemsToCarrier(pawn, animal);
            };
            return toil;
        }
    }

    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.ChoicesAtFor))]
    public static class FloatMenuMakerMap_ChoicesAtFor_Patch
    {
        [HarmonyPostfix]
        public static void AddPackInventoryOption(ref List<FloatMenuOption> __result, Vector3 clickPos, Pawn pawn, bool suppressAutoTakeableGoto)
        {
            IntVec3 c = IntVec3.FromVector3(clickPos);
            Map map = pawn.Map;

            foreach (Thing thing in c.GetThingList(map))
            {
                if (thing is Pawn animal &&
                    animal.RaceProps.Animal &&
                    animal.Faction == Faction.OfPlayer &&
                    //pawn.inventory?.innerContainer != null &&
                    //pawn.inventory.innerContainer.Count > 0 &&
                    animal.inventory != null &&
                    animal.Map == pawn.Map)
                {
                    //if (!JobDriver_PrepareCaravan_GatherItems.IsUsableCarrier(animal, pawn, false))
                    //    continue;

                    string label = $"Pack inventory onto {animal.LabelShortCap}";
                    FloatMenuOption option = new FloatMenuOption(label, () =>
                    {
                        pawn.GetLord()?.ReceiveMemo("CaravanBackToPacking");
                        Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("PackInventoryOnCaravanAnimal"), animal);
                        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    });

                    __result.Add(option);
                }
            }
        }
    }
}
