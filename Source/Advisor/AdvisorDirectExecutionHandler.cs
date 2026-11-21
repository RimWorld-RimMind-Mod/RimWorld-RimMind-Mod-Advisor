using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.ValueObjects;
using Verse;
using ClientStructuredToolCall = RimMind.Domain.Llm.StructuredToolCall;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Executes approved Advisor tool calls on the main thread, broadcasts perceptions,
    /// records history entries, and displays colonist thought-bubble motes.
    /// </summary>
    internal sealed class AdvisorDirectExecutionHandler
    {
        private readonly Pawn _pawn;
        private readonly Func<RimMindAdvisorSettings> _settings;
        private readonly IAdvisorToolCallExecutor _toolExecutor;

        public AdvisorDirectExecutionHandler(
            Pawn pawn,
            Func<RimMindAdvisorSettings> settings,
            IAdvisorToolCallExecutor toolExecutor)
        {
            _pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
        }

        public List<ToolResult> ExecuteCallsSafely(
            IReadOnlyList<ClientStructuredToolCall> calls,
            string? traceId,
            bool playerApproved = false) =>
            AdvisorToolCallBatchRunner.ExecuteOnMainThread(
                _toolExecutor,
                calls,
                $"NPC-{_pawn.thingIDNumber}",
                traceId,
                playerApproved,
                message => RimMindErrors.Warn($"{message} (pawn={_pawn.Name.ToStringShort})"));

        public (List<ClientStructuredToolCall> Calls, List<ToolResult> Results) ExecuteDirectBatch(
            AdvisorTaskDriver taskDriver,
            string? requestId,
            IReadOnlyList<ClientStructuredToolCall> approvedCalls,
            IReadOnlyDictionary<string, string?> approvedReasons)
        {
            var sanitizedCalls = approvedCalls.Select(ToolCallSummaryFormatter.StripPreApproval).ToList();
            var results = ExecuteCallsSafely(sanitizedCalls, requestId, playerApproved: false);

            foreach (var call in sanitizedCalls)
            {
                taskDriver.BroadcastDecisionExecuted(
                    call.Name,
                    ToolCallSummaryFormatter.ExtractToolCallReason(call) ?? call.Name);
            }

            foreach (var result in results)
            {
                if (result.IsError)
                {
                    RimMindErrors.Warn(
                        $"[RimMind-Advisor] Tool '{result.ToolName ?? "unknown"}' failed for {_pawn.Name.ToStringShort}: {result.Content}");
                }

                approvedReasons.TryGetValue(result.ToolCallId ?? result.ToolName ?? string.Empty, out var reason);
                RecordHistory(result.ToolName ?? "tool", reason, result.IsError ? result.Content : "approved");
            }

#if !RIMMIND_ADVISOR_TESTS
            AdvisorMotePresenter.ShowExecutedBatchMotes(_pawn, _settings(), sanitizedCalls);
#endif
            return (sanitizedCalls, results);
        }

        public void RecordHistory(string action, string? reason, string result)
        {
            AdvisorHistoryStore.Instance?.AddRecord(_pawn, new AdvisorRequestRecord
            {
                action = action,
                reason = reason ?? string.Empty,
                result = result,
                tick = Find.TickManager.TicksGame
            });
        }
    }
}
