using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace BillOrganizer
{
    /// <summary>Glyph textures (tools/gen_ui_tex.py). The game font cannot be trusted with stars/arrows, so none are drawn as text.</summary>
    [StaticConstructorOnStartup]
    public static class Tex
    {
        public static readonly Texture2D Star = Load("star"), StarO = Load("star_o"), ArrowR = Load("arrow_r"), ArrowD = Load("arrow_d"),
            TriUp = Load("tri_up"), TriDown = Load("tri_down"), Plus = Load("plus"), Check = Load("check"), Cross = Load("cross"), Pin = Load("pin"),
            Duplicate = Load("duplicate"), Link = Load("link"), Unlink = Load("unlink"), Bonus = Load("bonus"),
            Catalog = ContentFinder<Texture2D>.Get("BillOrganizer/Catalog", false) ?? BaseContent.BadTex; // workbench button icon

        static Texture2D Load(string n) => ContentFinder<Texture2D>.Get("BillOrganizer/UI/" + n, false) ?? BaseContent.BadTex;
    }

    /// <summary>Palette and small drawing helpers for the picker (own look: dark panels, one color per stat).</summary>
    public static class Ui
    {
        public static readonly Color Bg = new Color(0.075f, 0.09f, 0.105f);
        public static readonly Color Panel = new Color(0.105f, 0.125f, 0.145f);
        public static readonly Color PanelLight = new Color(0.14f, 0.165f, 0.19f);
        public static readonly Color Hover = new Color(0.19f, 0.24f, 0.28f);
        public static readonly Color Selected = new Color(0.2f, 0.32f, 0.4f);
        public static readonly Color Outline = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Accent = new Color(1f, 0.77f, 0.27f);
        public static readonly Color Good = new Color(0.36f, 0.82f, 0.48f);
        public static readonly Color Bad = new Color(0.96f, 0.37f, 0.32f);
        public static readonly Color Warn = new Color(1f, 0.76f, 0.3f);
        public static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
        public static readonly Color Faint = new Color(1f, 1f, 1f, 0.42f);
        /// <summary>Bonus marker and bonus columns (teal: the accent already means favorite/pinned).</summary>
        public static readonly Color Bonus = new Color(0.45f, 0.9f, 0.8f);

        public static void Box(Rect r, Color c) { Widgets.DrawBoxSolid(r, c); }

        public static void PanelBox(Rect r)
        {
            Widgets.DrawBoxSolidWithOutline(r, Panel, Outline);
        }

        public static void Glyph(Rect r, Texture2D t, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, t);
            GUI.color = old;
        }

        /// <summary>Text with an explicit color/anchor, restoring the previous GUI state.</summary>
        public static void Text_(Rect r, string s, Color c, TextAnchor a = TextAnchor.MiddleLeft, GameFont f = GameFont.Small, bool wrap = false)
        {
            Color oc = GUI.color; TextAnchor oa = Verse.Text.Anchor; GameFont of = Verse.Text.Font; bool ow = Verse.Text.WordWrap;
            GUI.color = c; Verse.Text.Anchor = a; Verse.Text.Font = f; Verse.Text.WordWrap = wrap;
            Widgets.Label(r, s);
            GUI.color = oc; Verse.Text.Anchor = oa; Verse.Text.Font = of; Verse.Text.WordWrap = ow;
        }

        /// <summary>Single-line text that is cut with "..." when it does not fit, with the full text as a tooltip (only when it was cut).</summary>
        public static void TextFit(Rect r, string s, Color c, TextAnchor a = TextAnchor.MiddleLeft, GameFont f = GameFont.Small)
        {
            if (s.NullOrEmpty()) return;
            Verse.Text.Font = f;
            bool cut = Verse.Text.CalcSize(s).x > r.width;
            Text_(r, cut ? s.Truncate(r.width) : s, c, a, f);
            if (cut) TooltipHandler.TipRegion(r, s);
        }

        // ---- adaptive labels: use all the free width first, and cut only when there is really no room

        static string verbPrefix;
        static LoadedLanguage verbLang;

        /// <summary>The generic start of a recipe name in the current language ("Make "), taken from the game's own RecipeMake string.</summary>
        static string VerbPrefix()
        {
            if (verbLang != LanguageDatabase.activeLanguage || verbPrefix == null)
            {
                verbLang = LanguageDatabase.activeLanguage;
                string t = "RecipeMake".Translate("\u0001").ToString();
                int i = t.IndexOf('\u0001');
                verbPrefix = i > 0 ? t.Substring(0, i) : "";
            }
            return verbPrefix;
        }

        // "<prefix> (<qualifier>) <words> x<batch>", e.g. ".408 Cheyenne Tactical (AP-HE) cartridge x200"
        static readonly System.Text.RegularExpressions.Regex qualRx =
            new System.Text.RegularExpressions.Regex(@"^(?<pre>.*?)(?<q>\s\([^()]*\))(?<mid>[^()]*?)(?<batch>\sx\d+)?\z", System.Text.RegularExpressions.RegexOptions.Singleline);
        static readonly System.Text.RegularExpressions.Regex batchRx =
            new System.Text.RegularExpressions.Regex(@"^(?<pre>.*?)(?<batch>\sx\d+)\z", System.Text.RegularExpressions.RegexOptions.Singleline);

        /// <summary>Generic nouns that may be dropped after a "(qualifier)": they say what the item is, not which one.</summary>
        static readonly HashSet<string> genericWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "cartridge", "cartridges", "shell", "shells", "round", "rounds", "magazine", "magazines", "ammo", "ammunition" };

        static readonly Dictionary<string, string> fitCache = new Dictionary<string, string>();
        static LoadedLanguage fitLang;

        /// <summary>
        /// A recipe / bill name that fits <paramref name="width"/>, keeping what tells recipes apart. In order: the whole name; without the
        /// generic verb ("Make "); without a generic noun after the "(qualifier)" or a copy of it before ("cartridge"); with the start cut
        /// ("6mm Railgun... (Sabot) x500"); and only then cut at the end. Cached per (name, width): it runs every frame for every visible row.
        /// </summary>
        public static string FitName(string s, float width)
        {
            if (s.NullOrEmpty()) return s;
            if (width <= 0f) return "";
            if (fitLang != LanguageDatabase.activeLanguage) { fitCache.Clear(); fitLang = LanguageDatabase.activeLanguage; } // the verb prefix is per language
            string key = ((int)width) + "|" + s;
            if (fitCache.TryGetValue(key, out string hit)) return hit;
            GameFont oldFont = Verse.Text.Font;
            Verse.Text.Font = GameFont.Small;
            string result;
            try { result = FitNameCore(s.Replace('\n', ' ').Replace('\r', ' '), width); }
            catch (Exception) { result = s; } // a label must never break the frame
            finally { Verse.Text.Font = oldFont; }
            if (fitCache.Count > 4000) fitCache.Clear();
            fitCache[key] = result;
            return result;
        }

        static string FitNameCore(string s, float width)
        {
            if (Verse.Text.CalcSize(s).x <= width) return s;
            string verb = VerbPrefix();
            if (verb.Length > 0 && s.Length > verb.Length && s.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(verb.Length).CapitalizeFirst();
                if (Verse.Text.CalcSize(s).x <= width) return s;
            }
            string basis = s; // what the final end-cut shortens: the name without the generic words, when we managed to drop them
            System.Text.RegularExpressions.Match m = qualRx.Match(s);
            if (m.Success)
            {
                string pre = m.Groups["pre"].Value, q = m.Groups["q"].Value, batch = m.Groups["batch"].Value;
                string mid = m.Groups["mid"].Value.Trim();
                bool repeat = mid.Length > 0 && pre.Length > mid.Length + 3 && pre.EndsWith(mid, StringComparison.OrdinalIgnoreCase) && pre[pre.Length - mid.Length - 1] == ' ';
                if (repeat) pre = pre.Substring(0, pre.Length - mid.Length).TrimEnd(); // ".30-30 Winchester cartridge (FMJ) cartridge" -> ".30-30 Winchester (FMJ)"
                if (mid.Length > 0 && (repeat || genericWords.Contains(mid))) mid = "";
                string tail = q + (mid.Length > 0 ? " " + mid : "") + batch; // a non-generic word after the qualifier stays: it tells recipes apart
                string noGeneric = pre + tail;
                if (Verse.Text.CalcSize(noGeneric).x <= width) return noGeneric;
                basis = noGeneric;
                if (pre.Trim().Length >= 3)
                {
                    string cut = CutHead(pre, tail, width);
                    // a kept non-generic word is the first thing to go when even the cut start cannot fit with it
                    if (cut == null && mid.Length > 0) cut = CutHead(pre, q + batch, width);
                    if (cut != null) return cut;
                }
            }
            else
            {
                m = batchRx.Match(s);
                if (m.Success && m.Groups["pre"].Value.Trim().Length >= 3)
                {
                    string cut = CutHead(m.Groups["pre"].Value, m.Groups["batch"].Value, width);
                    if (cut != null) return cut;
                }
            }
            string end = basis.Truncate(width);
            return Verse.Text.CalcSize(end).x <= width ? end : "";
        }

        /// <summary>
        /// "head... tail": the longest start of <paramref name="pre"/> that fits (binary search), on a word boundary when that leaves a real
        /// word; null when only a stub would be left (so the caller cuts the end instead).
        /// </summary>
        static string CutHead(string pre, string tail, float width)
        {
            const string Dots = "...";
            int lo = 0, hi = pre.Length - 1; // longest prefix length L (< pre.Length) with width(pre[..L] + dots + tail) <= width
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Verse.Text.CalcSize(pre.Substring(0, mid) + Dots + tail).x <= width) lo = mid; else hi = mid - 1;
            }
            if (lo < 3 || Verse.Text.CalcSize(pre.Substring(0, lo) + Dots + tail).x > width) return null;
            string head = pre.Substring(0, lo);
            if (char.IsHighSurrogate(head[head.Length - 1])) head = head.Substring(0, head.Length - 1);
            string cut = head.TrimEnd(' ', '(', '-');
            bool atBoundary = pre[head.Length] == ' ';
            int sp = head.LastIndexOf(' ');
            string snapped = !atBoundary && sp >= 5 ? head.Substring(0, sp).TrimEnd(' ', '(', '-') : cut;
            // prefer whole words; a long enough mid-word cut beats a stub (".45-70" -> ".45-70 Gove..."); a short whole word is fine
            string pick;
            if (snapped.Length >= 8) pick = snapped;
            else if (cut.Length >= 8) pick = cut;
            else if (atBoundary && cut.Length >= 5) pick = cut;
            else return null; // "WarB... (Incendiary)": a stub of the first word is worse than cutting the end
            return pick.TrimEnd() + Dots + tail;
        }

        /// <summary>Name with <see cref="FitName"/>; the full name is the tooltip only when something was cut.</summary>
        public static void NameFit(Rect r, string s, Color c, TextAnchor a = TextAnchor.MiddleLeft)
        {
            if (s.NullOrEmpty()) return;
            string fit = FitName(s, r.width);
            Text_(r, fit, c, a);
            if (fit != s) TooltipHandler.TipRegion(r, s);
        }

        /// <summary>1234 -> "1.2k", 1500000 -> "1.5M": the last form before a number would be cut.</summary>
        public static string Compact(int n)
        {
            if (Math.Abs(n) >= 1000000) return (n / 1000000f).ToString("0.#") + "M";
            if (Math.Abs(n) >= 10000) return (n / 1000f).ToString("0.#") + "k";
            return n.ToString();
        }

        /// <summary>10000 and above get a thousands separator ("496,000" / "496.000"); smaller numbers stay plain.</summary>
        public static string Thousands(int n)
        {
            if (Math.Abs(n) < 10000) return n.ToString();
            string sep = "BO_ThousandsSep".Translate();
            string d = Math.Abs(n).ToString();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < d.Length; i++)
            {
                if (i > 0 && (d.Length - i) % 3 == 0) sb.Append(sep);
                sb.Append(d[i]);
            }
            return (n < 0 ? "-" : "") + sb;
        }

        /// <summary>Small clickable pill; returns true when clicked. Highlighted when active.</summary>
        public static bool Chip(Rect r, string label, bool active, string tip = null)
        {
            bool over = Mouse.IsOver(r);
            Box(r, active ? Selected : over ? Hover : PanelLight);
            if (active) Widgets.DrawBox(r, 1);
            Text_(r, label, active ? Color.white : new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter);
            // no tooltip while a menu is open: the chip that opened it is still under the mouse and its tip would cover the first menu items
            if (tip != null && Find.WindowStack.WindowOfType<FloatMenu>() == null) TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        public static float ChipWidth(string label) => Mathf.Max(Verse.Text.CalcSize(label).x + 18f, 40f);

        // ------------------------------------------------------------ +/- steps
        // The modifiers are the game's own keys (Options > Keyboard, "increment 10x" / "increment 100x"; vanilla default Ctrl / Shift),
        // the same the vanilla bill dialog uses. Tooltips name the bound keys, so they stay right if the player rebinds them.

        /// <summary>1, 10, 100 or 1000 by the modifier keys held during this event.</summary>
        public static int StepMult() => GenUI.CurrentAdjustmentMultiplier();

        /// <summary>"Ctrl+click x10, Shift+click x100" with the keys the player actually bound.</summary>
        public static string StepKeysTip()
        {
            try { return "BO_StepKeys".Translate(RimWorld.KeyBindingDefOf.ModifierIncrement_10x.MainKeyLabel, RimWorld.KeyBindingDefOf.ModifierIncrement_100x.MainKeyLabel); }
            catch (Exception) { return ""; }
        }

        /// <summary>Next multiple of <paramref name="unit"/> plus <paramref name="step"/> - unit (501 by 500 -> 1000; 500 -> 1000).</summary>
        public static int StepUp(int v, int unit, int step)
        {
            unit = Mathf.Max(unit, 1);
            long r = (long)(Mathf.Max(v, 0) / unit) * unit + step;
            return (int)Math.Min(r, int.MaxValue);
        }

        /// <summary>Previous multiple of <paramref name="unit"/> minus <paramref name="step"/> - unit (501 by 500 -> 500; 1000 -> 500).</summary>
        public static int StepDown(int v, int unit, int step)
        {
            unit = Mathf.Max(unit, 1);
            long up = ((long)Mathf.Max(v, 0) + unit - 1) / unit * unit;
            return (int)Math.Max(up - step, int.MinValue);
        }

        /// <summary>Horizontal bar: value/max filled with color, thin, with the best-of-group case drawn brighter.</summary>
        public static void Bar(Rect r, float value, float max, Color c, bool best)
        {
            Box(r, new Color(1f, 1f, 1f, 0.07f));
            if (max > 0f && value > 0f)
            {
                var fill = new Rect(r.x, r.y, Mathf.Max(2f, r.width * Mathf.Clamp01(value / max)), r.height);
                Box(fill, best ? c : new Color(c.r, c.g, c.b, 0.65f));
            }
        }

        public static string Num(float v, string fmt = "0.#") => v.ToString(fmt);

        public static readonly string None = "-";
    }
}
