using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace BillOrganizer
{
    /// <summary>
    /// "Bills" button of the bottom bar (off by default, mod settings): every workbench of the map grouped by type with its queue,
    /// the queue editor (drag, pause, copy, templates) and a button to open the recipe window of the selected bench.
    /// </summary>
    public class MainTabWindow_BenchBills : MainTabWindow
    {
        static Building_WorkTable selected;
        readonly QueuePanel queue = new QueuePanel();
        Vector2 scroll;
        readonly QuickSearchWidget search = new QuickSearchWidget();

        public override Vector2 RequestedTabSize => new Vector2(Mathf.Min(900f, UI.screenWidth - 20f), Mathf.Min(620f, UI.screenHeight - 120f));

        public MainTabWindow_BenchBills()
        {
            doCloseX = true; draggable = true; absorbInputAroundWindow = false; forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            List<Building_WorkTable> benches = map.listerBuildings.AllBuildingsColonistOfClass<Building_WorkTable>()
                .Where(b => b.def.AllRecipes != null && b.def.AllRecipes.Count > 0).ToList();
            if (selected != null && (selected.Destroyed || selected.Map != map)) selected = null;

            search.OnGUI(new Rect(0f, 0f, 250f, 26f));
            Verse.Text.Font = GameFont.Small;
            float leftW = Mathf.Clamp(inRect.width * 0.38f, 300f, 380f); // proportional: bench names keep room as the window grows
            var leftR = new Rect(0f, 32f, leftW, inRect.height - 32f);
            var rightR = new Rect(leftW + 8f, 0f, inRect.width - leftW - 8f, inRect.height);

            // bench list, grouped by type
            IEnumerable<Building_WorkTable> q = benches;
            if (search.filter.Active) q = q.Where(b => search.filter.Matches(b.LabelCap) || search.filter.Matches(b.def.label)
                || b.BillStack.Bills.Any(bl => search.filter.Matches(bl.LabelCap)));
            List<IGrouping<ThingDef, Building_WorkTable>> groups = q.GroupBy(b => b.def).OrderBy(g => g.Key.label).ToList();
            Ui.PanelBox(leftR);
            Rect outR = leftR.ContractedBy(4f);
            const float PrioW = 78f, CountW = 52f, HeadH = 18f, Rpad = 10f; // Rpad: the numbers stay clear of the scroll bar
            {   // column headers (outside the scroll area): what the two numbers of each bench row are
                float hw = outR.width - 16f;
                Ui.Text_(new Rect(outR.x + 10f, outR.y, hw - PrioW - CountW - 14f, HeadH), "BO_ColBench".Translate(), Ui.Faint, TextAnchor.MiddleLeft);
                var prioHead = new Rect(outR.x + hw - CountW - PrioW - Rpad, outR.y, PrioW, HeadH);
                Ui.Text_(prioHead, "BO_BenchPrio".Translate(), Ui.Faint, TextAnchor.MiddleCenter);
                TooltipHandler.TipRegion(prioHead, "BO_BenchPrioTip".Translate());
                var countHead = new Rect(outR.x + hw - CountW - Rpad, outR.y, CountW, HeadH);
                Ui.Text_(countHead, "BO_ColBills".Translate(), Ui.Faint, TextAnchor.MiddleRight);
                TooltipHandler.TipRegion(countHead, "BO_ColBillsTip".Translate());
                outR = new Rect(outR.x, outR.y + HeadH + 2f, outR.width, outR.height - HeadH - 2f);
            }
            float rows = groups.Sum(g => 1 + g.Count());
            var view = new Rect(0f, 0f, outR.width - 16f, rows * 30f);
            Widgets.BeginScrollView(outR, ref scroll, view);
            float y = 0f;
            foreach (IGrouping<ThingDef, Building_WorkTable> g in groups)
            {
                Ui.TextFit(new Rect(4f, y, view.width - 4f, 28f), g.Key.LabelCap.ToString().ToUpperInvariant() + " (" + g.Count() + ")", Ui.Dim);
                y += 30f;
                foreach (Building_WorkTable b in g.OrderByDescending(x => BenchPriority.Get(x)).ThenBy(x => x.LabelCap.ToString()))
                {
                    var row = new Rect(0f, y, view.width, 28f);
                    bool sel = b == selected;
                    Ui.Box(row, sel ? Ui.Selected : Mouse.IsOver(row) ? Ui.Hover : Color.clear);
                    int active = b.BillStack.Bills.Count(bl => !bl.suspended);
                    int total = b.BillStack.Count;
                    Ui.TextFit(new Rect(10f, y, view.width - PrioW - CountW - Rpad - 14f, 28f), b.LabelCap.ToString(), sel ? Color.white : new Color(1f, 1f, 1f, 0.85f));
                    // "-" for an empty bench, just the total when nothing is paused, "running/total" otherwise
                    string cnt = total == 0 ? Ui.None : active == total ? total.ToString() : active + "/" + total;
                    var cntR = new Rect(view.width - CountW - Rpad, y, CountW, 28f);
                    Ui.Text_(cntR, cnt, total == 0 ? Ui.Faint : Ui.Dim, TextAnchor.MiddleRight);
                    if (total > 0) TooltipHandler.TipRegion(cntR, "BO_BenchCountTip".Translate(active, total));
                    int prio = BenchPriority.Get(b);
                    float px = view.width - CountW - PrioW - Rpad;
                    var minus = new Rect(px, y + 3f, 22f, 22f);
                    var plus = new Rect(px + PrioW - 22f, y + 3f, 22f, 22f);
                    int step = Event.current.shift ? 5 : 1;
                    if (Ui.Chip(minus, "-", false, "BO_BenchPrioTip".Translate())) BenchPriority.Set(b, prio - step);
                    Ui.Text_(new Rect(px + 22f, y, PrioW - 44f, 28f), prio > 0 ? "+" + prio : prio.ToString(), prio == 0 ? Ui.Faint : Ui.Accent, TextAnchor.MiddleCenter);
                    if (Ui.Chip(plus, "+", false, "BO_BenchPrioTip".Translate())) BenchPriority.Set(b, prio + step);
                    // the priority buttons are drawn first: the row click (below) would otherwise swallow their clicks
                    if (Widgets.ButtonInvisible(row)) { selected = b; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                    y += 30f;
                }
            }
            Widgets.EndScrollView();
            if (groups.Count == 0) Ui.Text_(outR, (search.filter.Active ? "BO_NoBenchMatch" : "BO_NoBenches").Translate(), Ui.Faint, TextAnchor.MiddleCenter, GameFont.Small, true);

            // right: selected bench
            if (selected == null) { Ui.Text_(rightR, "BO_PickBench".Translate(), Ui.Faint, TextAnchor.MiddleCenter, GameFont.Small, true); return; }
            string open = "BO_OpenRecipes".Translate(), go = "BO_GoToBench".Translate();
            float gw = Ui.ChipWidth(go), ow = Ui.ChipWidth(open);
            var titleR = new Rect(rightR.x, 0f, Mathf.Max(60f, rightR.width - gw - ow - 46f), 28f);
            Verse.Text.Font = GameFont.Medium;
            string title = selected.LabelCap;
            bool cutTitle = Verse.Text.CalcSize(title).x > titleR.width;
            Verse.Text.Font = GameFont.Small;
            Ui.Text_(titleR, cutTitle ? title.Truncate(titleR.width) : title, Color.white, TextAnchor.MiddleLeft, GameFont.Medium);
            if (cutTitle) TooltipHandler.TipRegion(titleR, title);
            if (Ui.Chip(new Rect(rightR.xMax - gw - 30f, 2f, gw, 26f), go, false)) CameraJumper.TryJumpAndSelect(selected);
            if (Ui.Chip(new Rect(rightR.xMax - gw - ow - 36f, 2f, ow, 26f), open, false)) Window_RecipePicker.OpenFor(selected.BillStack, true);
            queue.Draw(new Rect(rightR.x, 32f, rightR.width, rightR.height - 32f), selected.BillStack, map);
        }
    }

    /// <summary>The bottom-bar button is a normal def that stays hidden until the setting is on.</summary>
    [StaticConstructorOnStartup]
    public static class GlobalButton
    {
        static GlobalButton() { Apply(); }

        public static void Apply()
        {
            MainButtonDef d = DefDatabase<MainButtonDef>.GetNamedSilentFail("BO_Bills");
            BillOrganizerSettings s = BillOrganizerMod.Settings;
            if (d != null && s != null) d.buttonVisible = s.enabled && s.showGlobalButton;
        }
    }
}
