using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimMind.Advisor.Comps;
using RimWorld;
using Verse;

namespace RimMind.Advisor.Patches
{
    /// <summary>
    /// Dynamically injects CompProperties_AIAdvisor into all humanlike Pawn ThingDefs
    /// after ThingDef.ResolveReferences completes (excluding Corpse ThingDefs).
    /// </summary>
    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.ResolveReferences))]
    public static class AddAdvisorCompPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingDef __instance)
        {
            if (__instance == null || __instance.IsCorpse) return;
            if (__instance.thingClass == null || !typeof(Pawn).IsAssignableFrom(__instance.thingClass)) return;
            if (__instance.race?.intelligence != Intelligence.Humanlike) return;

            __instance.comps ??= new List<CompProperties>();

            if (__instance.comps.Any(c => c is CompProperties_AIAdvisor)) return;

            __instance.comps.Add(new CompProperties_AIAdvisor());
        }
    }
}
