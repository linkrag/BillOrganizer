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
    /// Detail/compare panel of one recipe (stats, production, materials, ingredients, bill options). Shared by the recipe window and the
    /// compact bill tab; the host supplies what to do on "add", on selecting another recipe and when the list must be rebuilt.
    /// </summary>
    public class RecipeDetail
    {
        const int MaxPinned = 3;

        readonly BillStack stack;
        readonly Thing bench;
        readonly Map map;
        readonly List<RecipeEntry> all;
        readonly Availability avail;
        readonly Window_RecipePicker.Memo memo;
        readonly Action<bool> add;
        readonly Action<RecipeEntry> select;
        readonly Action markDirty;

        Vector2 detailScroll;
        float detailViewH = 600f;
        readonly List<RecipeEntry> pinned = new List<RecipeEntry>();
        readonly Dictionary<string, float> kindMax = new Dictionary<string, float>();
        string matStat = "sharp";
        bool diffOnly, descExpanded;
        RecipeEntry selected;

        /// <summary>What the panel adds with: copies and material (repeat mode / quantity live in the memo).</summary>
        public readonly AddOptions opts = new AddOptions();
        public RecipeEntry stuffFor;

        public RecipeDetail(BillStack stack, Map map, Thing bench, List<RecipeEntry> all, Availability avail, Window_RecipePicker.Memo memo,
            Action<bool> add, Action<RecipeEntry> select, Action markDirty)
        {
            this.stack = stack; this.map = map; this.bench = bench; this.all = all; this.avail = avail; this.memo = memo;
            this.add = add; this.select = select; this.markDirty = markDirty;
        }

        public bool IsPinned(RecipeEntry e) => pinned.Contains(e);

        /// <param name="box">false when the host already drew the panel box around it (compact tab: header row above the detail)</param>
        public void Draw(Rect r, RecipeEntry current, bool box = true)
        {
            if (current != selected) { detailScroll = Vector2.zero; descExpanded = false; } // a new recipe starts at the top, description folded
            selected = current;
            if (box) Ui.PanelBox(r);
            Rect outR = r.ContractedBy(8f);
            if (selected == null && pinned.Count == 0)
            {
                Ui.Text_(outR, "BO_PickOne".Translate(), Ui.Faint, TextAnchor.MiddleCenter, GameFont.Small, true);
                return;
            }
            var view = new Rect(0f, 0f, outR.width - 16f, detailViewH);
            Widgets.BeginScrollView(outR, ref detailScroll, view);
            float y = 0f;
            if (selected != null) y = DrawSelected(selected, view.width, y);
            if (pinned.Count > 0) y = DrawCompare(view.width, y + 12f);
            Widgets.EndScrollView();
            detailViewH = y + 12f;
        }

        // ------------------------------------------------------------ helpers

        static float SectionHeader(float w, float y, string text)
        {
            Ui.Text_(new Rect(0f, y, w, 20f), text.ToUpperInvariant(), Ui.Dim, TextAnchor.MiddleLeft);
            Ui.Box(new Rect(0f, y + 21f, w, 1f), Ui.Outline);
            return y + 26f;
        }

        float KindMax(StatInfo c, Kind k)
        {
            string key = k + ":" + c.key;
            if (!kindMax.TryGetValue(key, out float m))
            {
                m = 0f;
                foreach (RecipeEntry e in all) if (e.kind == k) { float v = c.get(e); if (!float.IsNaN(v) && v > m) m = v; }
                kindMax[key] = m;
            }
            return m;
        }

        bool SmallButton(Rect r, Texture2D glyph, string tip, bool active = false)
        {
            bool over = Mouse.IsOver(r);
            Ui.Box(r, active ? Ui.Selected : over ? Ui.Hover : Ui.PanelLight);
            Ui.Glyph(r.ContractedBy(7f), glyph, active ? Ui.Accent : Color.white);
            TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        public void TogglePin(RecipeEntry e)
        {
            if (pinned.Contains(e)) pinned.Remove(e);
            else if (pinned.Count < MaxPinned) pinned.Add(e);
            else { pinned.RemoveAt(0); pinned.Add(e); }
        }

        /// <summary>
        /// [-] value [+]: one <paramref name="unit"/> per click (one batch for "until X"), times the vanilla modifier keys (Ui.StepMult).
        /// Values off the unit snap to it (501 by 500: + 1000, - 500). True when the value changed.
        /// </summary>
        static bool Stepper(float x, float y, ref int v, int min, int max, int unit = 1)
        {
            unit = Mathf.Max(unit, 1);
            int step = unit * Ui.StepMult();
            string tip = "BO_StepTip".Translate(unit) + "\n" + Ui.StepKeysTip();
            bool changed = false;
            if (Ui.Chip(new Rect(x, y, 24f, 24f), "-", false, tip)) { v = Mathf.Max(Mathf.Min(min, v), Ui.StepDown(v, unit, step)); changed = true; }
            Ui.Text_(new Rect(x + 24f, y, 48f, 24f), v.ToString(), Color.white, TextAnchor.MiddleCenter);
            if (Ui.Chip(new Rect(x + 72f, y, 24f, 24f), "+", false, tip)) { v = Mathf.Min(max, Ui.StepUp(v, unit, step)); changed = true; }
            return changed;
        }

        // ------------------------------------------------------------ bill options (repeat mode, quantity, copies, material)

        /// <summary>Quantity presets as multiples of the mode's unit (1 for "do X times", one batch for "until X").</summary>
        static readonly int[] QtyMults = { 1, 5, 25, 50 };
        /// <summary>
        /// The marked preset (index in QtyMults, -1 = none). Clicking a preset that is not marked sets the value to it and marks it;
        /// clicking the marked one again adds it (1, 5 -> 5, 5 again -> 10). The -/+ buttons always step by one unit.
        /// </summary>
        int qtyMark = 0;

        static int QtyUnit(RecipeEntry e, int mode) => mode == 2 ? e.TargetStep : 1;

        /// <summary>The preset whose value is <paramref name="qty"/> (the initial state of a recipe or mode), or -1.</summary>
        static int MarkFor(int qty, int unit)
        {
            for (int i = 0; i < QtyMults.Length; i++) if (unit * QtyMults[i] == qty) return i;
            return -1;
        }

        /// <summary>"Until X" starts at one batch: 200 cartridges, not 1. Only raises a value below the batch or replaces our own.</summary>
        void ApplyBatchTarget(RecipeEntry e)
        {
            if (memo.cfgMode != 2) return;
            int b = e.TargetStep;
            if (memo.cfgAuto || memo.cfgQty < b) { memo.cfgQty = b; memo.cfgAuto = true; }
            else if (memo.cfgQty % b != 0) memo.cfgQty = RecipeEntry.SnapTarget(memo.cfgQty, b); // carried over from another recipe: whole batches of this one
        }

        float DrawOptions(RecipeEntry e, float w, float y)
        {
            if (stuffFor != e) { stuffFor = e; opts.stuff = null; ApplyBatchTarget(e); qtyMark = MarkFor(memo.cfgQty, QtyUnit(e, memo.cfgMode)); }
            y = SectionHeader(w, y, "BO_SecBillOptions".Translate());
            string[] modes = { "BO_ModeDefault", "BO_ModeCount", "BO_ModeTarget", "BO_ModeForever" };
            float x = 0f;
            for (int i = 0; i < modes.Length; i++)
            {
                string label = modes[i].Translate();
                float cw = Ui.ChipWidth(label);
                if (x + cw > w) { x = 0f; y += 28f; }
                if (Ui.Chip(new Rect(x, y, cw, 24f), label, memo.cfgMode == i, (modes[i] + "Tip").Translate()) && memo.cfgMode != i)
                {
                    // repetitions and items are different units: 200 items must not become 200 repetitions. Start over at one unit (1x step).
                    memo.cfgMode = i;
                    memo.cfgQty = i == 2 ? e.TargetStep : 1;
                    memo.cfgAuto = true;
                    qtyMark = 0;
                }
                x += cw + 4f;
            }
            y += 30f;

            if (memo.cfgMode == 1 || memo.cfgMode == 2)
            {
                Ui.Text_(new Rect(0f, y, 80f, 24f), (memo.cfgMode == 1 ? "BO_QtyCount" : "BO_QtyTarget").Translate(), Ui.Dim);
                // "until X" counts items in batches like the vanilla dialog (200 for CE ammo); presets are 1x/5x/25x/50x of the unit.
                // -/+ step by one unit; a preset sets its value the first time (and is marked) and adds it on the next clicks.
                // The vanilla modifier keys multiply both (Ui.StepMult: Ctrl x10, Shift x100 by default)
                int unit = QtyUnit(e, memo.cfgMode);
                if (Stepper(84f, y, ref memo.cfgQty, unit, 99999, unit)) memo.cfgAuto = false; // "until X" never drops below one batch
                float px = 84f + 104f;
                string keys = Ui.StepKeysTip();
                for (int pi = 0; pi < QtyMults.Length; pi++)
                {
                    int preset = unit * QtyMults[pi];
                    string pl = preset.ToString();
                    Verse.Text.Font = GameFont.Small;
                    float pw = Mathf.Max(34f, Verse.Text.CalcSize(pl).x + 12f);
                    if (px + pw > w) break;
                    if (Ui.Chip(new Rect(px, y, pw, 24f), pl, qtyMark == pi, "BO_PresetTip".Translate(preset) + "\n" + keys))
                    {
                        long amount = (long)preset * Ui.StepMult();
                        long next = qtyMark == pi ? memo.cfgQty + amount : amount;
                        memo.cfgQty = (int)Math.Min(next, 99999L);
                        qtyMark = pi;
                        memo.cfgAuto = false;
                    }
                    px += pw + 2f;
                }
                int cm = e.canMake, mi = e.makeItems;
                if (stuffFor == e && opts.stuff != null && avail != null)
                {
                    int ws = avail.BatchesWith(e, opts.stuff, out string _);
                    if (ws >= 0) { cm = ws; mi = (int)Mathf.Min((long)ws * e.ItemsPerBatch, int.MaxValue); }
                }
                if (cm > 0 && px + 44f <= w)
                {
                    // fill with everything the stock allows: batches for "do X times"; for "until X" what is already in stock plus the items
                    // the stock can still make (the target counts the products you already have)
                    int haveNow = memo.cfgMode == 2 && e.product != null ? Mathf.Max(0, avail.CountOf(e.product)) : 0;
                    // "until X": rounded DOWN to whole batches (same crafts as stock + makeable, never one batch more than the stock pays)
                    int u = e.TargetStep;
                    int max = memo.cfgMode == 1 ? Mathf.Clamp(cm, 1, 99999) : Mathf.Clamp((mi + haveNow) / u * u, u, Mathf.Max(u, 99999 / u * u));
                    string maxTip = memo.cfgMode == 2 ? "BO_MaxTipTarget".Translate(haveNow, mi).ToString() : "BO_MaxTip".Translate(Ui.Thousands(cm), Ui.Thousands(mi)).ToString();
                    if (Ui.Chip(new Rect(px, y, 44f, 24f), "BO_Max".Translate(), memo.cfgQty == max, maxTip)) { memo.cfgQty = max; memo.cfgAuto = false; qtyMark = -1; }
                }
                y += 28f;
            }
            int room = Mathf.Max(BillStack.MaxCount - stack.Count, 1);
            opts.times = Mathf.Clamp(opts.times, 1, room);
            Ui.Text_(new Rect(0f, y, 80f, 24f), "BO_Times".Translate(), Ui.Dim);
            Stepper(84f, y, ref opts.times, 1, room);
            TooltipHandler.TipRegion(new Rect(0f, y, 84f, 24f), "BO_TimesTip".Translate() + "\n" + Ui.StepKeysTip());
            y += 28f;

            List<ThingDef> stuffs = e.AllowedStuffs();
            if (stuffs.Count > 1)
            {
                Ui.Text_(new Rect(0f, y, 80f, 24f), "BO_Material".Translate(), Ui.Dim);
                var btn = new Rect(84f, y, w - 84f, 24f);
                string cur = opts.stuff != null ? opts.stuff.LabelCap.ToString() : "BO_MatAny".Translate().ToString();
                if (Ui.Chip(btn, cur.Truncate(btn.width - 30f), opts.stuff != null, "BO_MaterialTip".Translate())) OpenMaterialMenu(e, stuffs);
                Ui.Glyph(new Rect(btn.xMax - 18f, btn.y + 6f, 12f, 12f), Tex.ArrowD, Ui.Dim); // it opens a menu
                y += 28f;
            }
            return y + 4f;
        }

        void OpenMaterialMenu(RecipeEntry e, List<ThingDef> stuffs)
        {
            var list = new List<FloatMenuOption> { new FloatMenuOption("BO_MatAny".Translate(), () => opts.stuff = null) };
            foreach (ThingDef s in stuffs.OrderByDescending(d => avail.CountOf(d)).ThenBy(d => d.label))
            {
                ThingDef pick = s;
                int have = avail.CountOf(s);
                list.Add(new FloatMenuOption(s.LabelCap + (have >= 0 ? " (" + have + ")" : ""), () => opts.stuff = pick));
            }
            Find.WindowStack.Add(new FloatMenu(list));
        }

        // ------------------------------------------------------------ selected recipe

        float DrawSelected(RecipeEntry e, float w, float y)
        {
            e.EnsureMaterials();
            // title block
            Window_RecipePicker.DrawIcon(new Rect(0f, y, 64f, 64f), e);
            Verse.Text.Font = GameFont.Medium;
            float titleH = Mathf.Clamp(Verse.Text.CalcHeight(e.label, w - 72f), 28f, 66f);
            Verse.Text.Font = GameFont.Small;
            var titleR = new Rect(72f, y, w - 72f, titleH);
            Ui.Text_(titleR, e.label, Color.white, TextAnchor.UpperLeft, GameFont.Medium, true);
            TooltipHandler.TipRegion(titleR, e.label);
            string sub = e.modName;
            if (e.kind == Kind.Ranged && e.ammoSetLabel != null) sub = e.ammoSetLabel + (sub.Length > 0 ? "  -  " + sub : "");
            var subR = new Rect(72f, y + titleH + 2f, w - 72f, 20f);
            Ui.Text_(subR, sub.Truncate(subR.width), Ui.Faint);
            if (sub.Length > 0) TooltipHandler.TipRegion(subR, sub);
            y += Mathf.Max(72f, titleH + 26f);

            // actions
            float bw = w - 3 * 36f - 6f;
            var addR = new Rect(0f, y, bw, 32f);
            bool full = stack.Count >= BillStack.MaxCount;
            bool addOver = Mouse.IsOver(addR) && !full;
            Ui.Box(addR, full ? Ui.PanelLight : addOver ? new Color(1f, 0.86f, 0.45f) : Ui.Accent);
            Ui.Text_(addR, (full ? "BO_StackFull" : "BO_AddBill").Translate(), full ? Ui.Faint : Color.black, TextAnchor.MiddleCenter, GameFont.Small);
            if (!full && Widgets.ButtonInvisible(addR)) add(Event.current.shift);
            float bx = addR.xMax + 6f;
            bool isPinned = pinned.Contains(e);
            if (SmallButton(new Rect(bx, y, 30f, 32f), Tex.Pin, "BO_PinTip".Translate(), isPinned)) TogglePin(e);
            bx += 36f;
            if (SmallButton(new Rect(bx, y, 30f, 32f), e.IsFavorite ? Tex.Star : Tex.StarO, "BO_FavoriteTip".Translate(), e.IsFavorite)) { e.ToggleFavorite(); markDirty(); }
            bx += 36f;
            var infoR = new Rect(bx, y, 30f, 32f);
            Ui.Box(infoR, Mouse.IsOver(infoR) ? Ui.Hover : Ui.PanelLight);
            Widgets.InfoCardButton(infoR.x + 3f, infoR.y + 4f, e.recipe, e.precept);
            y += 40f;

            y = DrawOptions(e, w, y);

            // warnings
            ThingDef pickedStuff = stuffFor == e ? opts.stuff : null;
            string stuffLimit = null;
            int withStuff = pickedStuff != null && avail != null ? avail.BatchesWith(e, pickedStuff, out stuffLimit) : -1;
            if (withStuff >= 0)
            {
                // a material is picked: count only that material (the bill will allow nothing else)
                var mk = new Rect(0f, y, w, 20f);
                int items = (int)Mathf.Min((long)withStuff * e.ItemsPerBatch, int.MaxValue);
                string lim = stuffLimit != null ? "BO_CanMakeBy".Translate(stuffLimit).ToString() : "";
                string mainText = items > withStuff && withStuff > 0
                    ? "BO_CanMake".Translate(Ui.Thousands(withStuff), Ui.Thousands(items)).ToString()
                    : "BO_CanMakeOne".Translate(Ui.Thousands(withStuff)).ToString();
                Ui.TextFit(mk, mainText + lim, withStuff > 0 ? Ui.Good : Ui.Bad);
                TooltipHandler.TipRegion(mk, "BO_CanMakeStuffTip".Translate(pickedStuff.LabelCap, Mathf.Max(avail.CountOf(pickedStuff), 0)));
                y += 22f;
            }
            else if (e.canMake >= 0)
            {
                var mk = new Rect(0f, y, w, 20f);
                // one sentence, at most one pair of parentheses; the items part only when a batch makes more than one item
                string lim = e.makeLimiter != null && e.canMake < int.MaxValue ? "BO_CanMakeBy".Translate(e.makeLimiter.defs.Count == 1 ? e.makeLimiter.defs[0].LabelCap.ToString() : e.makeLimiter.ing.filter.Summary.CapitalizeFirst()).ToString() : "";
                string mainText = e.makeItems > e.canMake && e.canMake > 0
                    ? "BO_CanMake".Translate(Ui.Thousands(e.canMake), Ui.Thousands(e.makeItems)).ToString()
                    : "BO_CanMakeOne".Translate(Ui.Thousands(e.canMake)).ToString();
                Ui.TextFit(mk, mainText + lim, e.canMake > 0 ? Ui.Good : Ui.Bad);
                TooltipHandler.TipRegion(mk, Availability.MakeTip(e));
                y += 22f;
            }
            else if (e.stock == Stock.Missing) { Ui.Text_(new Rect(0f, y, w, 20f), "BO_MissingMat".Translate(), Ui.Bad); y += 22f; }
            if (!e.SkillOk(map)) { Ui.Text_(new Rect(0f, y, w, 20f), "BO_NoSkill".Translate(), Ui.Warn); y += 22f; }

            // description
            string desc = (e.product?.description ?? e.recipe.description) ?? "";
            // Combat Extended ends an ammo description with "Used by:" and leaves the list to the info card; our "Used by" section below
            // has it, so the dangling header is dropped
            if (desc.Length > 0 && "CE_UsedBy".TryTranslate(out TaggedString usedBy))
            {
                string tail = usedBy.ToString() + ":";
                string trimmed = desc.TrimEnd();
                if (trimmed.EndsWith(tail, StringComparison.Ordinal)) desc = trimmed.Substring(0, trimmed.Length - tail.Length).TrimEnd();
            }
            if (desc.Length > 0)
            {
                desc = desc.CapitalizeFirst();
                Verse.Text.Font = GameFont.Small;
                float fullH = Verse.Text.CalcHeight(desc, w);
                bool longDesc = fullH > 110f;
                float dh = longDesc && !descExpanded ? 110f : fullH;
                var descR = new Rect(0f, y, w, dh);
                Ui.Text_(descR, desc, Ui.Dim, TextAnchor.UpperLeft, GameFont.Small, true);
                y += dh;
                if (longDesc)
                {
                    // "More" / "Less" instead of a tiny "...": the text is shown whole on click
                    string toggle = (descExpanded ? "BO_DescLess" : "BO_DescMore").Translate();
                    float tw = Verse.Text.CalcSize(toggle).x + 12f;
                    var tr = new Rect(w - tw, y + 1f, tw, 20f);
                    Ui.Text_(tr, toggle, Mouse.IsOver(tr) ? Ui.Accent : Ui.Dim, TextAnchor.MiddleRight);
                    if (Widgets.ButtonInvisible(tr)) descExpanded = !descExpanded;
                    y += 22f;
                }
                y += 8f;
            }

            y = DrawStats(e, w, y);
            y = DrawBonus(e, w, y);
            y = DrawProduction(e, w, y);
            if (e.kind == Kind.Apparel) y = DrawMaterials(e, w, y);
            y = DrawIngredients(e, w, y);
            if (e.kind == Kind.Ammo) y = DrawUsers(e, w, y);
            return y;
        }

        /// <summary>The material row of the material picked in the bill options (apparel), or null for "any".</summary>
        MaterialRow PickedRow(RecipeEntry e)
        {
            if (stuffFor != e || opts.stuff == null || e.kind != Kind.Apparel) return null;
            e.EnsureMaterials();
            return e.materials?.FirstOrDefault(m => m.stuff == opts.stuff);
        }

        static float RowValue(MaterialRow m, string key) => key == "sharp" ? m.sharp : key == "blunt" ? m.blunt : key == "heat" ? m.heat : key == "cold" ? m.cold : float.NaN;

        float DrawStats(RecipeEntry e, float w, float y)
        {
            MaterialRow picked = PickedRow(e);
            // apparel without any armor or insulation (airwire headset): no row of zeros; a range "0 to 3 mm" keeps its row
            List<StatInfo> stats = StatTable.ForKind(e.kind).Where(s => { float v = s.get(e); return !float.IsNaN(v) && (e.kind != Kind.Apparel || Mathf.Abs(v) > 0.0001f); }).ToList();
            if (stats.Count == 0 && e.ammoOpts == null) return y;
            y = SectionHeader(w, y, "BO_SecStats".Translate());

            // Combat Extended: the numbers depend on the ammo, so let the player pick it
            if (e.ammoOpts != null && e.ammoOpts.Count > 1)
            {
                Ui.Text_(new Rect(0f, y, w, 18f), "BO_AmmoPick".Translate(), Ui.Faint);
                y += 20f;
                float x = 0f;
                for (int i = 0; i < e.ammoOpts.Count; i++)
                {
                    AmmoOpt o = e.ammoOpts[i];
                    string label = o.classLabel ?? (o.ammo != null ? o.ammo.LabelCap.ToString() : "?");
                    float cw = Ui.ChipWidth(label);
                    if (x + cw > w) { x = 0f; y += 28f; }
                    if (Ui.Chip(new Rect(x, y, cw, 24f), label, e.ammoIdx == i, o.ammo?.LabelCap)) { e.ApplyAmmo(i); markDirty(); }
                    x += cw + 4f;
                }
                y += 32f;
            }

            foreach (StatInfo s in stats)
            {
                float v = s.get(e);
                // a picked material shows its own value; "any" keeps the range of every allowed material
                float pv = picked != null ? RowValue(picked, s.key) : float.NaN;
                if (!float.IsNaN(pv)) v = pv;
                float max = KindMax(s, e.kind);
                bool best = max > 0f && v >= max * 0.9999f;
                string text = !float.IsNaN(pv) ? s.fmt(v) : s.detail != null ? s.detail(e) : s.fmt(v);
                float valW = Mathf.Min(Verse.Text.CalcSize(text).x + 4f, w * 0.6f);
                Ui.TextFit(new Rect(0f, y, w - valW - 6f, 18f), s.Label, Ui.Dim);
                Ui.TextFit(new Rect(w - valW, y, valW, 18f), text, Color.white, TextAnchor.MiddleRight); // the bar carries the stat color (red numbers read as "missing")
                Ui.Bar(new Rect(0f, y + 21f, w, 6f), v, max, s.color, best);
                TooltipHandler.TipRegion(new Rect(0f, y, w, 28f), s.Tip + "\n" + "BO_BestOnBench".Translate(s.fmt(max)));
                y += 33f;
            }
            return y + 4f;
        }

        /// <summary>
        /// What the item does beyond its combat stats (Bonus.For): stats when worn/equipped, abilities, the hediff it applies, implant,
        /// mechanoid, drug and food effects. One subtitle per group; a line with a def (ability, hediff) opens its info card on click.
        /// </summary>
        float DrawBonus(RecipeEntry e, float w, float y)
        {
            if (e.product == null) return y;
            List<BonusLine> lines = Bonus.For(e.product);
            if (lines.Count == 0) return y;
            y = SectionHeader(w, y, "BO_SecBonus".Translate());
            bool several = lines.Select(b => b.group).Distinct().Count() > 1;
            foreach (string g in Bonus.Groups)
            {
                bool first = true;
                foreach (BonusLine b in lines)
                {
                    if (b.group != g) continue;
                    if (first && several)
                    {
                        Ui.Text_(new Rect(0f, y, w, 18f), g.Translate(), Ui.Faint);
                        y += 19f;
                    }
                    first = false;
                    var row = new Rect(0f, y, w, 20f);
                    if (b.link != null && Mouse.IsOver(row)) Ui.Box(row, Ui.Hover);
                    Verse.Text.Font = GameFont.Small;
                    float valW = Mathf.Min(Verse.Text.CalcSize(b.valueText).x + 4f, w * 0.55f);
                    Ui.TextFit(new Rect(4f, y, w - valW - 10f, 20f), b.label, Ui.Dim);
                    // white, sign only: StatDef has no "higher is better", so green/red would lie for half the stats (move speed vs. pain shock)
                    Ui.TextFit(new Rect(w - valW, y, valW, 20f), b.valueText, Color.white, TextAnchor.MiddleRight);
                    string tip = b.label + ": " + b.valueText + (b.tip.NullOrEmpty() ? "" : "\n\n" + b.tip) + (b.link != null ? "\n\n" + "BO_BonusLinkTip".Translate() : "");
                    TooltipHandler.TipRegion(row, tip);
                    if (b.link != null && Widgets.ButtonInvisible(row)) Find.WindowStack.Add(new Dialog_InfoCard(b.link));
                    y += 21f;
                }
            }
            return y + 6f;
        }

        /// <summary>Work to make, minimum skill and market value (with the cost of the ingredients as a tooltip).</summary>
        float DrawProduction(RecipeEntry e, float w, float y)
        {
            List<StatInfo> rows = StatTable.Production(e);
            if (rows.Count == 0) return y;
            y = SectionHeader(w, y, "BO_SecProduction".Translate());
            foreach (StatInfo s in rows)
            {
                float v = s.get(e);
                if (s.key == "value" && stuffFor == e && opts.stuff != null && e.product != null && e.product.MadeFromStuff)
                {
                    float mv = e.product.GetStatValueAbstract(StatDefOf.MarketValue, opts.stuff);
                    if (mv > 0f) v = mv;
                }
                string text = s.key == "value" ? StatDefOf.MarketValue.ValueToString(v, ToStringNumberSense.Absolute) : s.fmt(v); // the game's own money format
                Color c = Color.white;
                if (s.key == "skill")
                {
                    string names = string.Join(", ", e.recipe.skillRequirements.Select(r => r.skill.LabelCap + " " + r.minLevel));
                    text = names;
                    if (!e.SkillOk(map)) c = Ui.Warn;
                }
                Ui.Text_(new Rect(0f, y, w * 0.4f, 20f), s.Label, Ui.Dim);
                Ui.Text_(new Rect(w * 0.3f, y, w * 0.7f, 20f), text, c, TextAnchor.MiddleRight);
                TooltipHandler.TipRegion(new Rect(0f, y, w, 20f), s.Tip + (s.key == "value" ? "\n" + "BO_IngredientsValue".Translate(IngredientsValue(e).ToString("0.#")) : ""));
                y += 22f;
            }
            return y + 6f;
        }

        static float IngredientsValue(RecipeEntry e)
        {
            float sum = 0f;
            foreach (IngInfo i in e.ings)
                if (i.rep != null) sum += i.need * i.rep.BaseMarketValue;
            return sum;
        }

        float DrawMaterials(RecipeEntry e, float w, float y)
        {
            if (e.materials == null || e.materials.Count < 2) return y;
            y = SectionHeader(w, y, "BO_SecMaterials".Translate() + " (" + e.materials.Count + ")");
            // "best material for ...": pick the stat the list is ordered by
            StatInfo[] byStat = { StatTable.Sharp, StatTable.Blunt, StatTable.Heat, StatTable.Cold };
            float chipX = 0f;
            foreach (StatInfo s in byStat)
            {
                float cw = Ui.ChipWidth(s.Label);
                if (Ui.Chip(new Rect(chipX, y, cw, 22f), s.Label, matStat == s.key, "BO_MatSortTip".Translate())) matStat = s.key;
                chipX += cw + 4f;
            }
            y += 26f;
            Func<MaterialRow, float> val = m => matStat == "blunt" ? m.blunt : matStat == "heat" ? m.heat : matStat == "cold" ? m.cold : m.sharp;
            StatInfo main = byStat.First(s => s.key == matStat), second = matStat == "sharp" ? StatTable.Blunt : StatTable.Sharp;
            Func<MaterialRow, float> val2 = m => second == StatTable.Blunt ? m.blunt : m.sharp;
            float need = e.ings.Count > 0 ? e.ings[0].need : 0f;
            {   // column headers for the two numbers of each row
                float hx = 24f + w * 0.38f, hw = (w - hx) / 2f;
                Ui.Text_(new Rect(hx, y, hw, 16f), main.Label, main.color, TextAnchor.MiddleRight);
                Ui.Text_(new Rect(hx + hw, y, hw - 2f, 16f), second.Label, second.color, TextAnchor.MiddleRight);
                y += 17f;
            }
            bool marked = false;
            int shownRows = 0;
            foreach (MaterialRow m in e.materials.OrderByDescending(val).ThenByDescending(val2))
            {
                int have = avail.CountOf(m.stuff);
                bool inStock = have >= need && have >= 0;
                bool best = inStock && !marked;
                bool isPicked = stuffFor == e && opts.stuff == m.stuff;
                if (shownRows >= 7 && !best && !isPicked) continue;
                if (best) marked = true;
                var row = new Rect(0f, y, w, 22f);
                if (best) Ui.Box(row, new Color(Ui.Good.r, Ui.Good.g, Ui.Good.b, 0.14f));
                if (isPicked) { Ui.Box(row, Ui.Selected); Widgets.DrawBox(row, 1); }
                else if (Mouse.IsOver(row)) Ui.Box(row, Ui.Hover);
                Widgets.ThingIcon(new Rect(2f, y + 2f, 18f, 18f), m.stuff);
                Ui.Text_(new Rect(24f, y, w * 0.38f, 22f), m.stuff.LabelCap.ToString().Truncate(w * 0.38f), inStock ? Color.white : Ui.Faint);
                float vx = 24f + w * 0.38f;
                float cw = (w - vx) / 2f;
                Ui.Text_(new Rect(vx, y, cw, 22f), main.fmt(val(m)), inStock ? Color.white : Ui.Dim, TextAnchor.MiddleRight);
                Ui.Text_(new Rect(vx + cw, y, cw - 2f, 22f), second.fmt(val2(m)), inStock ? Color.white : Ui.Dim, TextAnchor.MiddleRight);
                TooltipHandler.TipRegion(row, m.stuff.LabelCap + "\n" + "BO_InStock".Translate(Mathf.Max(have, 0)) + "\n" +
                    StatTable.Heat.Label + ": " + StatTable.Heat.fmt(m.heat) + "   " + StatTable.Cold.Label + ": " + StatTable.Cold.fmt(m.cold) + "\n\n" + "BO_MatRowClickTip".Translate());
                if (Widgets.ButtonInvisible(row))
                {
                    // click = use this material for the bill (again = back to any)
                    stuffFor = e;
                    opts.stuff = isPicked ? null : m.stuff;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }
                y += 22f;
                shownRows++;
            }
            if (e.materials.Count > shownRows) { Ui.Text_(new Rect(0f, y, w, 18f), "BO_MoreMaterials".Translate(e.materials.Count - shownRows), Ui.Faint); y += 20f; }
            Ui.Text_(new Rect(0f, y, w, 18f), "BO_BestInStockHint".Translate(), Ui.Faint);
            return y + 24f;
        }

        float DrawIngredients(RecipeEntry e, float w, float y)
        {
            if (e.ings.Count == 0 && e.recipe.skillRequirements.NullOrEmpty()) return y;
            y = SectionHeader(w, y, "BO_SecIngredients".Translate());
            foreach (IngInfo i in e.ings)
            {
                var row = new Rect(0f, y, w, 24f);
                if (i.rep != null) Widgets.ThingIcon(new Rect(2f, y + 2f, 20f, 20f), i.rep);
                string name = i.defs.Count == 1 ? i.defs[0].LabelCap.ToString() : i.ing.filter.Summary.CapitalizeFirst();
                Ui.Text_(new Rect(28f, y, w - 28f - 90f, 24f), i.need.ToString("0.##") + "x " + name.Truncate(w - 150f), Color.white);
                if (i.known)
                {
                    Ui.Text_(new Rect(w - 90f, y, 66f, 24f), i.Have + "/" + i.need.ToString("0.##"), i.Ok ? Ui.Good : Ui.Bad, TextAnchor.MiddleRight);
                    if (i.batches >= 0 && i == e.makeLimiter && e.canMake < int.MaxValue) Ui.Box(new Rect(0f, y + 1f, 2f, 22f), Ui.Warn); // the bottleneck
                    Ui.Glyph(new Rect(w - 20f, y + 5f, 14f, 14f), i.Ok ? Tex.Check : Tex.Cross, i.Ok ? Ui.Good : Ui.Bad);
                }
                TooltipHandler.TipRegion(row, name + (i.single ? "\n" + "BO_SingleMaterial".Translate() : "") + "\n" + "BO_IngClickTip".Translate());
                if (Mouse.IsOver(row)) Ui.Box(row, new Color(1f, 1f, 1f, 0.05f));
                if (Widgets.ButtonInvisible(row)) OpenIngredientMenu(i);
                y += 25f;
            }
            if (!e.recipe.skillRequirements.NullOrEmpty())
            {
                y += 4f;
                foreach (SkillRequirement req in e.recipe.skillRequirements)
                {
                    bool ok = map == null || map.mapPawns.FreeColonists.Any(p => req.PawnSatisfies(p));
                    Ui.Glyph(new Rect(4f, y + 5f, 14f, 14f), ok ? Tex.Check : Tex.Cross, ok ? Ui.Good : Ui.Warn);
                    Ui.Text_(new Rect(28f, y, w - 28f, 24f), req.skill.LabelCap + " " + req.minLevel, ok ? Ui.Dim : Ui.Warn);
                    y += 24f;
                }
            }
            return y + 6f;
        }

        /// <summary>Click on an ingredient: info card, find it on the map, or queue it on a bench that makes it.</summary>
        void OpenIngredientMenu(IngInfo ing)
        {
            var list = new List<FloatMenuOption>();
            if (ing.rep != null)
            {
                ThingDef info = ing.rep;
                list.Add(new FloatMenuOption("BO_IngInfo".Translate(), () => Find.WindowStack.Add(new Dialog_InfoCard(info))));
            }
            foreach (ThingDef d0 in ing.defs.OrderByDescending(d => avail.CountOf(d)).Take(8))
            {
                ThingDef d = d0;
                int have = avail.CountOf(d);
                Thing nearest = NearestOnMap(d);
                list.Add(nearest == null
                    ? new FloatMenuOption("BO_IngFindNone".Translate(d.LabelCap), null)
                    : new FloatMenuOption("BO_IngFind".Translate(d.LabelCap, Mathf.Max(have, 0)), () => CameraJumper.TryJumpAndSelect(nearest)));
                int missing = Mathf.Max(1, Mathf.CeilToInt(ing.need - ing.Have));
                list.Add(CanQueue(d)
                    ? new FloatMenuOption("BO_IngQueue".Translate(missing, d.LabelCap), () => QueueIngredient(d, missing))
                    : new FloatMenuOption("BO_IngQueueNone".Translate(d.LabelCap), null));
            }
            if (list.Count > 0) Find.WindowStack.Add(new FloatMenu(list));
        }

        Thing NearestOnMap(ThingDef d)
        {
            if (map == null) return null;
            Thing best = null;
            float bestD = float.MaxValue;
            foreach (Thing t in map.listerThings.ThingsOfDef(d))
            {
                if (t.Destroyed || !t.Spawned) continue;
                float dist = (t.Position - bench.Position).LengthHorizontalSquared;
                if (dist < bestD) { bestD = dist; best = t; }
            }
            return best;
        }

        /// <summary>Player workbenches of the map with a recipe that makes this thing.</summary>
        IEnumerable<KeyValuePair<Building_WorkTable, RecipeDef>> Makers(ThingDef d)
        {
            if (map == null) yield break;
            foreach (Building_WorkTable b in map.listerBuildings.AllBuildingsColonistOfClass<Building_WorkTable>())
            {
                if (b.BillStack.Count >= BillStack.MaxCount) continue;
                foreach (RecipeDef r in b.def.AllRecipes)
                {
                    if (!r.AvailableNow || !r.AvailableOnNow(b)) continue;
                    if (r.products != null && r.products.Any(p => p.thingDef == d)) { yield return new KeyValuePair<Building_WorkTable, RecipeDef>(b, r); break; }
                }
            }
        }

        bool CanQueue(ThingDef d) => Makers(d).Any();

        void QueueIngredient(ThingDef d, int count)
        {
            // the bench with the shortest queue
            KeyValuePair<Building_WorkTable, RecipeDef> pick = Makers(d).OrderBy(kv => kv.Key.BillStack.Count).FirstOrDefault();
            if (pick.Key == null) { Messages.Message("BO_IngQueueNone".Translate(d.LabelCap), MessageTypeDefOf.RejectInput, false); return; }
            RecipeDef r = pick.Value;
            Bill bill = r.MakeNewBill(null);
            if (bill is Bill_Production bp)
            {
                int per = Mathf.Max(r.products.First(p => p.thingDef == d).count, 1);
                bp.repeatMode = BillRepeatModeDefOf.RepeatCount;
                bp.repeatCount = Mathf.Max(Mathf.CeilToInt(count / (float)per), 1);
            }
            pick.Key.BillStack.AddBill(bill);
            SoundDefOf.Click.PlayOneShotOnCamera();
            Messages.Message("BO_IngQueued".Translate(d.LabelCap, pick.Key.LabelCap), new LookTargets(pick.Key), MessageTypeDefOf.SilentInput, false);
        }

        float DrawUsers(RecipeEntry e, float w, float y)
        {
            List<ThingDef> users = CeCompat.Users(e.product);
            if (users == null || users.Count == 0) return y;
            y = SectionHeader(w, y, "BO_SecUsers".Translate() + " (" + users.Count + ")");
            string names = string.Join(", ", users.Select(d => d.LabelCap.ToString()).Distinct().OrderBy(s => s).Take(14));
            if (users.Count > 14) names += " ...";
            Verse.Text.Font = GameFont.Small;
            float h = Verse.Text.CalcHeight(names, w);
            Ui.Text_(new Rect(0f, y, w, h), names, Ui.Dim, TextAnchor.UpperLeft, GameFont.Small, true);
            return y + h + 8f;
        }

        // ------------------------------------------------------------ compare

        float DrawCompare(float w, float y)
        {
            y = SectionHeader(w, y, "BO_SecCompare".Translate() + " (" + pinned.Count + "/" + MaxPinned + ")");
            var clear = new Rect(w - 60f, y - 25f, 60f, 20f);
            if (Ui.Chip(clear, "BO_Clear".Translate(), false)) { pinned.Clear(); return y; }
            string diffLabel = "BO_DiffOnly".Translate();
            float dw = Ui.ChipWidth(diffLabel);
            if (Ui.Chip(new Rect(clear.x - dw - 6f, clear.y, dw, 20f), diffLabel, diffOnly, "BO_DiffOnlyTip".Translate())) diffOnly = !diffOnly;

            // label column as wide as the longest row label needs (bonus stats have long names), up to 42% of the panel; the rest is cut
            // with the full text in the tooltip (TextFit)
            List<StatInfo> stats = StatTable.For(pinned, 1, 99);
            Verse.Text.Font = GameFont.Small;
            float labelW = 78f;
            foreach (StatInfo s in stats) labelW = Mathf.Max(labelW, Verse.Text.CalcSize(s.Label).x + 8f);
            labelW = Mathf.Min(labelW, w * 0.42f);
            float colW = (w - labelW) / pinned.Count;
            for (int i = 0; i < pinned.Count; i++)
            {
                RecipeEntry p = pinned[i];
                var cell = new Rect(labelW + i * colW, y, colW - 4f, 52f);
                Ui.Box(cell, Ui.PanelLight);
                Window_RecipePicker.DrawIcon(new Rect(cell.x + 4f, cell.y + 3f, 26f, 26f), p);
                Ui.Text_(new Rect(cell.x + 2f, cell.y + 30f, cell.width - 4f, 20f), p.label.Truncate(cell.width - 6f), p == selected ? Color.white : Ui.Dim, TextAnchor.MiddleLeft);
                var x = new Rect(cell.xMax - 18f, cell.y + 2f, 16f, 16f);
                Ui.Glyph(x.ContractedBy(2f), Tex.Cross, Mouse.IsOver(x) ? Ui.Bad : Ui.Faint);
                if (Widgets.ButtonInvisible(x)) { pinned.RemoveAt(i); return y; }
                if (Widgets.ButtonInvisible(new Rect(cell.x, cell.y, cell.width - 20f, cell.height))) select(p);
            }
            y += 56f;

            // every bonus stat any pinned item has (union), not only the shared ones the list columns use (stats, built above)
            string[] grants = pinned.Select(Bonus.Grants).ToArray();
            if (grants.Any(g => g.Length > 0) && !(diffOnly && pinned.Count > 1 && grants.All(g => g == grants[0])))
            {
                // abilities: text, no best/worst
                Ui.Text_(new Rect(0f, y, labelW, 24f), "BO_BonusGrantsShort".Translate(), Ui.Dim);
                for (int i = 0; i < grants.Length; i++)
                {
                    var cell = new Rect(labelW + i * colW, y, colW - 6f, 24f);
                    Ui.TextFit(cell, grants[i].Length > 0 ? grants[i] : Ui.None, grants[i].Length > 0 ? Color.white : Ui.Faint);
                }
                y += 24f;
            }
            if (stats.Count == 0 && grants.All(g => g.Length == 0)) { Ui.Text_(new Rect(0f, y, w, 20f), "BO_NoCommonStats".Translate(), Ui.Faint); return y + 24f; }
            foreach (StatInfo s in stats)
            {
                float[] vals = pinned.Select(p => s.get(p)).ToArray();
                if (diffOnly && pinned.Count > 1 && vals.All(v => float.IsNaN(v) ? float.IsNaN(vals[0]) : (!float.IsNaN(vals[0]) && Mathf.Abs(v - vals[0]) < 0.0001f))) continue;
                bool bonusRow = s.labelOverride != null; // a bonus stat: no best/worst colors (StatDef does not say which way is better)
                Ui.TextFit(new Rect(0f, y, labelW, 24f), s.Label, Ui.Dim);
                if (bonusRow) TooltipHandler.TipRegion(new Rect(0f, y, labelW, 24f), s.Tip);
                float best = vals.Where(v => !float.IsNaN(v)).DefaultIfEmpty(float.NaN).Max();
                float worst = vals.Where(v => !float.IsNaN(v)).DefaultIfEmpty(float.NaN).Min();
                for (int i = 0; i < vals.Length; i++)
                {
                    Color c = Color.white;
                    if (!bonusRow && pinned.Count > 1 && !float.IsNaN(vals[i]) && Mathf.Abs(best - worst) > 0.0001f) c = vals[i] >= best ? Ui.Good : vals[i] <= worst ? Ui.Bad : Color.white;
                    Ui.Text_(new Rect(labelW + i * colW, y, colW - 6f, 24f), float.IsNaN(vals[i]) ? Ui.None : s.fmt(vals[i]), c, TextAnchor.MiddleLeft);
                }
                y += 24f;
            }
            return y + 4f;
        }
    }
}
