using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace BillOrganizer
{
    /// <summary>What a queued bill is allowed to use: the materials of its ingredient filter compared with the recipe's default.</summary>
    public class MatInfo
    {
        public readonly List<ThingDef> allowed = new List<ThingDef>();
        /// <summary>Materials the player may pick for the product (empty when the product is not made from stuff).</summary>
        public List<ThingDef> stuffs = new List<ThingDef>();
        public bool isDefault = true, none;
        public int candidates;
        public bool CanPick => stuffs.Count > 1;
    }

    /// <summary>
    /// Helpers of the queue card (QueuePanel.DrawRow): product icon, material chip (icon + name, icons only, or "n of m"), the menu
    /// that changes the material of a bill already in the queue, and the vanilla "Details" dialog. Everything null-safe.
    /// </summary>
    public static class BillCard
    {
        static readonly Dictionary<Bill, MatInfo> cache = new Dictionary<Bill, MatInfo>();
        static float cacheTime;

        public static void DrawProductIcon(Rect r, Bill b)
        {
            try
            {
                ThingDef d = b.recipe.UIIconThing ?? b.recipe.ProducedThingDef;
                if (d != null && d.uiIcon != null && d.uiIcon != BaseContent.BadTex) Widgets.ThingIcon(r, d);
                else if (b.recipe.UIIcon != null) Widgets.DrawTextureFitted(r, b.recipe.UIIcon, 1f);
            }
            catch (Exception) { /* icon is decoration only */ }
        }

        public static MatInfo Info(Bill b)
        {
            if (Time.realtimeSinceStartup - cacheTime > 0.5f) { cache.Clear(); cacheTime = Time.realtimeSinceStartup; }
            if (!cache.TryGetValue(b, out MatInfo m)) cache[b] = m = Compute(b);
            return m;
        }

        static MatInfo Compute(Bill b)
        {
            var m = new MatInfo();
            try
            {
                RecipeDef r = b.recipe;
                ThingFilter fixedF = r.fixedIngredientFilter, defF = r.defaultIngredientFilter, cur = b.ingredientFilter;
                if (cur == null) return m;
                // Bill.IsFixedOrAllowedIngredient (1.7): a fixed ingredient (a filter of one def: steel, devilstrand...) is always
                // accepted and never looks at the bill's filter, so only the ingredients that offer a choice have "materials"
                var cands = new List<ThingDef>();
                var seen = new HashSet<ThingDef>();
                if (r.ingredients != null)
                    foreach (IngredientCount ing in r.ingredients)
                    {
                        if (ing?.filter == null || ing.IsFixedIngredient) continue;
                        foreach (ThingDef d in ing.filter.AllowedThingDefs)
                            if ((fixedF == null || fixedF.Allows(d)) && seen.Add(d)) cands.Add(d);
                    }
                if (cands.Count == 0) return m; // nothing to choose: no chip
                m.candidates = cands.Count;
                bool same = true;
                foreach (ThingDef d in cands)
                {
                    bool a = cur.Allows(d), def = defF == null || defF.Allows(d);
                    if (a) m.allowed.Add(d);
                    if (a != def) same = false;
                }
                m.isDefault = same;
                m.none = m.allowed.Count == 0;
                ThingDef prod = r.ProducedThingDef;
                if (prod != null && prod.MadeFromStuff)
                {
                    var set = new HashSet<ThingDef>(cands);
                    foreach (ThingDef s in GenStuff.AllowedStuffsFor(prod)) if (set.Contains(s)) m.stuffs.Add(s);
                }
            }
            catch (Exception ex) { Log.WarningOnce("[BillOrganizer] Could not read the materials of a bill: " + ex.Message, 0x0B110010); }
            return m;
        }

        static int Stock(Map map, ThingDef d) => map != null && d.CountAsResource ? map.resourceCounter.GetCount(d) : -1;

        /// <summary>True when <see cref="DrawMaterial"/> will draw something for this bill (the queue card reserves room for it).</summary>
        public static bool HasMaterialChip(Bill b)
        {
            MatInfo m = Info(b);
            return !(m.isDefault && !(!m.none && m.CanPick));
        }

        /// <summary>
        /// Draws the material chip in <paramref name="area"/> (left aligned, never wider than the area) and returns the rect it used
        /// (width 0 when there is nothing to show: recipes without a material choice that are left at their default).
        /// </summary>
        public static Rect DrawMaterial(Rect area, Bill b, Map map)
        {
            MatInfo m = Info(b);
            bool showAny = m.isDefault && !m.none && m.CanPick;
            if (m.isDefault && !showAny) return new Rect(area.x, area.y, 0f, area.height);

            string text = null;
            int icons = 0;
            if (showAny) text = "BO_MatAnyShort".Translate();
            else if (m.none) text = "BO_MatNone".Translate();
            else if (m.allowed.Count == 1) { icons = 1; text = m.allowed[0].LabelCap; }
            else if (m.allowed.Count <= 3) icons = m.allowed.Count;
            else text = "BO_MatSome".Translate(m.allowed.Count, m.candidates);

            const float Ic = 18f, Pad = 5f;
            Color tc = m.none ? Ui.Bad : showAny ? Ui.Faint : Color.white;
            Verse.Text.Font = GameFont.Small;
            float w = Pad * 2f + icons * (Ic + 2f);
            float textW = 0f;
            if (text != null) { textW = Verse.Text.CalcSize(text).x; w += textW + 2f; }
            w = Mathf.Min(w, area.width);
            if (w < 24f) return new Rect(area.x, area.y, 0f, area.height);
            var r = new Rect(area.x, area.y, w, area.height);

            bool clickable = m.CanPick;
            bool over = Mouse.IsOver(r);
            Ui.Box(r, over && clickable ? Ui.Hover : Ui.PanelLight);
            if (!m.isDefault) Widgets.DrawBox(r, 1);
            float x = r.x + Pad;
            for (int i = 0; i < icons; i++)
            {
                Widgets.ThingIcon(new Rect(x, r.y + (r.height - Ic) / 2f, Ic, Ic), m.allowed[i]);
                x += Ic + 2f;
            }
            if (text != null) Ui.TextFit(new Rect(x, r.y, r.xMax - x - Pad + 2f, r.height), text, tc);

            var tip = new System.Text.StringBuilder();
            tip.Append("BO_MatTipHead".Translate());
            foreach (ThingDef d in m.allowed.Take(12))
            {
                int have = Stock(map, d);
                tip.Append("\n- ").Append(d.LabelCap);
                if (have >= 0) tip.Append(" (").Append(have).Append(")");
            }
            if (m.allowed.Count > 12) tip.Append("\n...");
            if (clickable) tip.Append("\n\n").Append("BO_MatClickTip".Translate());
            TooltipHandler.TipRegion(r, tip.ToString());
            if (clickable && Widgets.ButtonInvisible(r)) OpenMaterialMenu(b, m, map);
            return r;
        }

        /// <summary>Menu that restricts an existing bill to one material (same rule as RecipeEntry.Configure) or restores the default.</summary>
        public static void OpenMaterialMenu(Bill b, MatInfo m, Map map)
        {
            ThingFilter defF = b.recipe.defaultIngredientFilter;
            var list = new List<FloatMenuOption>
            {
                new FloatMenuOption("BO_MatAny".Translate(), () =>
                {
                    foreach (ThingDef s in m.stuffs) b.ingredientFilter.SetAllow(s, defF == null || defF.Allows(s));
                    cacheTime = -999f;
                })
            };
            foreach (ThingDef s in m.stuffs.OrderByDescending(d => Stock(map, d)).ThenBy(d => d.label))
            {
                ThingDef pick = s;
                int have = Stock(map, s);
                list.Add(new FloatMenuOption(s.LabelCap + (have >= 0 ? " (" + have + ")" : ""), () =>
                {
                    foreach (ThingDef o in m.stuffs) b.ingredientFilter.SetAllow(o, o == pick);
                    cacheTime = -999f;
                }));
            }
            Find.WindowStack.Add(new FloatMenu(list));
        }

        /// <summary>The vanilla settings dialog (filters, quality, hit points, radius, zone, pawn...).</summary>
        public static void OpenDetails(BillStack stack, Bill b)
        {
            var bench = stack.billGiver as Thing;
            if (b is Bill_Production bp && bench != null) Find.WindowStack.Add(new Dialog_BillConfig(bp, bench.Position));
        }

        /// <summary>Count -> until X -> forever -> count. Keeps the numbers the player already had.</summary>
        public static void CycleMode(Bill_Production bp)
        {
            if (bp.repeatMode == BillRepeatModeDefOf.RepeatCount) { bp.repeatMode = BillRepeatModeDefOf.TargetCount; RecipeEntry.SetTarget(bp, RecipeEntry.SnapTarget(bp.targetCount, RecipeEntry.TargetStepOf(bp.recipe))); } // the hidden value (10 by default) on whole batches
            else if (bp.repeatMode == BillRepeatModeDefOf.TargetCount) bp.repeatMode = BillRepeatModeDefOf.Forever;
            else { bp.repeatMode = BillRepeatModeDefOf.RepeatCount; bp.repeatCount = Mathf.Max(bp.repeatCount, 1); }
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }
    }
}
