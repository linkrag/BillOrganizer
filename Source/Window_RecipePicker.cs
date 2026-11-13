using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace BillOrganizer
{
    /// <summary>
    /// Replacement for the "Add bill" float menu. Three panes: group tree, recipe list (stat columns, sortable, filters) and a
    /// detail/compare panel. Own layout; nothing is taken from Nice Bill Tab or Dubs Mint Menus.
    /// </summary>
    public partial class Window_RecipePicker : Window
    {
        const float RowH = 46f, HeaderH = 26f, TreeRowH = 26f, TopH = 34f;

        readonly BillStack stack;
        readonly Thing bench;
        readonly Map map;
        readonly List<RecipeEntry> all;
        readonly Availability avail;
        readonly List<Node> roots;
        readonly HashSet<string> expanded = new HashSet<string>();
        readonly QuickSearchWidget search = new QuickSearchWidget();
        readonly Memo memo;

        // view state
        Vector2 listScroll, treeScroll;
        List<RecipeEntry> nodeEntries = new List<RecipeEntry>();
        List<RecipeEntry> shown = new List<RecipeEntry>();
        List<StatInfo> cols = new List<StatInfo>();
        readonly Dictionary<string, float> maxes = new Dictionary<string, float>();
        readonly Dictionary<string, float> minFilters = new Dictionary<string, float>();
        string ammoClassFilter;
        RecipeEntry selected;
        string selectedKey;
        string lastClickKey;
        float lastClickTime;
        float listViewHeight = 400f;
        bool dirty = true, needFocus = true;
        string flash;
        float flashUntil;

        public override Vector2 InitialSize => new Vector2(Mathf.Min(BillOrganizerMod.Settings.winW, UI.screenWidth - 40f), Mathf.Min(BillOrganizerMod.Settings.winH, UI.screenHeight - 90f));
        protected override float Margin => 10f;

        // what the detail panel adds with: copies and material (repeat mode / quantity live in the memo)
        readonly RecipeDetail detail;
        bool showQueue;
        readonly QueuePanel queue = new QueuePanel();

        /// <summary>Last choices per bench def, kept across windows and saved with the mod settings.</summary>
        public class Memo
        {
            public string node = Grouping.All, sort = "";
            public bool desc = true, availFirst, onlyStock;
            public int cfgMode, cfgQty = 1;
            /// <summary>cfgQty was set by us (one batch of the last recipe), not by the player: the next recipe may replace it.</summary>
            public bool cfgAuto = true;
        }

        // ------------------------------------------------------------ open

        public static void OpenFor(BillStack stack, bool fromButton, string selectKey = null)
        {
            Find.WindowStack.WindowOfType<Window_RecipePicker>()?.Close(false);
            List<RecipeEntry> entries = RecipeEntry.Build(stack);
            if (entries == null || entries.Count == 0)
            {
                Messages.Message("BO_NoRecipes".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new Window_RecipePicker(stack, entries, selectKey));
        }

        bool scrollToSelected;

        public Window_RecipePicker(BillStack stack, List<RecipeEntry> entries, string selectKey = null)
        {
            this.stack = stack;
            bench = stack.billGiver as Thing;
            map = bench?.Map;
            all = entries;
            avail = new Availability(map);
            avail.Refresh(all, true);
            roots = Grouping.Build(all);
            memo = BillOrganizerMod.Settings.GetMemo(bench?.def.defName ?? "");
            detail = new RecipeDetail(stack, map, bench, all, avail, memo, keepOpen => AddSelected(keepOpen),
                e => { Select(e); selectedKey = e.key; }, () => { dirty = true; });
            if (selectKey != null && all.Any(e => e.key == selectKey)) { memo.node = Grouping.All; selectedKey = selectKey; scrollToSelected = true; } // from the compact bill tab
            if (Grouping.Find(roots, memo.node) == null || (memo.node == Grouping.Fav && !all.Any(e => e.IsFavorite))) memo.node = Grouping.All;
            ExpandFor(memo.node);
            expanded.Add("weapons"); expanded.Add("apparel");

            doCloseX = true;
            closeOnClickedOutside = true;
            closeOnAccept = false;
            draggable = true;
            absorbInputAroundWindow = false;
            forcePause = false;
            soundAppear = SoundDefOf.FloatMenu_Open;
        }

        void ExpandFor(string id)
        {
            for (Node n = Grouping.Find(roots, id); n != null; n = n.parent) expanded.Add(n.id);
        }

        public override void PreClose() { BillOrganizerMod.Settings.SaveMemo(bench?.def.defName ?? "", memo); BillOrganizerMod.Settings.Write(); }

        // Enter is handled in HandleKeys (it needs the event before the search box sees it)
        public override void OnAcceptKeyPressed() { }

        // ------------------------------------------------------------ data refresh

        void SelectNode(string id)
        {
            memo.node = id;
            listScroll = Vector2.zero;
            minFilters.Clear();
            ammoClassFilter = null;
            ExpandFor(id);
            dirty = true;
        }

        void Refresh()
        {
            dirty = false;
            foreach (Node n in roots)
                if (n.id == Grouping.Fav || n.id == Grouping.Recent) n.count = all.Count(e => e.InNode(n.id));
            nodeEntries = all.Where(e => e.InNode(memo.node)).ToList();
            // armor over every allowed material is only worth its stat calls when the group is apparel (columns show it)
            if (nodeEntries.Count > 0 && nodeEntries.All(e => e.kind == Kind.Apparel))
                foreach (RecipeEntry e in nodeEntries) e.EnsureMaterials();
            // filters that do not depend on the columns first: the bonus columns follow what is left (search "+band" -> bandwidth)
            IEnumerable<RecipeEntry> q = nodeEntries;
            if (search.filter.Active)
            {
                string[] terms = (search.filter.Text ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (terms.Length > 0) q = q.Where(e => e.MatchesSearch(terms));
            }
            if (memo.onlyStock) q = q.Where(e => e.stock != Stock.Missing);
            if (ammoClassFilter != null) q = q.Where(e => e.ammoClassLabel == ammoClassFilter);
            List<RecipeEntry> filtered = q.ToList();
            cols = StatTable.For(nodeEntries, 2, 2, filtered.Count > 0 ? filtered : null);
            maxes.Clear();
            foreach (StatInfo c in cols)
            {
                float m = 0f;
                foreach (RecipeEntry e in nodeEntries) { float v = c.get(e); if (!float.IsNaN(v) && v > m) m = v; }
                maxes[c.key] = m;
            }

            q = filtered;
            foreach (KeyValuePair<string, float> f in minFilters)
            {
                StatInfo c = cols.FirstOrDefault(x => x.key == f.Key);
                if (c != null && f.Value > 0f) { float min = f.Value; q = q.Where(e => { float v = c.get(e); return !float.IsNaN(v) && v >= min; }); }
            }

            List<RecipeEntry> list = q.ToList();
            StatInfo sortCol = cols.FirstOrDefault(c => c.key == memo.sort);
            if (sortCol != null)
            {
                var with = list.Where(e => !float.IsNaN(sortCol.get(e)));
                var without = list.Where(e => float.IsNaN(sortCol.get(e))).OrderBy(e => e.label);
                with = (memo.desc ? with.OrderByDescending(e => sortCol.get(e)) : with.OrderBy(e => sortCol.get(e))).ThenBy(e => e.label);
                list = with.Concat(without).ToList();
            }
            else if (memo.sort == "name") list = (memo.desc ? list.OrderByDescending(e => e.label) : list.OrderBy(e => e.label)).ToList();
            else list = list.OrderBy(e => -e.recipe.displayPriority).ThenBy(e => e.order).ToList();
            if (memo.availFirst) list = list.OrderBy(e => e.stock == Stock.Ready ? 0 : e.stock == Stock.Unknown ? 1 : 2).ToList(); // stable

            shown = list;
            if (selectedKey != null) selected = shown.FirstOrDefault(e => e.key == selectedKey);
            if (selected == null && shown.Count > 0) { selected = shown[0]; selectedKey = selected.key; }
            if (shown.Count == 0) { selected = null; selectedKey = null; }
        }

        /// <summary>Click on a queued bill: show its recipe (all groups, search cleared) in the detail panel instead of the queue.</summary>
        void SelectRecipeOf(Bill b)
        {
            RecipeEntry e = RecipeEntry.ForBill(all, b);
            if (Prefs.DevMode && b?.recipe != null)
                Log.Message("[BillOrganizer] Queue click (window): bill recipe " + b.recipe.defName + " -> " + (e != null ? "entry " + e.key : "not in the bench list"));
            if (e == null) { RecipeEntry.SayUnavailable(b); return; } // never shown, same as the compact tab
            // every filter that could hide it goes: group, column/ammo filters (SelectNode), and "in stock only" when it is missing
            SelectNode(Grouping.All);
            if (memo.onlyStock && e.stock == Stock.Missing) memo.onlyStock = false;
            search.Reset();
            selectedKey = e.key;
            scrollToSelected = true;
            showQueue = false;
            dirty = true;
        }

        void Select(RecipeEntry e, bool scrollTo = false)
        {
            selected = e; selectedKey = e?.key;
            if (scrollTo && e != null)
            {
                int i = shown.IndexOf(e);
                float top = i * RowH;
                if (top < listScroll.y) listScroll.y = top;
                else if (top + RowH > listScroll.y + listViewHeight) listScroll.y = top + RowH - listViewHeight;
            }
        }

        void AddSelected(bool keepOpen)
        {
            if (selected == null) return;
            if (bench == null || bench.Destroyed || stack.Count >= BillStack.MaxCount)
            {
                Messages.Message("BO_StackFull".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            var o = new AddOptions { mode = memo.cfgMode, qty = memo.cfgQty, qtyAuto = memo.cfgAuto, times = detail.opts.times, stuff = detail.stuffFor == selected ? detail.opts.stuff : null };
            int n = selected.AddBill(stack, o);
            SoundDefOf.Click.PlayOneShotOnCamera();
            flash = (n > 1 ? "BO_AddedN".Translate(n, selected.label) : "BO_Added".Translate(selected.label)); flashUntil = Time.realtimeSinceStartup + 2f;
            dirty = true;
            if (!keepOpen && !BillOrganizerMod.Settings.keepOpen) Close();
        }

        // ------------------------------------------------------------ window

        public override void DoWindowContents(Rect inRect)
        {
            if (bench == null || bench.Destroyed || stack == null) { Close(false); return; }
            Verse.Text.Font = GameFont.Small;
            Verse.Text.Anchor = TextAnchor.UpperLeft;
            avail.Refresh(all);
            HandleKeys();
            if (dirty) Refresh();
            if (scrollToSelected && selected != null) { Select(selected, true); scrollToSelected = false; }

            // top bar: search, flash message, hint
            search.OnGUI(new Rect(0f, 4f, 270f, 26f), () => { dirty = true; });
            if (needFocus && Event.current.type == EventType.Repaint) { search.Focus(); needFocus = false; }
            var help = new Rect(274f, 6f, 22f, 22f);
            Ui.Box(help, Mouse.IsOver(help) ? Ui.Hover : Ui.PanelLight);
            Ui.Text_(help, "?", Ui.Dim, TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(help, "BO_SearchHelp".Translate() + "\n\n" + "BO_KeysHelp".Translate());
            string qLabel = "BO_QueueTab".Translate(stack.Count, BillStack.MaxCount);
            float qw = Ui.ChipWidth(qLabel);
            if (Ui.Chip(new Rect(inRect.width - 34f - qw, 5f, qw, 24f), qLabel, showQueue, "BO_QueueTabTip".Translate())) showQueue = !showQueue;
            float devW = 0f;
            if (Prefs.DevMode)
            {
                devW = 48f;
                if (Ui.Chip(new Rect(inRect.width - 38f - qw - devW, 5f, devW - 4f, 24f), "Dev", false, "BO_DevDumpGroups".Translate()))
                    Grouping.DumpGroups(all, (stack.billGiver as Thing)?.LabelCap.ToString() ?? "?");
            }
            var hint = new Rect(304f, 0f, inRect.width - 304f - 40f - qw - devW, TopH);
            if (flash != null && Time.realtimeSinceStartup < flashUntil) Ui.Text_(hint, flash, Ui.Good, TextAnchor.MiddleLeft);
            else Ui.TextFit(hint, "BO_Hint".Translate(), Ui.Faint, TextAnchor.MiddleLeft);

            float treeW = TreeWidth();
            float detailW = Mathf.Clamp(inRect.width * 0.34f, 330f, 450f);
            float top = TopH + 4f;
            float bodyH = inRect.height - top;
            var treeR = new Rect(0f, top, treeW, bodyH);
            var listR = new Rect(treeW + 8f, top, inRect.width - treeW - detailW - 16f, bodyH);
            var detailR = new Rect(inRect.width - detailW, top, detailW, bodyH);

            DrawTree(treeR);
            DrawList(listR);
            if (showQueue) queue.Draw(detailR, stack, map, false, SelectRecipeOf);
            else detail.Draw(detailR, selected);
        }

        void HandleKeys()
        {
            Event ev = Event.current;
            // QuickSearchWidget.Focus only works during Repaint (same as the first focus), so just raise the flag
            if (ev.type == EventType.KeyDown && ev.control && ev.keyCode == KeyCode.F) { needFocus = true; ev.Use(); return; }
            if (ev.type != EventType.KeyDown || shown.Count == 0) return;
            if (ev.keyCode == KeyCode.DownArrow || ev.keyCode == KeyCode.UpArrow)
            {
                int i = Mathf.Max(0, shown.IndexOf(selected)) + (ev.keyCode == KeyCode.DownArrow ? 1 : -1);
                Select(shown[Mathf.Clamp(i, 0, shown.Count - 1)], true);
                ev.Use();
            }
            else if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)
            {
                AddSelected(ev.shift);
                ev.Use();
            }
        }

        // ------------------------------------------------------------ tree

        /// <summary>Width of the group tree: as wide as its longest visible label needs (190 to 230), so "Belts and accessories" is not cut when there is room.</summary>
        float TreeWidth()
        {
            Verse.Text.Font = GameFont.Small;
            float w = 190f;
            foreach (Node n in Grouping.Flatten(roots, expanded))
            {
                float need = Verse.Text.CalcSize(n.label).x + n.depth * 12f + 86f + (n.id == Grouping.Fav ? 18f : 0f);
                if (need > w) w = need;
            }
            return Mathf.Min(w, 230f);
        }

        void DrawTree(Rect r)
        {
            Ui.PanelBox(r);
            List<Node> rows = Grouping.Flatten(roots, expanded).ToList();
            Rect outR = r.ContractedBy(4f);
            var view = new Rect(0f, 0f, outR.width - 16f, rows.Count * TreeRowH + 4f);
            Widgets.BeginScrollView(outR, ref treeScroll, view);
            for (int i = 0; i < rows.Count; i++)
            {
                Node n = rows[i];
                var row = new Rect(0f, i * TreeRowH, view.width, TreeRowH - 2f);
                bool sel = memo.node == n.id;
                Ui.Box(row, sel ? Ui.Selected : Mouse.IsOver(row) ? Ui.Hover : Color.clear);
                float x = row.x + 4f + n.depth * 12f;
                if (n.children.Count > 0)
                {
                    var arrow = new Rect(x, row.y + (row.height - 12f) / 2f, 12f, 12f);
                    Ui.Glyph(arrow, expanded.Contains(n.id) ? Tex.ArrowD : Tex.ArrowR, Ui.Dim);
                    if (Widgets.ButtonInvisible(new Rect(x - 2f, row.y, 18f, row.height)))
                    {
                        if (!expanded.Remove(n.id)) expanded.Add(n.id);
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    }
                }
                x += 16f;
                if (n.id == Grouping.Fav) Ui.Glyph(new Rect(x, row.y + 6f, 14f, 14f), Tex.Star, Ui.Accent);
                var lr = new Rect(x + (n.id == Grouping.Fav ? 18f : 0f), row.y, row.xMax - x - 38f, row.height);
                Ui.Text_(lr, n.label.Truncate(lr.width), sel ? Color.white : new Color(1f, 1f, 1f, 0.85f));
                Ui.Text_(new Rect(row.xMax - 36f, row.y, 32f, row.height), n.count.ToString(), Ui.Faint, TextAnchor.MiddleRight);
                TooltipHandler.TipRegion(lr, n.label);
                if (Widgets.ButtonInvisible(new Rect(x, row.y, row.xMax - x, row.height)) && !sel)
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    SelectNode(n.id);
                }
            }
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------ list

        static readonly string[] sliderKeys = { "range", "dps", "sharp" };

        void DrawList(Rect r)
        {
            float y = r.y;

            // filter chips
            float x = r.x;
            string stockLabel = "BO_FilterStock".Translate();
            float cw = Ui.ChipWidth(stockLabel);
            if (Ui.Chip(new Rect(x, y, cw, 24f), stockLabel, memo.onlyStock, "BO_FilterStockTip".Translate())) { memo.onlyStock = !memo.onlyStock; dirty = true; }
            x += cw + 6f;
            string availLabel = "BO_SortAvail".Translate();
            cw = Ui.ChipWidth(availLabel);
            if (Ui.Chip(new Rect(x, y, cw, 24f), availLabel, memo.availFirst, "BO_SortAvailTip".Translate())) { memo.availFirst = !memo.availFirst; dirty = true; }
            x += cw + 10f;
            // ammunition: round type filter (FMJ, AP, HE...). A faint label says what it is. When every type fits they are chips;
            // otherwise one "Rounds: <type> v" button with a menu of all of them (no arbitrary chips, nothing silently dropped).
            List<string> classes = nodeEntries.Where(e => e.ammoClassLabel != null).Select(e => e.ammoClassLabel).Distinct().OrderBy(s => s).ToList();
            if (classes.Count > 0)
            {
                Ui.Box(new Rect(x - 6f, y + 3f, 1f, 18f), Ui.Outline);
                string rounds = "BO_AmmoFilter".Translate();
                Verse.Text.Font = GameFont.Small;
                float lw = Verse.Text.CalcSize(rounds).x + 6f;
                Ui.Text_(new Rect(x, y, lw, 24f), rounds, Ui.Faint);
                x += lw;
                string tip = "BO_AmmoFilterTip".Translate();
                float allW = classes.Sum(c => Ui.ChipWidth(c) + 4f);
                if (x + allW <= r.xMax)
                {
                    foreach (string c in classes)
                    {
                        cw = Ui.ChipWidth(c);
                        if (Ui.Chip(new Rect(x, y, cw, 24f), c, ammoClassFilter == c, tip)) { ammoClassFilter = ammoClassFilter == c ? null : c; dirty = true; }
                        x += cw + 4f;
                    }
                }
                else
                {
                    string cur = ammoClassFilter ?? "BO_AmmoAll".Translate().ToString();
                    float bw = Mathf.Min(Ui.ChipWidth(cur) + 18f, Mathf.Max(r.xMax - x, 60f));
                    var btn = new Rect(x, y, bw, 24f);
                    if (Ui.Chip(btn, cur.Truncate(bw - 34f), ammoClassFilter != null, tip))
                    {
                        var opts = new List<FloatMenuOption> { new FloatMenuOption("BO_AmmoAll".Translate(), () => { ammoClassFilter = null; dirty = true; }) };
                        foreach (string c in classes)
                        {
                            string pick = c;
                            opts.Add(new FloatMenuOption(c, () => { ammoClassFilter = pick; dirty = true; }));
                        }
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                    Ui.Glyph(new Rect(btn.xMax - 16f, btn.y + 7f, 10f, 10f), Tex.ArrowD, Ui.Dim); // it opens a menu
                }
            }
            y += 28f;

            // minimum sliders for the stats that matter most in this group
            List<StatInfo> sliders = cols.Where(c => sliderKeys.Contains(c.key)).ToList();
            if (sliders.Count > 0)
            {
                float w = r.width / sliders.Count;
                for (int i = 0; i < sliders.Count; i++)
                {
                    StatInfo c = sliders[i];
                    float max = maxes.TryGetValue(c.key, out float m) ? m : 0f;
                    minFilters.TryGetValue(c.key, out float cur);
                    string minLabel = "BO_Min".Translate(c.Label) + " " + c.fmt(cur);
                    Verse.Text.Font = GameFont.Small;
                    float lw = Mathf.Min(Verse.Text.CalcSize(minLabel).x + 8f, w * 0.5f); // measured, not a fixed 134 px: PT-BR labels are longer
                    Ui.TextFit(new Rect(r.x + i * w, y, lw, 24f), minLabel, Ui.Dim);
                    float nv = Widgets.HorizontalSlider(new Rect(r.x + i * w + lw + 2f, y + 4f, Mathf.Max(w - lw - 18f, 40f), 18f), cur, 0f, Mathf.Max(max, 0.01f));
                    if (!Mathf.Approximately(nv, cur)) { minFilters[c.key] = nv; dirty = true; }
                }
                y += 28f;
            }

            // column budget: the name keeps at least 190px (the name is what tells recipes apart; the last column goes first)
            int maxCols = cols.Count;
            float colsW = cols.Take(maxCols).Sum(c => c.width);
            while (maxCols > 1 && r.width - 16f - 100f - colsW < 190f) { maxCols--; colsW = cols.Take(maxCols).Sum(c => c.width); }
            List<StatInfo> shownCols = cols.Take(maxCols).ToList();

            var panel = new Rect(r.x, y, r.width, r.yMax - y);
            Ui.PanelBox(panel);
            Rect inner = panel.ContractedBy(2f);

            // header: click to sort
            var head = new Rect(inner.x, inner.y, inner.width - 16f, HeaderH);
            Ui.Box(head, new Color(1f, 1f, 1f, 0.05f));
            float nameX = head.x + 72f;
            float nameW = head.width - 72f - colsW - 34f;
            DrawHeaderCell(new Rect(nameX, head.y, nameW, head.height), "BO_ColName".Translate() + " (" + shown.Count + ")", "name", TextAnchor.MiddleLeft, null);
            float cx = nameX + nameW;
            foreach (StatInfo c in shownCols)
            {
                DrawHeaderCell(new Rect(cx, head.y, c.width, head.height), c.Label, c.key, TextAnchor.MiddleRight, c);
                cx += c.width;
            }

            // rows (only the visible ones are drawn)
            var outR = new Rect(inner.x, inner.y + HeaderH + 2f, inner.width, inner.height - HeaderH - 2f);
            listViewHeight = outR.height;
            var view = new Rect(0f, 0f, outR.width - 16f, shown.Count * RowH);
            Widgets.BeginScrollView(outR, ref listScroll, view);
            int first = Mathf.Max(0, (int)(listScroll.y / RowH) - 1);
            int last = Mathf.Min(shown.Count, (int)((listScroll.y + outR.height) / RowH) + 2);
            for (int i = first; i < last; i++)
                DrawRow(new Rect(0f, i * RowH, view.width, RowH), shown[i], i, nameX - inner.x, nameW, shownCols);
            Widgets.EndScrollView();

            if (shown.Count == 0) Ui.Text_(outR, "BO_NoMatch".Translate(), Ui.Faint, TextAnchor.MiddleCenter);
        }

        void DrawHeaderCell(Rect cell, string text, string sortKey, TextAnchor anchor, StatInfo info)
        {
            bool active = memo.sort == sortKey;
            if (Mouse.IsOver(cell)) Ui.Box(cell, Ui.Hover);
            Rect tr = anchor == TextAnchor.MiddleRight ? new Rect(cell.x, cell.y, cell.width - (active ? 10f : 6f), cell.height) : new Rect(cell.x + 4f, cell.y, cell.width - 4f, cell.height);
            Ui.TextFit(tr, text, active ? Color.white : Ui.Dim, anchor);
            if (active) Ui.Glyph(new Rect(cell.xMax - 11f, cell.y + 9f, 8f, 8f), memo.desc ? Tex.TriDown : Tex.TriUp, info != null ? info.color : Ui.Accent);
            if (info != null) TooltipHandler.TipRegion(cell, info.Tip);
            if (Widgets.ButtonInvisible(cell))
            {
                if (active) memo.desc = !memo.desc; else { memo.sort = sortKey; memo.desc = sortKey != "name"; }
                dirty = true;
            }
        }

        void DrawRow(Rect row, RecipeEntry e, int index, float nameX, float nameW, List<StatInfo> shownCols)
        {
            bool sel = e == selected;
            bool over = Mouse.IsOver(row);
            Ui.Box(row, sel ? Ui.Selected : over ? Ui.Hover : (index % 2 == 1 ? new Color(1f, 1f, 1f, 0.025f) : Color.clear));

            // stock stripe on the left edge
            Color stripe = e.stock == Stock.Ready ? Ui.Good : e.stock == Stock.Missing ? Ui.Bad : new Color(1f, 1f, 1f, 0.18f);
            var stripeR = new Rect(row.x, row.y + 3f, 3f, row.height - 6f);
            Ui.Box(stripeR, stripe);
            TooltipHandler.TipRegion(new Rect(row.x, row.y, 8f, row.height), (e.stock == Stock.Ready ? "BO_StockReady" : e.stock == Stock.Missing ? "BO_StockMissing" : "BO_StockUnknown").Translate());
            if (sel) Ui.Box(new Rect(row.x + 3f, row.y, 2f, row.height), Ui.Accent); // selected: accent rail, so it differs from hover

            // star and the "+" button first: they sit on top of the row's click area
            var star = new Rect(row.x + 8f, row.y + (row.height - 20f) / 2f, 20f, 20f);
            bool fav = e.IsFavorite;
            Ui.Glyph(star, fav ? Tex.Star : Tex.StarO, fav ? Ui.Accent : (over ? Ui.Dim : Ui.Faint));
            TooltipHandler.TipRegion(star, "BO_FavoriteTip".Translate());
            if (Widgets.ButtonInvisible(star)) { e.ToggleFavorite(); dirty = true; SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); }

            var plus = new Rect(row.xMax - 30f, row.y + (row.height - 24f) / 2f, 24f, 24f);
            bool plusOver = Mouse.IsOver(plus);
            Ui.Box(plus, plusOver ? Ui.Accent : (over || sel ? Ui.PanelLight : Color.clear));
            Ui.Glyph(plus.ContractedBy(5f), Tex.Plus, plusOver ? Color.black : (over || sel ? Color.white : Ui.Faint));
            TooltipHandler.TipRegion(plus, "BO_AddTip".Translate());
            if (Widgets.ButtonInvisible(plus)) { Select(e); AddSelected(Event.current.shift); return; }

            DrawIcon(new Rect(row.x + 32f, row.y + 5f, 36f, row.height - 10f), e);

            bool isPinned = detail.IsPinned(e);
            bool bonus = Bonus.Has(e);
            var nameR = new Rect(nameX + 2f, row.y + 3f, nameW - 6f - (isPinned ? 18f : 0f) - (bonus ? 18f : 0f), 22f);
            Ui.NameFit(nameR, e.label, e.stock == Stock.Missing ? new Color(1f, 1f, 1f, 0.62f) : Color.white);
            float gx = nameR.xMax + 2f;
            if (bonus)
            {
                // the item does something beyond its stats (bandwidth, ability, drug effect...): the list is in the tooltip
                var br = new Rect(gx, row.y + 7f, 14f, 14f);
                Ui.Glyph(br, Tex.Bonus, Ui.Bonus);
                TooltipHandler.TipRegion(br, Bonus.MarkTip(e.product));
                gx += 18f;
            }
            if (isPinned)
            {
                var pr = new Rect(gx, row.y + 7f, 14f, 14f);
                Ui.Glyph(pr, Tex.Pin, Ui.Accent);
                TooltipHandler.TipRegion(pr, "BO_PinnedTip".Translate());
            }

            // ingredient mini icons
            float ix = nameX + 4f;
            foreach (IngInfo ing in BillOrganizerMod.Settings.showIngIcons ? e.ings : new List<IngInfo>())
            {
                if (ix + 18f > nameX + nameW - 4f) break;
                var ir = new Rect(ix, row.y + 26f, 17f, 17f);
                if (ing.rep != null) Widgets.ThingIcon(ir, ing.rep);
                if (ing.known && !ing.Ok) Ui.Box(new Rect(ir.x, ir.yMax, ir.width, 2f), Ui.Bad);
                TooltipHandler.TipRegion(ir, IngTip(ing));
                ix += 20f;
            }

            float cx = nameX + nameW;
            foreach (StatInfo c in shownCols)
            {
                float v = c.get(e);
                float max = maxes.TryGetValue(c.key, out float m) ? m : 0f;
                bool best = !float.IsNaN(v) && max > 0f && v >= max * 0.9999f;
                string txt = float.IsNaN(v) ? Ui.None : c.fmt(v);
                Color tc = float.IsNaN(v) ? Ui.Faint : Color.white; // the bar carries the stat color: red numbers read as "missing"
                if (c.key == "skill" && !float.IsNaN(v) && !e.SkillOk(map)) tc = Ui.Warn; // nobody on the map can do it
                Ui.Text_(new Rect(cx, row.y + 3f, c.width - 6f, 22f), txt, tc, TextAnchor.MiddleRight);
                if (!float.IsNaN(v)) Ui.Bar(new Rect(cx + 8f, row.y + 31f, c.width - 14f, 4f), v, max, c.color, best);
                cx += c.width;
            }

            // click selects, double click adds
            var click = new Rect(row.x + 30f, row.y, row.width - 62f, row.height);
            if (Widgets.ButtonInvisible(click))
            {
                if (Event.current.shift) { Select(e); detail.TogglePin(e); return; } // Shift+click pins for comparing
                bool dbl = lastClickKey == e.key && Time.realtimeSinceStartup - lastClickTime < 0.35f;
                lastClickKey = e.key; lastClickTime = Time.realtimeSinceStartup;
                Select(e);
                if (dbl) AddSelected(false);
            }
        }

        static string IngTip(IngInfo ing)
        {
            string name = ing.defs.Count == 1 ? ing.defs[0].LabelCap.ToString() : ing.ing.filter.Summary.CapitalizeFirst();
            string s = ing.need.ToString("0.##") + "x " + name;
            if (ing.known) s += "\n" + "BO_InStock".Translate(ing.Have);
            return s;
        }

        public static void DrawIcon(Rect r, RecipeEntry e)
        {
            ThingDef d = e.recipe.UIIconThing ?? e.product;
            if (d != null && d.uiIcon != null && d.uiIcon != BaseContent.BadTex) Widgets.ThingIcon(r, d);
            else if (e.recipe.UIIcon != null) Widgets.DrawTextureFitted(r, e.recipe.UIIcon, 1f);
        }
    }
}
