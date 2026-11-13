using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BillOrganizer
{
    /// <summary>
    /// BillStack.DoListing opens the "Add bill" menu with `new FloatMenu(recipeOptionsMaker())` (checked in the 1.7 IL).
    /// The transpiler swaps that constructor call for OpenPicker, which returns our window (or the vanilla FloatMenu).
    /// </summary>
    [HarmonyPatch(typeof(BillStack), nameof(BillStack.DoListing))]
    public static class Patch_DoListing
    {
        [ThreadStatic] public static BillStack Current;

        /// <summary>
        /// Inside the compact bill tab the queue gets a shorter rect and our own card queue is drawn instead of the vanilla one
        /// (returns false to skip it). If ours cannot draw, the vanilla queue runs in the shorter rect as before.
        /// </summary>
        public static bool Prefix(BillStack __instance, ref Rect rect)
        {
            Current = __instance;
            if (Patch_BillTab.InTab)
            {
                rect = Patch_BillTab.QueueRect;
                if (Patch_BillTab.TryDrawQueue(__instance)) return false;
            }
            return true;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo open = AccessTools.Method(typeof(Patch_DoListing), nameof(OpenPicker));
            int replaced = 0;
            foreach (CodeInstruction ci in instructions)
            {
                if (ci.opcode == OpCodes.Newobj && ci.operand is ConstructorInfo ctor && ctor.DeclaringType == typeof(FloatMenu)
                    && ctor.GetParameters().Length == 1)
                {
                    ci.opcode = OpCodes.Call; // keeps labels / exception blocks on the instruction
                    ci.operand = open;
                    replaced++;
                }
                yield return ci;
            }
            if (replaced != 1) Log.Warning("[BillOrganizer] BillStack.DoListing: expected 1 FloatMenu constructor call, replaced " + replaced + ". The game changed? The vanilla menu keeps working.");
        }

        /// <summary>Same stack signature as the replaced constructor: takes the vanilla options, returns the window to open.</summary>
        public static Window OpenPicker(List<FloatMenuOption> vanillaOptions)
        {
            try
            {
                BillStack stack = Current;
                BillOrganizerSettings s = BillOrganizerMod.Settings;
                if (s.enabled && s.integration == IntegrationMode.Auto && stack != null)
                {
                    List<RecipeEntry> entries = RecipeEntry.Build(stack);
                    if (entries != null && entries.Count > 0 && entries.Count >= s.minRecipes)
                    {
                        Find.WindowStack.WindowOfType<Window_RecipePicker>()?.Close(false);
                        return new Window_RecipePicker(stack, entries);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("[BillOrganizer] Picker failed, falling back to the vanilla menu: " + e);
            }
            return new FloatMenu(vanillaOptions);
        }
    }
}
