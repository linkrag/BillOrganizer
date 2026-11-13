using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BillOrganizer
{
    /// <summary>
    /// "Recipes" button on every player workbench: opens the same window as "Add bill" without going through the bill tab,
    /// so it works with Nice Bill Tab (which replaces the tab), Dubs Mint Menus and vanilla alike.
    /// </summary>
    [HarmonyPatch(typeof(Building), nameof(Building.GetGizmos))]
    public static class Patch_WorkbenchGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Building __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            Gizmo extra = null;
            try { extra = MakeGizmo(__instance); }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Workbench button failed: " + e, 0x0B110001); }
            if (extra != null) yield return extra;
            Gizmo link = null;
            try { if (__instance is Building_WorkTable wt) link = BillLinkManager.MakeGizmo(wt); }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Link button failed: " + e, 0x0B110012); }
            if (link != null) yield return link;
        }

        static Gizmo MakeGizmo(Building b)
        {
            var bench = b as Building_WorkTable;
            if (bench == null || bench.Faction != Faction.OfPlayer || !ShowButton()) return null;
            return new Command_Action
            {
                defaultLabel = "BO_GizmoLabel".Translate(),
                defaultDesc = "BO_GizmoDesc".Translate(),
                icon = Tex.Catalog,
                action = () => Window_RecipePicker.OpenFor(bench.BillStack, fromButton: true),
                Order = 1000f,
            };
        }

        public static bool ShowButton()
        {
            BillOrganizerSettings s = BillOrganizerMod.Settings;
            if (!s.enabled) return false;
            switch (s.integration)
            {
                case IntegrationMode.GizmoOnly: return s.gizmo != GizmoMode.Never;
                default:
                    if (s.gizmo == GizmoMode.Always) return true;
                    if (s.gizmo == GizmoMode.Never) return false;
                    return NiceCompat.TabReplaced;
            }
        }
    }
}
