using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace BillOrganizer
{
    /// <summary>
    /// Stock check for the recipes of one open window: counts what the map has of each ingredient (ResourceCounter, the same
    /// numbers as the resource readout) and marks every recipe Ready / Missing / Unknown. Refreshed about once a second.
    /// </summary>
    public class Availability
    {
        readonly Map map;
        readonly Dictionary<ThingDef, int> counts = new Dictionary<ThingDef, int>();
        float lastRefresh = -999f;

        public Availability(Map map) { this.map = map; }

        public void Refresh(List<RecipeEntry> entries, bool force = false)
        {
            if (!force && Time.realtimeSinceStartup - lastRefresh < 1f) return;
            lastRefresh = Time.realtimeSinceStartup;
            counts.Clear();
            foreach (RecipeEntry e in entries) Evaluate(e);
        }

        /// <summary>Stock of one thing on the map, or -1 if it is not a counted resource.</summary>
        public int CountOf(ThingDef d) => Count(d);

        int Count(ThingDef d)
        {
            if (map == null || !d.CountAsResource) return -1;
            if (!counts.TryGetValue(d, out int c)) counts[d] = c = map.resourceCounter.GetCount(d);
            return c;
        }

        void Evaluate(RecipeEntry e)
        {
            bool anyKnown = false, anyMissing = false;
            foreach (IngInfo i in e.ings)
            {
                float sum = 0f, best = 0f;
                ThingDef bestDef = null;
                bool known = false;
                foreach (ThingDef d in i.defs)
                {
                    int c = Count(d);
                    if (c < 0) continue;
                    known = true;
                    float v = c * Mathf.Max(e.recipe.IngredientValueGetter.ValuePerUnitOf(d), 0f);
                    sum += v;
                    if (v > best || bestDef == null) { best = v; bestDef = d; }
                }
                i.known = known;
                i.haveBase = i.single ? best : sum;
                if (bestDef != null && best > 0f) i.rep = bestDef;
                if (known)
                {
                    anyKnown = true;
                    if (!i.Ok) anyMissing = true;
                }
            }
            e.stock = e.ings.Count == 0 ? Stock.Ready : !anyKnown ? Stock.Unknown : anyMissing ? Stock.Missing : Stock.Ready;
            EvaluateBatches(e);
        }

        /// <summary>
        /// How many batches the stock allows: the minimum over the known lines of floor(have / need) in the recipe's own units,
        /// then lowered until the lines that accept the same thing (e.g. two lines that both take steel) fit in one shared pool.
        /// </summary>
        void EvaluateBatches(RecipeEntry e)
        {
            e.canMake = e.makeItems = -1;
            e.makeLimiter = null;
            int n = int.MaxValue;
            IngInfo limiter = null;
            var known = new List<IngInfo>();
            foreach (IngInfo i in e.ings)
            {
                i.batches = -1;
                if (!i.known || i.needBase <= 0f) continue;
                i.batches = Mathf.FloorToInt((i.haveBase + 0.0001f) / i.needBase);
                known.Add(i);
                if (i.batches < n) { n = i.batches; limiter = i; }
            }
            if (limiter == null) return;
            if (n > 0 && SharesStock(known)) n = FitShared(e, known, n);
            e.canMake = n;
            e.makeLimiter = limiter;
            e.makeItems = (int)Mathf.Min((long)n * e.ItemsPerBatch, int.MaxValue);
        }

        static bool SharesStock(List<IngInfo> lines)
        {
            var seen = new HashSet<ThingDef>();
            foreach (IngInfo i in lines)
                foreach (ThingDef d in i.defs)
                    if (!seen.Add(d)) return true;
            return false;
        }

        /// <summary>Largest k in [0, upper] for which k batches can be paid from the shared pool (binary search; feasibility is monotonic).</summary>
        int FitShared(RecipeEntry e, List<IngInfo> lines, int upper)
        {
            var ordered = lines.OrderBy(l => l.defs.Count).ToList();
            int lo = 0, hi = upper;
            while (lo < hi)
            {
                int mid = lo + (hi - lo + 1) / 2;
                if (Feasible(e, ordered, mid)) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        bool Feasible(RecipeEntry e, List<IngInfo> lines, int k)
        {
            var pool = new Dictionary<ThingDef, float>();
            foreach (IngInfo i in lines)
                foreach (ThingDef d in i.defs)
                    if (!pool.ContainsKey(d))
                    {
                        int c = Count(d);
                        pool[d] = c < 0 ? 0f : c * Mathf.Max(e.recipe.IngredientValueGetter.ValuePerUnitOf(d), 0f);
                    }
            foreach (IngInfo i in lines)
            {
                float need = i.needBase * k;
                if (i.single)
                {
                    ThingDef best = null;
                    foreach (ThingDef d in i.defs) if (best == null || pool[d] > pool[best]) best = d;
                    if (best == null || pool[best] + 0.0001f < need) return false;
                    pool[best] -= need;
                    continue;
                }
                foreach (ThingDef d in i.defs.OrderByDescending(x => pool[x]))
                {
                    float take = Mathf.Min(pool[d], need);
                    pool[d] -= take;
                    need -= take;
                    if (need <= 0.0001f) break;
                }
                if (need > 0.0001f) return false;
            }
            return true;
        }

        /// <summary>
        /// Batches with one material picked in the panel (the bill then allows only that stuff, see RecipeEntry.Configure): the lines
        /// that take that material count only it plus their non-stuff options. Returns -1 when nothing is known; the limiting
        /// material/line name goes to <paramref name="limit"/>. Shared pools are not split here (an upper bound, like the tooltip).
        /// </summary>
        public int BatchesWith(RecipeEntry e, ThingDef stuff, out string limit)
        {
            limit = null;
            int n = int.MaxValue;
            foreach (IngInfo i in e.ings)
            {
                if (i.needBase <= 0f) continue;
                bool restricted = i.defs.Contains(stuff);
                float sum = 0f, best = 0f;
                bool known = false;
                foreach (ThingDef d in i.defs)
                {
                    if (restricted && d.IsStuff && d != stuff) continue;
                    int c = Count(d);
                    if (c < 0) continue;
                    known = true;
                    float v = c * Mathf.Max(e.recipe.IngredientValueGetter.ValuePerUnitOf(d), 0f);
                    sum += v;
                    if (v > best) best = v;
                }
                if (!known) continue;
                int b = Mathf.FloorToInt(((i.single ? best : sum) + 0.0001f) / i.needBase);
                if (b < n)
                {
                    n = b;
                    limit = restricted ? stuff.LabelCap.ToString() : i.defs.Count == 1 ? i.defs[0].LabelCap.ToString() : i.ing.filter.Summary.CapitalizeFirst();
                }
            }
            return n == int.MaxValue ? -1 : n;
        }

        /// <summary>Tooltip for the "can make" number: batches, items, and the line that limits it.</summary>
        public static string MakeTip(RecipeEntry e)
        {
            if (e.canMake < 0) return "BO_CanMakeUnknown".Translate();
            string s = "BO_CanMakeTip".Translate(Ui.Thousands(e.canMake), Ui.Thousands(e.makeItems));
            IngInfo l = e.makeLimiter;
            if (l != null && l.needBase > 0f)
            {
                string name = l.defs.Count == 1 ? l.defs[0].LabelCap.ToString() : l.ing.filter.Summary.CapitalizeFirst();
                s += "\n" + "BO_CanMakeLimit".Translate(name, l.Have, l.need.ToString("0.##"));
            }
            return s;
        }
    }
}
