using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace BillOrganizer
{
    /// <summary>
    /// Nice Bill Tab prefixes ITab_Bills.FillTab and returns false while its own toggle (NiceBillTab.Settings.EnabledMod, the
    /// checkbox in the corner of the tab) is on, so BillStack.DoListing, our hook, never runs. We only read that flag (by
    /// reflection, no reference to its DLL) to decide whether the workbench button should be shown. Everything is null-safe:
    /// if the author renames the field, <see cref="TabReplaced"/> is false and the button logic falls back to "show it".
    /// </summary>
    public static class NiceCompat
    {
        public const string PackageId = "Andromeda.NiceBillTab";

        static readonly FieldInfo enabledField;
        static bool? installed;

        static NiceCompat()
        {
            try
            {
                Type t = AccessTools.TypeByName("NiceBillTab.Settings");
                enabledField = t == null ? null : AccessTools.Field(t, "EnabledMod");
                if (enabledField != null && (enabledField.FieldType != typeof(bool) || !enabledField.IsStatic)) enabledField = null;
            }
            catch (Exception e)
            {
                Log.Warning("[BillOrganizer] Nice Bill Tab lookup failed: " + e.Message);
            }
        }

        public static bool Installed
        {
            get
            {
                if (installed == null)
                {
                    installed = false;
                    foreach (ModContentPack p in LoadedModManager.RunningModsListForReading)
                        if (p.PackageIdPlayerFacing.EqualsIgnoreCase(PackageId)) { installed = true; break; }
                }
                return installed.Value;
            }
        }

        /// <summary>Nice Bill Tab is loaded and its tab replacement is switched on right now.</summary>
        public static bool TabReplaced
        {
            get
            {
                if (!Installed) return false;
                if (enabledField == null) return true; // cannot read the flag: assume the worst, keep the button available
                try { return (bool)enabledField.GetValue(null); }
                catch { return true; }
            }
        }
    }
}
