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
    /// The bills already queued on one bench, as cards (product icon, name, material, mode/quantity, pause/copy/duplicate/delete,
    /// "..." = vanilla details dialog): reorder by dragging, paste the vanilla clipboard, save/paste whole queues as named templates.
    /// Used inside the picker window, in the "Bills" window and (compact = true) in the compact bill tab instead of the vanilla queue.
    /// Works with any bill tab (vanilla, Nice Bill Tab, Dubs): it only touches the BillStack.
    /// </summary>
    public class QueuePanel
    {
        const float RowH = 58f, BtnS = 22f;

        Vector2 scroll;
        int lastGroup;
        string message;
        float messageUntil;

        // optional: click on a card selects its recipe in the host's detail panel; the selected card is highlighted
        Action<Bill> onSelect;
        Func<Bill, bool> isSelected;

        // "have" of "until X" bills: CountProducts walks the stockpiles, so ask at most twice a second per bill
        static readonly Dictionary<Bill, int> haveCache = new Dictionary<Bill, int>();
        static float haveCacheTime;

        static int Have(Bill_Production bp)
        {
            if (Time.realtimeSinceStartup - haveCacheTime > 0.5f) { haveCache.Clear(); haveCacheTime = Time.realtimeSinceStartup; }
            if (haveCache.TryGetValue(bp, out int n)) return n;
            try { n = bp.recipe.WorkerCounter.CountProducts(bp); }
            catch (Exception) { n = -1; }
            haveCache[bp] = n;
            return n;
        }

        public void Draw(Rect r, BillStack stack, Map map, bool compact = false, Action<Bill> onSelect = null, Func<Bill, bool> isSelected = null)
        {
            this.onSelect = onSelect;
            this.isSelected = isSelected;
            if (stack == null) return;
            Ui.PanelBox(r);
            Rect inner = r.ContractedBy(6f);
            var bench = stack.billGiver as Thing;
            if (bench == null) return;

            bool anyRunning = stack.Bills.Any(b => !b.suspended); // an empty bench shows "Pause all": it has nothing to resume
            string[] labels = { "BO_QCopyQueue".Translate(), "BO_QPaste".Translate(), "BO_QSaveTpl".Translate(), "BO_QTemplates".Translate(),
                (anyRunning || stack.Count == 0 ? "BO_QPauseAll" : "BO_QResumeAll").Translate(), "BO_QClearDone".Translate(), "BO_QClearAll".Translate() };
            string[] tips = { "BO_QCopyQueueTip".Translate(), "BO_QPasteTip".Translate(), "BO_QSaveTplTip".Translate(), "BO_QTemplatesTip".Translate(),
                "BO_QPauseAllTip".Translate(), "BO_QClearDoneTip".Translate(), "BO_QClearAllTip".Translate() };

            float y;
            string title = ("BO_QueueTitle".Translate(stack.Count, BillStack.MaxCount)).ToString().ToUpperInvariant();
            if (compact)
            {
                // one row: Add bill (opens the window) + title + a menu with the queue actions
                bool full = stack.Count >= BillStack.MaxCount;
                string addLabel = (full ? "BO_StackFullShort" : "BO_AddBill").Translate();
                Verse.Text.Font = GameFont.Small;
                var add = new Rect(inner.x, inner.y, Mathf.Max(118f, Ui.ChipWidth(addLabel)), 26f);
                bool addOver = Mouse.IsOver(add) && !full;
                Ui.Box(add, full ? Ui.PanelLight : addOver ? new Color(1f, 0.86f, 0.45f) : Ui.Accent);
                Ui.Text_(add, addLabel, full ? Ui.Faint : Color.black, TextAnchor.MiddleCenter);
                if (full) TooltipHandler.TipRegion(add, "BO_StackFull".Translate());
                if (!full && Widgets.ButtonInvisible(add)) Window_RecipePicker.OpenFor(stack, true);

                string actions = "BO_QActions".Translate();
                float aw = Ui.ChipWidth(actions);
                var act = new Rect(inner.xMax - aw, inner.y + 1f, aw, 24f);
                if (Ui.Chip(act, actions, false, "BO_QActionsTip".Translate()))
                {
                    var opts = new List<FloatMenuOption>();
                    for (int i = 0; i < labels.Length; i++)
                    {
                        int k = i;
                        opts.Add(new FloatMenuOption(labels[i], () => RunAction(k, stack, anyRunning)));
                    }
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
                // our paste button (the vanilla one is hidden in this tab, see Patch_BillTab.Clipboard): only while something is copied
                float titleRight = act.x;
                if (PasteButton(new Rect(act.x - 4f - 24f, inner.y + 1f, 24f, 24f), stack)) titleRight = act.x - 32f;
                Ui.TextFit(new Rect(add.xMax + 8f, inner.y, titleRight - add.xMax - 14f, 26f), title, Ui.Dim);
                y = inner.y + 32f;
            }
            else
            {
                // header: title + actions
                Ui.Text_(new Rect(inner.x, inner.y, inner.width, 22f), title, Ui.Dim);
                y = inner.y + 26f;
                float x = inner.x;
                for (int i = 0; i < labels.Length; i++)
                {
                    float cw = Ui.ChipWidth(labels[i]);
                    if (x + cw > inner.xMax) { x = inner.x; y += 28f; }
                    if (Ui.Chip(new Rect(x, y, cw, 24f), labels[i], false, tips[i])) RunAction(i, stack, anyRunning);
                    x += cw + 4f;
                }
                y += 30f;
            }
            if (message != null && Time.realtimeSinceStartup < messageUntil) { Ui.Text_(new Rect(inner.x, y, inner.width, 20f), message, Ui.Good); y += 22f; }

            var outR = new Rect(inner.x, y, inner.width, inner.yMax - y);
            if (stack.Count == 0)
            {
                Ui.Text_(outR, "BO_QueueEmpty".Translate(), Ui.Faint, TextAnchor.MiddleCenter, GameFont.Small, true);
                return;
            }

            var view = new Rect(0f, 0f, outR.width - 16f, stack.Count * RowH);
            Widgets.BeginScrollView(outR, ref scroll, view);
            List<Bill> bills = stack.Bills.ToList();
            // vanilla drag-and-drop (it hooks the window stack, which plain IMGUI events inside a window do not)
            // NewGroup answers -1 outside Repaint, but Reorderable stores the group on MouseDown: reuse the id of the last Repaint
            int group = ReorderableWidget.NewGroup((from, to) =>
            {
                if (from < 0 || from >= bills.Count) return;
                int final = to > from ? to - 1 : to;
                if (final != from) stack.Reorder(bills[from], final - from);
            }, ReorderableDirection.Vertical, new Rect(0f, 0f, view.width, view.height));
            if (group >= 0) lastGroup = group; else group = lastGroup;
            for (int i = 0; i < bills.Count; i++)
                DrawRow(new Rect(0f, i * RowH, view.width, RowH), stack, bills[i], i, map, group);
            Widgets.EndScrollView();
        }

        void RunAction(int i, BillStack stack, bool anyRunning)
        {
            if (i == 0) CopyQueue(stack);
            else if (i == 1) PasteClipboard(stack);
            else if (i == 2) SaveTemplate(stack);
            else if (i == 3) Templates.OpenPasteMenu(stack, m => Say(m));
            else if (i == 4) { foreach (Bill b in stack.Bills) b.suspended = anyRunning; SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); }
            else if (i == 5) ClearDone(stack);
            else Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("BO_QClearAllConfirm".Translate(stack.Count), () => ClearAll(stack), true));
        }

        /// <summary>Bills with "do X times" that reached zero (the vanilla tab keeps them in the list).</summary>
        void ClearDone(BillStack stack)
        {
            int n = 0;
            foreach (Bill b in stack.Bills.ToList())
                if (b is Bill_Production bp && bp.repeatMode == BillRepeatModeDefOf.RepeatCount && bp.repeatCount <= 0) { stack.Delete(b); n++; }
            Say(n == 0 ? "BO_QClearedNone".Translate().ToString() : "BO_QClearedN".Translate(n).ToString());
        }

        void ClearAll(BillStack stack)
        {
            int n = stack.Count;
            foreach (Bill b in stack.Bills.ToList()) stack.Delete(b);
            SoundDefOf.Click.PlayOneShotOnCamera();
            Say("BO_QClearedN".Translate(n));
        }

        void Say(string m) { message = m; messageUntil = Time.realtimeSinceStartup + 3f; }

        void DrawRow(Rect row, BillStack stack, Bill b, int index, Map map, int group)
        {
            bool over = Mouse.IsOver(row);
            bool shouldDo = SafeShouldDoNow(b);
            var bp = b as Bill_Production;
            // why the bill is not running: finished "do X times", target already reached, paused, or the game says not now
            bool done = bp != null && bp.repeatMode == BillRepeatModeDefOf.RepeatCount && bp.repeatCount <= 0;
            int have = bp != null && bp.repeatMode == BillRepeatModeDefOf.TargetCount ? Have(bp) : -1;
            bool targetMet = have >= 0 && have >= bp.targetCount;
            bool idle = b.suspended || done || targetMet || !shouldDo;
            // card: a lighter tile with a gap below it
            var card = new Rect(row.x, row.y + 2f, row.width, row.height - 4f);
            bool chosen = isSelected != null && isSelected(b);
            Ui.Box(card, chosen ? Ui.Selected : over ? Ui.Hover : Ui.PanelLight);
            Ui.Box(new Rect(card.x, card.y, 3f, card.height), b.suspended ? Ui.Warn : idle ? Ui.Faint : Ui.Good);
            string reason = StateReason(b, shouldDo, done, targetMet, bp);
            if (reason.Length > 0) TooltipHandler.TipRegion(new Rect(card.x, card.y, 6f, card.height), reason);

            // right side buttons first (they sit on top of the drag area)
            float bx = card.xMax - BtnS - 4f;
            float by = card.y + 4f;
            if (IconButton(new Rect(bx, by, BtnS, BtnS), TexButton.Delete, "BO_QDelete".Translate())) { stack.Delete(b); SoundDefOf.Click.PlayOneShotOnCamera(); return; }
            bx -= BtnS + 6f; // an extra gap keeps Delete away from the other buttons
            if (IconButton(new Rect(bx, by, BtnS, BtnS), TexButton.Copy, "BO_QCopy".Translate())) { BillUtility.Clipboard = b.Clone(); SoundDefOf.Tick_High.PlayOneShotOnCamera(); Say("BO_QCopied".Translate()); }
            bx -= BtnS + 2f;
            if (IconButton(new Rect(bx, by, BtnS, BtnS), Tex.Duplicate, "BO_QDuplicate".Translate()))
            {
                if (stack.Count >= BillStack.MaxCount) Say("BO_StackFull".Translate());
                else { Bill c = b.Clone(); c.InitializeAfterClone(); stack.AddBill(c); stack.Reorder(c, stack.IndexOf(b) + 1 - stack.IndexOf(c)); SoundDefOf.Tick_Low.PlayOneShotOnCamera(); }
                return;
            }
            bx -= BtnS + 2f;
            if (IconButton(new Rect(bx, by, BtnS, BtnS), TexButton.Suspend, (b.suspended ? "BO_QResume" : "BO_QPause").Translate(), b.suspended)) { b.suspended = !b.suspended; SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); }
            float labelRight = bx - 4f;

            // move buttons (reliable alternative to dragging): click = one step, Shift+click = to the top / bottom
            int n = stack.Count, at = stack.IndexOf(b);
            if (MoveButton(new Rect(card.x + 6f, card.y + 4f, 22f, 22f), TexButton.ReorderUp, at > 0, "BO_QMoveUp".Translate()))
            { stack.Reorder(b, Event.current.shift ? -at : -1); SoundDefOf.Tick_High.PlayOneShotOnCamera(); return; }
            if (MoveButton(new Rect(card.x + 6f, card.y + 28f, 22f, 22f), TexButton.ReorderDown, at < n - 1, "BO_QMoveDown".Translate()))
            { stack.Reorder(b, Event.current.shift ? n - 1 - at : 1); SoundDefOf.Tick_Low.PlayOneShotOnCamera(); return; }

            // second line, from the right: details "...", [-] [+], mode chip. The chip takes all the room it needs
            // (only the room reserved for the material chip is kept back), so "..." appears only when nothing more fits.
            float line2 = card.y + 30f;
            float tx = card.x + 78f;
            float cx = card.xMax - 4f;
            if (bp != null)
            {
                cx -= 28f;
                if (Ui.Chip(new Rect(cx, line2, 28f, 22f), "...", false, "BO_QDetails".Translate())) BillCard.OpenDetails(stack, b);
                cx -= 4f;
                bool counted = bp.repeatMode == BillRepeatModeDefOf.RepeatCount || bp.repeatMode == BillRepeatModeDefOf.TargetCount;
                if (counted)
                {
                    // "do X times" counts repetitions (1 per click); "until X" counts items, so one batch per click (500 for a x500 ammo bill)
                    int unit = bp.repeatMode == BillRepeatModeDefOf.TargetCount ? RecipeEntry.TargetStepOf(bp.recipe) : 1;
                    string stepTip = "BO_StepTip".Translate(unit) + "\n" + Ui.StepKeysTip();
                    cx -= BtnS;
                    if (Ui.Chip(new Rect(cx, line2, BtnS, 22f), "+", false, stepTip)) Adjust(bp, unit, true);
                    cx -= 2f + BtnS;
                    if (Ui.Chip(new Rect(cx, line2, BtnS, 22f), "-", false, stepTip)) Adjust(bp, unit, false);
                    cx -= 4f;
                }
                float matReserve = BillCard.HasMaterialChip(b) ? 30f : 0f;
                float avail = Mathf.Max(52f, cx - tx - matReserve - 6f);
                string mode = ModeText(bp, have, done, avail - 18f, out Color modeColor, out float fill);
                float mw = Mathf.Clamp(Verse.Text.CalcSize(mode).x + 18f, 52f, avail);
                cx -= mw;
                if (ModeChip(new Rect(cx, line2, mw, 22f), mode, modeColor, fill, ModeTip(bp, have, done))) BillCard.CycleMode(bp);
                cx -= 6f;
            }

            // icon, name, material chip + state
            var iconR = new Rect(card.x + 32f, card.y + (card.height - 38f) / 2f, 38f, 38f);
            Ui.Box(iconR.ExpandedBy(2f), Ui.Panel);
            BillCard.DrawProductIcon(iconR, b);
            var labelR = new Rect(tx, card.y + 3f, labelRight - tx, 24f);
            Ui.NameFit(labelR, b.LabelCap.ToString(), b.suspended ? Ui.Faint : Color.white);

            float left = tx;
            Rect mat = new Rect(tx, line2, 0f, 22f);
            if (cx - tx > 40f) mat = BillCard.DrawMaterial(new Rect(tx, line2, cx - tx, 22f), b, map);
            if (mat.width > 0f) left = mat.xMax + 6f;
            // state text only when it fits whole; the stripe tooltip carries the reason when it does not
            string state = StateText(b, shouldDo, done, targetMet);
            if (state.Length > 0)
            {
                Verse.Text.Font = GameFont.Small;
                float sw = Verse.Text.CalcSize(state).x;
                if (cx - left >= sw + 4f)
                {
                    var sr = new Rect(left, line2, sw + 4f, 22f);
                    Ui.Text_(sr, state, b.suspended ? Ui.Warn : Ui.Dim);
                    if (reason.Length > 0) TooltipHandler.TipRegion(sr, reason);
                }
            }

            // a press anywhere on the card selects the recipe in the detail panel. On MouseDown and without Use(): the buttons drawn
            // above already took their own presses (GUI.Button uses the event), the drag below still sees this one, and nothing depends
            // on the MouseUp landing on the same IMGUI control (that is what lost clicks with ButtonInvisible here)
            if (onSelect != null && Event.current.type == EventType.MouseDown && Event.current.button == 0
                && Mouse.IsOver(card) && Event.current.mousePosition.x > card.x + 30f)
            {
                onSelect(b);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            // drag to reorder: a press on the icon / name area (never on the controls of the second line)
            // the rect is the whole row: the vanilla insertion line is drawn on its top/bottom edge, so it lands between cards
            ReorderableWidget.Reorderable(group, row);
        }

        static bool SafeShouldDoNow(Bill b)
        {
            try { return b.ShouldDoNow(); } catch { return true; }
        }

        /// <summary>Mode chip: text + optional thin progress bar (have / target of "until X"). Returns true when clicked.</summary>
        static bool ModeChip(Rect r, string text, Color textColor, float fill, string tip)
        {
            bool over = Mouse.IsOver(r);
            Ui.Box(r, over ? Ui.Hover : Ui.PanelLight);
            if (fill > 0f) Ui.Box(new Rect(r.x, r.yMax - 2f, r.width * Mathf.Clamp01(fill), 2f), fill >= 1f ? Ui.Good : Ui.Accent);
            Ui.Text_(r, text, textColor, TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        /// <summary>First text that fits <paramref name="maxW"/>; the last one is cut only if even it does not fit.</summary>
        static string FitFirst(float maxW, params string[] candidates)
        {
            Verse.Text.Font = GameFont.Small;
            foreach (string c in candidates) if (Verse.Text.CalcSize(c).x <= maxW) return c;
            return candidates[candidates.Length - 1].Truncate(maxW);
        }

        /// <summary>
        /// What the mode chip says, longest form that fits first. Until X shows have/target like the vanilla bill row (and like the
        /// "4794/16" of the requirements); a finished "do X times" says Done instead of "0x".
        /// </summary>
        static string ModeText(Bill_Production bp, int have, bool done, float maxW, out Color color, out float fill)
        {
            color = new Color(1f, 1f, 1f, 0.8f);
            fill = 0f;
            if (bp.repeatMode == BillRepeatModeDefOf.Forever) return FitFirst(maxW, "BO_ModeForever".Translate().ToString());
            if (bp.repeatMode == BillRepeatModeDefOf.TargetCount)
            {
                int target = bp.targetCount;
                if (have < 0) return FitFirst(maxW, "BO_QTarget".Translate(target).ToString(), Ui.Compact(target));
                if (have >= target) color = Ui.Good;
                fill = target > 0 ? have / (float)target : 0f;
                return FitFirst(maxW, have + "/" + target, Ui.Compact(have) + "/" + Ui.Compact(target));
            }
            if (done) { color = Ui.Good; return FitFirst(maxW, "BO_QDone".Translate().ToString(), "0x"); }
            return FitFirst(maxW, "BO_QCount".Translate(bp.repeatCount).ToString(), Ui.Compact(bp.repeatCount));
        }

        /// <summary>Tooltip of the mode chip: what the numbers mean first, then how to change it.</summary>
        static string ModeTip(Bill_Production bp, int have, bool done)
        {
            string head;
            if (bp.repeatMode == BillRepeatModeDefOf.Forever) head = "BO_ModeForeverTip".Translate();
            else if (bp.repeatMode == BillRepeatModeDefOf.TargetCount)
                head = have >= 0 ? "BO_QTargetTip".Translate(Ui.Thousands(have), Ui.Thousands(bp.targetCount)).ToString() : "BO_ModeTargetTip".Translate().ToString();
            else head = done ? "BO_QDoneTip".Translate().ToString() : "BO_QCountTip".Translate(bp.repeatCount).ToString();
            return head + "\n\n" + "BO_QModeTip".Translate();
        }

        /// <summary>One word for the state, drawn only when it fits ("" = running, or the chip already says it).</summary>
        static string StateText(Bill b, bool shouldDo, bool done, bool targetMet)
        {
            if (b.suspended) return "BO_QSuspended".Translate();
            if (done || targetMet) return "";
            return shouldDo ? "" : "BO_QNotNow".Translate().ToString();
        }

        /// <summary>The longer explanation (stripe and state tooltip).</summary>
        static string StateReason(Bill b, bool shouldDo, bool done, bool targetMet, Bill_Production bp)
        {
            if (b.suspended) return "BO_QSuspended".Translate();
            if (done) return "BO_QDoneTip".Translate();
            if (targetMet) return "BO_QMetTip".Translate(bp.targetCount);
            return shouldDo ? "" : "BO_QNotNowTip".Translate().ToString();
        }

        /// <summary>
        /// One unit per click times the vanilla modifier keys. "Until X" snaps to whole batches (501 by 500: + 1000, - 500) and does not
        /// go below one batch (nor raise a smaller target when lowering).
        /// </summary>
        static void Adjust(Bill_Production bp, int unit, bool up)
        {
            unit = Mathf.Max(unit, 1);
            int step = unit * Ui.StepMult();
            if (bp.repeatMode == BillRepeatModeDefOf.RepeatCount)
                // "-" never raises: a finished bill (0, "Done") stays finished
                bp.repeatCount = up ? Ui.StepUp(bp.repeatCount, 1, step) : Mathf.Max(Mathf.Min(1, bp.repeatCount), bp.repeatCount - step);
            else if (bp.repeatMode == BillRepeatModeDefOf.TargetCount)
            {
                int old = bp.targetCount;
                bp.targetCount = up ? Ui.StepUp(old, unit, step) : Mathf.Max(Mathf.Min(unit, old), Ui.StepDown(old, unit, step));
                // the "resume at" value moves with the target, like Dialog_BillConfig and RecipeEntry.Configure
                bp.unpauseWhenYouHave = Mathf.Clamp(bp.unpauseWhenYouHave + (bp.targetCount - old), 0, Mathf.Max(bp.targetCount - 1, 0));
            }
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        static bool MoveButton(Rect r, Texture2D tex, bool enabled, string tip)
        {
            bool over = enabled && Mouse.IsOver(r);
            Ui.Box(r, over ? Ui.Hover : Ui.Panel);
            Ui.Glyph(r.ContractedBy(2f), tex, enabled ? Color.white : new Color(1f, 1f, 1f, 0.2f));
            if (enabled) TooltipHandler.TipRegion(r, tip);
            return enabled && Widgets.ButtonInvisible(r);
        }

        static bool IconButton(Rect r, Texture2D tex, string tip, bool active = false)
        {
            bool over = Mouse.IsOver(r);
            Ui.Box(r, active ? Ui.Selected : over ? Ui.Hover : Ui.Panel);
            Ui.Glyph(r.ContractedBy(3f), tex, Color.white);
            TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        void CopyQueue(BillStack stack)
        {
            var bench = stack.billGiver as Thing;
            if (bench == null || stack.Count == 0) { Say("BO_QueueEmpty".Translate()); return; }
            int n = Templates.CopyQueue(bench, stack);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            Say("BO_QQueueCopied".Translate(n));
        }

        /// <summary>Pastes whatever was copied last: the whole queue ("Copy queue") or the single bill (card copy button / vanilla tab).</summary>
        /// <summary>
        /// Paste icon of the compact header. Drawn only when something is copied (false = nothing drawn); dimmed with the reason
        /// when it does not fit this bench or the list is full; the tooltip names what will be pasted (a bill or the copied queue).
        /// </summary>
        bool PasteButton(Rect r, BillStack stack)
        {
            var bench = stack.billGiver as Thing;
            Bill clip = Patch_BillTab.Clipboard;
            bool queue = Templates.HasQueueClip;
            if (!queue && clip == null) return false;
            string tip;
            bool ok;
            if (queue)
            {
                int fit = Templates.QueueClipFit(bench, out int total);
                ok = fit > 0 && stack.Count < BillStack.MaxCount;
                tip = "BO_QPasteQueueTip".Translate(fit, total);
            }
            else
            {
                bool here = bench != null && clip.recipe != null && bench.def.AllRecipes.Contains(clip.recipe) && clip.recipe.AvailableNow && clip.recipe.AvailableOnNow(bench);
                ok = here && stack.Count < BillStack.MaxCount;
                tip = !here ? "ClipboardBillNotAvailableHere".Translate() + ": " + clip.LabelCap
                    : "PasteBillTip".Translate() + ": " + clip.LabelCap;
            }
            if (stack.Count >= BillStack.MaxCount) tip += "\n" + "BO_StackFull".Translate();
            bool over = ok && Mouse.IsOver(r);
            Ui.Box(r, over ? Ui.Hover : Ui.PanelLight);
            Ui.Glyph(r.ContractedBy(4f), TexButton.Paste, ok ? Color.white : new Color(1f, 1f, 1f, 0.3f));
            TooltipHandler.TipRegion(r, tip);
            if (ok && Widgets.ButtonInvisible(r)) PasteClipboard(stack);
            return true;
        }

        void PasteClipboard(BillStack stack)
        {
            if (Templates.TryPasteQueue(stack, out string queueMsg)) { Say(queueMsg); return; }
            Bill clip = Patch_BillTab.Clipboard;
            var bench = stack.billGiver as Thing;
            if (clip == null) { Say("BO_QClipEmpty".Translate()); return; }
            if (bench == null || !bench.def.AllRecipes.Contains(clip.recipe) || !clip.recipe.AvailableNow || !clip.recipe.AvailableOnNow(bench)) { Say("ClipboardBillNotAvailableHere".Translate()); return; }
            if (stack.Count >= BillStack.MaxCount) { Say("BO_StackFull".Translate()); return; }
            Bill c = clip.Clone();
            c.InitializeAfterClone();
            stack.AddBill(c);
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        void SaveTemplate(BillStack stack)
        {
            var bench = stack.billGiver as Thing;
            if (bench == null || stack.Count == 0) { Say("BO_QueueEmpty".Translate()); return; }
            Find.WindowStack.Add(new Dialog_NameTemplate(bench.def.LabelCap, name =>
            {
                void Save()
                {
                    string err = Templates.Save(name, bench.def, stack, out int n);
                    Say(err ?? "BO_TplSaved".Translate(name, n).ToString());
                }
                if (Templates.All.Any(t => t.name == name && t.benchDef == bench.def.defName))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("BO_TplOverwrite".Translate(name), Save, true));
                else Save();
            }));
        }
    }
}

