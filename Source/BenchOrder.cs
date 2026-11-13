using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BillOrganizer
{
    /// <summary>
    /// Reorders the <c>WorkGiver_DoBill</c> entries of a pawn's work list, inside each work type, by the highest priority of the benches they
    /// serve (<c>fixedBillGiverDefs</c>) that have bills queued. The game takes the first work giver of the list that finds a job, so a bench
    /// with higher priority is tried before another bench type of the same skill (Fabrication x Smithy). Everything else keeps its place;
    /// all zero = list untouched. Never crosses skills (that is the Work tab).
    /// </summary>
    public static class BenchOrder
    {
        static int version;
        static bool Enabled => BillOrganizerMod.Settings != null && BillOrganizerMod.Settings.enabled && BillOrganizerMod.Settings.benchPrioAcross;

        public static void Invalidate() { version++; }

        class MapTable { public int tick = -999999; public Dictionary<WorkGiverDef, int> prio = new Dictionary<WorkGiverDef, int>(); }
        static readonly Dictionary<Map, MapTable> tables = new Dictionary<Map, MapTable>();
        static Dictionary<ThingDef, List<WorkGiverDef>> giversOfBench;

        class Cached { public int version; public WorkGiver[] source; public List<WorkGiver> result; }
        static readonly ConditionalWeakTable<List<WorkGiver>, Cached> cache = new ConditionalWeakTable<List<WorkGiver>, Cached>();

        static Dictionary<ThingDef, List<WorkGiverDef>> GiversOfBench()
        {
            if (giversOfBench != null) return giversOfBench;
            var d = new Dictionary<ThingDef, List<WorkGiverDef>>();
            foreach (WorkGiverDef g in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (g.fixedBillGiverDefs == null || g.giverClass == null || !typeof(WorkGiver_DoBill).IsAssignableFrom(g.giverClass)) continue;
                foreach (ThingDef t in g.fixedBillGiverDefs)
                {
                    if (!d.TryGetValue(t, out List<WorkGiverDef> l)) d[t] = l = new List<WorkGiverDef>();
                    l.Add(g);
                }
            }
            return giversOfBench = d;
        }

        /// <summary>giver def -> best priority of its benches with queued bills (only entries different from 0), refreshed every 120 ticks.</summary>
        static Dictionary<WorkGiverDef, int> TableFor(Map map)
        {
            if (!tables.TryGetValue(map, out MapTable t)) tables[map] = t = new MapTable();
            if (Find.TickManager.TicksGame - t.tick < 120 && Find.TickManager.TicksGame >= t.tick) return t.prio;
            t.tick = Find.TickManager.TicksGame;
            var fresh = new Dictionary<WorkGiverDef, int>();
            Dictionary<ThingDef, List<WorkGiverDef>> byBench = GiversOfBench();
            foreach (Building_WorkTable b in map.listerBuildings.AllBuildingsColonistOfClass<Building_WorkTable>())
            {
                if (b.BillStack.Count == 0 || !byBench.TryGetValue(b.def, out List<WorkGiverDef> givers)) continue;
                int p = BenchPriority.Get(b);
                foreach (WorkGiverDef g in givers) fresh[g] = fresh.TryGetValue(g, out int old) ? Math.Max(old, p) : p;
            }
            foreach (WorkGiverDef k in fresh.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList()) fresh.Remove(k);
            bool changed = fresh.Count != t.prio.Count || fresh.Any(kv => !t.prio.TryGetValue(kv.Key, out int o) || o != kv.Value);
            t.prio = fresh;
            if (changed) version++;
            return fresh;
        }

        public static List<WorkGiver> Apply(Pawn pawn, List<WorkGiver> original)
        {
            try
            {
                if (!Enabled || original == null || original.Count < 2 || pawn?.Map == null || Current.Game == null || Find.TickManager == null) return original;
                Dictionary<WorkGiverDef, int> table = TableFor(pawn.Map);
                if (table.Count == 0) return original;
                if (cache.TryGetValue(original, out Cached c) && c.version == version && Same(c.source, original)) return c.result;
                List<WorkGiver> result = Reorder(original, table);
                cache.Remove(original);
                cache.Add(original, new Cached { version = version, source = original.ToArray(), result = result });
                return result;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[BillOrganizer] Bench order failed (list left as is): " + e, 0x0B110021);
                return original;
            }
        }

        static bool Same(WorkGiver[] a, List<WorkGiver> b)
        {
            if (a.Length != b.Count) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Inside each contiguous run of one work type, only the bill givers swap places (stable sort by priority, descending).</summary>
        static List<WorkGiver> Reorder(List<WorkGiver> src, Dictionary<WorkGiverDef, int> table)
        {
            var result = new List<WorkGiver>(src);
            int i = 0;
            while (i < result.Count)
            {
                int j = i;
                while (j < result.Count && result[j].def.workType == result[i].def.workType) j++;
                var slots = new List<int>();
                for (int k = i; k < j; k++) if (result[k] is WorkGiver_DoBill) slots.Add(k);
                if (slots.Count > 1)
                {
                    List<WorkGiver> sorted = slots.Select(k => result[k]).OrderByDescending(g => table.TryGetValue(g.def, out int p) ? p : 0).ToList();
                    for (int n = 0; n < slots.Count; n++) result[slots[n]] = sorted[n];
                }
                i = j;
            }
            return result;
        }
    }

    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.WorkGiversInOrderNormal), MethodType.Getter)]
    public static class Patch_WorkOrderNormal
    {
        static readonly AccessTools.FieldRef<Pawn_WorkSettings, Pawn> PawnOf = AccessTools.FieldRefAccess<Pawn_WorkSettings, Pawn>("pawn");

        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn_WorkSettings __instance, ref List<WorkGiver> __result) { __result = BenchOrder.Apply(PawnOf(__instance), __result); }
    }

    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.WorkGiversInOrderEmergency), MethodType.Getter)]
    public static class Patch_WorkOrderEmergency
    {
        static readonly AccessTools.FieldRef<Pawn_WorkSettings, Pawn> PawnOf = AccessTools.FieldRefAccess<Pawn_WorkSettings, Pawn>("pawn");

        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn_WorkSettings __instance, ref List<WorkGiver> __result) { __result = BenchOrder.Apply(PawnOf(__instance), __result); }
    }
}
