using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BillOrganizer
{
    /// <summary>Task force dialog (opened from the Work tab): work types, priority, duration and colonists; lists the active task
    /// forces with "End now" and a chip per colonist that takes only that one out.</summary>
    public class Dialog_TaskForce : Window
    {
        static readonly int[] PresetHours = { 1, 3, 6, 12, 24, 48 };
        const int MaxHours = 72;
        const float RowH = 26f;

        readonly List<WorkTypeDef> allWorks;
        readonly HashSet<WorkTypeDef> works = new HashSet<WorkTypeDef>();
        readonly List<Pawn> pawns;
        /// <summary>Colonists the player unchecked; everyone else who can do a chosen work is in.</summary>
        readonly HashSet<Pawn> excluded = new HashSet<Pawn>();
        int hours, prio;
        bool interrupt, turnOnManual = true;
        Vector2 workScroll, pawnScroll, activeScroll;

        public override Vector2 InitialSize => new Vector2(880f, 700f);

        public Dialog_TaskForce()
        {
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            forcePause = true;
            BillOrganizerSettings s = BillOrganizerMod.Settings;
            allWorks = DefDatabase<WorkTypeDef>.AllDefs.Where(w => w.visible).OrderByDescending(w => w.naturalPriority).ToList();
            foreach (string n in s.tfWorks)
            {
                WorkTypeDef w = DefDatabase<WorkTypeDef>.GetNamedSilentFail(n);
                if (w != null && w.visible) works.Add(w);
            }
            hours = Mathf.Clamp(s.tfHours, 1, MaxHours);
            prio = Mathf.Clamp(s.tfPrio, 1, Pawn_WorkSettings.LowestPriority);
            interrupt = s.tfInterrupt;
            pawns = TaskForceManager.Candidates();
        }

        List<WorkTypeDef> Chosen => allWorks.Where(works.Contains).ToList();

        bool CanAny(Pawn p) => works.Any(w => TaskForceManager.CanWork(p, w));

        List<Pawn> Selected => pawns.Where(p => !excluded.Contains(p) && CanAny(p)).ToList();

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 40f, 34f), "BO_TFTitle".Translate());
            Text.Font = GameFont.Small;
            Ui.Text_(new Rect(inRect.x, inRect.y + 34f, inRect.width, 22f), "BO_TFIntro".Translate(), Ui.Dim, TextAnchor.MiddleLeft, GameFont.Tiny);

            IReadOnlyList<TaskForce> active = TaskForceManager.Active;
            float activeH = active.Count == 0 ? 0f : Mathf.Min(40f + active.Count * 58f, 170f);
            const float footerH = 40f;
            float top = inRect.y + 62f;
            float bodyH = inRect.yMax - top - footerH - (activeH > 0 ? activeH + 8f : 0f);

            var left = new Rect(inRect.x, top, 270f, bodyH);
            var right = new Rect(left.xMax + 12f, top, inRect.width - left.width - 12f, bodyH);
            DrawOptions(left);
            DrawPawns(right);
            if (activeH > 0) DrawActive(new Rect(inRect.x, right.yMax + 8f, inRect.width, activeH), active);
            DrawFooter(new Rect(inRect.x, inRect.yMax - footerH + 6f, inRect.width, footerH - 6f));
        }

        // ------------------------------------------------------------ left: works, priority, duration, options

        void DrawOptions(Rect r)
        {
            Ui.PanelBox(r);
            r = r.ContractedBy(8f);
            float y = r.y;
            Header(new Rect(r.x, y, r.width, 22f), "BO_TFWorks");
            y += 24f;

            bool manual = Find.PlaySettings.useWorkPriorities;
            float optsH = (manual || turnOnManual ? 56f : 0f) + 82f + 30f + (manual ? 0f : WarnHeight(r.width) + 30f);
            var list = new Rect(r.x, y, r.width, Mathf.Max(80f, r.yMax - y - optsH - 6f));
            var view = new Rect(0f, 0f, list.width - 16f, allWorks.Count * 24f);
            Widgets.BeginScrollView(list, ref workScroll, view);
            for (int i = 0; i < allWorks.Count; i++)
            {
                WorkTypeDef w = allWorks[i];
                var row = new Rect(0f, i * 24f, view.width, 24f);
                if (Mouse.IsOver(row)) Ui.Box(row, Ui.Hover);
                bool on = works.Contains(w);
                bool was = on;
                Widgets.CheckboxLabeled(row, w.labelShort.CapitalizeFirst(), ref on);
                if (on != was) { if (on) works.Add(w); else works.Remove(w); }
                TooltipHandler.TipRegion(row, w.description);
            }
            Widgets.EndScrollView();
            y = list.yMax + 6f;

            if (manual || turnOnManual)
            {
                Header(new Rect(r.x, y, r.width, 22f), "BO_TFPrio");
                y += 24f;
                float cw = (r.width - 3 * 4f) / 4f;
                for (int p = 1; p <= Pawn_WorkSettings.LowestPriority; p++)
                    if (Ui.Chip(new Rect(r.x + (p - 1) * (cw + 4f), y, cw, 26f), p.ToString(), prio == p, "BO_TFPrioTip".Translate())) prio = p;
                y += 32f;
            }

            Header(new Rect(r.x, y, r.width, 22f), "BO_TFDuration");
            Ui.Text_(new Rect(r.x, y, r.width, 22f), HoursLabel(hours), Ui.Accent, TextAnchor.MiddleRight);
            y += 24f;
            float pw = (r.width - 5 * 4f) / 6f;
            for (int i = 0; i < PresetHours.Length; i++)
            {
                int h = PresetHours[i];
                if (Ui.Chip(new Rect(r.x + i * (pw + 4f), y, pw, 24f), ShortHours(h), hours == h)) hours = h;
            }
            y += 28f;
            hours = Mathf.Clamp(Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(r.x, y, r.width, 22f), hours, 1f, MaxHours)), 1, MaxHours);
            y += 30f;

            var ir = new Rect(r.x, y, r.width, 24f);
            Widgets.CheckboxLabeled(ir, "BO_TFInterrupt".Translate(), ref interrupt);
            TooltipHandler.TipRegion(ir, "BO_TFInterruptTip".Translate());
            y += 30f;

            if (!manual)
            {
                float wh = WarnHeight(r.width);
                Ui.Text_(new Rect(r.x, y, r.width, wh), "BO_TFManualOff".Translate(), Ui.Warn, TextAnchor.UpperLeft, GameFont.Tiny, true);
                y += wh;
                var mr = new Rect(r.x, y, r.width, 24f);
                Widgets.CheckboxLabeled(mr, "BO_TFTurnOnManual".Translate(), ref turnOnManual);
                TooltipHandler.TipRegion(mr, "BO_TFTurnOnManualTip".Translate());
            }
        }

        /// <summary>Height of the wrapped "manual priorities off" warning (measured, it wraps differently per language).</summary>
        static float WarnHeight(float width)
        {
            GameFont of = Text.Font; bool ow = Text.WordWrap;
            Text.Font = GameFont.Tiny; Text.WordWrap = true;
            float h = Text.CalcHeight("BO_TFManualOff".Translate(), width) + 4f;
            Text.Font = of; Text.WordWrap = ow;
            return h;
        }

        static void Header(Rect r, string key) => Ui.Text_(r, key.Translate().ToString().ToUpperInvariant(), Ui.Accent, TextAnchor.MiddleLeft, GameFont.Tiny);

        static string ShortHours(int h) => h % 24 == 0 ? "BO_TFDays".Translate(h / 24).ToString() : "BO_TFHours".Translate(h).ToString();

        /// <summary>Exact days + hours ("1d 6h"); the game's period string rounds 30 h to "1.3 days".</summary>
        static string HoursLabel(int h)
        {
            int d = h / 24, r = h % 24;
            if (d == 0) return ShortHours(r);
            string s = "BO_TFDays".Translate(d).ToString();
            return r == 0 ? s : s + " " + "BO_TFHours".Translate(r);
        }

        // ------------------------------------------------------------ right: colonists

        void DrawPawns(Rect r)
        {
            Ui.PanelBox(r);
            r = r.ContractedBy(8f);
            List<WorkTypeDef> chosen = Chosen;
            Header(new Rect(r.x, r.y, 200f, 22f), "BO_TFColonists");

            string allL = "BO_TFAll".Translate(), noneL = "BO_TFNone".Translate();
            float nw = Text.CalcSize(noneL).x + 16f, aw = Text.CalcSize(allL).x + 16f;
            if (Widgets.ButtonText(new Rect(r.xMax - nw, r.y, nw, 22f), noneL)) foreach (Pawn p in pawns) excluded.Add(p);
            if (Widgets.ButtonText(new Rect(r.xMax - nw - aw - 4f, r.y, aw, 22f), allL)) excluded.Clear();
            float y = r.y + 26f;

            if (chosen.Count == 0)
            {
                Ui.Text_(new Rect(r.x, y, r.width, 40f), "BO_TFPickWork".Translate(), Ui.Dim, TextAnchor.UpperLeft, GameFont.Small, true);
                return;
            }

            // columns: checkbox | name | one per chosen work (current priority) | best relevant skill
            int shown = Mathf.Min(chosen.Count, 4);
            const float colW = 64f, skillW = 110f;
            float nameW = Mathf.Max(120f, r.width - 16f - 28f - shown * colW - skillW);
            float cx = r.x + 28f + nameW;
            for (int i = 0; i < shown; i++)
                Ui.TextFit(new Rect(cx + i * colW, y, colW - 4f, 20f), chosen[i].labelShort.CapitalizeFirst(), Ui.Dim, TextAnchor.MiddleCenter, GameFont.Tiny);
            if (chosen.Count > shown) TooltipHandler.TipRegion(new Rect(cx, y, shown * colW, 20f), "BO_TFMoreWorks".Translate(chosen.Count - shown));
            SkillDef skill = chosen.SelectMany(w => w.relevantSkills ?? new List<SkillDef>()).FirstOrDefault();
            if (skill != null) Ui.TextFit(new Rect(cx + shown * colW, y, skillW, 20f), skill.LabelCap, Ui.Dim, TextAnchor.MiddleCenter, GameFont.Tiny);
            y += 22f;

            var list = new Rect(r.x, y, r.width, r.yMax - y);
            var view = new Rect(0f, 0f, list.width - 16f, pawns.Count * RowH);
            Widgets.BeginScrollView(list, ref pawnScroll, view);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                var row = new Rect(0f, i * RowH, view.width, RowH);
                bool can = CanAny(p);
                bool on = can && !excluded.Contains(p);
                if (i % 2 == 1) Ui.Box(row, new Color(1f, 1f, 1f, 0.03f));
                if (can && Mouse.IsOver(row)) Ui.Box(row, Ui.Hover);
                if (can)
                {
                    bool was = on;
                    Widgets.Checkbox(new Vector2(row.x + 2f, row.y + 1f), ref on, 24f);
                    if (on != was) { if (on) excluded.Remove(p); else excluded.Add(p); }
                }
                var nameR = new Rect(row.x + 28f, row.y, nameW, RowH);
                Ui.TextFit(nameR, p.LabelShortCap, can ? Color.white : Ui.Faint);
                if (!can) TooltipHandler.TipRegion(nameR, "BO_TFIncapable".Translate(p.LabelShortCap));
                else if (Widgets.ButtonInvisible(nameR)) { if (on) excluded.Add(p); else excluded.Remove(p); }

                float x = row.x + 28f + nameW;
                for (int c = 0; c < shown; c++)
                {
                    WorkTypeDef w = chosen[c];
                    var cell = new Rect(x + c * colW, row.y, colW - 4f, RowH);
                    if (!TaskForceManager.CanWork(p, w)) { Ui.Text_(cell, Ui.None, Ui.Faint, TextAnchor.MiddleCenter); continue; }
                    int cur = p.workSettings.GetPriority(w);
                    Color col = cur == 0 ? Ui.Faint : cur == 1 ? Ui.Good : Color.white;
                    Ui.Text_(cell, TaskForceManager.PrioLabel(cur), col, TextAnchor.MiddleCenter);
                }
                if (skill != null && p.skills != null)
                {
                    SkillRecord sr = p.skills.GetSkill(skill);
                    string sl = sr == null || sr.TotallyDisabled ? Ui.None : sr.Level.ToString();
                    Ui.Text_(new Rect(x + shown * colW, row.y, skillW, RowH), sl, can ? Ui.Dim : Ui.Faint, TextAnchor.MiddleCenter);
                }
            }
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------ bottom: active task forces

        void DrawActive(Rect r, IReadOnlyList<TaskForce> active)
        {
            Ui.PanelBox(r);
            r = r.ContractedBy(8f);
            Header(new Rect(r.x, r.y, r.width, 22f), "BO_TFActive");
            var list = new Rect(r.x, r.y + 24f, r.width, r.height - 24f);
            var view = new Rect(0f, 0f, list.width - 16f, active.Count * 58f);
            Widgets.BeginScrollView(list, ref activeScroll, view);
            TaskForce endIt = null;
            Pawn removePawn = null; TaskForce removeFrom = null;
            float y = 0f;
            foreach (TaskForce tf in active)
            {
                string endL = "BO_TFEndNow".Translate();
                float ew = Text.CalcSize(endL).x + 16f;
                var line = new Rect(0f, y, view.width - ew - 6f, 24f);
                Ui.TextFit(line, "BO_TFActiveLine".Translate(tf.WorksLabel, tf.PawnCount, tf.LeftLabel), Color.white);
                TooltipHandler.TipRegion(line, tf.Tip());
                if (Widgets.ButtonText(new Rect(view.width - ew, y, ew, 24f), endL)) endIt = tf;
                // one chip per colonist: click takes only that one out (restores their value)
                float x = 0f;
                foreach (Pawn p in tf.Pawns.ToList())
                {
                    string cl = p.LabelShortCap + "  x";
                    float cw = Ui.ChipWidth(cl);
                    if (x + cw > view.width) { if (x + 30f <= view.width) Ui.Text_(new Rect(x, y + 28f, 30f, 24f), "...", Ui.Dim); break; }
                    if (Ui.Chip(new Rect(x, y + 28f, cw, 24f), cl, false, "BO_TFRemoveTip".Translate(p.LabelShortCap))) { removePawn = p; removeFrom = tf; }
                    x += cw + 4f;
                }
                y += 58f;
            }
            Widgets.EndScrollView();
            TaskForceManager mgr = TaskForceManager.Instance;
            if (mgr == null) return;
            if (endIt != null) mgr.End(endIt, true);
            else if (removePawn != null) mgr.EndMember(removeFrom, removePawn);
        }

        // ------------------------------------------------------------ footer

        void DrawFooter(Rect r)
        {
            List<WorkTypeDef> chosen = Chosen;
            List<Pawn> sel = Selected;
            string reason = chosen.Count == 0 ? "BO_TFNeedWork".Translate().ToString() : sel.Count == 0 ? "BO_TFNeedPawn".Translate().ToString() : null;
            const float bw = 140f;
            var cancel = new Rect(r.xMax - bw, r.y, bw, r.height);
            var start = new Rect(cancel.x - bw - 8f, r.y, bw, r.height);
            string summary = reason ?? "BO_TFSummary".Translate(sel.Count, string.Join(", ", chosen.Select(w => w.labelShort.CapitalizeFirst())), HoursLabel(hours)).ToString();
            Ui.TextFit(new Rect(r.x, r.y, start.x - r.x - 8f, r.height), summary, reason != null ? Ui.Warn : Ui.Dim);
            if (Widgets.ButtonText(cancel, "CancelButton".Translate())) Close();
            GUI.color = reason == null ? Color.white : Ui.Faint;
            bool clicked = Widgets.ButtonText(start, "BO_TFStart".Translate());
            GUI.color = Color.white;
            if (clicked && reason == null) DoStart(chosen, sel);
        }

        void DoStart(List<WorkTypeDef> chosen, List<Pawn> sel)
        {
            BillOrganizerSettings s = BillOrganizerMod.Settings;
            s.tfWorks = chosen.Select(w => w.defName).ToList();
            s.tfHours = hours; s.tfPrio = prio; s.tfInterrupt = interrupt;
            s.Write();
            if (!Find.PlaySettings.useWorkPriorities && turnOnManual)
            {
                // the same as ticking the vanilla checkbox (MainTabWindow_Work.DoManualPrioritiesCheckbox)
                Current.Game.playSettings.useWorkPriorities = true;
                foreach (Pawn p in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                    if (p.Faction == Faction.OfPlayer && p.workSettings != null) p.workSettings.Notify_UseWorkPrioritiesChanged();
            }
            TaskForceManager mgr = TaskForceManager.Instance;
            TaskForce tf = mgr?.Start(chosen, sel, prio, hours * GenDate.TicksPerHour, interrupt);
            if (tf == null) { Messages.Message("BO_TFNothing".Translate(), MessageTypeDefOf.RejectInput, false); return; }
            Messages.Message("BO_TFStarted".Translate(tf.WorksLabel, tf.PawnCount, HoursLabel(hours)), MessageTypeDefOf.TaskCompletion, false);
            Close();
        }
    }
}
