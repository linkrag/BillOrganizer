using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BillOrganizer
{
    /// <summary>One effect of a product beyond its combat stats: a stat it gives when worn/equipped, an ability, a hediff, a drug effect...</summary>
    public class BonusLine
    {
        public string group;          // Bonus.G* (translation key of the subtitle)
        public StatDef stat;          // set only for equipped stat offsets (numeric: columns, sorting, compare)
        public float value = float.NaN;
        public string label, valueText, tip;
        public Def link;              // ability / hediff / thing whose info card a click opens
        /// <summary>Shown in the detail and found by the search, but does not light the list marker (every meal has nutrition).</summary>
        public bool minor;
    }

    /// <summary>
    /// Reads, once per product, what an item does beyond the stats the picker already shows: equippedStatOffsets (mechanitor bandwidth,
    /// psychic sensitivity, move speed...), abilities and charges, shields, the hediff an apparel applies, implants, mechanoids, drugs and
    /// food. Everything comes from defs and from the game's own StatDrawEntry texts, so mods using the same fields and comps show up too.
    /// Each source is guarded on its own: a broken comp of some mod loses its lines, never the panel.
    /// </summary>
    public static class Bonus
    {
        public const string GEquip = "BO_BonusEquip", GAbility = "BO_BonusAbility", GWorn = "BO_BonusWorn", GImplant = "BO_BonusImplant",
            GMech = "BO_BonusMech", GIngest = "BO_BonusIngest", GOther = "BO_BonusOther";
        /// <summary>Display order of the groups in the detail panel.</summary>
        public static readonly string[] Groups = { GEquip, GAbility, GWorn, GImplant, GMech, GIngest, GOther };

        static readonly List<BonusLine> Empty = new List<BonusLine>();
        static readonly Dictionary<ThingDef, List<BonusLine>> cache = new Dictionary<ThingDef, List<BonusLine>>();
        static readonly Dictionary<ThingDef, bool> markCache = new Dictionary<ThingDef, bool>();
        static readonly Dictionary<ThingDef, string> hayCache = new Dictionary<ThingDef, string>();
        static readonly Dictionary<StatDef, StatInfo> colCache = new Dictionary<StatDef, StatInfo>();
        static Dictionary<ThingDef, HediffDef> implantOf;

        public static List<BonusLine> For(ThingDef d)
        {
            if (d == null) return Empty;
            if (cache.TryGetValue(d, out List<BonusLine> l)) return l;
            l = new List<BonusLine>();
            try { Build(d, l); }
            catch (Exception ex) { Log.Warning("[BillOrganizer] Could not read the bonuses of " + d.defName + ": " + ex.Message); }
            cache[d] = l;
            return l;
        }

        /// <summary>True when the list marker is due: a line that is not minor (nutrition, joy, generic comp texts).</summary>
        public static bool Has(RecipeEntry e)
        {
            if (e?.product == null) return false;
            if (markCache.TryGetValue(e.product, out bool b)) return b;
            b = For(e.product).Any(x => !x.minor);
            markCache[e.product] = b;
            return b;
        }

        /// <summary>The game's own text for an equipped offset ("+6", "-10%"), the same call the info card makes.</summary>
        public static string FormatOffset(StatDef s, float v)
        {
            try { return s.ValueToString(v, ToStringNumberSense.Offset, s.finalizeEquippedStatOffset); }
            catch { return (v > 0f ? "+" : "") + v.ToString("0.##"); }
        }

        /// <summary>The offset this entry's product gives to <paramref name="s"/> when equipped; NaN = none.</summary>
        public static float Offset(RecipeEntry e, StatDef s)
        {
            List<StatModifier> mods = e?.product?.equippedStatOffsets;
            if (mods == null) return float.NaN;
            for (int i = 0; i < mods.Count; i++) if (mods[i].stat == s) return mods[i].value;
            return float.NaN;
        }

        /// <summary>
        /// Equipped-offset stats of a set of entries as columns: those present in at least <paramref name="minCount"/> entries, most common
        /// first, at most <paramref name="max"/>.
        /// </summary>
        public static List<StatInfo> Columns(IList<RecipeEntry> entries, int minCount, int max)
        {
            var count = new Dictionary<StatDef, int>();
            foreach (RecipeEntry e in entries)
            {
                List<StatModifier> mods = e.product?.equippedStatOffsets;
                if (mods == null) continue;
                foreach (StatModifier m in mods)
                    if (m?.stat != null && m.value != 0f) count[m.stat] = count.TryGetValue(m.stat, out int c) ? c + 1 : 1;
            }
            return count.Where(kv => kv.Value >= minCount).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.label)
                .Take(max).Select(kv => Column(kv.Key)).ToList();
        }

        static StatInfo Column(StatDef s)
        {
            if (colCache.TryGetValue(s, out StatInfo c)) return c;
            string label = s.LabelCap.ToString();
            Verse.Text.Font = GameFont.Small;
            c = new StatInfo
            {
                key = "bonus:" + s.defName, color = Ui.Bonus,
                get = e => Offset(e, s), fmt = v => FormatOffset(s, v),
                width = Mathf.Clamp(Verse.Text.CalcSize(label).x + 16f, 64f, 96f),
                labelOverride = label,
                tipOverride = "BO_BonusColTip".Translate(label) + (s.description.NullOrEmpty() ? "" : "\n\n" + s.description),
            };
            colCache[s] = c;
            return c;
        }

        /// <summary>Abilities the product grants (for the compare row), "" when none.</summary>
        public static string Grants(RecipeEntry e) =>
            e?.product == null ? "" : string.Join(", ", For(e.product).Where(b => b.group == GAbility && b.link is AbilityDef).Select(b => b.valueText));

        /// <summary>Search text of the "+" prefix: labels, values and defNames of every bonus line (lowercase).</summary>
        public static string Hay(ThingDef d)
        {
            if (d == null) return "";
            if (hayCache.TryGetValue(d, out string h)) return h;
            var parts = new List<string>();
            foreach (BonusLine b in For(d))
            {
                parts.Add(b.label); parts.Add(b.valueText);
                if (b.stat != null) parts.Add(b.stat.defName);
                if (b.link != null) { parts.Add(b.link.label); parts.Add(b.link.defName); }
            }
            h = string.Join("\n", parts.Where(s => !s.NullOrEmpty())).ToLowerInvariant();
            hayCache[d] = h;
            return h;
        }

        /// <summary>Tooltip of the list marker: the first lines, one per row.</summary>
        public static string MarkTip(ThingDef d)
        {
            List<BonusLine> l = For(d);
            IEnumerable<string> rows = l.Where(b => !b.minor).Take(10).Select(b => b.label + ": " + b.valueText);
            string s = "BO_BonusMarkTip".Translate() + "\n\n" + string.Join("\n", rows);
            if (l.Count(b => !b.minor) > 10) s += "\n...";
            return s;
        }

        // ------------------------------------------------------------ build

        static void Build(ThingDef d, List<BonusLine> l)
        {
            ThingDef stuff = d.MadeFromStuff ? GenStuff.DefaultStuffFor(d) : null;
            StatRequest req = StatRequest.For(d, stuff);
            bool pawn = d.race != null;

            // 1. stats when worn / equipped
            Guard(d, "equipped offsets", () =>
            {
                if (d.equippedStatOffsets == null) return;
                foreach (StatModifier m in d.equippedStatOffsets)
                {
                    if (m?.stat == null || m.value == 0f) continue;
                    l.Add(new BonusLine { group = GEquip, stat = m.stat, value = m.value, label = m.stat.LabelCap, valueText = FormatOffset(m.stat, m.value), tip = m.stat.description });
                }
            });

            // 2. abilities, charges, shields; 3. other comps (mods' comps show up through their own SpecialDisplayStats)
            if (!pawn && d.comps != null)
            {
                bool weaponOrAmmo = d.IsWeapon || Grouping.IsAmmo(d); // their comps repeat what the stats section already shows (CE magazines...)
                foreach (CompProperties c in d.comps)
                {
                    if (c == null) continue;
                    CompProperties comp = c;
                    Guard(d, comp.GetType().Name, () =>
                    {
                        if (comp is CompProperties_EquippableAbility ea && ea.abilityDef != null)
                            l.Add(new BonusLine { group = GAbility, label = "BO_BonusGrants".Translate(), valueText = ea.abilityDef.LabelCap, tip = ea.abilityDef.description, link = ea.abilityDef });
                        if (comp is CompProperties_CauseHediff_Apparel ch && ch.hediff != null)
                        {
                            l.Add(new BonusLine { group = GWorn, label = "BO_BonusApplies".Translate(), valueText = ch.hediff.LabelCap, tip = ch.hediff.description, link = ch.hediff });
                            AddHediff(l, GWorn, ch.hediff);
                            return;
                        }
                        if (comp is CompProperties_Shield)
                        {
                            AddStat(l, GAbility, d, stuff, "EnergyShieldEnergyMax");
                            AddStat(l, GAbility, d, stuff, "EnergyShieldRechargeRate");
                        }
                        string tn = comp.GetType().Name;
                        bool ability = tn.Contains("Ability") || tn.Contains("Reloadable") || tn.Contains("VerbOwner") || tn.Contains("Shield") || tn.Contains("Interceptor");
                        if (weaponOrAmmo && !ability) return;
                        AddEntries(l, ability ? GAbility : GOther, comp.SpecialDisplayStats(req), !ability);
                    });
                }
            }
            // verbs of apparel (jump pack and the like): what they let the wearer do
            if (!pawn && d.IsApparel && d.Verbs != null)
                Guard(d, "apparel verbs", () =>
                {
                    foreach (VerbProperties v in d.Verbs)
                        if (v != null && !v.label.NullOrEmpty())
                            l.Add(new BonusLine { group = GAbility, label = "BO_BonusGrants".Translate(), valueText = v.label.CapitalizeFirst() });
                });

            // 5. implants: the hediff that gives this item back when removed is the one it installs
            if (!pawn)
                Guard(d, "implant", () =>
                {
                    HediffDef h = ImplantOf(d);
                    if (h == null) return;
                    l.Add(new BonusLine { group = GImplant, label = "BO_BonusInstalls".Translate(), valueText = h.LabelCap, tip = h.description, link = h });
                    if (h.addedPartProps != null)
                        l.Add(new BonusLine { group = GImplant, label = "BO_BonusPartEff".Translate(), valueText = h.addedPartProps.partEfficiency.ToStringPercent(), tip = "BO_BonusPartEffTip".Translate() });
                    AddHediff(l, GImplant, h);
                });

            // 6. mechanoids (gestator): bandwidth cost, weight class, recharger, abilities
            if (pawn && d.race.IsMechanoid)
            {
                Guard(d, "mech bandwidth", () => AddStat(l, GMech, d, null, "BandwidthCost"));
                Guard(d, "mech stats", () => AddEntries(l, GMech, d.race.SpecialDisplayStats(d, req).Where(s => s.category == StatCategoryDefOf.Mechanoid)));
                Guard(d, "mech abilities", () =>
                {
                    PawnKindDef k = DefDatabase<PawnKindDef>.AllDefsListForReading.FirstOrDefault(x => x.race == d && !x.abilities.NullOrEmpty());
                    if (k == null) return;
                    foreach (AbilityDef a in k.abilities)
                        if (a != null) l.Add(new BonusLine { group = GMech, label = "BO_BonusAbilityOf".Translate(), valueText = a.LabelCap, tip = a.description, link = a });
                });
            }

            // 7. drugs and food
            if (!pawn && d.ingestible != null)
            {
                IngestibleProperties ing = d.ingestible;
                Guard(d, "nutrition", () =>
                {
                    float n = d.GetStatValueAbstract(StatDefOf.Nutrition, stuff);
                    if (n > 0f) l.Add(new BonusLine { group = GIngest, label = StatDefOf.Nutrition.LabelCap, valueText = StatDefOf.Nutrition.ValueToString(n), tip = StatDefOf.Nutrition.description, minor = true });
                });
                Guard(d, "joy", () =>
                {
                    if (ing.joy > 0f)
                        l.Add(new BonusLine { group = GIngest, label = "Joy".Translate().CapitalizeFirst(), valueText = ing.joy.ToStringPercent("F0") + " (" + ing.JoyKind.label + ")", tip = "Stat_Thing_Ingestible_Joy_Desc".Translate(), minor = true });
                    if (ing.drugCategory != DrugCategory.None)
                        l.Add(new BonusLine { group = GIngest, label = "DrugCategory".Translate().CapitalizeFirst(), valueText = ing.drugCategory.GetLabel().CapitalizeFirst(), tip = "Stat_Thing_Drug_Category_Desc".Translate(), minor = true });
                });
                if (ing.outcomeDoers != null)
                    foreach (IngestionOutcomeDoer o in ing.outcomeDoers)
                    {
                        IngestionOutcomeDoer od = o;
                        if (od != null) Guard(d, od.GetType().Name, () => AddEntries(l, GIngest, od.SpecialDisplayStats(d)));
                    }
                if (d.IsDrug) Guard(d, "drug stats", () => AddEntries(l, GIngest, DrugStatsUtility.SpecialDisplayStats(d)));
            }

            // the same text from two sources (a comp and the drug utility, an ability from a comp and from our own line): keep the first
            var seen = new HashSet<string>();
            l.RemoveAll(b => b.label.NullOrEmpty() || b.valueText.NullOrEmpty() || !seen.Add(b.label + "|" + b.valueText));
        }

        static void Guard(ThingDef d, string what, Action a)
        {
            try { a(); }
            catch (Exception ex)
            {
                if (Prefs.DevMode) Log.Message("[BillOrganizer] Bonus source '" + what + "' failed for " + d.defName + ": " + ex.Message);
            }
        }

        static void AddStat(List<BonusLine> l, string group, ThingDef d, ThingDef stuff, string statName)
        {
            StatDef s = DefDatabase<StatDef>.GetNamedSilentFail(statName);
            if (s == null) return;
            float v = d.GetStatValueAbstract(s, stuff);
            if (v <= 0f) return;
            l.Add(new BonusLine { group = group, label = s.LabelCap, valueText = s.ValueToString(v), tip = s.description });
        }

        /// <summary>The first stage of a hediff (stat offsets/factors, capacities, pain...), as the game describes it.</summary>
        static void AddHediff(List<BonusLine> l, string group, HediffDef h)
        {
            if (h.stages.NullOrEmpty()) return;
            AddEntries(l, group, h.stages[0].SpecialDisplayStats());
        }

        /// <summary>One line per StatDrawEntry; an entry that throws (one that needs a real thing) is skipped alone.</summary>
        static void AddEntries(List<BonusLine> l, string group, IEnumerable<StatDrawEntry> entries, bool minor = false)
        {
            if (entries == null) return;
            IEnumerator<StatDrawEntry> it = entries.GetEnumerator();
            for (int guard = 0; guard < 40; guard++)
            {
                StatDrawEntry s;
                try { if (!it.MoveNext()) break; s = it.Current; }
                catch { break; } // the generator itself failed: what came before stays
                if (s == null) continue;
                try
                {
                    string label = s.LabelCap, value = s.ValueString;
                    if (label.NullOrEmpty() || value.NullOrEmpty()) continue;
                    string tip;
                    try { tip = s.GetExplanationText(StatRequest.ForEmpty()); } catch { tip = null; }
                    l.Add(new BonusLine { group = group, label = label, valueText = value.Trim(), tip = tip, minor = minor });
                }
                catch { }
            }
        }

        static HediffDef ImplantOf(ThingDef d)
        {
            if (implantOf == null)
            {
                implantOf = new Dictionary<ThingDef, HediffDef>();
                foreach (HediffDef h in DefDatabase<HediffDef>.AllDefsListForReading)
                    if (h.spawnThingOnRemoved != null && !implantOf.ContainsKey(h.spawnThingOnRemoved)) implantOf[h.spawnThingOnRemoved] = h;
            }
            return implantOf.TryGetValue(d, out HediffDef r) ? r : null;
        }
    }
}
