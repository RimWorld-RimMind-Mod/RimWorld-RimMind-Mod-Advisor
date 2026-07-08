using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Enums;
using RimMind.Presentation.Api;
using RimWorld;
using Verse;
using Verse.AI;
using RimMind.Domain.ValueObjects;

namespace RimMind.Advisor.Advisor
{
    public static class JobCandidateBuilder
    {
        private const int MaxWorkCandidates = 10;

        private static readonly HashSet<string> AdvisorInstantActions = new HashSet<string>
        {
            "force_rest",
            "social_relax",
            "eat_food",
            "tend_pawn",
            "rescue_pawn",
            "inspire_work",
            "inspire_shoot",
            "inspire_trade",
            "move_to",
        };

        public static string Build(Pawn pawn)
        {
            var sb = new StringBuilder();

            var workCandidates = BuildWorkCandidates(pawn);

            sb.AppendLine("RimMind.Advisor.Prompt.WorkSectionHeader".Translate());
            if (workCandidates.Count == 0)
            {
                sb.AppendLine("RimMind.Advisor.Prompt.NoWorkTargets".Translate());
            }
            else
            {
                int idx = 1;
                foreach (var (label, defName, hint) in workCandidates)
                {
                    string line = $"{idx++}. {label}({defName})" + "RimMind.Advisor.Prompt.Risk.Low".Translate();
                    if (!string.IsNullOrEmpty(hint)) line += $" — {hint}";
                    sb.AppendLine(line);
                }
            }

            sb.AppendLine();

            sb.AppendLine("RimMind.Advisor.Prompt.InstantSectionHeader".Translate());
            var instantCandidates = BuildInstantCandidates(pawn);
            int idxB = workCandidates.Count + 1;
            foreach (var (display, intentId, risk, hint, description) in instantCandidates)
            {
                string riskTag = RiskTag(risk);
                string line = $"{idxB++}. {display}({intentId}){riskTag}";
                if (!string.IsNullOrEmpty(description)) line += $" | {description}";
                if (!string.IsNullOrEmpty(hint)) line += $" — {hint}";
                sb.AppendLine(line);
            }

            return sb.ToString().TrimEnd();
        }

        private static List<(string label, string defName, string? hint)> BuildWorkCandidates(Pawn pawn)
        {
            var result = new List<(string, string, string?)>();
            if (pawn.workSettings == null) return result;

            foreach (var workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (result.Count >= MaxWorkCandidates) break;
                if (!pawn.workSettings.WorkIsActive(workType)) continue;

                result.Add((workType.labelShort, workType.defName, null));
            }

            return result;
        }

        private static List<(string display, string intentId, RiskLevel risk, string? hint, string description)> BuildInstantCandidates(Pawn pawn)
        {
            var result = new List<(string, string, RiskLevel, string?, string)>();

            IReadOnlyList<ToolDefinition> allTools;
            try
            {
                allTools = RimMindAPI.Tools.GetAllDefinitions();
            }
            catch (Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] GetAllDefinitions failed: {ex.Message}");
                return result;
            }

            foreach (var tool in allTools)
            {
                var intentId = tool.Id;
                if (string.IsNullOrWhiteSpace(intentId)) continue;
                if (RimMindAPI.ShouldSkipAction(intentId)) continue;

                string? hint = BuildInstantHint(pawn, intentId);
                if (hint == null && AdvisorInstantActions.Contains(intentId)) continue;

                var riskLevel = AdvisorToolRiskResolver.Resolve(intentId);
                result.Add((intentId, intentId, riskLevel, hint, tool.Description ?? ""));
            }

            return result;
        }

        private static string? BuildInstantHint(Pawn pawn, string intentId)
        {
            switch (intentId)
            {
                case "force_rest":
                    {
                        float rest = pawn.needs?.rest?.CurLevelPercentage ?? 1f;
                        string restPct = $"{rest * 100:F0}";
                        return rest < 0.9f
                            ? "RimMind.Advisor.Prompt.RestLow".Translate(restPct)
                            : "RimMind.Advisor.Prompt.RestSufficient".Translate(restPct);
                    }

                case "social_relax":
                    {
                        float mood = pawn.needs?.mood?.CurLevelPercentage ?? 1f;
                        string moodPct = $"{mood * 100:F0}";
                        return mood < 0.6f
                            ? "RimMind.Advisor.Prompt.MoodLow".Translate(moodPct)
                            : "RimMind.Advisor.Prompt.MoodNormal".Translate(moodPct);
                    }

                case "eat_food":
                    {
                        if (pawn.Map == null) return null;
                        return "RimMind.Advisor.Prompt.EatFoodHint".Translate("");
                    }

                case "tend_pawn":
                    {
                        var injured = pawn.Map?.mapPawns.FreeColonistsSpawned
                            .Where(p => p != pawn && p.health?.HasHediffsNeedingTend() == true)
                            .ToList();
                        if (injured == null || injured.Count == 0) return null;
                        return "RimMind.Advisor.Prompt.TendPawnHint".Translate(injured[0].Name.ToStringShort);
                    }

                case "rescue_pawn":
                    {
                        var downed = pawn.Map?.mapPawns.FreeColonistsSpawned
                            .Where(p => p != pawn && p.Downed)
                            .ToList();
                        if (downed == null || downed.Count == 0) return null;
                        return "RimMind.Advisor.Prompt.RescuePawnHint".Translate(downed[0].Name.ToStringShort);
                    }

                case "inspire_work":
                    {
                        if (pawn.mindState?.inspirationHandler == null) return null;
                        if (pawn.Inspired) return null;
                        return "RimMind.Advisor.Prompt.InspireWork".Translate();
                    }

                case "inspire_shoot":
                    {
                        if (pawn.mindState?.inspirationHandler == null) return null;
                        if (pawn.Inspired) return null;
                        return "RimMind.Advisor.Prompt.InspireShoot".Translate();
                    }

                case "inspire_trade":
                    {
                        if (pawn.mindState?.inspirationHandler == null) return null;
                        if (pawn.Inspired) return null;
                        return "RimMind.Advisor.Prompt.InspireTrade".Translate();
                    }

                case "move_to":
                    return "RimMind.Advisor.Prompt.MoveToHint".Translate();

                default:
                    return "";
            }
        }

        private static string RiskTag(RiskLevel risk) => risk switch
        {
            RiskLevel.Low => "RimMind.Advisor.Prompt.Risk.Low".Translate(),
            RiskLevel.Medium => "RimMind.Advisor.Prompt.Risk.Medium".Translate(),
            RiskLevel.High => "RimMind.Advisor.Prompt.Risk.High".Translate(),
            RiskLevel.Critical => "RimMind.Advisor.Prompt.Risk.Critical".Translate(),
            _ => "",
        };

    }
}
