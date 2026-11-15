using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace BillOrganizer
{
    /// <summary>
    /// Pinned resources: Ctrl+click a resource in the readout on the left to pin it; a small panel next to the readout then lists
    /// the pinned ones with their stock, an optional minimum (turns red below it) and, on hover, the recipes that use the resource
    /// with how many batches each allows. The vanilla readout is only touched by two postfix/prefix pairs; any failure leaves it alone.
    /// </summary>
    public static class ResourcePanel
    {
        const float RowH = 24f, PanelW = 200f;
        static readonly AccessTools.FieldRef<Listing, float> CurY = AccessTools.FieldRefAccess<Listing, float>("curY");

        // ------------------------------------------------------------ patching (manual, so one failure cannot stop PatchAll)

        public static void TryPatch(Harmony h)
        {
            try
            {
                var thingDef = AccessTools.Method(typeof(Listing_ResourceReadout), "DoThingDef");
                var onGui = AccessTools.Method(typeof(ResourceReadout), nameof(ResourceReadout.ResourceReadoutOnGUI));
                if (thingDef == null || onGui == null)
                {
                    Log.Warning("[BillOrganizer] Resource readout methods not found; the pinned resources panel is off.");
                    return;
                }
                h.Patch(thingDef, prefix: new HarmonyMethod(typeof(ResourcePanel), nameof(RowPrefix)), postfix: new HarmonyMethod(typeof(ResourcePanel), nameof(RowPostfix)));
                h.Patch(onGui, prefix: new HarmonyMethod(typeof(ResourceList), nameof(ResourceList.Prefix)) { priority = Priority.Last },
                    postfix: new HarmonyMethod(typeof(ResourcePanel), nameof(PanelPostfix)));
            }
            catch (Exception e) { Log.Warning("[BillOrganizer] Could not patch the resource readout: " + e.Message); }
        }

        // ------------------------------------------------------------ pin / unpin in the vanilla readout

        static void RowPrefix(Listing_ResourceReadout __instance, out float __state) { __state = CurY(__instance); }

        static void RowPostfix(Listing_ResourceReadout __instance, ThingDef thingDef, float __state)
        {
            try
            {
                if (CurY(__instance) <= __state || !BillOrganizerMod.Settings.pinPanel) return; // the row was not drawn (count 0)
                var rect = new Rect(0f, __state, __instance.ColumnWidth, __instance.lineHeight);
                bool pinned = BillOrganizerMod.Settings.pinnedRes.Contains(thingDef.defName);
                if (pinned) Ui.Glyph(new Rect(rect.xMax - 14f, rect.y + (rect.height - 11f) / 2f, 11f, 11f), Tex.Pin, Ui.Accent);
                Event ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0 && ev.control && Mouse.IsOver(rect))
                {
                    ev.Use();
                    BillOrganizerMod.Settings.TogglePinned(thingDef);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }
                else if (ev.type == EventType.MouseDown && ev.button == 0 && Mouse.IsOver(rect))
                {
                    ev.Use();
                    ResourceJump.Click(Find.CurrentMap, thingDef, ev.shift);
                }
            }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Pin click failed: " + e.Message, 0x0B110031); }
        }

        // ------------------------------------------------------------ the panel

        static void PanelPostfix(ResourceReadout __instance)
        {
            try
            {
                BillOrganizerSettings s = BillOrganizerMod.Settings;
                if (!s.pinPanel || s.pinnedRes.Count == 0 || ResourceList.Active) return; // the new list has its own pinned section
                if (Event.current.type == EventType.Layout || Current.ProgramState != ProgramState.Playing || Find.MainTabsRoot.OpenTab == MainButtonDefOf.Menu) return;
                Map map = Find.CurrentMap;
                if (map == null) return;
                Draw(map);
            }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Pinned resources panel failed: " + e, 0x0B110032); }
        }

        static void Draw(Map map)
        {
            BillOrganizerSettings s = BillOrganizerMod.Settings;
            var defs = new List<ThingDef>();
            foreach (string n in s.pinnedRes)
            {
                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(n);
                if (d != null && d.CountAsResource) defs.Add(d);
            }
            if (defs.Count == 0) return;

            float x = (Prefs.ResourceReadoutCategorized ? 130f : 124f);
            float h = RowH * (s.pinCollapsed ? 1 : defs.Count + 1);
            var panel = new Rect(x, 7f, PanelW, h);
            Verse.Text.Font = GameFont.Small;
            Ui.Box(panel, new Color(0.06f, 0.07f, 0.08f, 0.72f));

            // header: click to collapse
            var head = new Rect(panel.x, panel.y, panel.width, RowH);
            if (Mouse.IsOver(head)) Ui.Box(head, new Color(1f, 1f, 1f, 0.06f));
            Ui.Glyph(new Rect(head.x + 6f, head.y + 6f, 12f, 12f), Tex.Pin, Ui.Accent);
            Ui.Text_(new Rect(head.x + 24f, head.y, head.width - 30f, RowH), "BO_PinPanelTitle".Translate(defs.Count), Ui.Dim);
            Ui.Glyph(new Rect(head.xMax - 20f, head.y + (RowH - 12f) / 2f, 12f, 12f), s.pinCollapsed ? Tex.ArrowR : Tex.ArrowD, Ui.Dim);
            TooltipHandler.TipRegion(head, "BO_PinPanelTip".Translate());
            if (Widgets.ButtonInvisible(head)) { s.pinCollapsed = !s.pinCollapsed; s.Write(); }
            if (s.pinCollapsed) return;

            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef d = defs[i];
                var row = new Rect(panel.x, panel.y + RowH * (i + 1), panel.width, RowH);
                if (Mouse.IsOver(row)) Ui.Box(row, new Color(1f, 1f, 1f, 0.08f));
                int count = map.resourceCounter.GetCount(d);
                int min = s.MinOf(d);
                Color c = min > 0 && count < min ? Ui.Bad : min > 0 && count < min * 3 / 2 ? Ui.Warn : Color.white;
                Widgets.ThingIcon(new Rect(row.x + 4f, row.y + 1f, 22f, 22f), d);
                string cnt = count.ToStringCached();
                float cw = Mathf.Max(Verse.Text.CalcSize(cnt).x + 4f, 30f);
                Ui.TextFit(new Rect(row.x + 30f, row.y, row.width - 34f - cw, RowH), d.LabelCap.ToString(), Ui.Dim);
                Ui.Text_(new Rect(row.xMax - cw - 4f, row.y, cw, RowH), cnt, c, TextAnchor.MiddleRight);
                ThingDef tipDef = d; int tipMin = min;
                TooltipHandler.TipRegion(row, () => RowTip(tipDef, tipMin), 0x0B110100 + d.shortHash);
                Event ev = Event.current;
                if (ev.type == EventType.MouseDown && Mouse.IsOver(row))
                {
                    if (ev.button == 1) { ev.Use(); OpenRowMenu(d); }
                    else if (ev.button == 0 && ev.control) { ev.Use(); s.TogglePinned(d); SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); }
                    else if (ev.button == 0) { ev.Use(); ResourceJump.Click(map, d, ev.shift); }
                }
            }
        }

        public static string RowTip(ThingDef d, int min)
        {
            string s = d.LabelCap + ": " + d.description.CapitalizeFirst();
            if (min > 0) s += "\n\n" + "BO_PinMinTip".Translate(min);
            string rec = PinRecipes.Describe(d);
            if (!rec.NullOrEmpty()) s += "\n\n" + "BO_PinUsedBy".Translate() + "\n" + rec;
            return s + "\n\n" + "BO_PinRowTip".Translate();
        }

        public static void OpenRowMenu(ThingDef d)
        {
            BillOrganizerSettings s = BillOrganizerMod.Settings;
            var list = new List<FloatMenuOption>
            {
                new FloatMenuOption("BO_PinSetMin".Translate(), () =>
                    Find.WindowStack.Add(new Dialog_Slider(v => "BO_PinMinLabel".Translate(d.LabelCap, v), 0, 5000, v => s.SetMin(d, v), Mathf.Max(s.MinOf(d), 0)))),
            };
            if (s.MinOf(d) > 0) list.Add(new FloatMenuOption("BO_PinClearMin".Translate(), () => s.SetMin(d, 0)));
            list.Add(new FloatMenuOption((s.pinnedRes.Contains(d.defName) ? "BO_PinRemove" : "BO_PinAdd").Translate(), () => s.TogglePinned(d)));
            Find.WindowStack.Add(new FloatMenu(list));
        }
    }

    /// <summary>
    /// Click on a resource row (our list, the pinned panel, the vanilla readout): select a stack on the map and center the camera on it.
    /// Each click on the same resource goes to the next stack (largest first, then wraps); Shift+click selects every stack.
    /// The stacks are the ones the resource counter counts (things held in storage), else any spawned one outside the fog.
    /// </summary>
    public static class ResourceJump
    {
        static ThingDef lastDef;
        static int lastIdx;

        public static List<Thing> Stacks(Map map, ThingDef d)
        {
            var list = new List<Thing>();
            foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
                foreach (Thing t in g.HeldThings)
                    if (t.def == d && t.Spawned && !t.Position.Fogged(map)) list.Add(t);
            if (list.Count == 0)
                foreach (Thing t in map.listerThings.ThingsOfDef(d))
                    if (t.Spawned && !t.Position.Fogged(map)) list.Add(t);
            // largest first; position breaks ties so the order is stable between clicks
            list.Sort((a, b) =>
            {
                int c = b.stackCount.CompareTo(a.stackCount);
                if (c != 0) return c;
                c = a.Position.x.CompareTo(b.Position.x);
                return c != 0 ? c : a.Position.z.CompareTo(b.Position.z);
            });
            return list;
        }

        public static void Click(Map map, ThingDef d, bool all)
        {
            try
            {
                if (map == null || d == null) return;
                List<Thing> stacks = Stacks(map, d);
                if (stacks.Count == 0)
                {
                    Messages.Message("BO_ResJumpNone".Translate(d.LabelCap), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                if (all)
                {
                    CameraJumper.TryJump(stacks[0]);
                    Find.Selector.ClearSelection();
                    foreach (Thing t in stacks) Find.Selector.Select(t, playSound: false);
                    lastDef = d; lastIdx = 0;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    return;
                }
                lastIdx = d == lastDef ? (lastIdx + 1) % stacks.Count : 0;
                lastDef = d;
                CameraJumper.TryJumpAndSelect(stacks[lastIdx]);
            }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Resource jump failed: " + e, 0x0B110033); }
        }
    }

    /// <summary>
    /// "Recipes that use this resource" for the pinned panel tooltip. Recipe entries are built once per bench type of the map,
    /// one bench type per frame while a tooltip is open (building them all at once would stutter), and stock-checked about once a second.
    /// </summary>
    public static class PinRecipes
    {
        static Map map;
        static readonly Dictionary<ThingDef, List<RecipeEntry>> entries = new Dictionary<ThingDef, List<RecipeEntry>>();
        static readonly Dictionary<ThingDef, Availability> avail = new Dictionary<ThingDef, Availability>();
        static List<Building_WorkTable> pending;

        public static string Describe(ThingDef res)
        {
            Map m = Find.CurrentMap;
            if (m == null) return null;
            if (m != map) { map = m; entries.Clear(); avail.Clear(); pending = null; }
            try
            {
                if (pending == null)
                    pending = m.listerBuildings.AllBuildingsColonistOfClass<Building_WorkTable>()
                        .GroupBy(b => b.def).Select(g => g.First()).ToList();
                if (pending.Count > 0)
                {
                    Building_WorkTable b = pending[pending.Count - 1];
                    pending.RemoveAt(pending.Count - 1);
                    List<RecipeEntry> built = RecipeEntry.Build(b.BillStack);
                    if (built != null) { entries[b.def] = built; avail[b.def] = new Availability(m); }
                }
                var lines = new List<KeyValuePair<string, int>>();
                var seen = new HashSet<string>();
                foreach (KeyValuePair<ThingDef, List<RecipeEntry>> kv in entries)
                {
                    avail[kv.Key].Refresh(kv.Value);
                    foreach (RecipeEntry e in kv.Value)
                    {
                        if (e.precept != null || !e.ings.Any(i => i.defs.Contains(res))) continue;
                        if (!seen.Add(e.recipe.defName)) continue;
                        lines.Add(new KeyValuePair<string, int>(e.label, e.canMake));
                    }
                }
                string text = string.Join("\n", lines.OrderByDescending(l => l.Value).ThenBy(l => l.Key).Take(8)
                    .Select(l => "- " + l.Key + (l.Value >= 0 ? ": " + l.Value : "")).ToArray());
                if (lines.Count > 8) text += "\n" + "BO_PinMoreRecipes".Translate(lines.Count - 8);
                if (pending.Count > 0) text += (text.Length > 0 ? "\n" : "") + "...";
                return text;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[BillOrganizer] Recipe list for pinned resources failed: " + e.Message, 0x0B110033);
                return null;
            }
        }
    }
}
