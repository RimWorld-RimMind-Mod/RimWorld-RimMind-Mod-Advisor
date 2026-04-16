using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimMind.Advisor.Comps;
using RimWorld;
using Verse;

namespace RimMind.Advisor.Patches
{
    /// <summary>
    /// 在所有 ThingDef 解析完继承关系之后，
    /// 动态为所有人形智能小人（含 AlienRaces 外星种族）注入 CompProperties_AIAdvisor。
    ///
    /// 使用 C# Harmony Postfix 而非 XML PatchOperation，
    /// 原因同 RimMind-Personality：XML Patch 在继承解析前运行，
    /// race/intelligence 字段尚未继承，XPath 过滤匹配 0 个节点。
    /// </summary>
    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.ResolveReferences))]
    public static class AddAdvisorCompPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingDef __instance)
        {
            if (__instance.race?.intelligence != Intelligence.Humanlike) return;

            __instance.comps ??= new List<CompProperties>();

            if (__instance.comps.Any(c => c is CompProperties_AIAdvisor)) return;

            __instance.comps.Add(new CompProperties_AIAdvisor());
        }
    }
}
