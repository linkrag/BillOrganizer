using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BillOrganizer
{
    /// <summary>One ammo type a CE gun can load, with the numbers of the projectile it fires.</summary>
    public class AmmoOpt
    {
        public ThingDef ammo, projectile;
        public float damage, penSharp, penBlunt;
        public string classLabel;
    }

    /// <summary>
    /// Combat Extended data by reflection, so the mod has no CE reference (same idea as Nice Bill Tab's CombatExtendedIntegration):
    /// a CE gun has no defaultProjectile; the damage comes from the ammo types of its CompProperties_AmmoUser.ammoSet.
    /// Every lookup is null-safe: if CE changes a field name the columns just show "-".
    /// </summary>
    public static class CeCompat
    {
        public static readonly bool Active;

        static readonly Type ammoUser, ammoSetDef, ammoLink, projCE, toolCE, secondaryDamage, ammoDef;
        static readonly FieldInfo fAmmoSet, fMagazine, fAmmoTypes, fLinkAmmo, fLinkProjectile;
        static readonly FieldInfo fApSharp, fApBlunt, fSecondary, fSecondaryAmount, fToolSharp, fToolBlunt, fAmmoClass;
        static readonly PropertyInfo pUsers;

        static readonly Dictionary<ThingDef, List<AmmoOpt>> optCache = new Dictionary<ThingDef, List<AmmoOpt>>();
        static readonly Dictionary<ThingDef, int> magCache = new Dictionary<ThingDef, int>();
        static readonly Dictionary<ThingDef, string> setCache = new Dictionary<ThingDef, string>();

        static CeCompat()
        {
            try
            {
                ammoUser = AccessTools.TypeByName("CombatExtended.CompProperties_AmmoUser");
                ammoSetDef = AccessTools.TypeByName("CombatExtended.AmmoSetDef");
                ammoLink = AccessTools.TypeByName("CombatExtended.AmmoLink");
                projCE = AccessTools.TypeByName("CombatExtended.ProjectilePropertiesCE");
                toolCE = AccessTools.TypeByName("CombatExtended.ToolCE");
                secondaryDamage = AccessTools.TypeByName("CombatExtended.SecondaryDamage");
                ammoDef = AccessTools.TypeByName("CombatExtended.AmmoDef");
                if (ammoUser == null || ammoSetDef == null || ammoLink == null) return;
                fAmmoSet = AccessTools.Field(ammoUser, "ammoSet");
                fMagazine = AccessTools.Field(ammoUser, "magazineSize");
                fAmmoTypes = AccessTools.Field(ammoSetDef, "ammoTypes");
                fLinkAmmo = AccessTools.Field(ammoLink, "ammo");
                fLinkProjectile = AccessTools.Field(ammoLink, "projectile");
                fApSharp = projCE == null ? null : AccessTools.Field(projCE, "armorPenetrationSharp");
                fApBlunt = projCE == null ? null : AccessTools.Field(projCE, "armorPenetrationBlunt");
                fSecondary = projCE == null ? null : AccessTools.Field(projCE, "secondaryDamage");
                fSecondaryAmount = secondaryDamage == null ? null : AccessTools.Field(secondaryDamage, "amount");
                fToolSharp = toolCE == null ? null : AccessTools.Field(toolCE, "armorPenetrationSharp");
                fToolBlunt = toolCE == null ? null : AccessTools.Field(toolCE, "armorPenetrationBlunt");
                fAmmoClass = ammoDef == null ? null : AccessTools.Field(ammoDef, "ammoClass");
                pUsers = ammoDef == null ? null : AccessTools.Property(ammoDef, "Users");
                Active = fAmmoSet != null && fAmmoTypes != null && fLinkProjectile != null;
            }
            catch (Exception e)
            {
                Log.Warning("[BillOrganizer] Combat Extended lookup failed, CE columns disabled: " + e.Message);
                Active = false;
            }
        }

        public static void ClearCaches()
        {
            optCache.Clear(); magCache.Clear(); setCache.Clear();
        }

        /// <summary>All ammo types of the weapon's ammo set (empty if it is not a CE gun).</summary>
        public static List<AmmoOpt> AmmoOptions(ThingDef weapon, out int magazine, out string setLabel)
        {
            magazine = 0; setLabel = null;
            if (!Active || weapon.comps == null) return null;
            if (optCache.TryGetValue(weapon, out List<AmmoOpt> cached))
            {
                magCache.TryGetValue(weapon, out magazine); setCache.TryGetValue(weapon, out setLabel);
                return cached;
            }
            List<AmmoOpt> result = null;
            try
            {
                CompProperties user = weapon.comps.FirstOrDefault(c => ammoUser.IsInstanceOfType(c));
                if (user != null)
                {
                    Def set = fAmmoSet.GetValue(user) as Def;
                    IEnumerable links = set == null ? null : fAmmoTypes.GetValue(set) as IEnumerable;
                    if (links != null)
                    {
                        result = new List<AmmoOpt>();
                        foreach (object link in links)
                        {
                            var proj = fLinkProjectile.GetValue(link) as ThingDef;
                            if (proj?.projectile == null) continue;
                            var o = new AmmoOpt
                            {
                                ammo = fLinkAmmo?.GetValue(link) as ThingDef,
                                projectile = proj,
                                damage = proj.projectile.GetDamageAmount((Thing)null),
                            };
                            if (fApSharp != null && projCE.IsInstanceOfType(proj.projectile))
                            {
                                o.penSharp = (float)fApSharp.GetValue(proj.projectile);
                                o.penBlunt = fApBlunt == null ? 0f : (float)fApBlunt.GetValue(proj.projectile);
                                if (fSecondary != null && fSecondaryAmount != null && fSecondary.GetValue(proj.projectile) is IEnumerable sec)
                                    foreach (object s in sec) o.damage += Convert.ToSingle(fSecondaryAmount.GetValue(s));
                            }
                            o.classLabel = AmmoClassLabel(o.ammo);
                            result.Add(o);
                        }
                        magazine = fMagazine == null ? 0 : Convert.ToInt32(fMagazine.GetValue(user));
                        setLabel = set.LabelCap;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[BillOrganizer] CE ammo lookup failed for " + weapon.defName + ": " + e.Message);
                result = null;
            }
            optCache[weapon] = result; magCache[weapon] = magazine; setCache[weapon] = setLabel;
            return result;
        }

        /// <summary>
        /// The ammo category of a CE gun (first ammo of its set, e.g. "Ammo12Gauge") and its family, the ancestor right under "Ammo"
        /// (AmmoPistols, AmmoShotguns, AmmoRifles...). Both null when the weapon is not a CE gun.
        /// </summary>
        public static void AmmoFamily(ThingDef weapon, out ThingCategoryDef cat, out ThingCategoryDef family, out int magazine)
        {
            cat = family = null;
            List<AmmoOpt> opts = AmmoOptions(weapon, out magazine, out string _);
            ThingDef ammo = opts?.FirstOrDefault(o => o.ammo != null)?.ammo;
            cat = ammo?.thingCategories?.FirstOrDefault();
            for (ThingCategoryDef x = cat; x != null; x = x.parent)
                if (x.parent != null && x.parent.defName == "Ammo") { family = x; break; }
        }

        /// <summary>Round type (FMJ, AP, HE...) of a CE AmmoDef, or null.</summary>
        public static string AmmoClassLabel(ThingDef ammo)
        {
            if (ammo == null || fAmmoClass == null || !ammoDef.IsInstanceOfType(ammo)) return null;
            try { return (fAmmoClass.GetValue(ammo) as Def)?.LabelCap; } catch { return null; }
        }

        /// <summary>Weapons that can load this CE ammo (AmmoDef.Users), or null.</summary>
        public static List<ThingDef> Users(ThingDef ammo)
        {
            if (ammo == null || pUsers == null || !ammoDef.IsInstanceOfType(ammo)) return null;
            try { return (pUsers.GetValue(ammo, null) as IEnumerable)?.Cast<ThingDef>().ToList(); } catch { return null; }
        }

        /// <summary>Melee armor penetration (sharp) of a CE weapon: tool average weighted by chanceFactor. Null if not CE tools.</summary>
        public static float? MeleePenSharp(ThingDef weapon)
        {
            if (!Active || toolCE == null || fToolSharp == null || weapon.tools.NullOrEmpty()) return null;
            float w = 0f, sum = 0f;
            foreach (Tool t in weapon.tools)
            {
                if (!toolCE.IsInstanceOfType(t)) return null;
                float cf = t.chanceFactor > 0f ? t.chanceFactor : 0.0001f;
                w += cf; sum += cf * (float)fToolSharp.GetValue(t);
            }
            return w > 0f ? sum / w : (float?)null;
        }

        static string Unit(string key, string fallback) => key.CanTranslate() ? key.Translate().ToString() : fallback;

        /// <summary>Armor in CE: sharp is mm RHA, blunt is MPa (CE's own translation keys).</summary>
        public static string FormatSharp(float v) => v.ToString(v < 10f ? "0.#" : "0") + " " + Unit("CE_mmRHA", "mm");
        public static string FormatBlunt(float v) => v.ToString(v < 10f ? "0.#" : "0") + " " + Unit("CE_MPa", "MPa");
    }
}
