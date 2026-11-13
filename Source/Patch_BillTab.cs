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
    /// Compact bill tab (the vanilla tab, wider and a bit taller): on the left the vanilla queue ("Add bill" opens our window, see
    /// <see cref="Patch_DoListing"/>) with a searchable recipe list under it, on the right the detail panel of the window (<see cref="RecipeDetail"/>).
    /// Click selects, "+" or double click adds, right click opens the window on that recipe.
    /// Nothing of the tab is replaced; the vanilla FillTab runs as is and we draw after it. Off while Nice Bill Tab replaces the tab.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Bills), "FillTab")]
    public static class Patch_BillTab
    {
        [ThreadStatic] public static bool InTab;

        const float Pad = 10f, RowH = 36f;

        /// <summary>Width of the left column (queue + list): proportional to the tab so the detail panel keeps room when the tab is narrow (400 at the default 920).</summary>
        static float LeftW => Mathf.Clamp(TabSize.x * 0.435f, 300f, 460f);

        public static bool Active
        {
            get
            {
                BillOrganizerSettings s = BillOrganizerMod.Settings;
                return s != null && s.enabled && s.compactTab && !NiceCompat.TabReplaced;
            }
        }

        public static Vector2 TabSize
        {
            get
            {
                float h = Mathf.Clamp(BillOrganizerMod.Settings.tabH, 480f, Mathf.Max(480f, UI.screenHeight - 260f));
                float w = Mathf.Clamp(BillOrganizerMod.Settings.tabW, 700f, Mathf.Max(700f, UI.screenWidth - 40f));
                return new Vector2(w, h);
            }
        }

        /// <summary>Where the vanilla queue (Add bill button + bills) is drawn: top of the left column.</summary>
        public static Rect QueueRect
        {
            get
            {
                Vector2 s = TabSize;
                float q = Mathf.Clamp(BillOrganizerMod.Settings.tabQueue, 150f, s.y - 2f * Pad - 150f);
                return new Rect(Pad, Pad, LeftW, q);
            }
        }

        // state of the open tab (one tab is open at a time)
        static readonly QuickSearchWidget search = new QuickSearchWidget();
        static Vector2 scroll;
        static Building_WorkTable cachedBench;
        static List<RecipeEntry> entries;
        static Availability avail;
        static RecipeDetail detail;
        static Window_RecipePicker.Memo memo;
        static List<RecipeEntry> shown = new List<RecipeEntry>();
        static bool shownDirty = true;
        static string lastSearch = "", selectedKey, lastClickKey, flash;
        static float lastDraw, lastClickTime, flashUntil;

        static void Prefix(ITab_Bills __instance)
        {
            InTab = false;
            try
            {
                if (!Active) return;
                Traverse.Create(__instance).Field("size").SetValue(TabSize);
                InTab = true;
                // vanilla FillTab draws its paste icon at a fixed spot (372, 3) that lands on our queue header; with the clipboard
                // empty for the length of FillTab it draws nothing. Ours (QueuePanel header) reads Clipboard below. Restored in Finalizer.
                hiddenClip = BillUtility.Clipboard;
                BillUtility.Clipboard = null;
                clipHidden = true;
            }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Compact bill tab (prefix) failed: " + e, 0x0B110002); }
        }

        static Bill hiddenClip;
        static bool clipHidden;

        /// <summary>
        /// The vanilla bill clipboard as the player sees it: while the compact tab hides it from vanilla FillTab this is the hidden one,
        /// unless a card's copy button put a new bill there meanwhile. Everything of ours reads the clipboard through this.
        /// </summary>
        public static Bill Clipboard => clipHidden ? (BillUtility.Clipboard ?? hiddenClip) : BillUtility.Clipboard;

        /// <summary>Always runs (even if FillTab threw): gives the clipboard back, keeping a bill copied during the tab.</summary>
        static Exception Finalizer(Exception __exception)
        {
            if (clipHidden)
            {
                if (BillUtility.Clipboard == null) BillUtility.Clipboard = hiddenClip;
                hiddenClip = null;
                clipHidden = false;
            }
            return __exception;
        }

        static readonly QueuePanel queuePanel = new QueuePanel();
        static bool queueFailed;

        /// <summary>Draws our card queue in <see cref="QueueRect"/> in place of the vanilla one. False = could not, let the vanilla queue run.</summary>
        public static bool TryDrawQueue(BillStack stack)
        {
            if (queueFailed) return false;
            try
            {
                BillOrganizerSettings s = BillOrganizerMod.Settings;
                if (!s.enabled || s.integration != IntegrationMode.Auto || !(stack.billGiver is Thing t) || t.Map == null) return false;
                queuePanel.Draw(QueueRect, stack, t.Map, true, SelectRecipeOf, IsSelectedBill);
                return true;
            }
            catch (Exception e)
            {
                queueFailed = true; // do not retry every frame: the vanilla queue takes over until the game restarts
                Log.Error("[BillOrganizer] Card queue failed, using the vanilla queue: " + e);
                return false;
            }
        }

        /// <summary>The queued bill whose recipe is the one in the detail panel (exact recipe, not a defName prefix).</summary>
        static bool IsSelectedBill(Bill b)
        {
            if (selectedKey == null || b?.recipe == null || entries == null) return false;
            RecipeEntry e = entries.FirstOrDefault(x => x.key == selectedKey);
            if (e == null) return false;
            return e.recipe == b.recipe || RecipeEntry.ForBill(entries, b) == e;
        }

        /// <summary>Click on a queued bill: show its recipe in the detail panel (clears the search if it would hide it).</summary>
        static void SelectRecipeOf(Bill b)
        {
            if (b?.recipe == null || entries == null) return;
            RecipeEntry e = RecipeEntry.ForBill(entries, b);
            if (Prefs.DevMode)
                Log.Message("[BillOrganizer] Queue click: bill recipe " + b.recipe.defName + " (" + b.recipe.modContentPack?.Name + ") -> "
                    + (e != null ? "entry " + e.key + (e.recipe == b.recipe ? "" : " (matched by name/product)") : "not in the bench list (" + entries.Count + " recipes)"));
            // a recipe the bench does not offer now (locked research, removed mod, unavailable def) is never shown: say why instead
            if (e == null) { RecipeEntry.SayUnavailable(b); return; }
            selectedKey = e.key;
            if (!shown.Contains(e))
            {
                search.Reset();
                lastSearch = "";
                shownDirty = true;
                Rebuild("");
            }
        }

        static void Postfix()
        {
            if (!InTab) return;
            InTab = false;
            try { DrawQuick(); }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Compact bill tab failed: " + e, 0x0B110003); }
        }

        static void DrawQuick()
        {
            var table = Find.Selector.SingleSelectedThing as Building_WorkTable;
            if (table == null || table.Map == null) return;
            BillStack stack = table.BillStack;

            // rebuilt when the bench changes or the tab was closed in between (the tab only draws while open)
            if (cachedBench != table || entries == null || Time.realtimeSinceStartup - lastDraw > 0.5f)
            {
                if (cachedBench != table) { selectedKey = null; scroll = Vector2.zero; }
                cachedBench = table;
                entries = RecipeEntry.Build(stack) ?? new List<RecipeEntry>();
                avail = new Availability(table.Map);
                avail.Refresh(entries, true);
                memo = BillOrganizerMod.Settings.GetMemo(table.def.defName);
                detail = new RecipeDetail(stack, table.Map, table, entries, avail, memo, keepOpen => AddSelected(stack, SelectedEntry()),
                    e => selectedKey = e.key, () => shownDirty = true);
                shownDirty = true;
            }
            else avail.Refresh(entries);
            lastDraw = Time.realtimeSinceStartup;

            string text = search.filter.Active ? (search.filter.Text ?? "") : "";
            if (text != lastSearch) { lastSearch = text; shownDirty = true; scroll = Vector2.zero; }
            if (shownDirty) Rebuild(text);
            RecipeEntry selected = SelectedEntry();

            Vector2 size = TabSize;
            Rect q = QueueRect;
            float top = q.yMax + 8f;
            Ui.Box(new Rect(Pad, q.yMax + 3f, LeftW, 1f), Ui.Outline);
            search.OnGUI(new Rect(Pad, top, 190f, 24f));
            bool flashing = flash != null && Time.realtimeSinceStartup < flashUntil;
            string hint = flashing ? flash : "BO_TabHint".Translate().ToString();
            float devW = 0f;
            if (Prefs.DevMode)
            {
                devW = 44f;
                if (Ui.Chip(new Rect(Pad + LeftW - devW, top, devW, 24f), "Dev", false, "BO_DevDumpGroups".Translate()))
                    Grouping.DumpGroups(entries ?? new List<RecipeEntry>(), table.LabelCap);
            }
            var hintR = new Rect(Pad + 196f, top, LeftW - 196f - devW - (devW > 0f ? 4f : 0f), 24f);
            Ui.TextFit(hintR, hint, flashing ? Ui.Good : Ui.Faint, TextAnchor.MiddleLeft);
            if (!flashing) TooltipHandler.TipRegion(hintR, "BO_TabHintTip".Translate());

            DrawList(new Rect(Pad, top + 28f, LeftW, size.y - Pad - top - 28f), stack, table);

            // right column: one panel with a header row (bench name + our close button, which replaces the vanilla X hidden by
            // Patch_TabCloseButton) and the detail under it
            float dx = Pad + LeftW + 8f;
            var right = new Rect(dx, Pad, size.x - dx - Pad, size.y - 2f * Pad);
            Ui.PanelBox(right);
            const float HeadH = 32f;
            var close = new Rect(right.xMax - 6f - 24f, right.y + 4f, 24f, 24f);
            bool closeOver = Mouse.IsOver(close);
            Ui.Box(close, closeOver ? new Color(Ui.Bad.r, Ui.Bad.g, Ui.Bad.b, 0.85f) : Ui.PanelLight);
            Ui.Glyph(close.ContractedBy(6f), Tex.Cross, closeOver ? Color.white : Ui.Dim);
            TooltipHandler.TipRegion(close, "BO_CloseTab".Translate());
            if (Widgets.ButtonInvisible(close)) CloseTab();
            Ui.TextFit(new Rect(right.x + 10f, right.y + 4f, close.x - right.x - 18f, 24f), table.LabelCap.ToString().ToUpperInvariant(), Ui.Dim);
            Ui.Box(new Rect(right.x + 1f, right.y + HeadH, right.width - 2f, 1f), Ui.Outline);
            detail.Draw(new Rect(right.x, right.y + HeadH + 1f, right.width, right.height - HeadH - 1f), selected, false);
        }

        /// <summary>Same as the vanilla X of the tab (ITab.CloseTab): close the open inspect tab.</summary>
        static void CloseTab()
        {
            try
            {
                (MainButtonDefOf.Inspect.TabWindow as MainTabWindow_Inspect)?.CloseOpenTab();
                SoundDefOf.TabClose.PlayOneShotOnCamera();
            }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Could not close the bill tab: " + e.Message, 0x0B110012); }
        }

        /// <summary>
        /// True while the inspect tab window (an ImmediateWindow) is drawing our compact bill tab: its vanilla X sits on top of our
        /// detail panel, so <see cref="Patch_TabCloseButton"/> skips it and the header draws ours.
        /// </summary>
        public static bool DrawingCompactTab()
        {
            if (!Active || !(Find.WindowStack?.currentlyDrawnWindow is ImmediateWindow)) return false;
            var inspect = MainButtonDefOf.Inspect?.TabWindow as MainTabWindow_Inspect;
            return inspect != null && inspect.OpenTabType != null && typeof(ITab_Bills).IsAssignableFrom(inspect.OpenTabType)
                && Find.Selector.SingleSelectedThing is Building_WorkTable;
        }


        static void Rebuild(string text)
        {
            shownDirty = false;
            IEnumerable<RecipeEntry> list = entries;
            string[] terms = text.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length > 0) list = list.Where(e => e.MatchesSearch(terms));
            shown = list.OrderBy(e => e.IsFavorite ? 0 : e.IsRecent ? 1 : 2).ThenBy(e => -e.recipe.displayPriority).ThenBy(e => e.order).ToList();
        }

        static RecipeEntry SelectedEntry()
        {
            RecipeEntry e = selectedKey == null ? null : shown.FirstOrDefault(x => x.key == selectedKey);
            if (e == null && shown.Count > 0) { e = shown[0]; selectedKey = e.key; }
            return e;
        }

        static void AddSelected(BillStack stack, RecipeEntry e)
        {
            if (e == null) return;
            if (stack.Count >= BillStack.MaxCount) { Messages.Message("BO_StackFull".Translate(), MessageTypeDefOf.RejectInput, false); return; }
            var o = new AddOptions { mode = memo.cfgMode, qty = memo.cfgQty, qtyAuto = memo.cfgAuto, times = detail.opts.times, stuff = detail.stuffFor == e ? detail.opts.stuff : null };
            int n = e.AddBill(stack, o);
            SoundDefOf.Click.PlayOneShotOnCamera();
            flash = n > 1 ? "BO_AddedN".Translate(n, e.label) : "BO_Added".Translate(e.label);
            flashUntil = Time.realtimeSinceStartup + 2f;
            shownDirty = true;
        }

        static void DrawList(Rect r, BillStack stack, Building_WorkTable table)
        {
            Ui.PanelBox(r);
            Rect outR = r.ContractedBy(2f);
            if (shown.Count == 0) { Ui.Text_(outR, "BO_NoMatch".Translate(), Ui.Faint, TextAnchor.MiddleCenter); return; }
            var view = new Rect(0f, 0f, outR.width - 16f, shown.Count * RowH);
            Widgets.BeginScrollView(outR, ref scroll, view);
            int first = Mathf.Max(0, (int)(scroll.y / RowH) - 1), last = Mathf.Min(shown.Count, (int)((scroll.y + outR.height) / RowH) + 2);
            for (int i = first; i < last && i < shown.Count; i++)
                DrawRow(new Rect(0f, i * RowH, view.width, RowH), shown[i], i, stack, table);
            Widgets.EndScrollView();
        }

        static void DrawRow(Rect row, RecipeEntry e, int index, BillStack stack, Building_WorkTable table)
        {
            bool sel = e.key == selectedKey;
            bool over = Mouse.IsOver(row);
            Ui.Box(row, sel ? Ui.Selected : over ? Ui.Hover : (index % 2 == 1 ? new Color(1f, 1f, 1f, 0.025f) : Color.clear));
            Color stripe = e.stock == Stock.Ready ? Ui.Good : e.stock == Stock.Missing ? Ui.Bad : new Color(1f, 1f, 1f, 0.18f);
            Ui.Box(new Rect(row.x, row.y + 3f, 3f, row.height - 6f), stripe);
            TooltipHandler.TipRegion(new Rect(row.x, row.y, 8f, row.height), (e.stock == Stock.Ready ? "BO_StockReady" : e.stock == Stock.Missing ? "BO_StockMissing" : "BO_StockUnknown").Translate());
            if (sel) Ui.Box(new Rect(row.x + 3f, row.y, 2f, row.height), Ui.Accent);

            // right click: the full window on this recipe
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && over)
            {
                Event.current.Use();
                Window_RecipePicker.OpenFor(stack, true, e.key);
                return;
            }

            // star and "+" first: they sit on top of the row's click area
            var star = new Rect(row.x + 8f, row.y + (row.height - 18f) / 2f, 18f, 18f);
            bool fav = e.IsFavorite;
            Ui.Glyph(star, fav ? Tex.Star : Tex.StarO, fav ? Ui.Accent : (over ? Ui.Dim : Ui.Faint));
            TooltipHandler.TipRegion(star, "BO_FavoriteTip".Translate());
            if (Widgets.ButtonInvisible(star)) { e.ToggleFavorite(); shownDirty = true; SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); return; }

            var plus = new Rect(row.xMax - 28f, row.y + (row.height - 22f) / 2f, 22f, 22f);
            bool plusOver = Mouse.IsOver(plus);
            Ui.Box(plus, plusOver ? Ui.Accent : (over || sel ? Ui.PanelLight : Color.clear));
            Ui.Glyph(plus.ContractedBy(4f), Tex.Plus, plusOver ? Color.black : (over || sel ? Color.white : Ui.Faint));
            TooltipHandler.TipRegion(plus, "BO_AddTip".Translate());
            if (Widgets.ButtonInvisible(plus)) { selectedKey = e.key; AddSelected(stack, e); return; }

            Color old = GUI.color;
            if (e.stock == Stock.Missing) GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Window_RecipePicker.DrawIcon(new Rect(row.x + 30f, row.y + 3f, row.height - 6f, row.height - 6f), e);
            GUI.color = old;

            bool pinned = detail.IsPinned(e);
            // batches the stock allows (green; red 0); nothing when it is not known. No "x": that glyph means "repeat" or "batch size" elsewhere
            const float BadgeW = 40f;
            if (e.canMake >= 0)
            {
                var badge = new Rect(row.xMax - 34f - BadgeW, row.y, BadgeW, row.height);
                Ui.Text_(badge, e.canMake.ToString(), e.canMake > 0 ? Ui.Good : Ui.Bad, TextAnchor.MiddleRight);
                TooltipHandler.TipRegion(badge, Availability.MakeTip(e));
            }
            bool bonus = Bonus.Has(e);
            var nameR = new Rect(row.x + 30f + row.height, row.y, row.width - 30f - row.height - 34f - (e.canMake >= 0 ? BadgeW : 0f) - (pinned ? 18f : 0f) - (bonus ? 18f : 0f), row.height);
            Ui.NameFit(nameR, e.label, e.stock == Stock.Missing ? new Color(1f, 1f, 1f, 0.62f) : Color.white);
            float gx = nameR.xMax + 2f;
            if (bonus)
            {
                var br = new Rect(gx, row.y + (row.height - 14f) / 2f, 14f, 14f);
                Ui.Glyph(br, Tex.Bonus, Ui.Bonus);
                TooltipHandler.TipRegion(br, Bonus.MarkTip(e.product));
                gx += 18f;
            }
            if (pinned) Ui.Glyph(new Rect(gx, row.y + (row.height - 14f) / 2f, 14f, 14f), Tex.Pin, Ui.Accent);
            if (!e.SkillOk(table.Map))
            {
                var warn = new Rect(row.xMax - 37f, row.y + 3f, 6f, 6f);
                Ui.Box(warn, Ui.Warn);
                TooltipHandler.TipRegion(warn.ExpandedBy(3f), "BO_NoSkill".Translate());
            }

            // click selects, double click adds, Shift+click pins for comparing
            if (Widgets.ButtonInvisible(new Rect(row.x + 28f, row.y, row.width - 62f, row.height)))
            {
                if (Event.current.shift) { selectedKey = e.key; detail.TogglePin(e); return; }
                bool dbl = lastClickKey == e.key && Time.realtimeSinceStartup - lastClickTime < 0.35f;
                lastClickKey = e.key; lastClickTime = Time.realtimeSinceStartup;
                selectedKey = e.key;
                if (dbl) AddSelected(stack, e);
            }
        }
    }

    /// <summary>The compact tab is wider and a bit taller than the vanilla one; size is also set in FillTab because the setting can change in game.</summary>
    [HarmonyPatch(typeof(ITab_Bills), MethodType.Constructor)]
    public static class Patch_BillTabSize
    {
        static void Postfix(ITab_Bills __instance)
        {
            try { if (Patch_BillTab.Active) Traverse.Create(__instance).Field("size").SetValue(Patch_BillTab.TabSize); }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Compact bill tab size failed: " + e, 0x0B110004); }
        }
    }

    /// <summary>
    /// Hides the vanilla close X of the inspect tab only while our compact bill tab is drawn (we draw our own in the right header).
    /// Everywhere else <c>Widgets.CloseButtonFor</c> runs as is; any failure here keeps the vanilla button.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.CloseButtonFor))]
    public static class Patch_TabCloseButton
    {
        static bool Prefix(ref bool __result)
        {
            try
            {
                if (!Patch_BillTab.DrawingCompactTab()) return true;
                __result = false;
                return false;
            }
            catch (Exception) { return true; }
        }
    }
}
