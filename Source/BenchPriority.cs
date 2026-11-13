using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BillOrganizer
{
    /// <summary>
    /// Priority of each workbench (saved with the game). Added to <c>WorkGiver_DoBill.GetPriority</c>: among the benches one work giver
    /// can reach, a pawn takes the highest priority first and only then the closest. <see cref="BenchOrder"/> extends this to the
    /// bench types (work givers) of the same skill. Nothing crosses skills (that is the Work tab).
    /// </summary>
    public class BenchPriority : GameComponent
    {
        public const int Min = -5, Max = 5;
        Dictionary<int, int> map = new Dictionary<int, int>();

        public BenchPriority(Game game) { }

        public static BenchPriority Instance => Current.Game?.GetComponent<BenchPriority>();

        public static int Get(Thing t)
        {
            BenchPriority i = Instance;
            return i != null && t != null && i.map.TryGetValue(t.thingIDNumber, out int v) ? v : 0;
        }

        public static void Set(Thing t, int value)
        {
            BenchPriority i = Instance;
            if (i == null || t == null) return;
            value = Math.Max(Min, Math.Min(Max, value));
            if (value == 0) i.map.Remove(t.thingIDNumber); else i.map[t.thingIDNumber] = value;
            BenchOrder.Invalidate();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref map, "benchPriority", LookMode.Value, LookMode.Value);
            if (map == null) map = new Dictionary<int, int>();
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.GetPriority), typeof(Pawn), typeof(TargetInfo))]
    public static class Patch_BenchPriority
    {
        static void Postfix(WorkGiver_Scanner __instance, TargetInfo t, ref float __result)
        {
            try
            {
                if (__instance is WorkGiver_DoBill && t.HasThing && t.Thing is Building_WorkTable) __result += BenchPriority.Get(t.Thing);
            }
            catch (Exception e) { Log.ErrorOnce("[BillOrganizer] Bench priority failed: " + e.Message, 0x0B110020); }
        }
    }
}
