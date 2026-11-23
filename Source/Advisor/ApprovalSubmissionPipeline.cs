using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Models.UI;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Verse;
using ClientStructuredToolCall = RimMind.Domain.Llm.StructuredToolCall;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Unified single-action and multi-action cabinet approval submission pipeline.
    /// Centralizes approval card construction, request-cycle tracking, player-approved execution,
    /// rejection history logging, mote feedback, and registration rollback.
    /// </summary>
    internal sealed class ApprovalSubmissionPipeline
    {
        private readonly Pawn _pawn;
        private readonly Func<RimMindAdvisorSettings> _settings;
        private readonly Func<AdvisorTaskDriver, AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult>, bool> _isCurrentCycle;
        private readonly Func<IReadOnlyList<ClientStructuredToolCall>, string?, bool, List<ToolResult>> _executeCallsSafely;
        private readonly Action<string, string?, string> _recordHistory;
        private readonly Action<AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult>, IReadOnlyList<ClientStructuredToolCall>, IReadOnlyList<ToolResult>> _queueFeedbackBatch;
        private readonly Action<AdvisorTaskDriver, AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult>, RequestEntry?> _finishTrackedApproval;

        private ApprovalManager? _approvalManager;

        public ApprovalSubmissionPipeline(
            Pawn pawn,
            Func<RimMindAdvisorSettings> settings,
            Func<AdvisorTaskDriver, AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult>, bool> isCurrentCycle,
            Func<IReadOnlyList<ClientStructuredToolCall>, string?, bool, List<ToolResult>> executeCallsSafely,
            Action<string, string?, string> recordHistory,
            Action<AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult>, IReadOnlyList<ClientStructuredToolCall>, IReadOnlyList<ToolResult>> queueFeedbackBatch,
            Action<AdvisorTaskDriver, AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult>, RequestEntry?> finishTrackedApproval)
        {
            _pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _isCurrentCycle = isCurrentCycle ?? throw new ArgumentNullException(nameof(isCurrentCycle));
            _executeCallsSafely = executeCallsSafely ?? throw new ArgumentNullException(nameof(executeCallsSafely));
            _recordHistory = recordHistory ?? throw new ArgumentNullException(nameof(recordHistory));
            _queueFeedbackBatch = queueFeedbackBatch ?? throw new ArgumentNullException(nameof(queueFeedbackBatch));
            _finishTrackedApproval = finishTrackedApproval ?? throw new ArgumentNullException(nameof(finishTrackedApproval));
        }

        private RimMindAdvisorSettings Settings => _settings();

        public bool SubmitSingle(
            ClientStructuredToolCall toolCall,
            RiskLevel riskLevel,
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle,
            string? requestId = null)
        {
            var reason = ToolCallSummaryFormatter.ExtractToolCallReason(toolCall) ?? toolCall.Name;
            if (!Settings.enableRequestSystem)
            {
                _recordHistory(toolCall.Name, reason, "blocked");
                return false;
            }

            var adviceItem = BuildAdviceItem(toolCall, riskLevel, reason);
            var singleCalls = new List<ClientStructuredToolCall> { toolCall };
            var singleReasons = new List<string> { reason };

            return SubmitTrackedApproval(
                taskDriver,
                requestCycle,
                singleCalls,
                singleReasons,
                register: (onApproved, onRejected, onDismissed, beforeRegister) =>
                {
                    _approvalManager ??= new ApprovalManager(Settings);
                    return _approvalManager.SubmitForApproval(
                        adviceItem,
                        _pawn,
                        onApproved,
                        onRejected,
                        onDismissed,
                        beforeRegister);
                },
                approvedTraceId: requestId ?? toolCall.Id,
                moteOnApproved: reason,
                errorContext: $"'{toolCall.Name}'");
        }

        public bool SubmitCabinet(
            IReadOnlyList<(ClientStructuredToolCall toolCall, RiskLevel riskLevel)> deferredCalls,
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle,
            string? requestId = null)
        {
            if (deferredCalls == null || deferredCalls.Count == 0)
                return false;

            if (!Settings.enableRequestSystem)
            {
                foreach (var (toolCall, _) in deferredCalls)
                {
                    var reason = ToolCallSummaryFormatter.ExtractToolCallReason(toolCall) ?? toolCall.Name;
                    _recordHistory(toolCall.Name, reason, "blocked");
                }
                return false;
            }

            string pawnName = _pawn.Name?.ToStringShort ?? "Advisor";
            string title = RimMindAPI.Text.TrOr(
                "RimMind.Advisor.Cabinet.StrategicProposalTitle",
                "[Cabinet Card] Strategic Plan by {0}",
                pawnName);

            var proposal = new CabinetProposal
            {
                Title = title,
                Summary = taskDriver.LastReasoningContent ?? string.Empty
            };

            var callsToExecute = new List<ClientStructuredToolCall>(deferredCalls.Count);
            var reasonsToRecord = new List<string>(deferredCalls.Count);

            for (int i = 0; i < deferredCalls.Count; i++)
            {
                var (toolCall, riskLevel) = deferredCalls[i];
                var reason = ToolCallSummaryFormatter.ExtractToolCallReason(toolCall) ?? toolCall.Name;
                proposal.Items.Add(BuildAdviceItem(toolCall, riskLevel, reason));
                callsToExecute.Add(toolCall);
                reasonsToRecord.Add(string.IsNullOrWhiteSpace(reason) ? "Cabinet Plan" : reason);
            }

            string executedMote = RimMindAPI.Text.TrOr(
                "RimMind.Advisor.Cabinet.ExecutedMote",
                "[RimMind] Cabinet plan approved & executed!");

            return SubmitTrackedApproval(
                taskDriver,
                requestCycle,
                callsToExecute,
                reasonsToRecord,
                register: (onApproved, onRejected, onDismissed, beforeRegister) =>
                {
                    _approvalManager ??= new ApprovalManager(Settings);
                    return _approvalManager.SubmitCabinetProposal(
                        proposal,
                        _pawn,
                        onApproved,
                        onRejected,
                        onDismissed,
                        beforeRegister);
                },
                approvedTraceId: requestId,
                moteOnApproved: executedMote,
                errorContext: $"cabinet proposal ({callsToExecute.Count} actions)");
        }

        private bool SubmitTrackedApproval(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle,
            IReadOnlyList<ClientStructuredToolCall> allCalls,
            IReadOnlyList<string> allReasons,
            Func<Action, Action, Action, Action<RequestEntry>, RequestEntry> register,
            string? approvedTraceId,
            string? moteOnApproved,
            string errorContext)
        {
            RequestEntry? approvalEntry = null;
            try
            {
                approvalEntry = register(
                    () =>
                    {
                        if (!_isCurrentCycle(taskDriver, requestCycle))
                            return;

                        try
                        {
                            if (_pawn == null || !RimMindPawnLookup.IsActiveSpawnedColonist(_pawn) || _pawn.Downed)
                                return;

                            var sanitizedCalls = allCalls.Select(ToolCallSummaryFormatter.StripPreApproval).ToList();
                            var results = _executeCallsSafely(sanitizedCalls, approvedTraceId, true);

                            for (int i = 0; i < allCalls.Count; i++)
                            {
                                taskDriver.BroadcastDecisionExecuted(allCalls[i].Name, allReasons[i]);
                            }

                            for (int i = 0; i < results.Count; i++)
                            {
                                var result = results[i];
                                string actionName = result.ToolName ?? (i < allCalls.Count ? allCalls[i].Name : "tool");
                                string reason = i < allReasons.Count && !string.IsNullOrWhiteSpace(allReasons[i])
                                    ? allReasons[i]
                                    : actionName;
                                _recordHistory(actionName, reason, result.IsError ? result.Content : "approved");
                                if (result.IsError)
                                {
                                    RimMindErrors.Warn(
                                        $"[RimMind-Advisor] Approved tool '{actionName}' failed for {_pawn.Name?.ToStringShort ?? _pawn.LabelShort}: {result.Content}");
                                }
                            }

#if !RIMMIND_ADVISOR_TESTS
                            AdvisorMotePresenter.ShowToolThoughtBubble(_pawn, Settings, moteOnApproved);
#endif
                            _queueFeedbackBatch(requestCycle, sanitizedCalls, results);
                        }
                        finally
                        {
                            _finishTrackedApproval(taskDriver, requestCycle, approvalEntry);
                        }
                    },
                    () =>
                    {
                        if (!_isCurrentCycle(taskDriver, requestCycle))
                            return;

                        try
                        {
                            for (int i = 0; i < allCalls.Count; i++)
                            {
                                _recordHistory(allCalls[i].Name, allReasons[i], "rejected");
                            }
                        }
                        finally
                        {
                            _finishTrackedApproval(taskDriver, requestCycle, approvalEntry);
                        }
                    },
                    () => _finishTrackedApproval(taskDriver, requestCycle, approvalEntry),
                    entry =>
                    {
                        approvalEntry = entry;
                        requestCycle.TrackPendingApproval(
                            entry,
                            () => RimMindAPI.Bus.DismissPendingRequest(entry));
                    });
                return true;
            }
            catch (Exception exception)
            {
                if (approvalEntry != null)
                {
                    requestCycle.TryFinishApproval(approvalEntry);
                    RimMindAPI.Bus.DismissPendingRequest(approvalEntry);
                }

                for (int i = 0; i < allCalls.Count; i++)
                {
                    _recordHistory(allCalls[i].Name, allReasons[i], "approval_error");
                }
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Failed to register approval for {errorContext} ({_pawn.Name?.ToStringShort ?? _pawn.LabelShort}): {exception.Message}");
                return false;
            }
        }

        private static AdviceItem BuildAdviceItem(
            ClientStructuredToolCall toolCall,
            RiskLevel riskLevel,
            string? reason)
        {
            var arguments = ToolCallSummaryFormatter.ParseToolCallArguments(toolCall.Arguments);
            arguments.TryGetValue("target", out var targetName);
            arguments.TryGetValue("param", out var parameter);

            return new AdviceItem
            {
                Action = toolCall.Name,
                Target = targetName,
                Param = parameter,
                Reason = reason,
                RiskLevel = riskLevel,
                request_type = AdvisorApprovalPolicy.IsExplicitRequest(toolCall.Arguments)
                    ? "request"
                    : "normal",
            };
        }
    }
}
