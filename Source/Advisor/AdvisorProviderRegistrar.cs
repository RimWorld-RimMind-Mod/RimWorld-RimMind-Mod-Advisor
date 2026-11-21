using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Advisor.Data;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Features.Context;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Advisor.Advisor
{
    internal static class AdvisorProviderRegistrar
    {
        private const string PublicProviderOwner = "RimMind.Advisor";

        private static readonly string[] TaskInstructionSubKeys =
        {
            "Role",
            "Goal",
            "Process",
            "Constraint",
            "Output",
            "FieldRules",
            "OutputRules",
            "RiskControl",
            "DiversityHint",
            "RequestRules",
            "LanguageRule",
            "Example"
        };

        private static readonly Dictionary<string, float> AdvisorScenarioRelevanceWeights = new Dictionary<string, float>
        {
            ["advisor_task"] = 1.0f,
            ["actions_list"] = 0.9f,
            ["advisor_history"] = 0.85f,
            ["pawn_state"] = 0.95f,
            ["pawn_needs"] = 0.95f,
            ["pawn_skills"] = 0.85f,
            ["pawn_health"] = 0.9f,
            ["colony_state"] = 0.9f,
            ["environment"] = 0.8f
        };

        internal static void RegisterAll()
        {
            // Register the typed event before any key names it, so the trigger resolves
            // to a real event (unknown names never fall back to the base bus event).
            AdvisorHistoryChangedEvent.Register();

            // Register Advisor's dedicated scenario so Advisor context never collides with Core's agent_decision.
            RimMindAPI.Context.RegisterScenario(new ScenarioDescriptor
            {
                Id = ScenarioIds.AdvisorDecision,
                OwnerModId = RimMindOwnerConsts.AdvisorModId,
                DefaultBaseRounds = 0,
                Description = "RimMind-Advisor colonist recommendation & strategic decision cycle",
                DefaultBudget = 0.55f,
                L4Mode = L4Mode.None,
                RelevanceWeights = AdvisorScenarioRelevanceWeights
            });

            RimMindAPI.Context.ContextKeys.Register(new ContextProviderDef(
                "advisor_history", ContextLayer.L4_History, 0.8f,
                (ctx, ct) =>
                {
                    if (ctx.PawnId <= 0)
                        return System.Threading.Tasks.Task.FromResult<string?>(null);

                    return RimMindAPI.MainThreadSnapshot.GetAsync<string?>(
                        () => BuildHistoryText(ctx.PawnId),
                        fallback: null,
                        ct: ct);
                }, RimMindOwnerConsts.AdvisorModId, stalenessTicks: 3000,
                invalidationTriggers: new[] { AdvisorHistoryChangedEvent.EventName }));

            RimMindAPI.Context.ContextKeys.Register(new ContextProviderDef(
                "actions_list", ContextLayer.L3_State, 0.85f,
                (ctx, ct) =>
                {
                    if (ctx.Scenario != ScenarioIds.AdvisorDecision || ctx.PawnId <= 0)
                        return System.Threading.Tasks.Task.FromResult<string?>(null);

                    return RimMindAPI.MainThreadSnapshot.GetAsync<string?>(
                        () => BuildScopedToolGuidanceText(ctx.PawnId),
                        fallback: null,
                        ct: ct);
                }, RimMindOwnerConsts.AdvisorModId, stalenessTicks: 750));

            RimMindAPI.Context.ContextKeys.Register(new ContextProviderDef(
                "advisor_task", ContextLayer.L0_Static, 0.95f,
                (ctx, ct) =>
                {
                    if (ctx.Scenario != ScenarioIds.AdvisorDecision)
                        return System.Threading.Tasks.Task.FromResult<string?>(null);

                    return RimMindAPI.MainThreadSnapshot.GetAsync<string?>(
                        () =>
                        {
                            var instruction = RimMindAPI.Prompt.BuildTaskInstruction(
                                "RimMind.Advisor.Prompt.TaskInstruction",
                                null,
                                TaskInstructionSubKeys);
                            return string.IsNullOrEmpty(instruction) ? null : instruction;
                        },
                        fallback: null,
                        ct: ct);
                }, RimMindOwnerConsts.AdvisorModId, stalenessTicks: 0));

            RegisterPublicProviders();
        }

        private static void RegisterPublicProviders()
        {
            RimMindAPI.Providers.RegisterPawnProvider(
                "advisor.history_brief",
                PublicProviderOwner,
                pawn =>
                {
                    var history = AdvisorHistoryStore.Instance?.GetRecords(pawn);
                    if (history == null || history.Count == 0)
                        return string.Empty;

                    var text = new StringBuilder("[RimMind Advisor]");
                    foreach (var record in history.Skip(Math.Max(0, history.Count - 5)))
                    {
                        text.AppendLine(
                            $"- {record.action}: {record.reason} ({record.result})");
                    }

                    return text.ToString().TrimEnd();
                },
                priority: 100,
                overrideExisting: true);
        }

        private static string? BuildHistoryText(int pawnId)
        {
            var pawn = RimMindPawnLookup.FindPawnByNumber(pawnId);
            if (pawn == null) return null;
            var historyStore = AdvisorHistoryStore.Instance;
            if (historyStore == null) return null;
            var records = historyStore.GetRecords(pawn);
            if (records.Count == 0) return null;

            var recent = records.Skip(Math.Max(0, records.Count - 5)).ToList();
            var sb = new StringBuilder();
            sb.AppendLine(RimMindAPI.Text.TrOr("RimMind.Advisor.Prompt.RecentHistory", "[Recent Request History]"));
            foreach (var r in recent)
            {
                string resultLabel = FormatHistoryResultLabel(r.result);
                sb.AppendLine($"- {r.action}: {r.reason} → {resultLabel}");
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatHistoryResultLabel(string? result)
        {
            if (result == "approved")
                return RimMindAPI.Text.TrOr("RimMind.Advisor.Prompt.ResultApproved", "Approved");
            if (result == "rejected")
                return RimMindAPI.Text.TrOr("RimMind.Advisor.Prompt.ResultRejected", "Player Rejected");
            if (result == "system_blocked" || result == "blocked")
                return RimMindAPI.Text.TrOr("RimMind.Advisor.Prompt.ResultBlocked", "System Blocked");
            if (result == "ignored")
                return RimMindAPI.Text.TrOr("RimMind.Advisor.Prompt.ResultIgnored", "Ignored");
            if (!string.IsNullOrWhiteSpace(result))
                return $"Failed ({result})";
            return RimMindAPI.Text.TrOr("RimMind.Advisor.Prompt.ResultIgnored", "Ignored");
        }

        private static string? BuildScopedToolGuidanceText(int pawnId)
        {
            try
            {
                var pawn = RimMindPawnLookup.FindPawnByNumber(pawnId);
                var defs = RimMindAPI.ToolSet.GetDefinitionsForScope(
                    ToolScopeProjection.AdvisorDefaultScope,
                    compactSchema: false);
                if (defs == null || defs.Count == 0)
                    return null;

                int availableCount = 0;
                var elevatedRiskIds = new List<string>();
                for (int i = 0; i < defs.Count; i++)
                {
                    var def = defs[i];
                    if (def == null || string.IsNullOrWhiteSpace(def.Id))
                        continue;
                    if (AdvisorToolRiskResolver.ShouldSkipAction(def.Id, pawn))
                        continue;

                    availableCount++;
                    var risk = AdvisorToolRiskResolver.Resolve(def.Id);
                    if (risk >= RiskLevel.High)
                    {
                        elevatedRiskIds.Add($"{def.Id}{AdvisorPromptHelper.RiskTag(risk)}");
                    }
                }

                if (availableCount == 0)
                    return null;

                var sb = new StringBuilder();
                sb.AppendLine(RimMindAPI.Text.TrOr(
                    "RimMind.Advisor.Prompt.ActionsGuidanceHeader",
                    "[Tool Execution Guidance]"));
                sb.AppendLine(RimMindAPI.Text.TrOr(
                    "RimMind.Advisor.Prompt.ActionsGuidanceBody",
                    "{0} scoped action tools are attached via native Tool Calling. Call matching tools directly using ONLY parameters declared in each tool's JSON schema.",
                    availableCount));

                if (elevatedRiskIds.Count > 0)
                {
                    sb.AppendLine(RimMindAPI.Text.TrOr(
                        "RimMind.Advisor.Prompt.ActionsElevatedRisk",
                        "Elevated-risk / approval-gated tools (use only when warranted): {0}",
                        string.Join(", ", elevatedRiskIds)));
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] BuildScopedToolGuidanceText failed: {ex.Message}");
                return null;
            }
        }
    }
}
