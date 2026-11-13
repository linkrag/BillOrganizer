using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace BillOrganizer
{
    /// <summary>A named copy of the bills of one bench. Stored in its own file (not in the save, not in ModSettings), so it is shared by every save.</summary>
    public class BillTemplate : IExposable, IRenameable
    {
        public string name, benchDef;
        public List<Bill> bills = new List<Bill>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref benchDef, "benchDef");
            Scribe_Collections.Look(ref bills, "bills", LookMode.Deep);
            if (bills == null) bills = new List<Bill>();
        }

        public string RenamableLabel { get => name; set => name = value; }
        public string BaseLabel => name;
        public string InspectLabel => name;
    }

    /// <summary>
    /// Template storage. The file is read lazily the first time it is needed (defs exist by then; the mod constructor runs before them),
    /// with the game's own Scribe so bills keep every setting. References to things of a save (pawn restriction, stockpile, ideology style...)
    /// are cleared when saving a template; bills whose recipe no longer exists are dropped when loading.
    /// </summary>
    public static class Templates
    {
        static List<BillTemplate> list;
        static readonly List<BillTemplate> empty = new List<BillTemplate>();
        static string FilePath => Path.Combine(GenFilePaths.ConfigFolderPath, "BillOrganizer_Templates.xml");

        public static List<BillTemplate> All { get { Ensure(); return list ?? empty; } }

        static void Ensure()
        {
            if (list != null || Scribe.mode != LoadSaveMode.Inactive) return; // while the game loads/saves, try again later
            List<BillTemplate> loaded = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    Scribe.loader.InitLoading(FilePath);
                    try { Scribe_Collections.Look(ref loaded, "templates", LookMode.Deep); }
                    finally { if (Scribe.mode == LoadSaveMode.LoadingVars) Scribe.loader.FinalizeLoading(); }
                }
                catch (Exception e)
                {
                    Log.Error("[BillOrganizer] Could not read the bill templates (" + FilePath + "): " + e);
                    if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                    // the next save would overwrite the unreadable file with an empty list: keep a copy first
                    try { File.Copy(FilePath, FilePath + ".bak", true); Log.Warning("[BillOrganizer] A copy of the unreadable file was kept as " + FilePath + ".bak"); }
                    catch (Exception e2) { Log.Warning("[BillOrganizer] Could not back up the templates file: " + e2.Message); }
                    loaded = null;
                }
            }
            list = loaded ?? new List<BillTemplate>();
            list.RemoveAll(t => t == null);
            int dropped = 0;
            foreach (BillTemplate t in list) dropped += t.bills.RemoveAll(b => b == null || b.recipe == null);
            if (dropped > 0) Log.Warning("[BillOrganizer] " + dropped + " template bill(s) use recipes that no longer exist and were dropped.");
        }

        static bool Write()
        {
            if (list == null || Scribe.mode != LoadSaveMode.Inactive) return false;
            try
            {
                SafeSaver.Save(FilePath, "BillOrganizerTemplates", () => Scribe_Collections.Look(ref list, "templates", LookMode.Deep));
                return true;
            }
            catch (Exception e) { Log.Error("[BillOrganizer] Could not write the bill templates: " + e); return false; }
        }

        static readonly string[] saveRefs = { "pawnRestriction", "precept", "xenogerm", "boundUft", "includeGroup", "includeFromZone", "storeZone" };

        /// <summary>Does this field hold something that only exists inside one save (a thing, pawn, zone, stockpile, precept...)?</summary>
        static bool IsSaveRef(FieldInfo f)
        {
            Type t = f.FieldType;
            if (t.IsValueType || t == typeof(string) || typeof(Def).IsAssignableFrom(t) || typeof(BillStack).IsAssignableFrom(t)) return false;
            if (Array.IndexOf(saveRefs, f.Name) >= 0) return true;
            return typeof(Thing).IsAssignableFrom(t) || typeof(ILoadReferenceable).IsAssignableFrom(t) || typeof(ISlotGroup).IsAssignableFrom(t) || typeof(Zone).IsAssignableFrom(t);
        }

        /// <summary>Clears the references that only make sense inside one save (every reference-type field of the bill class chain, not a fixed list).</summary>
        static void Scrub(Bill b)
        {
            for (Type t = b.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    try { if (IsSaveRef(f)) f.SetValue(b, null); }
                    catch (Exception e) { Log.WarningOnce("[BillOrganizer] Could not clear " + t.Name + "." + f.Name + " in a template bill: " + e.Message, t.Name.GetHashCode() ^ f.Name.GetHashCode()); }
                }
            }
            try { if (b is Bill_Production bp && bp.GetStoreMode() == BillStoreModeDefOf.SpecificStockpile) bp.SetStoreMode(BillStoreModeDefOf.BestStockpile, null); }
            catch { }
        }

        /// <summary>Saves the queue of a bench as a template (same name + bench replaces). Returns an error text or null.</summary>
        public static string Save(string name, ThingDef bench, BillStack stack, out int count)
        {
            count = 0;
            Ensure();
            if (list == null) return "BO_TplBusy".Translate();
            var t = new BillTemplate { name = name, benchDef = bench.defName };
            foreach (Bill b in stack.Bills)
            {
                try { Bill c = b.Clone(); Scrub(c); t.bills.Add(c); }
                catch (Exception e) { Log.Warning("[BillOrganizer] Could not copy a bill (" + b.recipe?.defName + ") into the template: " + e.Message); }
            }
            list.RemoveAll(x => x.name == name && x.benchDef == t.benchDef);
            list.Add(t);
            count = t.bills.Count;
            return Write() ? null : "BO_TplWriteFail".Translate().ToString();
        }

        public static void Delete(BillTemplate t) { if (list != null && list.Remove(t)) Write(); }

        public static void Rename(BillTemplate t, string name) { t.name = name; Write(); }

        public static int Compatible(BillTemplate t, Thing bench) => t.bills.Count(b => Fits(b, bench));

        static bool Fits(Bill b, Thing bench) => bench != null && b.recipe != null && bench.def.AllRecipes.Contains(b.recipe) && b.recipe.AvailableNow && b.recipe.AvailableOnNow(bench);

        /// <summary>Pastes what fits (recipe available on this bench) and stops at the stack limit. Reports what was left out.</summary>
        public static string Paste(BillTemplate t, BillStack stack)
        {
            var bench = stack.billGiver as Thing;
            int added = 0, noRecipe = 0, full = 0;
            foreach (Bill b in t.bills)
            {
                if (!Fits(b, bench)) { noRecipe++; continue; }
                if (stack.Count >= BillStack.MaxCount) { full++; continue; }
                try { Bill c = b.Clone(); c.InitializeAfterClone(); stack.AddBill(c); added++; }
                catch (Exception e) { Log.Warning("[BillOrganizer] Could not paste a bill (" + b.recipe.defName + "): " + e.Message); noRecipe++; }
            }
            if (added > 0) SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            string s = "BO_TplPasted".Translate(added, t.name);
            if (noRecipe > 0) s += " " + "BO_TplSkippedRecipe".Translate(noRecipe);
            if (full > 0) s += " " + "BO_TplSkippedFull".Translate(full);
            return s;
        }

        // ------------------------------------------------------------ "Copy queue": an unnamed, in-memory template of one queue
        static BillTemplate queueClip;
        static Game queueClipGame;   // a copied queue only pastes in the game it came from (its bills may point at its stockpiles/pawns)
        static Bill queueClipMark;   // the vanilla clipboard when the queue was copied: a later single-bill copy replaces it, so Paste takes the newest

        /// <summary>Copies the whole queue (clones, references kept like the vanilla copy). Returns how many bills.</summary>
        public static int CopyQueue(Thing bench, BillStack stack)
        {
            var t = new BillTemplate { name = bench.LabelCap, benchDef = bench.def.defName };
            foreach (Bill b in stack.Bills)
            {
                try { t.bills.Add(b.Clone()); }
                catch (Exception e) { Log.Warning("[BillOrganizer] Could not copy a bill (" + b.recipe?.defName + "): " + e.Message); }
            }
            queueClip = t;
            queueClipGame = Current.Game;
            queueClipMark = Patch_BillTab.Clipboard;
            return t.bills.Count;
        }

        /// <summary>Pastes the copied queue when it is newer than the vanilla single-bill clipboard. False = paste the single bill instead.</summary>
        /// <summary>The copied queue is still the newest copy (no single bill was copied after it) and belongs to this game.</summary>
        public static bool HasQueueClip => queueClip != null && queueClipGame == Current.Game && Patch_BillTab.Clipboard == queueClipMark;

        /// <summary>Bills of the copied queue that fit on this bench (0 = none), and how many were copied.</summary>
        public static int QueueClipFit(Thing bench, out int total)
        {
            total = queueClip?.bills.Count ?? 0;
            return HasQueueClip && bench != null ? queueClip.bills.Count(b => b?.recipe != null && bench.def.AllRecipes.Contains(b.recipe) && b.recipe.AvailableNow && b.recipe.AvailableOnNow(bench)) : 0;
        }

        public static bool TryPasteQueue(BillStack stack, out string message)
        {
            message = null;
            if (!HasQueueClip) return false;
            message = Paste(queueClip, stack);
            return true;
        }

        public static void OpenPasteMenu(BillStack stack, Action<string> say)
        {
            var bench = stack.billGiver as Thing;
            var opts = new List<FloatMenuOption>();
            foreach (BillTemplate t0 in All.OrderByDescending(x => x.benchDef == bench?.def.defName).ThenBy(x => x.name))
            {
                BillTemplate t = t0;
                int fit = Compatible(t, bench);
                string label = fit == 0 ? "BO_TplNoFit".Translate(t.name).ToString() : "BO_TplFit".Translate(t.name, fit, t.bills.Count).ToString();
                opts.Add(new FloatMenuOption(label, fit == 0 ? (Action)null : () => say(Paste(t, stack))));
            }
            opts.Add(new FloatMenuOption("BO_TplManage".Translate(), () => Find.WindowStack.Add(new Window_Templates())));
            Find.WindowStack.Add(new FloatMenu(opts));
        }
    }

    /// <summary>Small "type a name" dialog.</summary>
    public class Dialog_NameTemplate : Window
    {
        readonly Action<string> ok;
        string text;
        bool focus = true;

        public override Vector2 InitialSize => new Vector2(340f, 160f);

        public Dialog_NameTemplate(string defaultName, Action<string> ok)
        {
            this.ok = ok; text = defaultName ?? "";
            doCloseX = true; absorbInputAroundWindow = true; closeOnClickedOutside = true; forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Verse.Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 24f), "BO_TplNamePrompt".Translate());
            GUI.SetNextControlName("BO_NameField");
            text = Widgets.TextField(new Rect(0f, 30f, inRect.width, 28f), text);
            if (focus) { GUI.FocusControl("BO_NameField"); focus = false; }
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if ((Widgets.ButtonText(new Rect(0f, inRect.height - 34f, inRect.width, 32f), "OK".Translate()) || enter) && !text.NullOrEmpty() && text.Trim().Length > 0)
            {
                if (enter) Event.current.Use();
                ok(text.Trim());
                Close();
            }
        }
    }

    /// <summary>Lists the templates: paste is done from a bench; here you rename and delete.</summary>
    public class Window_Templates : Window
    {
        Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(520f, 420f);

        public Window_Templates()
        {
            doCloseX = true; draggable = true; absorbInputAroundWindow = false; forcePause = false; closeOnClickedOutside = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Verse.Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "BO_TplTitle".Translate());
            Verse.Text.Font = GameFont.Small;
            List<BillTemplate> all = Templates.All.ToList();
            Ui.Text_(new Rect(0f, 34f, inRect.width, 22f), "BO_TplWindowHint".Translate(), Ui.Faint, TextAnchor.MiddleLeft, GameFont.Small, true);
            var outR = new Rect(0f, 60f, inRect.width, inRect.height - 60f);
            if (all.Count == 0) { Ui.Text_(outR, "BO_TplNone".Translate(), Ui.Faint, TextAnchor.MiddleCenter, GameFont.Small, true); return; }
            var view = new Rect(0f, 0f, outR.width - 16f, all.Count * 36f);
            Widgets.BeginScrollView(outR, ref scroll, view);
            for (int i = 0; i < all.Count; i++)
            {
                BillTemplate t = all[i];
                var row = new Rect(0f, i * 36f, view.width, 34f);
                Ui.Box(row, i % 2 == 0 ? Ui.PanelLight : Ui.Panel);
                ThingDef bench = DefDatabase<ThingDef>.GetNamedSilentFail(t.benchDef);
                // buttons sized to their text ("Renomear" is wider than "Rename"); the name takes what is left
                Verse.Text.Font = GameFont.Small;
                string renL = "BO_TplRename".Translate(), delL = "BO_TplDelete".Translate();
                float delW = Ui.ChipWidth(delL), renW = Ui.ChipWidth(renL);
                var del = new Rect(row.xMax - delW - 4f, row.y + 5f, delW, 24f);
                var ren = new Rect(del.x - renW - 4f, row.y + 5f, renW, 24f);
                float textW = ren.x - 14f;
                Ui.TextFit(new Rect(8f, row.y, textW, 22f), t.name, Color.white);
                Ui.TextFit(new Rect(8f, row.y + 16f, textW, 18f), (bench != null ? bench.LabelCap.ToString() : t.benchDef) + " - " + "BO_TplBills".Translate(t.bills.Count), Ui.Faint);
                if (Ui.Chip(ren, renL, false))
                    Find.WindowStack.Add(new Dialog_NameTemplate(t.name, n => Templates.Rename(t, n)));
                if (Ui.Chip(del, delL, false))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("BO_TplConfirmDelete".Translate(t.name), () => Templates.Delete(t), true));
            }
            Widgets.EndScrollView();
        }
    }
}
