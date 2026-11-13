using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BillOrganizer
{
    /// <summary>One (colonist, work type) pair raised by a task force, with the value to give back when it ends.</summary>
    public class TaskForceMember : IExposable
    {
        public Pawn pawn;
        public WorkTypeDef work;
        /// <summary>Priority before the task force (as GetPriority read it), restored at the end.</summary>
        public int original;
        /// <summary>Priority the task force set. The end restores only while the pawn still has it (a manual change wins).</summary>
        public int boosted;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Defs.Look(ref work, "work");
            Scribe_Values.Look(ref original, "original");
            Scribe_Values.Look(ref boosted, "boosted");
        }
    }

    /// <summary>Temporary work priority for a group of colonists, saved with the game.</summary>
    public class TaskForce : IExposable
    {
        public int id;
        public List<WorkTypeDef> works = new List<WorkTypeDef>();
        public int startTick, endTick;
        public List<TaskForceMember> members = new List<TaskForceMember>();

        public int TicksLeft => Math.Max(0, endTick - Find.TickManager.TicksGame);
        public IEnumerable<Pawn> Pawns => members.Select(m => m.pawn).Where(p => p != null).Distinct();
        public int PawnCount => Pawns.Count();
        public string WorksLabel => string.Join(", ", works.Where(w => w != null).Select(w => w.labelShort.CapitalizeFirst()));
        public string LeftLabel => TicksLeft.ToStringTicksToPeriod(false, true);

        /// <summary>Tooltip: works, time left and every colonist with the value they go back to.</summary>
        public string Tip()
        {
            var lines = new List<string> { "BO_TFActiveLine".Translate(WorksLabel, PawnCount, LeftLabel).ToString() };
            foreach (Pawn p in Pawns)
            {
                string back = string.Join(", ", members.Where(m => m.pawn == p && m.work != null)
                    .Select(m => m.work.labelShort.CapitalizeFirst() + " " + TaskForceManager.PrioLabel(m.original)));
                lines.Add("  " + p.LabelShortCap + ": " + "BO_TFBackTo".Translate(back));
            }
            return string.Join("\n", lines);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Collections.Look(ref works, "works", LookMode.Def);
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref endTick, "endTick");
            Scribe_Collections.Look(ref members, "members", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (works == null) works = new List<WorkTypeDef>();
                works.RemoveAll(w => w == null);
                if (members == null) members = new List<TaskForceMember>();
                members.RemoveAll(m => m == null || m.pawn == null || m.work == null); // pawn discarded or work type from a removed mod
            }
        }
    }

    /// <summary>
    /// "Task force": raises one or more work types to a priority (default 1) on many colonists for a while and then gives each one
    /// back the value they had (e.g. everyone on Handling for 6 h to rope the animals that ran out of a broken pen). A value the
    /// player changed by hand in the meantime is kept. Saved with the game; checked every 250 ticks.
    /// </summary>
    public class TaskForceManager : GameComponent
    {
        List<TaskForce> forces = new List<TaskForce>();
        int nextId = 1;

        public TaskForceManager(Game game) { }

        public static TaskForceManager Instance => Current.Game?.GetComponent<TaskForceManager>();

        public static IReadOnlyList<TaskForce> Active => (IReadOnlyList<TaskForce>)Instance?.forces ?? Array.Empty<TaskForce>();

        public static string PrioLabel(int p) => p <= 0 ? "BO_TFOff".Translate().ToString() : p.ToString();

        static bool Manual => Find.PlaySettings.useWorkPriorities;

        /// <summary>The pawn still has what the task force set. With manual priorities off GetPriority reads only 0/3, so compare on/off.</summary>
        static bool StillBoosted(int current, int boosted) => Manual ? current == boosted : (current > 0) == (boosted > 0);

        public static bool CanWork(Pawn p, WorkTypeDef w) =>
            p != null && w != null && p.workSettings != null && p.workSettings.EverWork && !p.WorkTypeIsDisabled(w);

        /// <summary>Colonists the dialog offers: the same as the vanilla Work tab (free colonists of the current map, no babies).</summary>
        public static List<Pawn> Candidates()
        {
            Map map = Find.CurrentMap;
            if (map == null) return new List<Pawn>();
            return map.mapPawns.FreeColonists.Where(p => !p.DevelopmentalStage.Baby() && p.workSettings != null && p.workSettings.EverWork)
                .OrderBy(p => p.LabelShortCap.ToString()).ToList();
        }

        /// <summary>Starts a task force; returns null when no pair could be raised (everyone incapable). Pairs already in another
        /// task force move to the new one and keep the value from before the first (so it never "restores" to the boosted 1).</summary>
        public TaskForce Start(List<WorkTypeDef> works, List<Pawn> pawns, int prio, int ticks, bool interrupt)
        {
            int now = Find.TickManager.TicksGame;
            prio = Mathf.Clamp(prio, 1, Pawn_WorkSettings.LowestPriority);
            int target = Manual ? prio : Pawn_WorkSettings.DefaultPriority; // manual off: the task force only turns the work on
            var tf = new TaskForce { id = nextId++, works = works.ToList(), startTick = now, endTick = now + Math.Max(GenDate.TicksPerHour, ticks) };
            foreach (Pawn p in pawns)
                foreach (WorkTypeDef w in works)
                {
                    try
                    {
                        if (!CanWork(p, w)) continue;
                        int cur = p.workSettings.GetPriority(w);
                        TaskForceMember old = Take(p, w);
                        int original = old != null && StillBoosted(cur, old.boosted) ? old.original : cur;
                        if (old == null && !Manual && cur > 0) continue; // already on and nothing else to give: leave it out
                        // always write: with manual priorities off GetPriority masks the raw value (any > 0 reads 3), so "cur == target" proves nothing
                        p.workSettings.SetPriority(w, target);
                        tf.members.Add(new TaskForceMember { pawn = p, work = w, original = original, boosted = target });
                    }
                    catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Task force start failed for a colonist: " + e.Message, 0x0B110040); }
                }
            forces.RemoveAll(f => f.members.Count == 0); // older task forces emptied by the move
            if (tf.members.Count == 0) return null;
            forces.Add(tf);
            if (interrupt) foreach (Pawn p in tf.Pawns.ToList()) Interrupt(p, tf.works, target);
            return tf;
        }

        /// <summary>Removes the pair from the task force that holds it (no restore) and returns its member, or null.</summary>
        TaskForceMember Take(Pawn p, WorkTypeDef w)
        {
            foreach (TaskForce f in forces)
            {
                TaskForceMember m = f.members.FirstOrDefault(x => x.pawn == p && x.work == w);
                if (m != null) { f.members.Remove(m); return m; }
            }
            return null;
        }

        /// <summary>Care work that is never cut (rescue, tend, surgery, bed rest, fires): every job from a work giver has a workGiverDef,
        /// so without this a doctor carrying a downed colonist would drop them.</summary>
        static bool IsCare(WorkTypeDef w) =>
            w == WorkTypeDefOf.Doctor || w == WorkTypeDefOf.Firefighter || w.defName == "Patient" || w.defName == "PatientBedRest";

        /// <summary>Ends the current job when it is ordinary work (work giver, not ordered by the player) that the new priority should
        /// beat, so the colonist picks the task force work now. Eating, sleeping, care work (doctor, patient, fires), emergency work,
        /// jobs that are not casually interruptible, carrying a pawn or corpse, drafted and forced jobs are left alone.</summary>
        static void Interrupt(Pawn p, List<WorkTypeDef> works, int target)
        {
            try
            {
                Job j = p.jobs?.curJob;
                if (j == null || !p.Spawned || p.Drafted || p.Downed || j.playerForced || j.workGiverDef?.workType == null) return;
                if (j.workGiverDef.emergency || (j.def != null && !j.def.casualInterruptible)) return;
                if (p.carryTracker?.CarriedThing is Pawn || p.carryTracker?.CarriedThing is Corpse) return;
                WorkTypeDef cw = j.workGiverDef.workType;
                if (works.Contains(cw) || IsCare(cw)) return;
                List<WorkTypeDef> mine = works.Where(w => CanWork(p, w)).ToList();
                if (mine.Count == 0) return;
                int cp = p.workSettings.GetPriority(cw);
                int bestNatural = mine.Max(w => w.naturalPriority);
                bool beaten = cp == 0 || cp > target || (cp == target && cw.naturalPriority < bestNatural);
                if (beaten) p.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
            }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Task force interrupt failed: " + e.Message, 0x0B110041); }
        }

        enum RestoreResult { Restored, ChangedByHand, NotApplicable }

        /// <summary>Gives the pair its old value back when it still has the task force value. NotApplicable = the pawn can no longer
        /// do that work (trait, injury, gone): the game already turned it off and there is nothing to give back.</summary>
        static RestoreResult Restore(TaskForceMember m)
        {
            try
            {
                if (!CanWork(m.pawn, m.work)) return RestoreResult.NotApplicable;
                int cur = m.pawn.workSettings.GetPriority(m.work);
                if (!StillBoosted(cur, m.boosted)) return RestoreResult.ChangedByHand;
                // always write: with manual priorities off GetPriority masks the raw value, so "cur == original" may hide a raw 1
                m.pawn.workSettings.SetPriority(m.work, m.original);
                return RestoreResult.Restored;
            }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Task force restore failed: " + e.Message, 0x0B110042); return RestoreResult.NotApplicable; }
        }

        /// <summary>Ends a task force (time over or "End now"): restores every pair and says how many kept a manual change.</summary>
        public void End(TaskForce tf, bool early)
        {
            if (tf == null || !forces.Remove(tf)) return;
            var restored = new HashSet<Pawn>();
            var kept = new HashSet<Pawn>();
            foreach (TaskForceMember m in tf.members)
            {
                if (m.pawn == null) continue;
                RestoreResult r = Restore(m);
                if (r == RestoreResult.Restored) restored.Add(m.pawn);
                else if (r == RestoreResult.ChangedByHand) kept.Add(m.pawn);
            }
            kept.ExceptWith(restored);
            string msg = (early ? "BO_TFEndedEarly" : "BO_TFEnded").Translate(tf.WorksLabel, restored.Count);
            if (kept.Count > 0) msg += " " + "BO_TFKept".Translate(kept.Count);
            Messages.Message(msg, MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>Takes one colonist out of a task force (restores all their works there); an empty task force ends silently.</summary>
        public void EndMember(TaskForce tf, Pawn p)
        {
            if (tf == null || p == null) return;
            foreach (TaskForceMember m in tf.members.Where(x => x.pawn == p).ToList())
            {
                Restore(m);
                tf.members.Remove(m);
            }
            if (tf.members.Count == 0) forces.Remove(tf);
        }

        public override void GameComponentTick()
        {
            if (forces.Count == 0 || Find.TickManager.TicksGame % 250 != 0) return;
            int now = Find.TickManager.TicksGame;
            foreach (TaskForce tf in forces.Where(f => now >= f.endTick).ToList()) End(tf, false);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref forces, "taskForces", LookMode.Deep);
            Scribe_Values.Look(ref nextId, "taskForceNextId", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (forces == null) forces = new List<TaskForce>();
                forces.RemoveAll(f => f == null || f.members.Count == 0);
            }
        }
    }

    /// <summary>"Task force" button on the vanilla Work tab, in the free space between the "Manual priorities" checkbox (x 5-145) and
    /// the "<= Higher priority" label (x 370), positions read in the 1.7 IL. Active task forces: short status + "End" on the right
    /// of the "Lower priority =>" label (x 790) when the window is wide enough, else everything in the button's tooltip.</summary>
    [HarmonyPatch(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoWindowContents))]
    public static class Patch_WorkTab
    {
        static void Postfix(Rect rect)
        {
            if (BillOrganizerMod.Settings == null || !BillOrganizerMod.Settings.taskForce || Event.current.type == EventType.Layout) return;
            try { Draw(rect); }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Task force button failed: " + e.Message, 0x0B110043); }
        }

        static void Draw(Rect rect)
        {
            GameFont of = Text.Font; TextAnchor oa = Text.Anchor;
            Text.Font = GameFont.Small;
            IReadOnlyList<TaskForce> active = TaskForceManager.Active;
            string label = "BO_TFButton".Translate();
            if (active.Count > 0) label += " (" + active.Count + ")";
            float w = Mathf.Min(Text.CalcSize(label).x + 20f, 210f);
            var btn = new Rect(rect.x + 155f, rect.y + 4f, w, 28f);
            string tip = "BO_TFButtonTip".Translate();
            if (active.Count > 0) tip += "\n\n" + string.Join("\n\n", active.Select(f => f.Tip()));
            TooltipHandler.TipRegion(btn, tip);
            if (Widgets.ButtonText(btn, label)) Find.WindowStack.Add(new Dialog_TaskForce());

            if (active.Count > 0)
            {
                TaskForce first = active.OrderBy(f => f.endTick).First();
                string endLabel = "BO_TFEnd".Translate();
                float endW = Text.CalcSize(endLabel).x + 16f;
                var area = new Rect(800f, rect.y + 4f, rect.xMax - 800f - 8f, 28f);
                if (area.width >= endW + 120f)
                {
                    var endBtn = new Rect(area.xMax - endW, area.y, endW, 28f);
                    string status = "BO_TFActiveLine".Translate(first.WorksLabel, first.PawnCount, first.LeftLabel);
                    if (active.Count > 1) status += " (+" + (active.Count - 1) + ")";
                    var textR = new Rect(area.x, area.y, area.width - endW - 6f, 28f);
                    Ui.TextFit(textR, status, Ui.Accent, TextAnchor.MiddleRight);
                    TooltipHandler.TipRegion(textR, string.Join("\n\n", active.Select(f => f.Tip())));
                    TooltipHandler.TipRegion(endBtn, "BO_TFEndTip".Translate(first.WorksLabel));
                    if (Widgets.ButtonText(endBtn, endLabel)) TaskForceManager.Instance?.End(first, true);
                }
            }
            Text.Font = of; Text.Anchor = oa;
        }
    }
}
