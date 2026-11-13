using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BillOrganizer
{
    /// <summary>
    /// Replaces the drawing of the resource readout on the left (icon + number only) with a list that shows names, sorts by
    /// category / amount / name, folds categories, searches and keeps the pinned resources in a section on top. Same numbers as the
    /// vanilla readout (ResourceCounter). Any failure switches back to the vanilla readout for the rest of the session.
    /// </summary>
    public static class ResourceList
    {
        const float RowH = 22f, HeadH = 22f, TopH = 32f;
        public const string PinnedId = "__pinned", OtherId = "__other";

        public static bool Failed;
        static readonly QuickSearchWidget search = new QuickSearchWidget();
        static Vector2 scroll;

        // cached view (rebuilt about 4x/s, or when the map / filter / sort changes)
        sealed class Row { public ThingDef def; public int count; public string label; public string group; }
        sealed class Line { public bool header; public string id, title; public int total; public Row row; public bool open; }
        static readonly List<Line> lines = new List<Line>();
        static float lastBuild = -999f;
        static Map builtFor;
        static bool viewDirty = true;
        static List<ThingCategoryDef> roots;
        static readonly Dictionary<ThingDef, string> groupOf = new Dictionary<ThingDef, string>();
        static readonly Dictionary<string, string> groupLabel = new Dictionary<string, string>();

        public static bool Active => BillOrganizerMod.Settings.resList && !Failed;

        // ------------------------------------------------------------ entry (prefix of ResourceReadout.ResourceReadoutOnGUI)

        /// <summary>Returns false (skips the vanilla readout) when our list drew it.</summary>
        public static bool Prefix()
        {
            if (!Active) return true;
            try
            {
                if (Event.current.type == EventType.Layout || Current.ProgramState != ProgramState.Playing) return false;
                if (Find.MainTabsRoot.OpenTab == MainButtonDefOf.Menu) return false;
                Map map = Find.CurrentMap;
                if (map == null) return false;
                Draw(map);
                return false;
            }
            catch (Exception e)
            {
                Failed = true;
                Log.Error("[BillOrganizer] The resource list failed; the vanilla readout is back. " + e);
                return true;
            }
        }

        // ------------------------------------------------------------ groups

        static void EnsureGroups()
        {
            if (roots != null) return;
            roots = DefDatabase<ThingCategoryDef>.AllDefs.Where(c => c.resourceReadoutRoot).ToList();
            foreach (ThingCategoryDef r in roots) groupLabel[r.defName] = r.LabelCap.ToString();
            groupLabel[OtherId] = "BO_ResOther".Translate();
            groupLabel[PinnedId] = "BO_ResPinned".Translate();
        }

        static string GroupOf(ThingDef d)
        {
            if (groupOf.TryGetValue(d, out string g)) return g;
            g = OtherId;
            if (d.thingCategories != null)
                foreach (ThingCategoryDef c in d.thingCategories)
                {
                    for (ThingCategoryDef p = c; p != null; p = p.parent)
                        if (p.resourceReadoutRoot) { g = p.defName; break; }
                    if (g != OtherId) break;
                }
            groupOf[d] = g;
            return g;
        }

        // ------------------------------------------------------------ view

        static void Rebuild(Map map)
        {
            EnsureGroups();
            BillOrganizerSettings s = BillOrganizerMod.Settings;
            string[] terms = (search.filter.Text ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool searching = terms.Length > 0;

            var all = new List<Row>();
            foreach (KeyValuePair<ThingDef, int> kv in map.resourceCounter.AllCountedAmounts)
            {
                if (kv.Value <= 0 && !kv.Key.resourceReadoutAlwaysShow) continue;
                var r = new Row { def = kv.Key, count = kv.Value, label = kv.Key.LabelCap.ToString(), group = GroupOf(kv.Key) };
                if (searching)
                {
                    string hay = (r.label + "\n" + kv.Key.defName + "\n" + (groupLabel.TryGetValue(r.group, out string gl) ? gl : "")).ToLowerInvariant();
                    if (!terms.All(t => hay.IndexOf(t, StringComparison.Ordinal) >= 0)) continue;
                }
                all.Add(r);
            }

            lines.Clear();
            List<Row> pinned = all.Where(r => s.pinnedRes.Contains(r.def.defName)).OrderBy(r => r.label, StringComparer.CurrentCultureIgnoreCase).ToList();
            List<Row> rest = all.Where(r => !s.pinnedRes.Contains(r.def.defName)).ToList();
            if (pinned.Count > 0) AddSection(PinnedId, pinned, searching);

            if (s.resSort == 0)
            {
                foreach (ThingCategoryDef root in roots.Concat(new ThingCategoryDef[] { null }))
                {
                    string id = root != null ? root.defName : OtherId;
                    List<Row> g = rest.Where(r => r.group == id).OrderBy(r => r.label, StringComparer.CurrentCultureIgnoreCase).ToList();
                    if (g.Count > 0) AddSection(id, g, searching);
                }
            }
            else
            {
                IEnumerable<Row> flat = s.resSort == 1
                    ? rest.OrderByDescending(r => r.count).ThenBy(r => r.label, StringComparer.CurrentCultureIgnoreCase)
                    : rest.OrderBy(r => r.label, StringComparer.CurrentCultureIgnoreCase);
                foreach (Row r in flat) lines.Add(new Line { row = r });
            }
            builtFor = map;
            lastBuild = Time.realtimeSinceStartup;
            viewDirty = false;
        }

        static void AddSection(string id, List<Row> rows, bool searching)
        {
            bool open = searching || BillOrganizerMod.Settings.resExpanded.Contains(id); // collapsed by default
            lines.Add(new Line { header = true, id = id, title = groupLabel.TryGetValue(id, out string t) ? t : id, total = rows.Sum(r => r.count), open = open });
            if (open) foreach (Row r in rows) lines.Add(new Line { row = r });
        }

        // ------------------------------------------------------------ drawing

        static void Draw(Map map)
        {
            BillOrganizerSettings s = BillOrganizerMod.Settings;
            if (viewDirty || builtFor != map || Time.realtimeSinceStartup - lastBuild > 0.25f) Rebuild(map);

            Verse.Text.Font = GameFont.Small;
            float w = Mathf.Clamp(s.resListW, 160, 420);
            float maxH = UI.screenHeight - 7f - 200f;
            float contentH = lines.Sum(l => l.header ? HeadH : RowH);
            var panel = new Rect(2f, 7f, w, Mathf.Min(maxH, TopH + contentH + 4f));

            // top: search, then sort chips + fold all
            search.OnGUI(new Rect(panel.x + 2f, panel.y + 2f, panel.width - 4f, 24f), () => { viewDirty = true; scroll = Vector2.zero; });
            var outR = new Rect(panel.x, panel.y + TopH, panel.width, panel.height - TopH - 2f);
            var view = new Rect(0f, 0f, outR.width - 16f, contentH);
            bool bar = view.height > outR.height;
            if (!bar) view.width = outR.width;
            Widgets.BeginScrollView(outR, ref scroll, view, false);
            float y = 0f;
            foreach (Line l in lines)
            {
                float h = l.header ? HeadH : RowH;
                if (y + h >= scroll.y && y <= scroll.y + outR.height)
                {
                    var r = new Rect(0f, y, view.width, h);
                    if (l.header) DrawHeader(r, l); else DrawRow(r, l.row, map);
                }
                y += h;
            }
            Widgets.EndScrollView();
            if (lines.Count == 0) Out(new Rect(outR.x, outR.y, outR.width, 30f), "BO_ResNoMatch".Translate(), Ui.Faint, TextAnchor.MiddleCenter);
        }

        static void DrawHeader(Rect r, Line l)
        {
            BillOrganizerSettings s = BillOrganizerMod.Settings;
            bool over = Mouse.IsOver(r);
            Ui.Box(r, over ? new Color(1f, 1f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.05f));
            Ui.Glyph(new Rect(r.x + 7f, r.y + (r.height - 12f) / 2f, 12f, 12f), l.open ? Tex.ArrowD : Tex.ArrowR, new Color(1f, 1f, 1f, 0.8f)); // same fold arrow as the recipe tree
            Color c = l.id == PinnedId ? Ui.Accent : new Color(1f, 1f, 1f, 0.85f);
            string tot = l.total.ToStringCached();
            float tw = Verse.Text.CalcSize(tot).x + 6f;
            OutFit(new Rect(r.x + 24f, r.y, r.width - 28f - tw, r.height), l.title, c);
            Out(new Rect(r.xMax - tw - 4f, r.y, tw, r.height), tot, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleRight);
            if (Widgets.ButtonInvisible(r))
            {
                if (!s.resExpanded.Remove(l.id)) s.resExpanded.Add(l.id);
                s.Write();
                viewDirty = true;
            }
        }

        static void DrawRow(Rect r, Row row, Map map)
        {
            BillOrganizerSettings s = BillOrganizerMod.Settings;
            ThingDef d = row.def;
            bool over = Mouse.IsOver(r);
            if (over) Ui.Box(r, new Color(1f, 1f, 1f, 0.09f));
            int min = s.MinOf(d);
            Color c = min > 0 && row.count < min ? Ui.Bad : min > 0 && row.count < min * 3 / 2 ? Ui.Warn : Color.white;
            Widgets.ThingIcon(new Rect(r.x + 8f, r.y + 1f, 22f, 22f), d);
            string cnt = row.count.ToStringCached();
            float cw = Mathf.Max(Verse.Text.CalcSize(cnt).x + 6f, 34f);
            bool pinned = s.pinnedRes.Contains(d.defName);
            OutFit(new Rect(r.x + 34f, r.y, r.width - 38f - cw, r.height), row.label, pinned ? Ui.Accent : Color.white);
            Out(new Rect(r.xMax - cw - 4f, r.y, cw, r.height), cnt, c, TextAnchor.MiddleRight);
            if (!over) return;
            ThingDef tipDef = d; int tipMin = min;
            TooltipHandler.TipRegion(r, () => ResourcePanel.RowTip(tipDef, tipMin), 0x0B110100 + d.shortHash);
            Event ev = Event.current;
            if (ev.type != EventType.MouseDown) return;
            if (ev.button == 0 && ev.control) { ev.Use(); s.TogglePinned(d); viewDirty = true; }
            else if (ev.button == 1) { ev.Use(); ResourcePanel.OpenRowMenu(d); viewDirty = true; }
        }

        static readonly Color Edge = new Color(0f, 0f, 0f, 0.9f);
        static readonly Vector2[] Offs = { new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(0f, 1f), new Vector2(-1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f) };

        /// <summary>Text with a thin dark outline (no panel behind the list, so it must read over any terrain).</summary>
        static void Out(Rect r, string s, Color c, TextAnchor a = TextAnchor.MiddleLeft)
        {
            if (s.NullOrEmpty()) return;
            foreach (Vector2 o in Offs) Ui.Text_(new Rect(r.x + o.x, r.y + o.y, r.width, r.height), s, Edge, a);
            Ui.Text_(r, s, c, a);
        }

        static void OutFit(Rect r, string s, Color c, TextAnchor a = TextAnchor.MiddleLeft)
        {
            if (s.NullOrEmpty()) return;
            Verse.Text.Font = GameFont.Small;
            bool cut = Verse.Text.CalcSize(s).x > r.width;
            Out(r, cut ? s.Truncate(r.width) : s, c, a);
        }

        public static void MarkDirty() { viewDirty = true; }
    }
}
