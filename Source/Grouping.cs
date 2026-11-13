using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BillOrganizer
{
    /// <summary>A node of the group tree on the left of the picker.</summary>
    public class Node
    {
        public string id, label;
        public int order;
        public Node parent;
        public readonly List<Node> children = new List<Node>();
        public int depth, count;
    }

    /// <summary>
    /// Where each recipe goes in the tree. Weapons and apparel are fixed groups; ammunition (Combat Extended: by caliber) and
    /// everything else (by the product's thing category: meals, drugs, medicine, resources...) are built from the game's own
    /// ThingCategoryDefs, so any mod's items land somewhere sensible without a list to maintain.
    /// </summary>
    public static class Grouping
    {
        public const string All = "all", Fav = "fav", Recent = "recent", MiscId = "misc";

        static string T(string key) => key.Translate().ToString();

        public static void Misc(RecipeEntry e) => e.path.Add(new NodeKey(MiscId, T("BO_NodeOther"), 900));

        public static void Weapon(RecipeEntry e, string sub)
        {
            e.path.Add(new NodeKey("weapons", T("BO_NodeWeapons"), 10));
            switch (sub)
            {
                case "melee": e.path.Add(new NodeKey("melee", T("BO_NodeMelee"), 11)); break;
                case "ranged": e.path.Add(new NodeKey("ranged", T("BO_NodeRanged"), 12)); break;
                case "grenade": e.path.Add(new NodeKey("grenade", T("BO_NodeGrenades"), 13)); break;
                default: break; // the caller adds its own node (thrown, a mod's weapon category)
            }
        }

        public static void Apparel(RecipeEntry e, string slot)
        {
            e.path.Add(new NodeKey("apparel", T("BO_NodeApparel"), 30));
            switch (slot)
            {
                case "head": e.path.Add(new NodeKey("head", T("BO_NodeHead"), 31)); break;
                case "torso": e.path.Add(new NodeKey("torso", T("BO_NodeTorso"), 32)); break;
                case "legs": e.path.Add(new NodeKey("legs", T("BO_NodeLegs"), 33)); break;
                case "hands": e.path.Add(new NodeKey("hands", T("BO_NodeHands"), 34)); break;
                case "feet": e.path.Add(new NodeKey("feet", T("BO_NodeFeet"), 35)); break;
                default: e.path.Add(new NodeKey("acc", T("BO_NodeAccessories"), 36)); break;
            }
        }

        /// <summary>
        /// Ammunition: Ammunition > family (rifles, pistols...) > caliber, from the first category under CE's "Ammo" (not just the
        /// first category). Non-CE ammo keeps its own category (Rimatomics sabot rounds) under an "a:" id: Build() shares node ids
        /// across the whole tree, so a "c:" id would merge with an Other node of the same category.
        /// </summary>
        public static void Ammo(RecipeEntry e, ThingDef d)
        {
            e.path.Add(new NodeKey("ammo", T("BO_NodeAmmo"), 20));
            if (d.thingCategories != null)
                foreach (ThingCategoryDef c in d.thingCategories)
                {
                    List<ThingCategoryDef> chain = ChainOf(c);
                    int ai = chain.FindIndex(x => x.defName == "Ammo");
                    if (ai <= 0) continue;
                    for (int i = ai - 1; i >= 0 && i >= ai - 2; i--)
                        e.path.Add(new NodeKey("c:" + chain[i].defName, chain[i].LabelCap, 0));
                    return;
                }
            ThingCategoryDef leaf = d.thingCategories?.FirstOrDefault();
            if (leaf != null && leaf.defName != "MortarShells" && leaf.parent?.parent != null)
                e.path.Add(new NodeKey("a:" + leaf.defName, leaf.LabelCap, 0));
            else
                e.path.Add(new NodeKey("a:shells", T("BO_NodeShells"), 0));
        }

        /// <summary>
        /// Everything else: top-level thing category > its child (Foods > Meals, Items > Manufactured...). A first category that sits
        /// right under the root gives way to a deeper one; an "Apparel"/"Weapons" top (magic artifacts that are not apparel) is dropped
        /// so it never makes a second Apparel group; "...Misc" gets its child too.
        /// </summary>
        public static void Other(RecipeEntry e, ThingDef d)
        {
            ThingCategoryDef cat = d?.thingCategories?.FirstOrDefault();
            if (cat == null) { Misc(e); return; }
            if (cat.parent != null && cat.parent.parent == null)
            {
                ThingCategoryDef deeper = d.thingCategories.Where(c => c != null).OrderByDescending(Depth).FirstOrDefault();
                if (deeper != null && Depth(deeper) > Depth(cat)) cat = deeper;
            }
            List<ThingCategoryDef> chain = ChainOf(cat);
            if (chain.Count > 0 && (chain[chain.Count - 1].defName == "Apparel" || chain[chain.Count - 1].defName == "Weapons")) chain.RemoveAt(chain.Count - 1);
            if (chain.Count == 0) { Misc(e); return; }
            ThingCategoryDef top = chain[chain.Count - 1];
            e.path.Add(new NodeKey("c:" + top.defName, top.LabelCap, 100));
            if (chain.Count >= 2)
            {
                ThingCategoryDef second = chain[chain.Count - 2];
                e.path.Add(new NodeKey("c:" + second.defName, second.LabelCap, 0));
                if (chain.Count >= 3 && second.defName.EndsWith("Misc"))
                    e.path.Add(new NodeKey("c:" + chain[0].defName, chain[0].LabelCap, 0));
            }
        }

        /// <summary>
        /// Recipes without a product: butchering, smelting, destroying, burning, cremating (Processing), mech resurrection
        /// (Mechanoids), anything else (Other). Never by defName or worker class: modded upgrades would land in Processing.
        /// </summary>
        public static void NoProduct(RecipeEntry e)
        {
            RecipeDef r = e.recipe;
            if (r.mechResurrection) { e.path.Add(new NodeKey("pawn:mech", T("BO_NodeMechanoids"), 700)); return; }
            // a RecipeDef subclass with no product at all is a mod's special recipe (A Rimworld of Magic rituals: MagicRecipeDef)
            if (r.GetType() != typeof(RecipeDef) && r.products.NullOrEmpty() && r.specialProducts.NullOrEmpty())
            { e.path.Add(new NodeKey("special", T("BO_NodeRituals"), 850)); return; }
            List<ThingDef> defs = r.fixedIngredientFilter != null ? r.fixedIngredientFilter.AllowedThingDefs.ToList() : new List<ThingDef>();
            // "stone blocks from any chunk" (specialProducts = butcher products): grouped with what the chunks become
            if (!r.specialProducts.NullOrEmpty() && defs.Count > 0 && defs.All(t => !t.IsCorpse && !t.butcherProducts.NullOrEmpty()))
            {
                List<ThingDef> prods = defs.SelectMany(t => t.butcherProducts).Select(c => c.thingDef).Where(x => x != null).Distinct().ToList();
                if (prods.Count > 0 && prods.Select(p => p.thingCategories?.FirstOrDefault()).Distinct().Count() == 1) { Other(e, prods[0]); return; }
            }
            bool processing = !r.specialProducts.NullOrEmpty();
            if (!processing && r.products.NullOrEmpty() && !r.ingredients.NullOrEmpty())
                processing = defs.Count > 0 && defs.All(ProcIn);
            if (processing) e.path.Add(new NodeKey("proc", T("BO_NodeProcessing"), 800));
            else Misc(e);
        }

        /// <summary>
        /// Something a bench destroys, smelts or butchers. Category membership counts too: Combat Extended puts minified turrets
        /// under Weapons, A Rimworld of Magic puts gemstones under Apparel, and "destroy weapon" must still be Processing.
        /// </summary>
        static bool ProcIn(ThingDef t)
        {
            Cats();
            return t.IsCorpse || t.IsWeapon || t.IsApparel || t.IsDrug || t.isTechHediff || IsAmmo(t)
                || In(t, catWeapons) || In(t, catApparel) || In(t, catCorpses) || In(t, catBodyParts);
        }

        /// <summary>
        /// A recipe with products that takes apart ammo, weapons or apparel, or gives back exactly what an item cost (recycling
        /// charged ammo, dismantling a warhead): Processing, not the group of the steel it returns. Corpses are left out on purpose.
        /// </summary>
        public static bool IsSalvage(RecipeDef r)
        {
            if (r.products.NullOrEmpty() || r.fixedIngredientFilter == null) return false;
            Cats();
            List<ThingDef> ins = r.fixedIngredientFilter.AllowedThingDefs.ToList();
            if (ins.Count == 0) return false;
            List<ThingDef> outs = r.products.Select(p => p.thingDef).Where(x => x != null).ToList();
            return ins.All(t => !t.IsCorpse && (IsAmmo(t) || t.IsWeapon || t.IsApparel || In(t, catWeapons) || In(t, catApparel)
                || (t.costList != null && outs.Count > 0 && outs.All(p => t.costList.Any(c => c.thingDef == p)))));
        }

        public static void Processing(RecipeEntry e) => e.path.Add(new NodeKey("proc", T("BO_NodeProcessing"), 800));

        /// <summary>
        /// Ranged weapons that are not guns, as siblings of Grenades: thrown weapons (one-use verb or a CE AmmoDef, never a "Gun"
        /// tag: knives, rocks, axes, javelins) and weapons from a mod's own child of the Weapons category (A Rimworld of Magic foci).
        /// Null = an ordinary ranged weapon.
        /// </summary>
        public static NodeKey? RangedSpecial(ThingDef d)
        {
            bool gun = d.weaponTags != null && d.weaponTags.Any(t => t.StartsWith("Gun", StringComparison.OrdinalIgnoreCase) || t == "HeavyGun");
            if (!gun)
            {
                string verb = d.Verbs?.FirstOrDefault()?.verbClass?.Name ?? "";
                if (verb.IndexOf("OneUse", StringComparison.OrdinalIgnoreCase) >= 0 || d.GetType().Name == "AmmoDef")
                    return new NodeKey("thrown", T("BO_NodeThrown"), 14);
            }
            if (d.thingCategories != null)
                foreach (ThingCategoryDef c in d.thingCategories)
                {
                    ThingCategoryDef x = c;
                    while (x?.parent != null && x.parent.defName != "Weapons") x = x.parent;
                    if (x?.parent == null) continue;
                    if (x.defName == "WeaponsRanged" || x.defName == "WeaponsMelee" || x.defName == "Grenades") continue;
                    return new NodeKey("wc:" + x.defName, x.LabelCap, 15);
                }
            return null;
        }

        /// <summary>Mech gestation and other recipes that make a pawn.</summary>
        public static void Pawn(RecipeEntry e, ThingDef d)
        {
            if (d.race != null && d.race.IsMechanoid) e.path.Add(new NodeKey("pawn:mech", T("BO_NodeMechanoids"), 700));
            else e.path.Add(new NodeKey("pawn:creature", T("BO_NodeCreatures"), 710));
        }

        static int Depth(ThingCategoryDef c) { int n = 0; for (ThingCategoryDef x = c; x?.parent != null; x = x.parent) n++; return n; }

        /// <summary>The category and its parents, leaf first, root excluded.</summary>
        static List<ThingCategoryDef> ChainOf(ThingCategoryDef c)
        {
            var list = new List<ThingCategoryDef>();
            if (c == null) return list;
            if (c.parent != null) list.Add(c);
            foreach (ThingCategoryDef p in c.Parents) if (p.parent != null) list.Add(p);
            return list;
        }

        public static bool IsAmmo(ThingDef d)
        {
            if (d.IsShell) return true;
            if (d.thingCategories == null) return false;
            foreach (ThingCategoryDef c in d.thingCategories)
            {
                if (c.defName.StartsWith("Ammo") || c.defName == "MortarShells") return true;
                foreach (ThingCategoryDef par in c.Parents)
                    if (par.defName.StartsWith("Ammo") || par.defName == "MortarShells") return true;
            }
            return false;
        }

        static ThingCategoryDef catWeapons, catRanged, catGrenades, catApparel, catCorpses, catBodyParts;
        static bool catsDone;
        static void Cats()
        {
            if (catsDone) return;
            catsDone = true;
            catWeapons = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
            catRanged = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("WeaponsRanged");
            catGrenades = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Grenades");
            catApparel = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Apparel");
            catCorpses = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Corpses");
            catBodyParts = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("BodyParts");
        }

        static bool In(ThingDef d, ThingCategoryDef c) => c != null && !d.thingCategories.NullOrEmpty() && d.IsWithinCategory(c);

        /// <summary>
        /// A weapon for the tree: drinks and logs have melee "tools" too (VBE lemonade, beer), so ingestibles and stuff are out, and a
        /// categorized item must be under Weapons or Grenades. Grenades are tested on their own: More Useful Grenade moves the
        /// "Grenades" category under Manufactured, out of Weapons.
        /// </summary>
        public static bool IsWeaponProduct(ThingDef d)
        {
            if (!d.IsWeapon || d.IsIngestible || d.IsStuff) return false;
            Cats();
            return d.thingCategories.NullOrEmpty() || In(d, catWeapons) || In(d, catGrenades);
        }

        /// <summary>
        /// Thrown weapon: by the Grenades category first (tear gas and modded throwables have no "Grenade" tag), launchers in
        /// WeaponsRanged stay ranged (CE grenade launcher, hand mortar), and the tag only decides for items with no such category.
        /// </summary>
        public static bool IsGrenade(ThingDef d)
        {
            Cats();
            if (In(d, catGrenades)) return true;
            if (In(d, catRanged)) return false;
            if (d.weaponTags == null) return false;
            bool grenade = false;
            foreach (string t in d.weaponTags)
            {
                if (t.StartsWith("Gun", StringComparison.OrdinalIgnoreCase)) return false;
                if (t.StartsWith("Grenade", StringComparison.OrdinalIgnoreCase)) grenade = true;
            }
            return grenade;
        }

        // ------------------------------------------------------------ third level (only where a node is long, see RecipeEntry.Build)

        static bool HasTag(ThingDef d, params string[] tags) => d.weaponTags != null && d.weaponTags.Any(t => tags.Contains(t));
        static bool TagStarts(ThingDef d, params string[] pre) => d.weaponTags != null && d.weaponTags.Any(t => pre.Any(p => t.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
        static NodeKey Sub(string id, string key, int order) => new NodeKey(id, T(key), order);

        /// <summary>
        /// Kind of an ordinary ranged weapon (audited on the in-game dump, 5/nov): tags first (CE AI tags, vanilla weapon tags),
        /// then Combat Extended data read at runtime (ammo family, magazine, burst), then the tech level; rifle when nothing matches.
        /// </summary>
        public static NodeKey RangedSub(ThingDef d)
        {
            string verb = d.Verbs?.FirstOrDefault()?.verbClass?.Name ?? "";
            int burst = d.Verbs?.FirstOrDefault()?.burstShotCount ?? 1;
            CeCompat.AmmoFamily(d, out ThingCategoryDef ammoCat, out ThingCategoryDef fam, out int mag);
            string f = fam?.defName ?? "";
            if (verb.IndexOf("MarkForArtillery", StringComparison.OrdinalIgnoreCase) >= 0 || HasTag(d, "CE_FlareLauncher")) return Sub("rc:utility", "BO_RcUtility", 59);
            if (HasTag(d, "NonLethal")) return Sub("rc:nonlethal", "BO_RcNonLethal", 58);
            if (HasTag(d, "CE_MachineGun", "CE_AI_LMG", "Minigun", "CE_AI_Suppressive", "Bipod_LMG", "Bipod_SAW")) return Sub("rc:mg", "BO_RcMG", 55);
            if (HasTag(d, "GunHeavy", "HeavyGun", "GunSingleUse", "GunGrenadeLauncher", "CE_AI_Launcher", "Flamethrower")
                || (HasTag(d, "CE_AI_AOE") && !HasTag(d, "CE_AI_AR"))
                || f == "AmmoGrenades" || f == "AmmoRockets" || f == "AmmoMissiles" || f == "AmmoShells") return Sub("rc:heavy", "BO_RcHeavy", 56);
            if (HasTag(d, "CE_Bow", "CE_XBow") || TagStarts(d, "Neolithic", "VFEM2_Medieval", "Classical", "VikingRanged")
                || f == "AmmoNeolithic" || f == "AmmoMedieval" || d.techLevel <= TechLevel.Medieval) return Sub("rc:primitive", "BO_RcPrimitive", 57);
            if (HasTag(d, "CE_AI_SR", "Bipod_AMR", "Bipod_DMR", "Bipod_ATR") || (HasTag(d, "SniperRifle") && !HasTag(d, "AssaultRifle", "CE_AI_AR"))) return Sub("rc:sniper", "BO_RcSniper", 54);
            if (HasTag(d, "Revolver", "Autopistol", "ModernHandgun", "CE_AI_Pistol", "CWF_Handgun")) return Sub("rc:pistol", "BO_RcPistol", 50);
            if (HasTag(d, "PumpShotgun", "CWF_Shotgun") || f == "AmmoShotguns" || (ammoCat != null && ammoCat.defName.IndexOf("Gauge", StringComparison.OrdinalIgnoreCase) >= 0)) return Sub("rc:shotgun", "BO_RcShotgun", 52);
            if (HasTag(d, "CE_SMG", "CWF_SMG") || (f == "AmmoPistols" && (burst > 1 || mag >= 20))) return Sub("rc:smg", "BO_RcSMG", 51);
            if (HasTag(d, "CE_Sidearm", "CE_OneHandedWeapon")) return Sub("rc:pistol", "BO_RcPistol", 50);
            if (mag >= 40 && (f == "AmmoRifles" || f == "AmmoHighCaliber")) return Sub("rc:mg", "BO_RcMG", 55);
            return Sub("rc:rifle", "BO_RcRifle", 53);
        }

        static HashSet<string> Layers(ThingDef d) => new HashSet<string>((d.apparel?.layers ?? new List<ApparelLayerDef>()).Where(l => l != null).Select(l => l.defName));
        static HashSet<string> Groups(ThingDef d) => new HashSet<string>((d.apparel?.bodyPartGroups ?? new List<BodyPartGroupDef>()).Where(g => g != null).Select(g => g.defName));
        static bool InCat(ThingDef d, string cat) => d.thingCategories != null && d.thingCategories.Any(c => c.defName == cat || c.Parents.Any(p => p.defName == cat));

        /// <summary>Head: helmets / face and neck (masks, glasses, collars) / hats and hoods.</summary>
        public static NodeKey HeadSub(ThingDef d)
        {
            string p = d.defName.ToLowerInvariant();
            if (new[] { "mask", "veil", "glasses", "goggle", "nightvision", "gag" }.Any(p.Contains)) return Sub("hs:face", "BO_HsFace", 62);
            if (InCat(d, "ArmorHeadgear") || p.Contains("helm")) return Sub("hs:helmet", "BO_HsHelmet", 61);
            HashSet<string> G = Groups(d), L = Layers(d);
            if (!G.Contains("UpperHead") && (!G.Contains("FullHead") || L.All(l => l == "StrappedHead"))) return Sub("hs:face", "BO_HsFace", 62);
            return Sub("hs:hat", "BO_HsHat", 63);
        }

        /// <summary>Torso: armor / outerwear (shell layer) / clothes.</summary>
        public static NodeKey TorsoSub(ThingDef d)
        {
            string p = d.defName.ToLowerInvariant();
            if (InCat(d, "ApparelArmor") || new[] { "armor", "armour", "mail", "plate", "dragonslayer" }.Any(p.Contains)) return Sub("ts:armor", "BO_TsArmor", 71);
            HashSet<string> L = Layers(d);
            if ((L.Contains("Shell") || L.Contains("VFEC_OuterShell")) && !L.Contains("OnSkin")) return Sub("ts:outer", "BO_TsOuter", 72);
            return Sub("ts:clothes", "BO_TsClothes", 73);
        }

        static readonly System.Text.RegularExpressions.Regex TierSuffix = new System.Text.RegularExpressions.Regex(
            @"_(I|II|III|IV|V|VI|VII|VIII|IX|X|minor|major|lesser|greater|Mk\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Family of tiered products (AIRobot_..._Hauler, _II.._V; TM_EStone_x_minor/x/_major): the defName without the tier suffix.</summary>
        public static string FamilyKey(ThingDef d) => d == null ? null : TierSuffix.Replace(d.defName, "");

        /// <summary>
        /// Third level of long nodes, per bench: the subgroup (ranged kind, head/torso kind) when the node has more than 15 entries and
        /// at least two kinds; otherwise, in nodes with more than 12 entries, families of tiered products (2+ products each, 2+
        /// families, none covering the whole node). A single-child level is never created.
        /// </summary>
        public static void AddThirdLevel(List<RecipeEntry> list)
        {
            foreach (var g in list.Where(e => e.path.Count > 0).GroupBy(e => e.path[e.path.Count - 1].id).ToList())
            {
                List<RecipeEntry> members = g.ToList();
                if (members.Count > 15 && members.All(e => e.sub != null) && members.Select(e => e.sub.Value.id).Distinct().Count() >= 2)
                {
                    foreach (RecipeEntry e in members) e.path.Add(e.sub.Value);
                    continue;
                }
                if (members.Count <= 12) continue;
                var fams = members.Where(e => e.product != null).GroupBy(e => FamilyKey(e.product))
                    .Where(f => f.Select(e => e.product).Distinct().Count() >= 2).ToList();
                if (fams.Count < 2 || fams.Any(f => f.Count() == members.Count)) continue;
                string leaf = g.Key;
                List<string> labels = fams.Select(f => f.Select(e => e.product).OrderBy(x => x.defName.Length).First().LabelCap.ToString()).ToList();
                labels = FamilyLabels(labels);
                for (int i = 0; i < fams.Count; i++)
                {
                    var key = new NodeKey("fam:" + leaf + ":" + fams[i].Key, labels[i], 0);
                    foreach (RecipeEntry e in fams[i]) e.path.Add(key);
                }
            }
        }

        static readonly System.Text.RegularExpressions.Regex TierMark = new System.Text.RegularExpressions.Regex(
            @"\s*\((I|II|III|IV|V|VI|VII|VIII|IX|X|\d+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Short family names for the tree: the words every family shares at the start or the end are dropped ("Arcane Gem of Force"
        /// -> "Force", "Builder(I) Base Station" -> "Builder"), and so is a tier mark like "(I)". Falls back to the full name when
        /// nothing would be left or two names would become equal.
        /// </summary>
        static List<string> FamilyLabels(List<string> full)
        {
            List<string> clean = full.Select(s => TierMark.Replace(s, "").Trim()).ToList();
            List<string[]> words = clean.Select(s => s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).ToList();
            int min = words.Min(w => w.Length);
            int pre = 0, suf = 0;
            while (pre < min - 1 && words.All(w => string.Equals(w[pre], words[0][pre], StringComparison.OrdinalIgnoreCase))) pre++;
            while (suf < min - 1 - pre && words.All(w => string.Equals(w[w.Length - 1 - suf], words[0][words[0].Length - 1 - suf], StringComparison.OrdinalIgnoreCase))) suf++;
            List<string> res = words.Select(w => string.Join(" ", w.Skip(pre).Take(w.Length - pre - suf).ToArray()).CapitalizeFirst()).ToList();
            if (res.Any(s => s.NullOrEmpty()) || res.Distinct(StringComparer.OrdinalIgnoreCase).Count() < res.Count) res = clean;
            if (res.Any(s => s.NullOrEmpty()) || res.Distinct(StringComparer.OrdinalIgnoreCase).Count() < res.Count) res = full;
            return res;
        }

        /// <summary>The tree for these entries: Favorites, Recent and All first, then weapons, ammo, apparel, the dynamic groups and Other.</summary>
        public static List<Node> Build(List<RecipeEntry> entries)
        {
            var byId = new Dictionary<string, Node>();
            var roots = new List<Node>();

            Node Special(string id, string key, int order)
            {
                var n = new Node { id = id, label = T(key), order = order, count = entries.Count(x => x.InNode(id)) };
                byId[id] = n; roots.Add(n);
                return n;
            }
            Special(Fav, "BO_NodeFavorites", -3);
            Special(Recent, "BO_NodeRecent", -2);
            Special(All, "BO_NodeAll", -1);

            foreach (RecipeEntry e in entries)
            {
                Node parent = null;
                foreach (NodeKey k in e.path)
                {
                    if (!byId.TryGetValue(k.id, out Node n))
                    {
                        n = new Node { id = k.id, label = k.label, order = k.order, parent = parent };
                        byId[k.id] = n;
                        (parent == null ? roots : parent.children).Add(n);
                    }
                    n.count++;
                    parent = n;
                }
            }

            Comparison<Node> cmp = (a, b) =>
            {
                int o = (a.order == 0 ? 500 : a.order).CompareTo(b.order == 0 ? 500 : b.order);
                if (o != 0) return o;
                int c = b.count.CompareTo(a.count);
                return c != 0 ? c : string.Compare(a.label, b.label, StringComparison.CurrentCultureIgnoreCase);
            };
            roots.Sort(cmp);
            foreach (Node n in byId.Values) n.children.Sort(cmp);
            SetDepth(roots, 0);
            return roots;
        }

        static void SetDepth(List<Node> nodes, int d)
        {
            foreach (Node n in nodes) { n.depth = d; SetDepth(n.children, d + 1); }
        }

        public static IEnumerable<Node> Flatten(List<Node> roots, HashSet<string> expanded)
        {
            foreach (Node n in roots)
            {
                if (n.id != Fav && n.id != Recent && n.count == 0) continue;
                yield return n;
                if (n.children.Count > 0 && expanded.Contains(n.id))
                    foreach (Node c in Flatten(n.children, expanded)) yield return c;
            }
        }

        /// <summary>
        /// Dev mode: one line per recipe in the Player.log with the group it got and the data the rules read (kind, path, apparel
        /// layers / body parts, weapon tags, thing categories), so a wrong group can be fixed by rule instead of guessed.
        /// </summary>
        public static void DumpGroups(IEnumerable<RecipeEntry> entries, string bench)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[BillOrganizer] Groups for ").Append(bench).Append(" (defName | product | kind | path | layers | bodyParts | weaponTags | categories | verb | techLevel | recipeClass | sub | ceAmmo)");
            foreach (RecipeEntry e in entries)
            {
                try
                {
                    ThingDef d = e.product;
                    string J<T>(IEnumerable<T> xs, Func<T, string> f) => xs == null ? "" : string.Join(",", xs.Select(f).ToArray());
                    sb.Append(Environment.NewLine).Append(e.recipe.defName).Append(" | ").Append(d?.defName ?? "-").Append(" | ").Append(e.kind)
                      .Append(" | ").Append(string.Join(" > ", e.path.Select(k => k.label).ToArray()))
                      .Append(" | ").Append(J(d?.apparel?.layers, l => l.defName))
                      .Append(" | ").Append(J(d?.apparel?.bodyPartGroups, g => g.defName))
                      .Append(" | ").Append(J(d?.weaponTags, t => t))
                      .Append(" | ").Append(J(d?.thingCategories, c => c.defName))
                      .Append(" | ").Append(d?.Verbs?.FirstOrDefault()?.verbClass?.Name ?? "")
                      .Append(" | ").Append(d != null ? d.techLevel.ToString() : "")
                      .Append(" | ").Append(e.recipe.GetType().Name)
                      .Append(" | ").Append(e.sub?.label ?? "")
                      .Append(" | ").Append(CeAmmoText(d));
                }
                catch (Exception ex) { sb.Append(Environment.NewLine).Append(e.recipe?.defName).Append(" | error: ").Append(ex.Message); }
            }
            Log.Message(sb.ToString());
            Messages.Message("BO_DevDumpDone".Translate(), RimWorld.MessageTypeDefOf.SilentInput, false);
        }

        static string CeAmmoText(ThingDef d)
        {
            if (d == null || !d.IsRangedWeapon) return "";
            CeCompat.AmmoFamily(d, out ThingCategoryDef cat, out ThingCategoryDef fam, out int mag);
            return cat == null ? "" : cat.defName + "/" + (fam?.defName ?? "-") + " mag " + mag;
        }

        public static Node Find(List<Node> roots, string id)
        {
            foreach (Node n in roots)
            {
                if (n.id == id) return n;
                Node c = Find(n.children, id);
                if (c != null) return c;
            }
            return null;
        }
    }
}
