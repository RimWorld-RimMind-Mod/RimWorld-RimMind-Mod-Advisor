using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Actions;
using RimMind.Actions.Actions;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimMind.Advisor.Advisor
{
    public static class JobCandidateBuilder
    {
        private const int MaxWorkCandidates = 10;

        private static readonly HashSet<string> AdvisorInstantActions = new HashSet<string>
        {
            "force_rest",
            "social_relax",
            "social_dining",
            "eat_food",
            "tend_pawn",
            "rescue_pawn",
            "inspire_work",
            "inspire_fight",
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
            foreach (var (display, intentId, risk, hint) in instantCandidates)
            {
                string riskTag = RiskTag(risk);
                string desc = GetActionDesc(intentId);
                string line = $"{idxB++}. {display}({intentId}){riskTag}";
                if (!string.IsNullOrEmpty(desc)) line += $" | {desc}";
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

                string? hint = null;
                try
                {
                    var targets = RimMindActionsAPI.GetWorkTargets(pawn, workType.defName, 3);
                    if (targets.Count == 0) continue;

                    var nearest = targets[0];
                    string distStr = $"{nearest.Distance:F0}";
                    hint = targets.Count == 1
                        ? "RimMind.Advisor.Prompt.TargetCountSingle".Translate(targets.Count, distStr)
                        : "RimMind.Advisor.Prompt.TargetCountMulti".Translate(targets.Count, distStr, nearest.Label);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Advisor] GetWorkTargets failed for {workType.defName}: {ex.Message}");
                    continue;
                }

                result.Add((workType.labelShort, workType.defName, hint));
            }

            return result;
        }

        private static List<(string display, string intentId, RiskLevel risk, string? hint)> BuildInstantCandidates(Pawn pawn)
        {
            var result = new List<(string, string, RiskLevel, string?)>();

            var allActions = RimMindActionsAPI.GetActionDescriptions();

            foreach (var (intentId, displayName, riskLevel) in allActions)
            {
                if (!AdvisorInstantActions.Contains(intentId)) continue;
                if (!RimMindActionsAPI.IsAllowed(intentId)) continue;

                string? hint = BuildInstantHint(pawn, intentId);
                if (hint == null) continue;

                result.Add((displayName, intentId, riskLevel, hint));
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

                case "social_dining":
                {
                    var others = pawn.Map?.mapPawns.FreeColonistsSpawned
                        .Where(p => p != pawn).ToList();
                    if (others == null || others.Count == 0) return null;
                    var partner = others[0];
                    return "RimMind.Advisor.Prompt.SocialDiningHint".Translate(partner.Name.ToStringShort);
                }

                case "eat_food":
                {
                    if (pawn.Map == null) return null;
                    var joyFoods = EatFoodAction.GetJoyFoodLabels(pawn, 4);
                    if (joyFoods.Count == 0) return null;
                    string foodList = string.Join(", ", joyFoods);
                    return "RimMind.Advisor.Prompt.EatFoodHint".Translate(foodList);
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

                case "inspire_fight":
                {
                    if (pawn.mindState?.inspirationHandler == null) return null;
                    if (pawn.Inspired) return null;
                    return "RimMind.Advisor.Prompt.InspireFight".Translate();
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
            RiskLevel.Low      => "RimMind.Advisor.Prompt.Risk.Low".Translate(),
            RiskLevel.Medium   => "RimMind.Advisor.Prompt.Risk.Medium".Translate(),
            RiskLevel.High     => "RimMind.Advisor.Prompt.Risk.High".Translate(),
            RiskLevel.Critical => "RimMind.Advisor.Prompt.Risk.Critical".Translate(),
            _                  => "",
        };

        private static string GetActionDesc(string intentId)
        {
            string key = $"RimMind.Actions.Desc.{intentId}";
            string translated = key.Translate();
            return translated != key ? translated : "";
        }
    }
}
