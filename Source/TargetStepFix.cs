using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BillOrganizer
{
    /// <summary>
    /// "Until X" steps by what one craft yields, for every recipe and every UI that reads recipe.targetCountAdjustment (the vanilla bill
    /// row -/+ in Bill_Production.DoConfigInterface, Nice Bill Tab, other mods). CE puts 20 on every ammo recipe (AmmoRecipeBase), so a
    /// x5 mortar shell recipe stepped by 20. The field is only read by those -/+ buttons (verified in the decompiled 1.7 core), so
    /// rewriting it is UI-only. Applied when a game starts or loads (all defs exist, including the ones CE builds in code) and when the
    /// setting changes; turning the setting off restores the original values.
    /// </summary>
    public static class TargetStepFix
    {
        static readonly Dictionary<RecipeDef, int> original = new Dictionary<RecipeDef, int>();

        public static bool On => BillOrganizerMod.Settings == null || BillOrganizerMod.Settings.stepByBatch;

        public static void Apply()
        {
            try
            {
                bool on = On;
                int changed = 0;
                foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefsListForReading)
                {
                    if (r?.products == null || r.products.Count == 0 || r.products[0] == null) continue;
                    if (!original.TryGetValue(r, out int orig)) { orig = r.targetCountAdjustment; original[r] = orig; }
                    int want = on ? Math.Max(r.products[0].count, 1) : orig;
                    if (r.targetCountAdjustment != want) { r.targetCountAdjustment = want; changed++; }
                }
                if (Prefs.DevMode && changed > 0) Log.Message("[BillOrganizer] 'Until X' step " + (on ? "set to the items per craft" : "restored") + " on " + changed + " recipes.");
            }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] 'Until X' step fix failed: " + e, 0x0B110010); }
        }

        /// <summary>
        /// Puts the "until X" targets of the bills already on the maps on whole batches (once per game start/load). Vanilla starts every bill
        /// at 10, so a x200 cartridge bill switched to "until X" showed 10, and a x3 one 10 (3.3 batches). Rounded up, never below one batch;
        /// "unpause when you have" moves by the same change. Only the target is touched.
        /// </summary>
        public static void NormalizeBills()
        {
            if (!On || Current.Game == null) return;
            try
            {
                int changed = 0;
                foreach (Map map in Find.Maps)
                    foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver))
                    {
                        if (!(t is IBillGiver giver) || giver.BillStack == null) continue;
                        foreach (Bill b in giver.BillStack)
                            if (b is Bill_Production bp && SnapBill(bp)) changed++;
                    }
                if (changed > 0) Log.Message("[BillOrganizer] 'Until X': " + changed + " bill target(s) put on whole batches (items per craft).");
            }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] 'Until X' bill normalization failed: " + e, 0x0B110011); }
        }

        /// <summary>True when the target changed. Recipes that make 1 per craft are always on the grid.</summary>
        public static bool SnapBill(Bill_Production bp)
        {
            if (bp?.recipe == null) return false;
            int step = RecipeEntry.TargetStepOf(bp.recipe);
            if (step <= 1) return false;
            int want = RecipeEntry.SnapTarget(bp.targetCount, step);
            if (want == bp.targetCount) return false;
            RecipeEntry.SetTarget(bp, want);
            return true;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    static class Patch_GameFinalizeInit_TargetStep
    {
        static void Postfix() { TargetStepFix.Apply(); TargetStepFix.NormalizeBills(); SurgeryFix.RemoveBroken(); }
    }

    /// <summary>
    /// Every new bill (vanilla tab, Nice, Dubs, ours: all go through MakeNewBill) starts its "until X" target on whole batches instead of the
    /// vanilla 10 (200 for x200 cartridges, 12 for x3, 10 stays for x5). Clones and loaded bills do not pass here.
    /// </summary>
    [HarmonyPatch(typeof(BillUtility), nameof(BillUtility.MakeNewBill))]
    static class Patch_MakeNewBill_TargetStep
    {
        static void Postfix(Bill __result)
        {
            try { if (TargetStepFix.On && __result is Bill_Production bp) TargetStepFix.SnapBill(bp); }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] New bill target failed: " + e, 0x0B110012); }
        }
    }
}
