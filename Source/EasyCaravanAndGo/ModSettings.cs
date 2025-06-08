using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace EasyCaravanAndGo
{
    public class EasyCaravanAndGo_Settings : ModSettings
    {
        public static bool enableFormCaravanButton = true;

        public static bool enableLordPatches = true;

        public static bool enableSetHitchingSpotButton = true;
        public static bool enableSetCaravanExitButton = true;
        public static bool enableCaravanGoButton = true;

        public static bool enableLoadOnCaravanFix = true;
        public static bool enableGatherDownedPawnsFixes = true;

        public static bool loaded_enableLordPatches = true;
        public static bool loaded_enableLoadOnCaravanFix = true;
        public static bool loaded_enableGatherDownedPawnsFixes = true;

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref enableFormCaravanButton, nameof(enableFormCaravanButton), true);

            Scribe_Values.Look(ref enableLordPatches, nameof(enableLordPatches), true);

            Scribe_Values.Look(ref enableSetHitchingSpotButton, nameof(enableSetHitchingSpotButton), true);
            Scribe_Values.Look(ref enableSetCaravanExitButton, nameof(enableSetCaravanExitButton), true);
            Scribe_Values.Look(ref enableCaravanGoButton, nameof(enableCaravanGoButton), true);

            Scribe_Values.Look(ref enableLoadOnCaravanFix, nameof(enableLoadOnCaravanFix), true);
            Scribe_Values.Look(ref enableGatherDownedPawnsFixes, nameof(enableGatherDownedPawnsFixes), true);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                loaded_enableLordPatches = enableLordPatches;
                loaded_enableLoadOnCaravanFix = enableLoadOnCaravanFix;
                loaded_enableGatherDownedPawnsFixes = enableGatherDownedPawnsFixes;
            }
        }
    }

    public class EasyCaravanAndGo_Mod : Mod
    {
        EasyCaravanAndGo_Settings settings;
        public EasyCaravanAndGo_Mod(ModContentPack content) : base(content)
        {
            this.settings = GetSettings<EasyCaravanAndGo_Settings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);

            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            listingStandard.CheckboxLabeled("ECAG_EnableFormCaravanButton".Translate(), ref EasyCaravanAndGo_Settings.enableFormCaravanButton);

            listingStandard.GapLine();

            listingStandard.CheckboxLabeled("ECAG_EnableLordPatches".Translate(), ref EasyCaravanAndGo_Settings.enableLordPatches);
            listingStandard.Label("ECAG_EnableLordPatches_desc".Translate());

            if (EasyCaravanAndGo_Settings.enableLordPatches)
            {
                listingStandard.Gap();
                listingStandard.CheckboxLabeled("ECAG_EnableSetHitchingSpotButton".Translate(), ref EasyCaravanAndGo_Settings.enableSetHitchingSpotButton);
                listingStandard.CheckboxLabeled("ECAG_EnableSetCaravanExitButton".Translate(), ref EasyCaravanAndGo_Settings.enableSetCaravanExitButton);
                listingStandard.CheckboxLabeled("ECAG_EnableCaravanGoButton".Translate(), ref EasyCaravanAndGo_Settings.enableCaravanGoButton);
            }

            listingStandard.GapLine();
            listingStandard.CheckboxLabeled("ECAG_EnableLoadOnCaravanFix".Translate(), ref EasyCaravanAndGo_Settings.enableLoadOnCaravanFix);
            listingStandard.Label("ECAG_EnableLoadOnCaravanFix_desc".Translate());

            listingStandard.GapLine();
            listingStandard.CheckboxLabeled("ECAG_EnableGatherDownedPawnsFixes".Translate(), ref EasyCaravanAndGo_Settings.enableGatherDownedPawnsFixes);
            listingStandard.Label("ECAG_EnableGatherDownedPawnsFixes_desc".Translate());

            if (EasyCaravanAndGo_Settings.loaded_enableLordPatches != EasyCaravanAndGo_Settings.enableLordPatches ||
                EasyCaravanAndGo_Settings.loaded_enableLoadOnCaravanFix != EasyCaravanAndGo_Settings.enableLoadOnCaravanFix ||
                EasyCaravanAndGo_Settings.loaded_enableGatherDownedPawnsFixes != EasyCaravanAndGo_Settings.enableGatherDownedPawnsFixes)
            {
                listingStandard.Gap(24);

                Color originalColor = GUI.color;
                GUI.color = Color.red;
                
                listingStandard.Label("ECAG_GameRestartRequired".Translate());

                if (EasyCaravanAndGo_Settings.loaded_enableLordPatches != EasyCaravanAndGo_Settings.enableLordPatches)
                { 
                    listingStandard.Label("ECAG_MakeSureNoCarvanIsForming".Translate());
                }

                GUI.color = originalColor;
            }

            listingStandard.End();
        }

        public override string SettingsCategory() => "Easy Caravan and Go!";
    }
}
