using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BillOrganizer
{
    /// <summary>One number the picker can show as a column, a bar and a sort key.</summary>
    public class StatInfo
    {
        public string key;                       // sort key, and the translation key BO_Stat_<key>
        public Color color;
        public Func<RecipeEntry, float> get;
        public Func<float, string> fmt;
        public float width = 66f;
        /// <summary>Optional text for the detail panel (e.g. "12-34 mm" across materials); null = just fmt(get(e)).</summary>
        public Func<RecipeEntry, string> detail;
        /// <summary>Text for columns built at runtime (a bonus stat: its own StatDef label), instead of the BO_Stat_ key.</summary>
        public string labelOverride, tipOverride;

        public string Label => labelOverride ?? ("BO_Stat_" + key).Translate();
        public string Tip => tipOverride ?? ("BO_StatTip_" + key).Translate();
    }

    public static class StatTable
    {
        static string Pct(float v) => v.ToStringPercent("F0");
        static string SharpFmt(float v) => CeCompat.Active ? CeCompat.FormatSharp(v) : StatDefOf.ArmorRating_Sharp.ValueToString(v, ToStringNumberSense.Absolute);
        static string BluntFmt(float v) => CeCompat.Active ? CeCompat.FormatBlunt(v) : StatDefOf.ArmorRating_Blunt.ValueToString(v, ToStringNumberSense.Absolute);
        static string HeatFmt(float v) => StatDefOf.ArmorRating_Heat.ValueToString(v, ToStringNumberSense.Absolute);
        static string ColdFmt(float v) => StatDefOf.Insulation_Cold.ValueToString(v, ToStringNumberSense.Absolute);
        static string PenFmt(float v) => CeCompat.Active ? CeCompat.FormatSharp(v) : Pct(v);

        /// <summary>"0 to 1.5 mm RHA": a word instead of a hyphen (a minus sign next to it reads as "-2.4C - 10.8C") and the unit written once.</summary>
        static string RangeText(float min, float max, Func<float, string> f)
        {
            if (float.IsNaN(max)) return Ui.None;
            if (Mathf.Abs(max - min) < 0.0005f) return f(max);
            string a = f(min), b = f(max);
            int ia = a.IndexOf(' '), ib = b.IndexOf(' ');
            if (ia > 0 && ib > 0 && a.Substring(ia) == b.Substring(ib)) a = a.Substring(0, ia);
            return "BO_Range".Translate(a, b);
        }

        public static readonly StatInfo Dps = new StatInfo { key = "dps", color = new Color(1f, 0.6f, 0.25f), get = e => e.dps, fmt = v => v.ToString("0.0"), width = 56f };
        public static readonly StatInfo Damage = new StatInfo { key = "damage", color = new Color(1f, 0.4f, 0.35f), get = e => e.damage, fmt = v => v.ToString("0.#"), width = 64f };
        public static readonly StatInfo Pen = new StatInfo { key = "pen", color = new Color(0.95f, 0.88f, 0.4f), get = e => e.pen, fmt = PenFmt, width = 78f };
        public static readonly StatInfo Range = new StatInfo { key = "range", color = new Color(0.55f, 0.85f, 0.45f), get = e => e.range, fmt = v => v.ToString("0"), width = 56f };
        public static readonly StatInfo Burst = new StatInfo { key = "burst", color = new Color(0.5f, 0.7f, 1f), get = e => e.burst, fmt = v => v.ToString("0"), width = 52f };
        public static readonly StatInfo Mag = new StatInfo { key = "mag", color = new Color(0.72f, 0.72f, 0.78f), get = e => e.mag, fmt = v => v.ToString("0"), width = 56f };
        public static readonly StatInfo Users = new StatInfo { key = "users", color = new Color(0.72f, 0.72f, 0.78f), get = e => e.users, fmt = v => v.ToString("0"), width = 56f };
        public static readonly StatInfo Sharp = new StatInfo
        {
            key = "sharp", color = new Color(1f, 0.45f, 0.35f), get = e => e.sharp, fmt = SharpFmt, width = 88f,
            detail = e => RangeText(e.sharpMin, e.sharp, SharpFmt),
        };
        public static readonly StatInfo Blunt = new StatInfo
        {
            key = "blunt", color = new Color(0.45f, 0.65f, 1f), get = e => e.blunt, fmt = BluntFmt, width = 88f,
            detail = e => RangeText(e.bluntMin, e.blunt, BluntFmt),
        };
        public static readonly StatInfo Heat = new StatInfo
        {
            key = "heat", color = new Color(1f, 0.62f, 0.2f), get = e => e.heat, fmt = HeatFmt, width = 62f,
            detail = e => RangeText(e.heatMin, e.heat, HeatFmt),
        };
        public static readonly StatInfo Cold = new StatInfo
        {
            key = "cold", color = new Color(0.35f, 0.75f, 1f), get = e => e.cold, fmt = ColdFmt, width = 62f,
            detail = e => RangeText(e.coldMin, e.cold, ColdFmt),
        };

        public static readonly StatInfo Work = new StatInfo { key = "work", color = new Color(0.62f, 0.7f, 0.85f), get = e => e.work, fmt = v => v.ToStringWorkAmount(), width = 58f };
        public static readonly StatInfo Skill = new StatInfo { key = "skill", color = new Color(0.8f, 0.65f, 1f), get = e => e.minSkill, fmt = v => v.ToString("0"), width = 48f };
        public static readonly StatInfo Value = new StatInfo { key = "value", color = new Color(0.95f, 0.85f, 0.5f), get = e => e.value, fmt = v => v < 1f ? v.ToString("0.##") : v.ToString("0.#"), width = 56f };

        /// <summary>Batches the stock allows (NaN = unknown). A column and sort key; the detail panel draws it in its own line.</summary>
        public static readonly StatInfo Make = new StatInfo { key = "make", color = new Color(0.5f, 0.85f, 0.55f), get = e => e.canMake < 0 ? float.NaN : e.canMake, fmt = v => ((int)v).ToString(), width = 62f };

        static readonly StatInfo[] weapons = { Dps, Damage, Range };
        static readonly StatInfo[] production = { Make, Work, Skill, Value };

        public static List<StatInfo> ForKind(Kind k)
        {
            switch (k)
            {
                case Kind.Melee: return new List<StatInfo> { Dps, Damage, Pen };
                case Kind.Ranged: return new List<StatInfo> { Range, Dps, Damage, Pen, Burst, Mag };
                case Kind.Grenade: return new List<StatInfo> { Range, Damage };
                case Kind.Ammo: return new List<StatInfo> { Users };
                case Kind.Apparel: return new List<StatInfo> { Sharp, Blunt, Heat, Cold };
                default: return new List<StatInfo>();
            }
        }

        /// <summary>
        /// Columns for a set of entries: their kind's stats (a weapon mix shares DPS/damage/range), then the bonus stats when worn/equipped
        /// that at least <paramref name="bonusMin"/> entries share (at most <paramref name="bonusMax"/>; mechanitor bandwidth...), then
        /// production. Empty columns are dropped. <paramref name="bonusFrom"/>: the entries the bonus columns are picked from (the list the
        /// search left; null = <paramref name="entries"/>), so "+band" in "All" shows bandwidth, not the most common bonus of the whole group.
        /// </summary>
        public static List<StatInfo> For(IList<RecipeEntry> entries, int bonusMin = 2, int bonusMax = 2, IList<RecipeEntry> bonusFrom = null)
        {
            var kinds = entries.Select(e => e.kind).Distinct().ToList();
            List<StatInfo> cols;
            if (kinds.Count == 1) cols = ForKind(kinds[0]);
            else if (kinds.Count > 0 && kinds.All(k => k == Kind.Melee || k == Kind.Ranged || k == Kind.Grenade)) cols = weapons.ToList();
            else cols = new List<StatInfo>();
            IList<RecipeEntry> src = bonusFrom ?? entries;
            try { cols.AddRange(Bonus.Columns(src, Mathf.Min(bonusMin, Mathf.Max(src.Count, 1)), bonusMax)); }
            catch (Exception ex) { Log.Warning("[BillOrganizer] Bonus columns failed: " + ex.Message); }
            cols.AddRange(production);
            cols.RemoveAll(c => !entries.Any(e => !float.IsNaN(c.get(e))));
            return cols;
        }

        /// <summary>Work and value, for the detail panel (the combat stats are drawn by their own section). The skill is not repeated here: Requirements already lists it.</summary>
        public static List<StatInfo> Production(RecipeEntry e) => production.Where(c => c != Make && c != Skill && !float.IsNaN(c.get(e))).ToList();
    }
}
