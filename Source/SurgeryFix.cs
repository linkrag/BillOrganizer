using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace BillOrganizer
{
    /// <summary>
    /// Up to 1.3.0 our recipe window also opened in the pawn Operations tab and added surgeries with MakeNewBill, which makes a plain
    /// Bill_Production (no body part, never a Bill_Medical): the doctor used up the ingredients and nothing was installed. Those bills
    /// can only do harm, so on game start/load every non-medical surgery bill on a pawn is deleted, with a log line and one message.
    /// </summary>
    public static class SurgeryFix
    {
        public static void RemoveBroken()
        {
            if (Current.Game == null) return;
            try
            {
                int removed = 0;
                foreach (Map map in Find.Maps)
                    foreach (Pawn p in map.mapPawns.AllPawns)
                    {
                        BillStack stack = p?.health?.surgeryBills;
                        if (stack == null || stack.Count == 0) continue;
                        var bad = new List<Bill>();
                        foreach (Bill b in stack)
                            if (!(b is Bill_Medical) && b?.recipe != null && b.recipe.IsSurgery) bad.Add(b);
                        foreach (Bill b in bad) { stack.Delete(b); removed++; }
                    }
                if (removed > 0)
                {
                    Log.Message("[BillOrganizer] Removed " + removed + " broken surgery bill(s) (plain bills without a body part, added by the recipe window up to 1.3.0).");
                    Messages.Message("BO_BrokenSurgeryRemoved".Translate(removed), MessageTypeDefOf.CautionInput, false);
                }
            }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Broken surgery bill cleanup failed: " + e, 0x0B110013); }
        }
    }
}
