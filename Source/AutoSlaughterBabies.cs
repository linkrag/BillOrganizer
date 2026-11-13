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
    /// "Babies" column in the vanilla auto-slaughter window (checked in the 1.7 IL: Verse.AutoSlaughterManager / RimWorld.Dialog_AutoSlaughter).
    /// A baby (first life stage, not reproductive) gives BodySize x 0.2 (0.1 for tiny babies) of an adult's meat (MeatAmount has
    /// StatPart_BodySize), so by default babies are left out of auto slaughter: not slaughtered and not counted in the limits,
    /// like vanilla does with bonded animals when "Allow slaughter" is off. One checkbox per animal (per map) lets them in again.
    /// </summary>
    public static class AutoSlaughterBabies
    {
        /// <summary>Off also when the window column failed: protection without the checkbox to undo it would trap the babies.</summary>
        public static bool On => BillOrganizerMod.Settings != null && BillOrganizerMod.Settings.slaughterBabies && !Patch_AutoSlaughterDialog.failed;

        /// <summary>Animals have no developmentalStage; the first of several life stages, not reproductive, is the baby (vanilla and mod races).</summary>
        public static bool IsBaby(Pawn p)
        {
            if (p?.RaceProps == null || !p.RaceProps.Animal || p.ageTracker == null) return false;
            List<LifeStageAge> stages = p.RaceProps.lifeStageAges;
            return stages != null && stages.Count > 1 && p.ageTracker.CurLifeStageIndex == 0 && !p.ageTracker.CurLifeStage.reproductive;
        }

        /// <summary>Replaces AutoSlaughterManager.CanAutoSlaughterNow inside AnimalsToSlaughter: a protected baby is skipped there,
        /// so it is neither slaughtered nor counted (vanilla does the same for bonded animals).</summary>
        public static bool CanAutoSlaughterNow(Pawn animal)
        {
            if (!AutoSlaughterManager.CanAutoSlaughterNow(animal)) return false;
            try
            {
                return !On || !IsBaby(animal) || MapComponent_SlaughterBabies.Allowed(animal.MapHeld, animal.def);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[BillOrganizer] Auto slaughter baby filter failed (vanilla rule used): " + e.Message, 0x0B110030);
                return true;
            }
        }

        /// <summary>Babies of each animal that the window can count (same filters as vanilla), filled by the CountPlayerAnimals postfix.</summary>
        public static readonly Dictionary<ThingDef, int> babyCounts = new Dictionary<ThingDef, int>();

        public static void DirtyAllMaps()
        {
            if (Current.Game == null) return;
            foreach (Map m in Find.Maps) m.autoSlaughterManager?.Notify_ConfigChanged();
        }
    }

    /// <summary>Which animals may have their babies auto-slaughtered on this map (vanilla configs are per map). Default: none.</summary>
    public class MapComponent_SlaughterBabies : MapComponent
    {
        HashSet<ThingDef> allowBabies = new HashSet<ThingDef>();

        public MapComponent_SlaughterBabies(Map map) : base(map) { }

        public static MapComponent_SlaughterBabies For(Map map) => map?.GetComponent<MapComponent_SlaughterBabies>();

        public static bool Allowed(Map map, ThingDef animal)
        {
            MapComponent_SlaughterBabies c = For(map);
            return c == null || c.allowBabies.Contains(animal); // no component = do not interfere
        }

        public static void Set(Map map, ThingDef animal, bool allowed)
        {
            MapComponent_SlaughterBabies c = For(map);
            if (c == null || animal == null) return;
            if (allowed) c.allowBabies.Add(animal); else c.allowBabies.Remove(animal);
            map.autoSlaughterManager?.Notify_ConfigChanged();
        }

        public override void MapComponentTick()
        {
            // vanilla only rebuilds the slaughter list on spawn/despawn/config change; a baby that grew up must start counting
            if (AutoSlaughterBabies.On && (Find.TickManager.TicksGame + map.uniqueID * 37) % 2500 == 0)
                map.autoSlaughterManager?.Notify_ConfigChanged();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref allowBabies, "allowBabies", LookMode.Def);
            if (allowBabies == null) allowBabies = new HashSet<ThingDef>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) allowBabies.RemoveWhere(d => d == null);
        }
    }

    [HarmonyPatch(typeof(AutoSlaughterManager), nameof(AutoSlaughterManager.AnimalsToSlaughter), MethodType.Getter)]
    public static class Patch_AutoSlaughterList
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo vanilla = AccessTools.Method(typeof(AutoSlaughterManager), nameof(AutoSlaughterManager.CanAutoSlaughterNow));
            MethodInfo ours = AccessTools.Method(typeof(AutoSlaughterBabies), nameof(AutoSlaughterBabies.CanAutoSlaughterNow));
            List<CodeInstruction> list = instructions.ToList();
            List<CodeInstruction> hits = list.Where(ci => ci.Calls(vanilla)).ToList();
            if (hits.Count != 1)
            {
                Log.Warning("[BillOrganizer] AutoSlaughterManager.AnimalsToSlaughter: expected 1 CanAutoSlaughterNow call, found " + hits.Count + ". The game changed? Auto slaughter keeps the vanilla rules (babies not protected).");
                return list;
            }
            hits[0].operand = ours; // same signature (static bool (Pawn)); labels stay on the instruction
            return list;
        }
    }

    /// <summary>The window: counts without protected babies, plus a "Babies" column (current + allow) at the right end.</summary>
    [HarmonyPatch(typeof(Dialog_AutoSlaughter))]
    public static class Patch_AutoSlaughterDialog
    {
        const float ColW = 140f, CurW = 60f, AllowW = 72f;
        const float MinScreenSlack = 20f, MinLabelW = 40f;

        internal static bool failed;

        static readonly AccessTools.FieldRef<Dialog_AutoSlaughter, Map> mapRef = AccessTools.FieldRefAccess<Dialog_AutoSlaughter, Map>("map");
        static readonly MethodInfo recalc = AccessTools.Method(typeof(Dialog_AutoSlaughter), "RecalculateAnimals");

        /// <summary>The column always shows while the feature is on: it is the only way to allow babies again.</summary>
        static bool Active => AutoSlaughterBabies.On;

        static void Fail(string where, Exception e)
        {
            failed = true; // also turns the protection off (AutoSlaughterBabies.On), back to vanilla rules
            AutoSlaughterBabies.DirtyAllMaps();
            Log.Warning("[BillOrganizer] Auto slaughter 'Babies' column failed (" + where + "); babies follow the vanilla rules again: " + e);
        }

        [HarmonyPostfix, HarmonyPatch(nameof(Dialog_AutoSlaughter.InitialSize), MethodType.Getter)]
        static void InitialSizePostfix(ref Vector2 __result)
        {
            if (!Active) return;
            // grow by the column if the screen allows; on narrow screens (high UI scale) the name column gives up the rest
            float grown = Mathf.Clamp(UI.screenWidth - MinScreenSlack - __result.x, 0f, ColW);
            __result.x += grown;
        }

        [HarmonyPostfix, HarmonyPatch("CalculateLabelWidth")]
        static void LabelWidthPostfix(ref float __result)
        {
            if (Active) __result = Mathf.Max(MinLabelW, __result - ColW);
        }

        [HarmonyPostfix, HarmonyPatch("CountPlayerAnimals")]
        static void CountPostfix(Map map, AutoSlaughterConfig config, ThingDef animal, ref int currentMalesYoung, ref int currentFemalesYoung, ref int currentTotal)
        {
            if (failed || !AutoSlaughterBabies.On) return;
            try
            {
                bool allowed = MapComponent_SlaughterBabies.Allowed(map, animal);
                int babies = 0;
                foreach (Pawn p in map.mapPawns.SpawnedColonyAnimals)
                {
                    if (p.def != animal || !AutoSlaughterManager.CanEverAutoSlaughter(p) || !AutoSlaughterBabies.IsBaby(p)) continue;
                    if (!config.allowSlaughterBonded && p.relations != null && p.relations.GetDirectRelationsCount(PawnRelationDefOf.Bond) > 0) continue; // vanilla did not count it
                    babies++;
                    if (allowed) continue;
                    // vanilla counted it as young (not reproductive) and in the total
                    if (p.gender == Gender.Male) currentMalesYoung--;
                    else if (p.gender == Gender.Female) currentFemalesYoung--;
                    currentTotal--;
                }
                AutoSlaughterBabies.babyCounts[animal] = babies;
            }
            catch (Exception e) { Fail("count", e); }
        }

        [HarmonyPostfix, HarmonyPatch("DoAnimalHeader")]
        static void HeaderPostfix(Rect rect1, Rect rect2)
        {
            if (!Active) return;
            try
            {
                float x0 = rect1.xMax - ColW;
                var band = new Rect(x0, rect1.y, ColW, rect1.height + rect2.height + 1f);
                Widgets.DrawLightHighlight(band);
                GUI.color = Color.gray;
                Widgets.DrawLineVertical(x0, band.y, band.height);
                GUI.color = Color.white;
                TextAnchor anchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                var title = new Rect(x0, rect1.y, ColW, rect1.height);
                Widgets.Label(title, "BO_SlBabies".Translate());
                TooltipHandler.TipRegion(title, "BO_SlBabiesTip".Translate());
                var cur = new Rect(x0 + 4f, rect2.y, CurW, rect2.height);
                var allow = new Rect(x0 + 4f + CurW + 4f, rect2.y, AllowW, rect2.height);
                Widgets.DrawHighlightIfMouseover(cur);
                Widgets.DrawHighlightIfMouseover(allow);
                Widgets.Label(cur, "AutoSlaugtherHeaderColCurrent".Translate());
                Widgets.Label(allow, "AllowSlaughter".Translate());
                TooltipHandler.TipRegion(cur, "BO_SlCurrentTip".Translate());
                TooltipHandler.TipRegion(allow, "BO_SlAllowTip".Translate());
                Text.Anchor = anchor;
            }
            catch (Exception e) { Fail("header", e); }
        }

        [HarmonyPostfix, HarmonyPatch("DoAnimalRow")]
        static void RowPostfix(Dialog_AutoSlaughter __instance, Rect rect, AutoSlaughterConfig config)
        {
            if (!Active || config?.animal == null) return;
            try
            {
                Map map = mapRef(__instance);
                float x0 = rect.xMax - ColW;
                AutoSlaughterBabies.babyCounts.TryGetValue(config.animal, out int babies);
                TextAnchor anchor = Text.Anchor;
                Color color = GUI.color;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = babies == 0 ? Color.gray : Color.white;
                Widgets.Label(new Rect(x0 + 4f, rect.y, CurW, rect.height), babies.ToString());
                GUI.color = color;
                Text.Anchor = anchor;

                bool allowed = MapComponent_SlaughterBabies.Allowed(map, config.animal);
                bool was = allowed;
                float cx = x0 + 4f + CurW + 4f + (AllowW - 24f) / 2f;
                Widgets.Checkbox(cx, rect.y, ref allowed, 24f, disabled: false, paintable: true);
                TooltipHandler.TipRegion(new Rect(cx, rect.y, 24f, 24f), "BO_SlAllowTip".Translate());
                if (allowed != was)
                {
                    MapComponent_SlaughterBabies.Set(map, config.animal, allowed);
                    recalc?.Invoke(__instance, null);
                }
            }
            catch (Exception e) { Fail("row", e); }
        }
    }
}
