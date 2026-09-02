using System;
using System.Linq;
using System.Text;
using RimMind.Advisor.Data;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Advisor.Advisor
{
    internal static class AdvisorProviderRegistrar
    {
        private const string PublicProviderOwner = "RimMind.Advisor";

        internal static void RegisterAll()
        {
            RimMindAPI.Context.ContextKeys.Register(new ContextProviderDef(
                "advisor_history", ContextLayer.L4_History, 0.8f,
                async (ctx, ct) =>
                {
                    if (ctx.PawnId <= 0) return null;
                    var pawn = Find.WorldPawns.AllPawnsAlive.FirstOrDefault(p => p.thingIDNumber == ctx.PawnId)
                        ?? Find.CurrentMap?.mapPawns?.FreeColonists.FirstOrDefault(p => p.thingIDNumber == ctx.PawnId);
                    if (pawn == null) return null;
                    var historyStore = AdvisorHistoryStore.Instance;
                    if (historyStore == null) return null;
                    var records = historyStore.GetRecords(pawn);
                    if (records.Count == 0) return null;
                    var recent = records.Skip(Math.Max(0, records.Count - 5)).ToList();
                    var sb = new StringBuilder();
                    sb.AppendLine("RimMind.Advisor.Prompt.RecentHistory".Translate());
                    foreach (var r in recent)
                    {
                        string resultLabel = r.result switch
                        {
                            "approved" => "RimMind.Advisor.Prompt.ResultApproved".Translate(),
                            "rejected" => "RimMind.Advisor.Prompt.ResultRejected".Translate(),
                            "system_blocked" => "RimMind.Advisor.Prompt.ResultBlocked".Translate(),
                            _ => "RimMind.Advisor.Prompt.ResultIgnored".Translate()
                        };
                        sb.AppendLine($"- {r.action}: {r.reason} → {resultLabel}");
                    }
                    return sb.ToString().TrimEnd();
                }, "RimMind.Advisor", stalenessTicks: 3000, invalidationTriggers: new[] { "AdvisorEvent" }));

            RimMindAPI.Context.ContextKeys.Register(new ContextProviderDef(
                "actions_list", ContextLayer.L3_State, 0.85f,
                async (ctx, ct) =>
                {
                    if (ctx.Scenario != RimMindAPI.Context.ScenarioDecision) return null;
                    if (ctx.PawnId <= 0) return null;
                    var pawn = Find.WorldPawns.AllPawnsAlive.FirstOrDefault(p => p.thingIDNumber == ctx.PawnId)
                        ?? Find.CurrentMap?.mapPawns?.FreeColonists.FirstOrDefault(p => p.thingIDNumber == ctx.PawnId);
                    var text = BuildToolListText();
                    return string.IsNullOrEmpty(text) ? null : text;
                }, "RimMind.Advisor", stalenessTicks: 750, invalidationTriggers: new[] { "AdvisorEvent" }));

            RimMindAPI.Context.ContextKeys.Register(new ContextProviderDef(
                "advisor_task", ContextLayer.L0_Static, 0.95f,
                async (ctx, ct) =>
                {
                    if (ctx.Scenario != RimMindAPI.Context.ScenarioDecision) return null;
                    var instruction = string.Join("\n\n", new[] { "Role", "Goal", "Process", "Constraint", "Output", "FieldRules", "OutputRules", "RiskControl", "DiversityHint", "RequestRules", "Example" }
                        .Select(k => (string)$"RimMind.Advisor.Prompt.TaskInstruction.{k}".Translate())
                        .Where(t => !string.IsNullOrEmpty(t)));
                    return instruction;
                }, "RimMind.Advisor", stalenessTicks: 0, invalidationTriggers: new[] { "AdvisorEvent" }));

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

                    var text = new StringBuilder();
                    text.AppendLine("[RimMind Advisor]");
                    foreach (var record in history.Take(5))
                    {
                        text.AppendLine(
                            $"- {record.action}: {record.reason} ({record.result})");
                    }

                    return text.ToString().TrimEnd();
                },
                priority: 100,
                overrideExisting: true);
        }

        private static string BuildToolListText()
        {
            try
            {
                var defs = RimMindAPI.Tools.GetAllDefinitions();
                if (defs.Count == 0) return string.Empty;

                var sb = new StringBuilder();
                sb.AppendLine("RimMind.Advisor.Prompt.InstantSectionHeader".Translate());
                foreach (var def in defs)
                {
                    if (string.IsNullOrWhiteSpace(def.Id)) continue;
                    if (RimMindAPI.ShouldSkipAction(def.Id)) continue;

                    var riskTag = AdvisorPromptHelper.RiskTag(AdvisorToolRiskResolver.Resolve(def.Id));
                    var category = string.IsNullOrWhiteSpace(def.Category) ? "general" : def.Category;
                    var description = string.IsNullOrWhiteSpace(def.Description) ? "" : $" | {def.Description}";
                    sb.AppendLine($"- {def.Id} [{category}]{riskTag}{description}");
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] BuildToolListText failed: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
