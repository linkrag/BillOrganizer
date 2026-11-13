using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace BillOrganizer
{
    /// <summary>What the "Add bill" button of the bill tab does.</summary>
    public enum IntegrationMode
    {
        /// <summary>Our window replaces the vanilla menu (works while no other mod replaces the whole bill tab).</summary>
        Auto,
        /// <summary>Leave "Add bill" alone; only the workbench button opens our window.</summary>
        GizmoOnly,
    }

    public enum GizmoMode
    {
        /// <summary>Show the workbench button only while Nice Bill Tab has replaced the tab (when "Add bill" cannot reach us).</summary>
        WithNice,
        Always,
        Never,
    }

    public class BillOrganizerSettings : ModSettings
    {
        /// <summary>Master switch: false = vanilla "Add bill" menu everywhere, no button.</summary>
        public bool enabled = true;
        public IntegrationMode integration = IntegrationMode.Auto;
        public GizmoMode gizmo = GizmoMode.WithNice;
        /// <summary>Benches with fewer available recipes than this keep the vanilla menu (the workbench button ignores it).</summary>
        public int minRecipes = 12;
        /// <summary>Recipe keys of favorite recipes.</summary>
        public List<string> favorites = new List<string>();
        /// <summary>Recipe keys of the last bills added, newest first.</summary>
        public List<string> recent = new List<string>();
        /// <summary>Keep the window open after adding a bill (Shift does it for a single click).</summary>
        public bool keepOpen;
        /// <summary>Ingredient mini icons under the name in the recipe list.</summary>
        public bool showIngIcons = true;
        /// <summary>Name bills "Recipe (Material)" when a material is picked in the window.</summary>
        public bool autoName;
        /// <summary>"Bills" button on the bottom bar (bench list, queues, templates).</summary>
        public bool showGlobalButton;
        public int winW = 1220, winH = 760;
        /// <summary>Vanilla bill tab with a recipe list and a detail panel next to the queue (off automatically while Nice Bill Tab replaces the tab).</summary>
        public bool compactTab = true;
        /// <summary>Linked benches (shared queue); off keeps existing links but stops syncing and hides the button.</summary>
        public bool linkBenches = true;
        /// <summary>Bench priority also orders the bench types of the same skill (Fabrication x Smithy), not only benches of the same work giver.</summary>
        public bool benchPrioAcross = true;
        /// <summary>"Babies" column in the vanilla auto-slaughter window; babies are protected unless allowed per animal (AutoSlaughterBabies).</summary>
        public bool slaughterBabies = true;
        /// <summary>"Until X" -/+ step = items per craft for every recipe (TargetStepFix rewrites recipe.targetCountAdjustment).</summary>
        public bool stepByBatch = true;
        /// <summary>"Task force" button on the vanilla Work tab (temporary work priority, TaskForce.cs). Last choices of its dialog below.</summary>
        public bool taskForce = true;
        public List<string> tfWorks = new List<string>();
        public int tfHours = 6, tfPrio = 1;
        public bool tfInterrupt = true;
        public int tabH = 620, tabQueue = 250, tabW = 920;
        /// <summary>Pinned resources panel next to the resource readout (Ctrl+click a resource to pin).</summary>
        public bool pinPanel = true, pinCollapsed;
        /// <summary>Own resource list on the left (names, sort, categories, search); resSort 0 category / 1 amount / 2 name.</summary>
        public bool resList = true;
        public int resListW = 200, resSort;
        public List<string> resExpandedList = new List<string>();
        public HashSet<string> resExpanded = new HashSet<string>(); // categories left open; everything else starts collapsed
        public List<string> pinnedRes = new List<string>();
        List<string> pinMinLines = new List<string>(); // "defName|minimum"
        readonly Dictionary<string, int> pinMin = new Dictionary<string, int>();

        public int MinOf(Verse.ThingDef d) => pinMin.TryGetValue(d.defName, out int v) ? v : 0;

        public void SetMin(Verse.ThingDef d, int v)
        {
            if (v > 0) pinMin[d.defName] = v; else pinMin.Remove(d.defName);
            Write();
        }

        public void TogglePinned(Verse.ThingDef d)
        {
            if (!pinnedRes.Remove(d.defName)) pinnedRes.Add(d.defName);
            Write();
        }
        /// <summary>Per bench def: "bench|node|sort|desc|availFirst|onlyStock|cfgMode|cfgQty".</summary>
        List<string> memoLines = new List<string>();
        readonly Dictionary<string, Window_RecipePicker.Memo> memos = new Dictionary<string, Window_RecipePicker.Memo>();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref integration, "integration", IntegrationMode.Auto);
            Scribe_Values.Look(ref gizmo, "gizmo", GizmoMode.WithNice);
            Scribe_Values.Look(ref minRecipes, "minRecipes", 12);
            Scribe_Values.Look(ref keepOpen, "keepOpen", false);
            Scribe_Values.Look(ref showIngIcons, "showIngIcons", true);
            Scribe_Values.Look(ref autoName, "autoName", false);
            Scribe_Values.Look(ref showGlobalButton, "showGlobalButton", false);
            Scribe_Values.Look(ref winW, "winW", 1220);
            Scribe_Values.Look(ref winH, "winH", 760);
            Scribe_Values.Look(ref compactTab, "compactTab", true);
            Scribe_Values.Look(ref linkBenches, "linkBenches", true);
            Scribe_Values.Look(ref benchPrioAcross, "benchPrioAcross", true);
            Scribe_Values.Look(ref slaughterBabies, "slaughterBabies", true);
            Scribe_Values.Look(ref stepByBatch, "stepByBatch", true);
            Scribe_Values.Look(ref taskForce, "taskForce", true);
            Scribe_Collections.Look(ref tfWorks, "tfWorks", LookMode.Value);
            if (tfWorks == null) tfWorks = new List<string>();
            Scribe_Values.Look(ref tfHours, "tfHours", 6);
            Scribe_Values.Look(ref tfPrio, "tfPrio", 1);
            Scribe_Values.Look(ref tfInterrupt, "tfInterrupt", true);
            Scribe_Values.Look(ref tabH, "tabH", 620);
            Scribe_Values.Look(ref tabQueue, "tabQueue", 250);
            Scribe_Values.Look(ref tabW, "tabW", 920);
            Scribe_Values.Look(ref pinPanel, "pinPanel", true);
            Scribe_Values.Look(ref pinCollapsed, "pinCollapsed", false);
            Scribe_Values.Look(ref resList, "resList", true);
            Scribe_Values.Look(ref resListW, "resListW", 200);
            Scribe_Values.Look(ref resSort, "resSort", 0);
            if (Scribe.mode == LoadSaveMode.Saving) resExpandedList = resExpanded.ToList();
            Scribe_Collections.Look(ref resExpandedList, "resExpanded", LookMode.Value);
            if (resExpandedList == null) resExpandedList = new List<string>();
            if (Scribe.mode == LoadSaveMode.LoadingVars) resExpanded = new HashSet<string>(resExpandedList);
            Scribe_Collections.Look(ref pinnedRes, "pinnedRes", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.Saving) pinMinLines = pinMin.Select(kv => kv.Key + "|" + kv.Value).ToList();
            Scribe_Collections.Look(ref pinMinLines, "pinMin", LookMode.Value);
            if (pinnedRes == null) pinnedRes = new List<string>();
            if (pinMinLines == null) pinMinLines = new List<string>();
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                foreach (string line in pinMinLines)
                {
                    string[] p = line.Split('|');
                    if (p.Length == 2 && int.TryParse(p[1], out int v) && v > 0) pinMin[p[0]] = v;
                }
            Scribe_Collections.Look(ref favorites, "favorites", LookMode.Value);
            Scribe_Collections.Look(ref recent, "recent", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                memoLines = memos.Select(kv => string.Join("|", kv.Key, kv.Value.node, kv.Value.sort, kv.Value.desc ? "1" : "0", kv.Value.availFirst ? "1" : "0", kv.Value.onlyStock ? "1" : "0",
                    kv.Value.cfgMode.ToString(), kv.Value.cfgQty.ToString(), kv.Value.cfgAuto ? "1" : "0")).ToList();
            }
            Scribe_Collections.Look(ref memoLines, "memos", LookMode.Value);
            if (favorites == null) favorites = new List<string>();
            if (recent == null) recent = new List<string>();
            if (memoLines == null) memoLines = new List<string>();
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                foreach (string line in memoLines)
                {
                    string[] p = line.Split('|');
                    if (p.Length < 6) continue;
                    var m = new Window_RecipePicker.Memo { node = p[1], sort = p[2], desc = p[3] == "1", availFirst = p[4] == "1", onlyStock = p[5] == "1" };
                    if (p.Length >= 8) { int.TryParse(p[6], out m.cfgMode); int.TryParse(p[7], out m.cfgQty); if (m.cfgQty < 1) m.cfgQty = 1; }
                    m.cfgAuto = p.Length < 9 || p[8] == "1"; // older lines: the value may be a batch we set, let the next recipe replace it
                    memos[p[0]] = m;
                }
            }
        }

        public Window_RecipePicker.Memo GetMemo(string bench)
        {
            if (!memos.TryGetValue(bench, out Window_RecipePicker.Memo m)) memos[bench] = m = new Window_RecipePicker.Memo();
            return m;
        }

        public void SaveMemo(string bench, Window_RecipePicker.Memo m) { memos[bench] = m; }
    }

    public class BillOrganizerMod : Mod
    {
        public static BillOrganizerSettings Settings;
        static Vector2 settingsScroll;

        public BillOrganizerMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<BillOrganizerSettings>();
            var harmony = new Harmony("linkrag.billorganizer");
            harmony.PatchAll();
            ResourcePanel.TryPatch(harmony);
            if (NiceCompat.Installed)
                Log.Message("[BillOrganizer] Nice Bill Tab is active. While its tab replacement is on, 'Add bill' belongs to it and this window opens from the workbench button instead (mod settings).");
        }

        public override string SettingsCategory() => "Colony Organizer";

        public override void WriteSettings()
        {
            base.WriteSettings();
            GlobalButton.Apply();
            AutoSlaughterBabies.DirtyAllMaps();
            if (Current.Game != null) TargetStepFix.Apply();
        }

        float settingsH = 1500f; // content height of the previous frame (Listing_Standard only knows it after drawing)

        /// <summary>Faint section title with a line under it, so ~25 options read as five groups.</summary>
        static void Section(Listing_Standard l, string key)
        {
            l.Gap(8f);
            GUI.color = new Color(1f, 0.77f, 0.27f, 0.9f);
            l.Label(key.Translate().ToString().ToUpperInvariant());
            GUI.color = Color.white;
            l.GapLine(4f);
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // many options: scroll instead of cutting them off; the height follows what was drawn
            var view = new Rect(0f, 0f, inRect.width - 20f, settingsH);
            Widgets.BeginScrollView(inRect, ref settingsScroll, view);
            // maxOneColumn: without it, content taller than the view wraps into a 2nd column off to the right
            // (clipped, invisible) and CurHeight resets, so settingsH never grows back after a section expands
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(view);

            Section(l, "BO_SecGeneral");
            l.CheckboxLabeled("BO_SetEnabled".Translate(), ref Settings.enabled, "BO_SetEnabledTip".Translate());
            l.Gap(6f);
            l.Label("BO_SetIntegration".Translate());
            if (l.RadioButton("BO_SetIntAuto".Translate(), Settings.integration == IntegrationMode.Auto, 16f, "BO_SetIntAutoTip".Translate()))
                Settings.integration = IntegrationMode.Auto;
            if (l.RadioButton("BO_SetIntGizmo".Translate(), Settings.integration == IntegrationMode.GizmoOnly, 16f, "BO_SetIntGizmoTip".Translate()))
                Settings.integration = IntegrationMode.GizmoOnly;
            if (NiceCompat.Installed)
            {
                // right under the options it explains
                GUI.color = new Color(1f, 0.85f, 0.4f);
                l.Label((NiceCompat.TabReplaced ? "BO_SetNiceOn" : "BO_SetNiceOff").Translate());
                GUI.color = Color.white;
            }
            l.Gap(6f);
            l.Label("BO_SetGizmo".Translate());
            if (l.RadioButton("BO_SetGizmoNice".Translate(), Settings.gizmo == GizmoMode.WithNice, 16f)) Settings.gizmo = GizmoMode.WithNice;
            if (l.RadioButton("BO_SetGizmoAlways".Translate(), Settings.gizmo == GizmoMode.Always, 16f)) Settings.gizmo = GizmoMode.Always;
            if (l.RadioButton("BO_SetGizmoNever".Translate(), Settings.gizmo == GizmoMode.Never, 16f)) Settings.gizmo = GizmoMode.Never;
            l.Gap(6f);
            l.Label("BO_SetMin".Translate(Settings.minRecipes), -1f, "BO_SetMinTip".Translate().ToString());
            Settings.minRecipes = Mathf.RoundToInt(l.Slider(Settings.minRecipes, 0, 60));

            Section(l, "BO_SecWindow");
            l.CheckboxLabeled("BO_SetKeepOpen".Translate(), ref Settings.keepOpen, "BO_SetKeepOpenTip".Translate());
            l.CheckboxLabeled("BO_SetIngIcons".Translate(), ref Settings.showIngIcons);
            l.CheckboxLabeled("BO_SetAutoName".Translate(), ref Settings.autoName, "BO_SetAutoNameTip".Translate());
            l.Label("BO_SetWinW".Translate(Settings.winW));
            Settings.winW = Mathf.RoundToInt(l.Slider(Settings.winW, 900, 1800) / 10f) * 10;
            l.Label("BO_SetWinH".Translate(Settings.winH));
            Settings.winH = Mathf.RoundToInt(l.Slider(Settings.winH, 500, 1100) / 10f) * 10;

            Section(l, "BO_SecTab");
            l.CheckboxLabeled("BO_SetCompactTab".Translate(), ref Settings.compactTab, "BO_SetCompactTabTip".Translate());
            if (Settings.compactTab)
            {
                l.Label("BO_SetTabW".Translate(Settings.tabW));
                Settings.tabW = Mathf.RoundToInt(l.Slider(Settings.tabW, 700, 1200) / 10f) * 10;
                l.Label("BO_SetTabH".Translate(Settings.tabH));
                Settings.tabH = Mathf.RoundToInt(l.Slider(Settings.tabH, 480, 900) / 10f) * 10;
                l.Label("BO_SetTabQueue".Translate(Settings.tabQueue));
                Settings.tabQueue = Mathf.RoundToInt(l.Slider(Settings.tabQueue, 150, 500) / 10f) * 10;
            }

            Section(l, "BO_SecBenches");
            l.CheckboxLabeled("BO_SetLink".Translate(), ref Settings.linkBenches, "BO_SetLinkTip".Translate());
            bool across = Settings.benchPrioAcross;
            l.CheckboxLabeled("BO_SetBenchPrio".Translate(), ref Settings.benchPrioAcross, "BO_SetBenchPrioTip".Translate());
            if (across != Settings.benchPrioAcross) BenchOrder.Invalidate();
            l.CheckboxLabeled("BO_SetStepByBatch".Translate(), ref Settings.stepByBatch, "BO_SetStepByBatchTip".Translate());
            l.CheckboxLabeled("BO_SetGlobalButton".Translate(), ref Settings.showGlobalButton, "BO_SetGlobalButtonTip".Translate());

            Section(l, "BO_SecAnimals");
            l.CheckboxLabeled("BO_SetSlBabies".Translate(), ref Settings.slaughterBabies, "BO_SetSlBabiesTip".Translate());

            Section(l, "BO_SecWork");
            l.CheckboxLabeled("BO_SetTaskForce".Translate(), ref Settings.taskForce, "BO_SetTaskForceTip".Translate());

            Section(l, "BO_SecResources");
            l.CheckboxLabeled("BO_SetResList".Translate(), ref Settings.resList, "BO_SetResListTip".Translate());
            if (Settings.resList)
            {
                l.Label("BO_SetResListW".Translate(Settings.resListW));
                Settings.resListW = Mathf.RoundToInt(l.Slider(Settings.resListW, 160, 420) / 10f) * 10;
                l.Label("BO_SetResSort".Translate());
                string[] sortKeys = { "BO_ResSortCat", "BO_ResSortQty", "BO_ResSortName" };
                for (int i = 0; i < sortKeys.Length; i++)
                    if (l.RadioButton(sortKeys[i].Translate(), Settings.resSort == i, 16f, "BO_ResSortTip".Translate())) { Settings.resSort = i; ResourceList.MarkDirty(); }
            }
            l.CheckboxLabeled("BO_SetPinPanel".Translate(), ref Settings.pinPanel, "BO_SetPinPanelTip".Translate());

            if (Event.current.type == EventType.Repaint) settingsH = Mathf.Max(l.CurHeight + 12f, inRect.height);
            l.End();
            Widgets.EndScrollView();
        }
    }
}
