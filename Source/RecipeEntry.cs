using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BillOrganizer
{
    public enum Kind { Melee, Ranged, Grenade, Ammo, Apparel, Other }

    public enum Stock { Unknown, Ready, Missing }

    /// <summary>What the detail panel asks for when adding: repeat mode (0 = recipe default, 1 do X, 2 until X, 3 forever), quantity, copies, one material.</summary>
    public class AddOptions
    {
        public int mode, qty = 1, times = 1;
        /// <summary>qty was set by us (memo.cfgAuto), not by the player: "until X" then uses one batch of THIS recipe (the row "+" adds
        /// before the detail has recomputed the memo for the new recipe, so qty may still be the previous recipe's batch).</summary>
        public bool qtyAuto;
        public ThingDef stuff;
    }

    /// <summary>One step of an entry's group path (top level first), used to build the tree on the left.</summary>
    public struct NodeKey
    {
        public string id, label;
        public int order;
        public NodeKey(string id, string label, int order) { this.id = id; this.label = label; this.order = order; }
    }

    /// <summary>One apparel material and the armor the item gets from it.</summary>
    public class MaterialRow
    {
        public ThingDef stuff;
        public float sharp, blunt, heat, cold;
    }

    /// <summary>One ingredient line of a recipe, with the stock of the map (filled by <see cref="Availability"/>).</summary>
    public class IngInfo
    {
        public IngredientCount ing;
        public float need;        // in items, as the vanilla bill tooltip shows it
        public float needBase;    // in the recipe's ingredient units (volume or nutrition); stock is compared in these
        public List<ThingDef> defs = new List<ThingDef>();
        public ThingDef rep;      // icon shown for it: the allowed thing with most stock, else the first one
        public float haveBase;
        public int batches = -1;  // whole batches this line alone allows with the stock; -1 = not known
        public bool known;        // false: nothing in the filter is a counted resource (weapons, apparel...)
        public bool single;       // one material is needed (stuff of the product), not a mix
        public bool Ok => !known || haveBase + 0.0001f >= needBase;
        public int Have => needBase > 0f ? Mathf.FloorToInt(haveBase * need / needBase) : (int)haveBase;
    }

    /// <summary>One recipe the bench can make right now, with its group path, the stats the picker shows and the stock state.</summary>
    public class RecipeEntry
    {
        public RecipeDef recipe;
        public Precept_ThingStyle precept;
        public ThingDef product;
        /// <summary>Third-level group this entry would take if its node is long at this bench (Grouping.AddThirdLevel).</summary>
        public NodeKey? sub;
        public string label;
        public string modName;
        public Kind kind;
        public int order;
        public string key;
        public readonly List<NodeKey> path = new List<NodeKey>();

        // NaN = the stat does not apply to this item
        public float range = float.NaN, damage = float.NaN, burst = float.NaN, dps = float.NaN, pen = float.NaN, mag = float.NaN, users = float.NaN;
        // apparel: "best" is the best allowed material (or the only value); "Min" the worst
        public float sharp = float.NaN, blunt = float.NaN, heat = float.NaN, cold = float.NaN;
        public float sharpMin = float.NaN, bluntMin = float.NaN, heatMin = float.NaN, coldMin = float.NaN;

        // production: work in the recipe's work units, highest minimum skill level, market value of the product
        public float work = float.NaN, minSkill = float.NaN, value = float.NaN;

        public string ammoSetLabel;
        public string ammoClassLabel;
        public List<AmmoOpt> ammoOpts;
        public int ammoIdx;
        float dmgMult = 1f, cycleSeconds, burstCount = 1f;

        public List<MaterialRow> materials;
        bool materialsDone;

        public List<IngInfo> ings = new List<IngInfo>();
        public Stock stock;
        /// <summary>How many batches of the recipe the stock allows (-1 = unknown), the line that limits it, and the items that makes.</summary>
        public int canMake = -1, makeItems = -1;
        public IngInfo makeLimiter;
        public bool? skillOk;

        /// <summary>Items one batch yields (the count of the first product, 1 if the recipe has none).</summary>
        public int ItemsPerBatch => recipe.products != null && recipe.products.Count > 0 ? Mathf.Max(recipe.products[0].count, 1) : 1;
        /// <summary>
        /// Step of the "until X" target: the items one craft yields (200 for a CE x200 cartridge recipe, 5 for 81mm shells x5).
        /// recipe.targetCountAdjustment is only the fallback for recipes without products: CE puts 20 on every ammo recipe
        /// (AmmoRecipeBase), which made "until X" start and step at 20 for a x5 recipe.
        /// </summary>
        public int TargetStep => TargetStepOf(recipe);

        /// <summary>Same as <see cref="TargetStep"/> for any recipe (the queue card has a Bill, not an entry).</summary>
        public static int TargetStepOf(RecipeDef r)
        {
            if (r == null) return 1;
            if (r.products != null && r.products.Count > 0) return Mathf.Max(r.products[0].count, 1);
            return Mathf.Max(r.targetCountAdjustment, 1);
        }

        /// <summary>An "until X" target on whole batches: rounded up to a multiple of the step, never below one batch.</summary>
        public static int SnapTarget(int v, int step)
        {
            step = Mathf.Max(step, 1);
            long up = ((long)Mathf.Max(v, 0) + step - 1) / step * step;
            return (int)Math.Min(Math.Max(up, step), int.MaxValue / step * step);
        }

        /// <summary>Sets an "until X" target and moves "unpause when you have" by the same change, like Dialog_BillConfig.</summary>
        public static void SetTarget(Bill_Production bp, int target)
        {
            int old = bp.targetCount;
            bp.targetCount = target;
            bp.unpauseWhenYouHave = Mathf.Clamp(bp.unpauseWhenYouHave + (target - old), 0, Mathf.Max(target - 1, 0));
        }

        /// <summary>
        /// The entry of a queued bill: same RecipeDef, else same defName, else same product (a bill whose recipe is not offered by the
        /// bench right now, or a duplicate def from another mod). Null when none matches.
        /// </summary>
        public static RecipeEntry ForBill(List<RecipeEntry> list, Bill b)
        {
            if (list == null || b?.recipe == null) return null;
            RecipeDef r = b.recipe;
            RecipeEntry e = list.FirstOrDefault(x => x.recipe == r && x.precept == null) ?? list.FirstOrDefault(x => x.recipe == r)
                ?? list.FirstOrDefault(x => x.recipe.defName == r.defName);
            ThingDef p = r.ProducedThingDef;
            if (e == null && p != null) e = list.FirstOrDefault(x => x.product == p);
            return e;
        }

        /// <summary>
        /// Click on a queued bill whose recipe the bench does not offer now (locked research, removed mod, unavailable def): its
        /// details are never shown (the lists only hold what can be made), the player gets the reason instead.
        /// </summary>
        public static void SayUnavailable(Bill b)
        {
            string name = b?.recipe != null ? b.recipe.LabelCap.ToString() : "?";
            Messages.Message("BO_RecipeUnavailable".Translate(name), MessageTypeDefOf.RejectInput, false);
        }

        static readonly Dictionary<RecipeDef, List<MaterialRow>> matCache = new Dictionary<RecipeDef, List<MaterialRow>>();

        public bool IsFavorite => BillOrganizerMod.Settings.favorites.Contains(key);
        public bool IsRecent => BillOrganizerMod.Settings.recent.Contains(key);
        public bool IsWeapon => kind == Kind.Melee || kind == Kind.Ranged || kind == Kind.Grenade;

        public void ToggleFavorite()
        {
            List<string> f = BillOrganizerMod.Settings.favorites;
            if (!f.Remove(key)) f.Add(key);
            BillOrganizerMod.Settings.Write();
        }

        public bool InNode(string id)
        {
            switch (id)
            {
                case Grouping.All: return true;
                case Grouping.Fav: return IsFavorite;
                case Grouping.Recent: return IsRecent;
            }
            for (int i = 0; i < path.Count; i++) if (path[i].id == id) return true;
            return false;
        }

        // ---------------------------------------------------------------- build

        public static List<RecipeEntry> Build(BillStack stack)
        {
            var bench = stack.billGiver as Thing;
            if (bench == null) return null;
            var list = new List<RecipeEntry>();
            List<RecipeDef> recipes = bench.def.AllRecipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef r = recipes[i];
                if (!r.AvailableNow || !r.AvailableOnNow(bench)) continue;
                list.Add(Make(r, null, r.LabelCap, i));
                // same as vanilla: one extra entry per ideology building style that makes this product
                if (Faction.OfPlayer?.ideos != null)
                {
                    foreach (Ideo ideo in Faction.OfPlayer.ideos.AllIdeos)
                        foreach (Precept_Building pb in ideo.cachedPossibleBuildings)
                            if (pb.ThingDef == r.ProducedThingDef)
                                list.Add(Make(r, pb, "RecipeMake".Translate(pb.LabelCap).CapitalizeFirst(), i));
                }
            }
            Grouping.AddThirdLevel(list);
            // two mods with the same item name ("lab coat", "gas mask", "backpack"): show the mod so the rows can be told apart
            foreach (var g in list.GroupBy(x => (x.label ?? "").ToLowerInvariant()).Where(g => g.Count() > 1 && g.Select(x => x.modName).Distinct().Count() > 1))
                foreach (RecipeEntry x in g)
                    if (!x.modName.NullOrEmpty()) x.label = x.label + " (" + x.modName + ")";
            return list;
        }

        static RecipeEntry Make(RecipeDef r, Precept_ThingStyle precept, string label, int index)
        {
            ThingDef p = r.ProducedThingDef;
            var e = new RecipeEntry
            {
                recipe = r, precept = precept, product = p, label = label, order = index,
                modName = (p?.modContentPack ?? r.modContentPack)?.Name ?? "",
                key = r.defName + (precept != null ? "#" + precept.ThingDef.defName : ""),
            };
            try { e.Classify(); }
            catch (Exception ex)
            {
                e.kind = Kind.Other; e.path.Clear(); Grouping.Misc(e);
                Log.Warning("[BillOrganizer] Could not classify " + r.defName + ": " + ex.Message);
            }
            try { e.BuildIngredients(); }
            catch (Exception ex) { Log.Warning("[BillOrganizer] Could not read the ingredients of " + r.defName + ": " + ex.Message); }
            try { e.BuildProduction(); }
            catch (Exception ex) { Log.Warning("[BillOrganizer] Could not read the work/skill of " + r.defName + ": " + ex.Message); }
            return e;
        }

        void BuildProduction()
        {
            float w = recipe.WorkAmountTotal((Thing)null);
            if (w > 0f) work = w;
            if (!recipe.skillRequirements.NullOrEmpty()) minSkill = recipe.skillRequirements.Max(s => s.minLevel);
            if (product != null)
            {
                float v = product.GetStatValueAbstract(StatDefOf.MarketValue, GenStuff.DefaultStuffFor(product));
                if (v > 0f) value = v;
            }
        }

        // ---------------------------------------------------------------- search

        string hayAll, hayCat, hayLayer, hayMod, hayBonus;

        void BuildHay()
        {
            var cat = new List<string>();
            var layer = new List<string>();
            var all = new List<string> { label, modName };
            if (product != null)
            {
                all.Add(product.label);
                if (product.thingCategories != null) foreach (ThingCategoryDef c in product.thingCategories) { cat.Add(c.label); cat.Add(c.defName); }
                if (product.stuffCategories != null) foreach (StuffCategoryDef s in product.stuffCategories) all.Add(s.label);
                ApparelProperties ap = product.apparel;
                if (ap != null)
                {
                    foreach (ApparelLayerDef l in ap.layers) { layer.Add(l.label); layer.Add(l.defName); }
                    foreach (BodyPartGroupDef g in ap.bodyPartGroups) { layer.Add(g.label); layer.Add(g.defName); }
                    if (ap.tags != null) foreach (string t in ap.tags) layer.Add(t);
                }
                if (product.researchPrerequisites != null) foreach (ResearchProjectDef p in product.researchPrerequisites) all.Add(p.label);
            }
            if (recipe.researchPrerequisite != null) all.Add(recipe.researchPrerequisite.label);
            hayBonus = Bonus.Hay(product); // bonus stats, abilities, hediffs ("+bandwidth", "+banda")
            all.AddRange(cat); all.AddRange(layer); all.Add(hayBonus);
            hayAll = string.Join("\n", all.Where(s => !s.NullOrEmpty())).ToLowerInvariant();
            hayCat = string.Join("\n", cat.Where(s => !s.NullOrEmpty())).ToLowerInvariant();
            hayLayer = string.Join("\n", layer.Where(s => !s.NullOrEmpty())).ToLowerInvariant();
            hayMod = (modName ?? "").ToLowerInvariant();
        }

        /// <summary>
        /// Space separated terms, all must match. "@x" matches categories, "%x" apparel layers / body parts / tags, "#x" the mod,
        /// "+x" the bonuses (stats when worn/equipped, abilities, hediffs, drug effects); a plain term matches all of these plus the name,
        /// materials and research.
        /// </summary>
        public bool MatchesSearch(string[] terms)
        {
            if (hayAll == null) { try { BuildHay(); } catch (Exception ex) { hayAll = hayCat = hayLayer = hayMod = (label ?? "").ToLowerInvariant(); hayBonus = ""; Log.Warning("[BillOrganizer] Search text failed for " + recipe.defName + ": " + ex.Message); } }
            foreach (string t in terms)
            {
                string hay = hayAll, term = t;
                if (t.Length > 1 && (t[0] == '@' || t[0] == '%' || t[0] == '#' || t[0] == '+'))
                {
                    hay = t[0] == '@' ? hayCat : t[0] == '%' ? hayLayer : t[0] == '+' ? hayBonus ?? "" : hayMod;
                    term = t.Substring(1);
                }
                if (hay.IndexOf(term, StringComparison.Ordinal) < 0) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- classification + stats

        void Classify()
        {
            // the def that decides the group: the product, or the first product of a recipe with several (a gun that also gives back
            // wood and steel). Stats only run with a real product; a guessed one only places the recipe in the tree.
            ThingDef d = product ?? (recipe.specialProducts.NullOrEmpty() && recipe.products != null && recipe.products.Count > 1 ? recipe.products[0].thingDef : null);
            kind = Kind.Other;
            if (d == null) { Grouping.NoProduct(this); return; }
            if (d.race != null) { Grouping.Pawn(this, d); return; }   // mech gestation: pawns have no thing categories
            if (d.IsApparel)
            {
                string slot = ApparelSlot(d);
                if (product != null) { kind = Kind.Apparel; ClassifyApparel(); }
                else Grouping.Apparel(this, slot);
                if (slot == "head") sub = Grouping.HeadSub(d);
                else if (slot == "torso") sub = Grouping.TorsoSub(d);
                return;
            }
            if (Grouping.IsAmmo(d))
            {
                Grouping.Ammo(this, d);
                if (product != null) { kind = Kind.Ammo; AmmoStats(); }
                return;
            }
            if (Grouping.IsWeaponProduct(d))
            {
                string sub = !d.IsRangedWeapon ? "melee" : Grouping.IsGrenade(d) ? "grenade" : "ranged";
                NodeKey? special = sub == "ranged" ? Grouping.RangedSpecial(d) : null;
                if (special != null)
                {
                    path.Add(new NodeKey("weapons", "BO_NodeWeapons".Translate().ToString(), 10));
                    path.Add(special.Value);
                }
                else
                {
                    Grouping.Weapon(this, sub);
                    if (sub == "ranged") this.sub = Grouping.RangedSub(d);
                }
                if (product == null) return;
                if (sub == "melee") { kind = Kind.Melee; MeleeStats(); }
                else { kind = sub == "grenade" ? Kind.Grenade : Kind.Ranged; RangedStats(); }
                return;
            }
            if (Grouping.IsSalvage(recipe)) { Grouping.Processing(this); return; }
            Grouping.Other(this, d);
        }

        void AmmoStats()
        {
            ammoClassLabel = CeCompat.AmmoClassLabel(product);
            List<ThingDef> u = CeCompat.Users(product);
            if (u != null) users = u.Count;
        }

        void MeleeStats()
        {
            ThingDef stuff = GenStuff.DefaultStuffFor(product);
            dps = product.GetStatValueAbstract(StatDefOf.MeleeWeapon_AverageDPS, stuff);
            StatDef avgPen = DefDatabase<StatDef>.GetNamedSilentFail("MeleeWeapon_AverageArmorPenetration");
            pen = CeCompat.MeleePenSharp(product) ?? (avgPen != null ? product.GetStatValueAbstract(avgPen, stuff) : float.NaN);
            if (product.tools.NullOrEmpty()) return;
            float wSum = 0f, dmg = 0f;
            foreach (Tool t in product.tools)
            {
                float w = Mathf.Max(t.chanceFactor, 0.0001f);
                wSum += w; dmg += w * t.power;
            }
            if (wSum > 0f) damage = dmg / wSum;
        }

        void RangedStats()
        {
            VerbProperties v = product.Verbs?.FirstOrDefault(x => x.isPrimary) ?? product.Verbs?.FirstOrDefault();
            if (v == null) return;
            range = v.range;
            burst = burstCount = Mathf.Max(v.burstShotCount, 1);
            dmgMult = product.GetStatValueAbstract(StatDefOf.RangedWeapon_DamageMultiplier);
            float cooldown = product.GetStatValueAbstract(StatDefOf.RangedWeapon_Cooldown);
            cycleSeconds = v.warmupTime + cooldown + Mathf.Max(burstCount - 1f, 0f) * v.ticksBetweenBurstShots / 60f;

            ammoOpts = CeCompat.AmmoOptions(product, out int magazine, out ammoSetLabel);
            if (ammoOpts != null && ammoOpts.Count > 0)
            {
                if (magazine > 0) mag = magazine;
                ApplyAmmo(0);
            }
            else
            {
                ammoOpts = null;
                if (v.defaultProjectile?.projectile != null)
                    SetShot(v.defaultProjectile.projectile.GetDamageAmount((Thing)null), v.defaultProjectile.projectile.GetArmorPenetration((Thing)null));
            }
        }

        /// <summary>Picks the ammo type a CE gun is evaluated with (damage, penetration, DPS).</summary>
        public void ApplyAmmo(int i)
        {
            if (ammoOpts == null || i < 0 || i >= ammoOpts.Count) return;
            ammoIdx = i;
            SetShot(ammoOpts[i].damage, ammoOpts[i].penSharp);
        }

        void SetShot(float baseDamage, float penetration)
        {
            if (baseDamage <= 0f) { damage = pen = dps = float.NaN; return; }
            damage = baseDamage * dmgMult;
            pen = penetration;
            dps = cycleSeconds > 0.01f ? damage * burstCount / cycleSeconds : float.NaN;
        }

        void ClassifyApparel()
        {
            ThingDef stuff = GenStuff.DefaultStuffFor(product);
            sharp = sharpMin = product.GetStatValueAbstract(StatDefOf.ArmorRating_Sharp, stuff);
            blunt = bluntMin = product.GetStatValueAbstract(StatDefOf.ArmorRating_Blunt, stuff);
            heat = heatMin = product.GetStatValueAbstract(StatDefOf.ArmorRating_Heat, stuff);
            cold = coldMin = product.GetStatValueAbstract(StatDefOf.Insulation_Cold, stuff);
            Grouping.Apparel(this, ApparelSlot(product));
        }

        static readonly HashSet<string> AccLayers = new HashSet<string> { "Belt", "Webbing", "Backpack", "Shield", "Accessory" };
        static readonly HashSet<string> BodyLayers = new HashSet<string> { "OnSkin", "Middle", "Shell" };
        static readonly HashSet<string> HeadLayers = new HashSet<string> { "Overhead", "EyeCover", "OnHead", "StrappedHead" };
        static readonly HashSet<string> HeadGroups = new HashSet<string> { "UpperHead", "FullHead", "Eyes", "Teeth", "Mouth" };

        /// <summary>
        /// head / torso / legs / hands / feet / acc, from layers and covered body part groups (audited against Core, DLCs, CE, CE Armory,
        /// VAE, VFE and other active mods, 5/nov). Neck is NOT a head part (flak jacket, parka, duster and most armor cover Torso + Neck),
        /// Torso wins over head parts, and accessory layers only win when the item has no body layer (CE composite armor has Webbing too).
        /// </summary>
        static string ApparelSlot(ThingDef d)
        {
            ApparelProperties ap = d?.apparel;
            if (ap == null) return "acc";
            var L = new HashSet<string>();
            var G = new HashSet<string>();
            if (ap.layers != null) foreach (ApparelLayerDef l in ap.layers) if (l != null) L.Add(l.defName);
            if (ap.bodyPartGroups != null) foreach (BodyPartGroupDef g in ap.bodyPartGroups) if (g != null) G.Add(g.defName);
            bool body = L.Overlaps(BodyLayers);
            if (L.Overlaps(AccLayers) && !body) return "acc";                    // belts, packs, webbing, CE shields, tactical vest
            if (G.Contains("Torso")) return "torso";                             // flak jacket, parka, duster, armor, vacsuit, burka
            if (G.Overlaps(HeadGroups) || L.Overlaps(HeadLayers)) return "head"; // helmets, hats, masks, glasses; the slave collar (Overhead)
            if (body && (G.Contains("Shoulders") || G.Contains("Arms"))) return "torso"; // shoulder pads
            if (G.Contains("Legs")) return "legs";
            if (G.Contains("Hands") || (G.Contains("LeftHand") && G.Contains("RightHand"))) return "hands";
            if (G.Contains("Feet")) return "feet";
            return "acc";                                                        // neck amulets, waist items, bracers, shields, one ring
        }

        /// <summary>Armor with every allowed material (min..max). Lazy and memoized per recipe: it is a lot of stat calls.</summary>
        public void EnsureMaterials()
        {
            if (materialsDone) return;
            materialsDone = true;
            if (kind != Kind.Apparel || product == null || !product.MadeFromStuff) return;
            if (!matCache.TryGetValue(recipe, out List<MaterialRow> rows))
            {
                rows = new List<MaterialRow>();
                try
                {
                    foreach (ThingDef s in GenStuff.AllowedStuffsFor(product))
                    {
                        if (!StuffAllowed(s)) continue;
                        rows.Add(new MaterialRow
                        {
                            stuff = s,
                            sharp = product.GetStatValueAbstract(StatDefOf.ArmorRating_Sharp, s),
                            blunt = product.GetStatValueAbstract(StatDefOf.ArmorRating_Blunt, s),
                            heat = product.GetStatValueAbstract(StatDefOf.ArmorRating_Heat, s),
                            cold = product.GetStatValueAbstract(StatDefOf.Insulation_Cold, s),
                        });
                    }
                    rows.Sort((a, b) => b.sharp != a.sharp ? b.sharp.CompareTo(a.sharp) : b.blunt.CompareTo(a.blunt));
                }
                catch (Exception e) { Log.Warning("[BillOrganizer] Material stats failed for " + product.defName + ": " + e.Message); }
                matCache[recipe] = rows;
            }
            materials = rows;
            if (rows.Count == 0) return;
            sharp = rows.Max(r => r.sharp); sharpMin = rows.Min(r => r.sharp);
            blunt = rows.Max(r => r.blunt); bluntMin = rows.Min(r => r.blunt);
            heat = rows.Max(r => r.heat); heatMin = rows.Min(r => r.heat);
            cold = rows.Max(r => r.cold); coldMin = rows.Min(r => r.cold);
        }

        bool StuffAllowed(ThingDef s)
        {
            ThingFilter fixedF = recipe.fixedIngredientFilter, defF = recipe.defaultIngredientFilter;
            if (fixedF != null && fixedF.AllowedDefCount > 0 && !fixedF.Allows(s)) return false;
            if (defF != null && !defF.Allows(s)) return false;
            foreach (IngredientCount i in recipe.ingredients) if (i.filter.Allows(s)) return true;
            return false;
        }

        // ---------------------------------------------------------------- ingredients

        void BuildIngredients()
        {
            ThingFilter fixedF = recipe.fixedIngredientFilter, defF = recipe.defaultIngredientFilter;
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount ing = recipe.ingredients[i];
                var info = new IngInfo { ing = ing, need = ing.CountFor(recipe), needBase = ing.GetBaseCount() };
                foreach (ThingDef d in ing.filter.AllowedThingDefs)
                {
                    if (!ing.IsFixedIngredient)
                    {
                        if (fixedF != null && fixedF.AllowedDefCount > 0 && !fixedF.Allows(d)) continue;
                        if (defF != null && !defF.Allows(d)) continue;
                    }
                    info.defs.Add(d);
                }
                // the stuff of a made-from-stuff product is one material, not a mix
                info.single = i == 0 && product != null && product.MadeFromStuff && info.defs.Any(d => d.IsStuff);
                info.rep = info.defs.FirstOrDefault();
                ings.Add(info);
            }
        }

        // ---------------------------------------------------------------- the bill (same steps as ITab_Bills, 1.7)

        /// <summary>Adds the bill (or <c>o.times</c> of them) with the options picked in the window. Returns how many were added.</summary>
        public int AddBill(BillStack stack, AddOptions o = null)
        {
            Map map = (stack.billGiver as Thing)?.Map;
            if (map != null)
            {
                if (ModsConfig.BiotechActive && recipe.mechanitorOnlyRecipe && !map.mapPawns.FreeColonists.Any(MechanitorUtility.IsMechanitor))
                    Find.WindowStack.Add(new Dialog_MessageBox("RecipeRequiresMechanitor".Translate(recipe.LabelCap)));
                else if (!map.mapPawns.FreeColonists.Any(c => recipe.PawnSatisfiesSkillRequirements(c)))
                    Bill.CreateNoPawnsWithSkillDialog(recipe);
            }
            int times = Mathf.Max(o?.times ?? 1, 1), added = 0;
            for (int n = 0; n < times && stack.Count < BillStack.MaxCount; n++)
            {
                Bill bill = recipe.MakeNewBill(precept);
                Configure(bill, o);
                stack.AddBill(bill);
                added++;
            }
            if (recipe.conceptLearned != null) PlayerKnowledgeDatabase.KnowledgeDemonstrated(recipe.conceptLearned, KnowledgeAmount.Total);
            if (TutorSystem.TutorialMode) TutorSystem.Notify_Event("AddBill-" + recipe.LabelCap.Resolve());

            List<string> rec = BillOrganizerMod.Settings.recent;
            rec.Remove(key);
            rec.Insert(0, key);
            if (rec.Count > 12) rec.RemoveRange(12, rec.Count - 12);
            return added;
        }

        /// <summary>Repeat mode / quantity / one material on the fresh bill. Every step is optional and null-safe.</summary>
        void Configure(Bill bill, AddOptions o)
        {
            if (o == null) return;
            try
            {
                if (bill is Bill_Production bp && o.mode > 0)
                {
                    if (o.mode == 1) { bp.repeatMode = BillRepeatModeDefOf.RepeatCount; bp.repeatCount = Mathf.Max(o.qty, 1); }
                    else if (o.mode == 2)
                    {
                        // never below one batch of this recipe; a value we set is one batch of this recipe, the player's value is kept
                        bp.repeatMode = BillRepeatModeDefOf.TargetCount;
                        SetTarget(bp, o.qtyAuto ? TargetStep : Mathf.Max(o.qty, TargetStep));
                    }
                    else if (o.mode == 3) bp.repeatMode = BillRepeatModeDefOf.Forever;
                }
                if (o.stuff != null && bill.ingredientFilter != null)
                {
                    // the filter is shared by every ingredient of the recipe: only the other materials are switched off
                    foreach (ThingDef d in bill.ingredientFilter.AllowedThingDefs.ToList())
                        if (d.IsStuff && d != o.stuff) bill.ingredientFilter.SetAllow(d, false);
                    bill.ingredientFilter.SetAllow(o.stuff, true);
                    if (BillOrganizerMod.Settings.autoName && bill is IRenameable ren)
                        ren.RenamableLabel = recipe.LabelCap + " (" + o.stuff.LabelCap + ")";
                }
            }
            catch (Exception ex) { Log.Warning("[BillOrganizer] Could not apply the bill options for " + recipe.defName + ": " + ex.Message); }
        }

        List<ThingDef> allowedStuffs;

        /// <summary>Materials this recipe can use for its product (empty when the product is not made from stuff).</summary>
        public List<ThingDef> AllowedStuffs()
        {
            if (allowedStuffs != null) return allowedStuffs;
            allowedStuffs = new List<ThingDef>();
            try
            {
                if (product != null && product.MadeFromStuff)
                    foreach (ThingDef s in GenStuff.AllowedStuffsFor(product)) if (StuffAllowed(s)) allowedStuffs.Add(s);
            }
            catch (Exception ex) { Log.Warning("[BillOrganizer] Could not list the materials of " + recipe.defName + ": " + ex.Message); }
            return allowedStuffs;
        }

        public bool SkillOk(Map map)
        {
            if (skillOk == null) skillOk = map == null || map.mapPawns.FreeColonists.Any(c => recipe.PawnSatisfiesSkillRequirements(c));
            return skillOk.Value;
        }
    }
}
