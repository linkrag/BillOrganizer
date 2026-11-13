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
    /// <summary>Benches of the same type that share one queue (like the linked storage of the game, but for bills).</summary>
    public class BillLinkGroup : IExposable
    {
        public int id;
        public List<Building_WorkTable> members = new List<Building_WorkTable>();
        /// <summary>Signature of each member the last time the group was in sync (not saved: after loading, the saved state is the truth).</summary>
        [Unsaved] public Dictionary<Building_WorkTable, long> last = new Dictionary<Building_WorkTable, long>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Collections.Look(ref members, "members", LookMode.Reference);
            if (members == null) members = new List<Building_WorkTable>();
        }
    }

    /// <summary>
    /// Keeps the bills of linked benches equal. Mirror model: whichever bench was edited since the last check becomes the source and its queue
    /// is copied onto the others (in place when the recipes are the same, otherwise the other queues are rebuilt). Polled a few times a second
    /// from <c>GameComponentUpdate</c> (which also runs while paused) instead of patching every setter, so it works with any bill tab or mod that edits bills.
    /// The "do X times" counter is the one thing that changes by itself: <see cref="Patch_IterationCompleted"/> decrements it on the peers too.
    /// </summary>
    public class BillLinkManager : GameComponent
    {
        List<BillLinkGroup> groups = new List<BillLinkGroup>();
        int nextId = 1;
        float nextCheck;
        bool syncing;

        public BillLinkManager(Game game) { }

        public static BillLinkManager Instance => Current.Game?.GetComponent<BillLinkManager>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref groups, "groups", LookMode.Deep);
            Scribe_Values.Look(ref nextId, "nextId", 1);
            if (groups == null) groups = new List<BillLinkGroup>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                foreach (BillLinkGroup g in groups) g.members.RemoveAll(m => m == null);
                groups.RemoveAll(g => g.members.Count < 2);
            }
        }

        public BillLinkGroup GroupOf(Building_WorkTable b) => groups.FirstOrDefault(g => g.members.Contains(b));

        // ------------------------------------------------------------ polling

        public override void GameComponentUpdate()
        {
            if (groups.Count == 0 || Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 0.3f;
            if (!BillOrganizerMod.Settings.linkBenches) return;
            try { CheckAll(); }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Linked benches check failed: " + e, 0x0B110010); }
        }

        void CheckAll()
        {
            for (int gi = groups.Count - 1; gi >= 0; gi--)
            {
                BillLinkGroup g = groups[gi];
                g.members.RemoveAll(m => m == null || m.Destroyed || !m.Spawned || m.Faction != Faction.OfPlayer);
                g.last.Keys.Where(k => !g.members.Contains(k)).ToList().ForEach(k => g.last.Remove(k));
                if (g.members.Count < 2) { groups.RemoveAt(gi); continue; }

                Building_WorkTable source = null;
                foreach (Building_WorkTable m in g.members)
                {
                    long sig = Signature(m.BillStack);
                    if (!g.last.TryGetValue(m, out long old)) { g.last[m] = sig; continue; } // first look (new group or just loaded)
                    if (sig != old && source == null) source = m;
                }
                if (source == null) continue;
                foreach (Building_WorkTable m in g.members) if (m != source) Apply(source, m);
                Resync(g);
            }
        }

        static void Resync(BillLinkGroup g)
        {
            foreach (Building_WorkTable m in g.members) g.last[m] = Signature(m.BillStack);
        }

        // ------------------------------------------------------------ signature and copy

        static long Mix(long h, long v) { unchecked { return (h ^ v) * 1099511628211L; } }

        /// <summary>Hash of everything the player can edit in a queue (not the automatic "paused" flag, which every bench works out by itself).</summary>
        public static long Signature(BillStack stack)
        {
            long h = 1469598103934665603L;
            h = Mix(h, stack.Count);
            foreach (Bill b in stack.Bills)
            {
                h = Mix(h, b.recipe?.shortHash ?? 0);
                h = Mix(h, b.suspended ? 1 : 0);
                h = Mix(h, (long)(b.ingredientSearchRadius * 10f));
                h = Mix(h, b.allowedSkillRange.min * 31 + b.allowedSkillRange.max);
                h = Mix(h, b.PawnRestriction?.thingIDNumber ?? -1);
                h = Mix(h, (b.SlavesOnly ? 1 : 0) | (b.MechsOnly ? 2 : 0) | (b.NonMechsOnly ? 4 : 0));
                if (b.ingredientFilter != null)
                {
                    long s = 0;
                    foreach (ThingDef d in b.ingredientFilter.AllowedThingDefs) s += d.shortHash;
                    h = Mix(h, s);
                    h = Mix(h, b.ingredientFilter.AllowedDefCount);
                }
                if (b is Bill_Production p)
                {
                    h = Mix(h, p.repeatMode?.shortHash ?? 0);
                    h = Mix(h, p.repeatCount);
                    h = Mix(h, p.targetCount);
                    h = Mix(h, p.pauseWhenSatisfied ? 1 : 0);
                    h = Mix(h, p.unpauseWhenYouHave);
                    h = Mix(h, (p.includeEquipped ? 1 : 0) | (p.includeTainted ? 2 : 0) | (p.limitToAllowedStuff ? 4 : 0));
                    h = Mix(h, (long)(p.hpRange.min * 1000f) * 1009 + (long)(p.hpRange.max * 1000f));
                    h = Mix(h, (int)p.qualityRange.min * 8 + (int)p.qualityRange.max);
                    h = Mix(h, p.GetStoreMode()?.shortHash ?? 0);
                    h = Mix(h, p.GetSlotGroup()?.GetHashCode() ?? 0);
                    h = Mix(h, p.GetIncludeSlotGroup()?.GetHashCode() ?? 0);
                    h = Mix(h, p.RenamableLabel?.GetHashCode() ?? 0);
                }
            }
            return h;
        }

        static void CopySettings(Bill_Production f, Bill_Production t)
        {
            t.suspended = f.suspended;
            t.ingredientSearchRadius = f.ingredientSearchRadius;
            t.allowedSkillRange = f.allowedSkillRange;
            if (f.PawnRestriction != null) t.SetPawnRestriction(f.PawnRestriction);
            else if (f.SlavesOnly) t.SetAnySlaveRestriction();
            else if (f.MechsOnly) t.SetAnyMechRestriction();
            else if (f.NonMechsOnly) t.SetAnyNonMechRestriction();
            else t.SetAnyPawnRestriction();
            if (f.ingredientFilter != null) { if (t.ingredientFilter == null) t.ingredientFilter = new ThingFilter(); t.ingredientFilter.CopyAllowancesFrom(f.ingredientFilter); }
            t.repeatMode = f.repeatMode;
            t.repeatCount = f.repeatCount;
            t.targetCount = f.targetCount;
            t.pauseWhenSatisfied = f.pauseWhenSatisfied;
            t.unpauseWhenYouHave = f.unpauseWhenYouHave;
            t.includeEquipped = f.includeEquipped;
            t.includeTainted = f.includeTainted;
            t.hpRange = f.hpRange;
            t.qualityRange = f.qualityRange;
            t.limitToAllowedStuff = f.limitToAllowedStuff;
            t.SetStoreMode(f.GetStoreMode(), f.GetSlotGroup());
            t.SetIncludeGroup(f.GetIncludeSlotGroup());
            if (t.RenamableLabel != f.RenamableLabel) t.RenamableLabel = f.RenamableLabel;
        }

        /// <summary>Makes the queue of <paramref name="to"/> equal to the one of <paramref name="from"/>.</summary>
        static void Apply(Building_WorkTable from, Building_WorkTable to)
        {
            BillStack a = from.BillStack, b = to.BillStack;
            bool sameShape = a.Count == b.Count;
            for (int i = 0; sameShape && i < a.Count; i++)
                sameShape = a[i].recipe == b[i].recipe && a[i].GetType() == b[i].GetType() && a[i] is Bill_Production;
            if (sameShape)
            {
                for (int i = 0; i < a.Count; i++) CopySettings((Bill_Production)a[i], (Bill_Production)b[i]);
                return;
            }
            foreach (Bill old in b.Bills.ToList()) b.Delete(old);
            foreach (Bill src in a.Bills)
            {
                try { Bill c = src.Clone(); c.InitializeAfterClone(); b.AddBill(c); }
                catch (Exception e) { Log.Warning("[BillOrganizer] Could not copy a bill (" + src.recipe?.defName + ") to a linked bench: " + e.Message); }
            }
        }

        /// <summary>A bill was finished on one bench: the same bill of the others counts it too (one shared "do X times").</summary>
        public void OnIteration(Bill_Production bp)
        {
            if (groups.Count == 0 || syncing || bp.repeatMode != BillRepeatModeDefOf.RepeatCount) return;
            var bench = bp.billStack?.billGiver as Building_WorkTable;
            BillLinkGroup g = bench == null ? null : GroupOf(bench);
            if (g == null) return;
            int idx = bench.BillStack.IndexOf(bp);
            foreach (Building_WorkTable peer in g.members)
            {
                if (peer == bench || idx < 0 || idx >= peer.BillStack.Count) continue;
                if (peer.BillStack[idx] is Bill_Production pb && pb.recipe == bp.recipe && pb.repeatMode == BillRepeatModeDefOf.RepeatCount && pb.repeatCount > 0)
                    pb.repeatCount--;
            }
            Resync(g);
        }

        /// <summary>
        /// The shared "do X times" is one pool, so two benches must not both start the last item: while pawns are already working on the
        /// same bill of the other benches and they cover everything that is left, this bench's bill is "not now" for anybody else.
        /// A bill somebody is already working on is never blocked (so a running job is not cancelled).
        /// </summary>
        public bool ReservedByPeers(Bill_Production bp)
        {
            if (groups.Count == 0 || bp.repeatMode != BillRepeatModeDefOf.RepeatCount || bp.repeatCount <= 0) return false;
            var bench = bp.billStack?.billGiver as Building_WorkTable;
            BillLinkGroup g = bench == null ? null : GroupOf(bench);
            Map map = bench?.Map;
            if (g == null || map == null) return false;
            int idx = bench.BillStack.IndexOf(bp);
            int mine = 0, peers = 0;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                Bill jb = p.CurJob?.bill;
                if (jb == null || p.Faction != Faction.OfPlayer) continue;
                if (jb == bp) { mine++; continue; }
                var other = jb.billStack?.billGiver as Building_WorkTable;
                if (other == null || other == bench || !g.members.Contains(other)) continue;
                if (idx >= 0 && idx < other.BillStack.Count && other.BillStack[idx] == jb) peers++;
            }
            return mine == 0 && peers >= bp.repeatCount;
        }

        // ------------------------------------------------------------ link / unlink

        public static string CanLink(Building_WorkTable a, Building_WorkTable b)
        {
            if (b == null || a == b) return "BO_LinkRejectSelf".Translate();
            if (a.def != b.def) return "BO_LinkRejectDef".Translate();
            if (a.Map != b.Map) return "BO_LinkRejectMap".Translate();
            if (b.Faction != Faction.OfPlayer) return "BO_LinkRejectFaction".Translate();
            BillLinkManager m = Instance;
            if (m == null) return "BO_LinkRejectSelf".Translate();
            BillLinkGroup ga = m.GroupOf(a), gb = m.GroupOf(b);
            if (ga != null && ga == gb) return "BO_LinkRejectSame".Translate();
            if (ga != null && gb != null) return "BO_LinkRejectGroups".Translate();
            return null;
        }

        /// <summary>The queue of <paramref name="source"/> wins: every other bench of the (new or joined) group gets a copy.</summary>
        public void Link(Building_WorkTable source, Building_WorkTable target)
        {
            BillLinkGroup gs = GroupOf(source), gt = GroupOf(target);
            BillLinkGroup g = gs ?? gt;
            if (g == null) { g = new BillLinkGroup { id = nextId++ }; groups.Add(g); g.members.Add(source); }
            if (!g.members.Contains(source)) g.members.Add(source);
            if (!g.members.Contains(target)) g.members.Add(target);
            foreach (Building_WorkTable m in g.members) if (m != source) Apply(source, m);
            Resync(g);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            Messages.Message("BO_LinkDone".Translate(g.members.Count), source, MessageTypeDefOf.TaskCompletion, false);
        }

        public void Unlink(Building_WorkTable b)
        {
            BillLinkGroup g = GroupOf(b);
            if (g == null) return;
            g.members.Remove(b);
            g.last.Remove(b);
            if (g.members.Count < 2) groups.Remove(g);
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            Messages.Message("BO_UnlinkDone".Translate(), b, MessageTypeDefOf.NeutralEvent, false);
        }

        // ------------------------------------------------------------ gizmos

        public static Gizmo MakeGizmo(Building_WorkTable bench)
        {
            if (!BillOrganizerMod.Settings.enabled || !BillOrganizerMod.Settings.linkBenches || bench.Faction != Faction.OfPlayer) return null;
            BillLinkManager m = Instance;
            if (m == null) return null;
            BillLinkGroup g = m.GroupOf(bench);
            if (g != null)
                return new Command_Action
                {
                    defaultLabel = "BO_UnlinkLabel".Translate(g.members.Count),
                    defaultDesc = "BO_UnlinkDesc".Translate(),
                    icon = Tex.Unlink, // own glyphs: Paste already means "paste" in the queue
                    action = () => m.Unlink(bench),
                    Order = 1001f,
                };
            return new Command_Action
            {
                defaultLabel = "BO_LinkLabel".Translate(),
                defaultDesc = "BO_LinkDesc".Translate(),
                icon = Tex.Link,
                action = () => BeginLink(bench),
                Order = 1001f,
            };
        }

        static void BeginLink(Building_WorkTable source)
        {
            var parms = new TargetingParameters
            {
                canTargetBuildings = true,
                canTargetPawns = false,
                canTargetItems = false,
                validator = t => t.Thing is Building_WorkTable w && CanLink(source, w) == null,
            };
            Find.Targeter.BeginTargeting(parms, target =>
            {
                var dest = target.Thing as Building_WorkTable;
                string why = CanLink(source, dest);
                if (why != null) { Messages.Message(why, MessageTypeDefOf.RejectInput, false); return; }
                BillLinkManager m = Instance;
                // every other bench of the group loses its own bills: ask when that means losing something
                BillLinkGroup gs = m.GroupOf(source), gt = m.GroupOf(dest);
                var losing = new List<Building_WorkTable> { dest };
                if (gt != null && gs == null) losing = gt.members.ToList();
                else if (gs != null) losing = new List<Building_WorkTable> { dest };
                int lost = losing.Where(w => w != source && !(gs != null && gs.members.Contains(w))).Sum(w => w.BillStack.Count);
                if (lost > 0)
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("BO_LinkConfirm".Translate(lost), () => m.Link(source, dest), true));
                else m.Link(source, dest);
            });
        }
    }

    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow))]
    public static class Patch_ShouldDoNow
    {
        static void Postfix(Bill_Production __instance, ref bool __result)
        {
            if (!__result) return;
            try { if (BillLinkManager.Instance != null && BillLinkManager.Instance.ReservedByPeers(__instance)) __result = false; }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Linked benches (reserve) failed: " + e, 0x0B110013); }
        }
    }

    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.Notify_IterationCompleted))]
    public static class Patch_IterationCompleted
    {
        static void Postfix(Bill_Production __instance)
        {
            try { BillLinkManager.Instance?.OnIteration(__instance); }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Linked benches (iteration) failed: " + e, 0x0B110011); }
        }
    }
}
