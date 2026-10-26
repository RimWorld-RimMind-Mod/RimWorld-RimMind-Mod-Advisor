using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Models.UI;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using RimWorld;
using UnityEngine;
using Verse;
using ClientStructuredToolCall = RimMind.Domain.Llm.StructuredToolCall;

namespace RimMind.Advisor.Advisor
{
    public sealed class AdvisorCycleCoordinator
    {
        private readonly Pawn _pawn;
        private readonly Func<RimMindAdvisorSettings> _settings;
        private readonly IAdvisorToolCallExecutor _toolExecutor;

        private bool _hasPendingRequest;
        private int _lastRequestTick = -9999;
        private int _pendingRequestTick;
        private AdvisorTaskDriver? _taskDriver;
        private AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult>? _requestCycle;
        private ApprovalManager? _approvalManager;

        public AdvisorCycleCoordinator(
            Pawn pawn,
            Func<RimMindAdvisorSettings> settings,
            IAdvisorToolCallExecutor? toolExecutor = null)
        {
            _pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _toolExecutor = toolExecutor ?? new AdvisorToolCallExecutor();
        }

        public bool HasPendingRequest => _hasPendingRequest;
        public int LastRequestTick { get => _lastRequestTick; set => _lastRequestTick = value; }
        public AdvisorTaskDriver? TaskDriver => _taskDriver;
        public int CooldownTicksLeft =>
            Math.Max(0, AdvisorCooldownCalculator.CalculateCooldownForPawn(_pawn, Settings) - (Find.TickManager.TicksGame - _lastRequestTick));

        private RimMindAdvisorSettings Settings => _settings();

        public void RequestAdvice(RimMindAdvisorSettings settings)
        {
            _hasPendingRequest = true;
            _pendingRequestTick = Find.TickManager.TicksGame;
            AdvisorConcurrencyTracker.Increment();

            var taskDriver = new AdvisorTaskDriver(_pawn, settings);
            var requestCycle = new AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult>();
            _taskDriver = taskDriver;
            _requestCycle = requestCycle;

            try
            {
                taskDriver.BuildAndSendRequest(result =>
                    OnAdviceReceived(taskDriver, requestCycle, isFeedbackResponse: false, result));
            }
            catch (Exception exception)
            {
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Failed to submit request for {_pawn.Name.ToStringShort}: {exception.Message}");
                CompleteRequestCycle(taskDriver, requestCycle);
            }
        }

        public void ForceRequestAdvice()
        {
            if (_hasPendingRequest)
            {
                if (Find.TickManager.TicksGame - _pendingRequestTick > RimMindTime.TicksPerDay)
                {
                    RimMindErrors.Warn(
                        $"[RimMind-Advisor] ForceRequest: {_pawn.Name.ToStringShort} pending request timed out, resetting.");
                    CompleteRequestCycle();
                }
                else
                {
                    RimMindErrors.Warn(
                        $"[RimMind-Advisor] ForceRequest: {_pawn.Name.ToStringShort} already has a pending request, skipping.");
                    return;
                }
            }

            _lastRequestTick = -9999;
            RimMindAPI.ClearModCooldown("Advisor");
            Log.Message("[RimMind-Advisor] ForceRequest: Core-layer cooldown cleared (Advisor), sending request...");
            RequestAdvice(Settings);
        }

        private void OnAdviceReceived(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle,
            bool isFeedbackResponse,
            Result<LlmResponse, RimMindError> result)
        {
            if (!IsCurrentCycle(taskDriver, requestCycle))
                return;

            if (isFeedbackResponse)
            {
                if (!requestCycle.FeedbackInFlight)
                    return;
                requestCycle.FinishFeedback();
            }

            if (!RimMindPawnLookup.IsActiveSpawnedColonist(_pawn) || _pawn.Downed)
            {
                CompleteRequestCycle(taskDriver, requestCycle);
                return;
            }

            if (result.IsErr && result.Error.Code == RimMindErrorCode.Cancelled) { CompleteRequestCycle(taskDriver, requestCycle); return; } if (result.IsErr)
            {
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Request failed for {_pawn.Name.ToStringShort}: {result.Error}");
                CompleteRequestCycle(taskDriver, requestCycle);
                return;
            }

            var response = result.Value;
            List<ClientStructuredToolCall>? toolCalls = null;

            if (!string.IsNullOrEmpty(response.ToolCallsJson))
            {
                taskDriver.SetReasoningContent(response.ReasoningContent);
                if (!taskDriver.TryParseToolCalls(response.ToolCallsJson ?? string.Empty, out toolCalls))
                {
                    CompleteRequestCycle(taskDriver, requestCycle);
                    return;
                }
            }
            else if (!string.IsNullOrEmpty(response.Content))
            {
                taskDriver.SetReasoningContent(response.ReasoningContent);
                toolCalls = taskDriver.TryParseContentAsToolCallsIfEnabled(response.Content);
            }

            if (toolCalls == null || toolCalls.Count == 0)
            {
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] No actionable response for {_pawn.Name.ToStringShort} (no tool_calls, content unparseable)");
                CompleteRequestCycle(taskDriver, requestCycle);
                return;
            }

            var approvedCalls = new List<ClientStructuredToolCall>();
            var approvedReasons = new Dictionary<string, string?>();
            var deferredCalls = new List<(ClientStructuredToolCall toolCall, RiskLevel riskLevel)>();
            requestCycle.BeginResponseBatch();
            try
            {
                foreach (var toolCall in toolCalls)
                {
                    if (toolCall.Name.NullOrEmpty())
                        continue;

                    if (RimMindAPI.Tools.FindById(toolCall.Name) == null)
                    {
                        RimMindErrors.Warn(
                            $"[RimMind-Advisor] Unknown tool call '{toolCall.Name}' for {_pawn.Name.ToStringShort}, skipping.");
                        continue;
                    }

                    if (AdvisorToolRiskResolver.IsAutonomyForbidden(_pawn, toolCall.Name))
                    {
                        var blockedReason = ExtractToolCallReason(toolCall) ?? toolCall.Name;
                        RecordToolHistory(toolCall.Name, blockedReason, "blocked");
                        RimMindErrors.Warn(
                            $"[RimMind-Advisor] Tool '{toolCall.Name}' blocked by autonomy policy for {_pawn.Name.ToStringShort}.");
                        continue;
                    }

                    var riskLevel = AdvisorToolRiskResolver.Resolve(toolCall.Name);
                    if (ShouldDeferForApproval(toolCall, riskLevel))
                    {
                        deferredCalls.Add((toolCall, riskLevel));
                    }
                    else
                    {
                        approvedCalls.Add(toolCall);
                        approvedReasons[toolCall.Id ?? toolCall.Name] =
                            ExtractToolCallReason(toolCall) ?? toolCall.Name;
                    }
                }

                if (deferredCalls.Count > 1)
                {
                    SubmitCabinetProposalForApproval(deferredCalls, taskDriver, requestCycle);
                }
                else if (deferredCalls.Count == 1)
                {
                    SubmitToolCallForApproval(deferredCalls[0].toolCall, deferredCalls[0].riskLevel, taskDriver, requestCycle);
                }

                if (approvedCalls.Count > 0)
                    ExecuteDirectCalls(taskDriver, requestCycle, response.RequestId, approvedCalls, approvedReasons);
            }
            catch (Exception exception)
            {
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Failed to process response for {_pawn.Name.ToStringShort}: {exception.Message}");
            }
            finally
            {
                if (IsCurrentCycle(taskDriver, requestCycle) && requestCycle.ResponseBatchOpen)
                    requestCycle.EndResponseBatch();
            }

            TryAdvanceRequestCycle(taskDriver, requestCycle);
        }

        private void ExecuteDirectCalls(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle,
            string? requestId,
            IReadOnlyList<ClientStructuredToolCall> approvedCalls,
            IReadOnlyDictionary<string, string?> approvedReasons)
        {
            var sanitizedCalls = approvedCalls.Select(StripPreApproval).ToList();
            var results = ExecuteToolCallsSafely(sanitizedCalls, requestId);

            foreach (var result in results.Where(result => result.IsError))
            {
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Tool '{result.ToolName ?? "unknown"}' failed for {_pawn.Name.ToStringShort}: {result.Content}");
            }

            foreach (var call in sanitizedCalls)
                taskDriver.BroadcastDecisionExecuted(call.Name, ExtractToolCallReason(call) ?? call.Name);

            var historyStore = AdvisorHistoryStore.Instance;
            if (historyStore != null)
            {
                foreach (var result in results)
                {
                    var resultKey = result.ToolCallId ?? result.ToolName ?? string.Empty;
                    approvedReasons.TryGetValue(resultKey, out var reason);
                    historyStore.AddRecord(_pawn, new AdvisorRequestRecord
                    {
                        action = result.ToolName ?? "tool",
                        reason = reason ?? string.Empty,
                        result = result.IsError ? result.Content : "approved",
                        tick = Find.TickManager.TicksGame
                    });
                }
            }

            if (Settings.showThoughtBubble && _pawn.Map != null)
            {
                var reasons = sanitizedCalls
                    .Select(call => ExtractToolCallReason(call) ?? call.Name)
                    .Where(reason => !reason.NullOrEmpty())
                    .ToList();
                if (reasons.Count > 0)
                {
                    var moteText = reasons.Count == 1
                        ? $"[RimMind] {reasons[0]}"
                        : $"[RimMind] {reasons[0]} / {reasons[1]}";
                    MoteMaker.ThrowText(
                        _pawn.DrawPos,
                        _pawn.Map,
                        FormatAdvisorMoteText(moteText),
                        new Color(0.6f, 0.9f, 1f),
                        5f);
                }
            }

            QueueFeedbackBatch(requestCycle, sanitizedCalls, results);
        }

        private bool IsCurrentCycle(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle) =>
            ReferenceEquals(_taskDriver, taskDriver) &&
            ReferenceEquals(_requestCycle, requestCycle);

        private void TryAdvanceRequestCycle(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle)
        {
            if (!IsCurrentCycle(taskDriver, requestCycle) ||
                requestCycle.ResponseBatchOpen ||
                requestCycle.PendingApprovals > 0 ||
                requestCycle.FeedbackInFlight)
                return;

            if (requestCycle.HasQueuedFeedback)
            {
                if (!taskDriver.ShouldRequestFeedback())
                {
                    requestCycle.DiscardQueuedFeedback();
                    CompleteRequestCycle(taskDriver, requestCycle);
                    return;
                }

                if (!requestCycle.TryStartFeedback(out var calls, out var results))
                    return;

                try
                {
                    taskDriver.RequestToolFeedback(
                        calls,
                        results,
                        result => OnAdviceReceived(
                            taskDriver,
                            requestCycle,
                            isFeedbackResponse: true,
                            result));
                }
                catch (Exception exception)
                {
                    requestCycle.FinishFeedback();
                    RimMindErrors.Warn(
                        $"[RimMind-Advisor] Failed to submit feedback request for {_pawn.Name.ToStringShort}: {exception.Message}");
                    CompleteRequestCycle(taskDriver, requestCycle);
                }
                return;
            }

            if (requestCycle.CanComplete)
                CompleteRequestCycle(taskDriver, requestCycle);
        }

        private void CompleteRequestCycle(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle)
        {
            if (IsCurrentCycle(taskDriver, requestCycle))
                CompleteRequestCycle();
        }

        private void CompleteRequestCycle()
        {
            if (_hasPendingRequest)
            {
                _hasPendingRequest = false;
                _lastRequestTick = Find.TickManager.TicksGame;
                AdvisorConcurrencyTracker.Decrement();
            }

            var completedDriver = _taskDriver;
            var completedCycle = _requestCycle;
            _taskDriver = null;
            _requestCycle = null;
            var cancellationErrors = completedCycle?.CancelPendingApprovals();
            completedDriver?.ClearState();
            if (cancellationErrors == null)
                return;

            foreach (var error in cancellationErrors)
            {
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Failed to cancel a pending approval for {_pawn.Name.ToStringShort}: {error.Message}");
            }
        }

        private bool ShouldDeferForApproval(ClientStructuredToolCall call, RiskLevel riskLevel) =>
            ShouldDeferForApproval(riskLevel, call.Arguments) ||
            AdvisorToolRiskResolver.RequiresAutonomyApproval(_pawn, call.Name ?? string.Empty);

        private bool ShouldDeferForApproval(RiskLevel riskLevel, string? arguments) =>
            AdvisorApprovalPolicy.RequiresApproval(
                Settings.enableRiskApproval,
                Settings.autoBlockRiskLevel,
                riskLevel,
                arguments);

        private bool SubmitToolCallForApproval(
            ClientStructuredToolCall toolCall,
            RiskLevel riskLevel,
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle)
        {
            var reason = ExtractToolCallReason(toolCall) ?? toolCall.Name;
            var arguments = ParseToolCallArguments(toolCall.Arguments);
            arguments.TryGetValue("target", out var targetName);
            arguments.TryGetValue("param", out var parameter);

            if (!Settings.enableRequestSystem)
            {
                RecordToolHistory(toolCall.Name, reason, "blocked");
                return false;
            }

            _approvalManager ??= new ApprovalManager(Settings);
            var adviceItem = new AdviceItem
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

            RequestEntry? approvalEntry = null;
            try
            {
                approvalEntry = _approvalManager.SubmitForApproval(
                    adviceItem,
                    _pawn,
                    onApproved: () =>
                    {
                        if (!IsCurrentCycle(taskDriver, requestCycle))
                            return;

                        try
                        {
                            if (_pawn == null || !RimMindPawnLookup.IsActiveSpawnedColonist(_pawn) || _pawn.Downed)
                                return;

                            var approvedCall = StripPreApproval(toolCall);
                            var calls = new List<ClientStructuredToolCall> { approvedCall };
                            var results = ExecuteToolCallsSafely(calls, toolCall.Id, playerApproved: true);
                            taskDriver.BroadcastDecisionExecuted(toolCall.Name, reason);
                            foreach (var result in results)
                            {
                                RecordToolHistory(
                                    result.ToolName ?? toolCall.Name,
                                    reason,
                                    result.IsError ? result.Content : "approved");
                                if (result.IsError)
                                {
                                    RimMindErrors.Warn(
                                        $"[RimMind-Advisor] Approved tool '{result.ToolName ?? toolCall.Name}' failed for {_pawn.Name.ToStringShort}: {result.Content}");
                                }
                            }

                            ShowToolThoughtBubble(reason);
                            QueueFeedbackBatch(requestCycle, calls, results);
                        }
                        finally
                        {
                            FinishTrackedApproval(taskDriver, requestCycle, approvalEntry);
                        }
                    },
                    onRejected: () =>
                    {
                        if (!IsCurrentCycle(taskDriver, requestCycle))
                            return;

                        try
                        {
                            RecordToolHistory(toolCall.Name, reason, "rejected");
                        }
                        finally
                        {
                            FinishTrackedApproval(taskDriver, requestCycle, approvalEntry);
                        }
                    },
                    onDismissed: () =>
                        FinishTrackedApproval(taskDriver, requestCycle, approvalEntry),
                    beforeRegister: entry =>
                    {
                        approvalEntry = entry;
                        requestCycle.TrackPendingApproval(
                            entry,
                            () => RimMindAPI.DismissPendingRequest(entry));
                    });
                return true;
            }
            catch (Exception exception)
            {
                if (approvalEntry != null)
                {
                    requestCycle.TryFinishApproval(approvalEntry);
                    RimMindAPI.DismissPendingRequest(approvalEntry);
                }

                RecordToolHistory(toolCall.Name, reason, "approval_error");
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Failed to register approval for '{toolCall.Name}': {exception.Message}");
                return false;
            }
        }

        private bool SubmitCabinetProposalForApproval(
            IReadOnlyList<(ClientStructuredToolCall toolCall, RiskLevel riskLevel)> deferredCalls,
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle)
        {
            if (deferredCalls == null || deferredCalls.Count == 0) return false;

            if (!Settings.enableRequestSystem)
            {
                foreach (var (toolCall, _) in deferredCalls)
                {
                    var reason = ExtractToolCallReason(toolCall) ?? toolCall.Name;
                    RecordToolHistory(toolCall.Name, reason, "blocked");
                }
                return false;
            }

            _approvalManager ??= new ApprovalManager(Settings);

            string title = "RimMind.Advisor.Cabinet.StrategicProposalTitle".CanTranslate()
                ? "RimMind.Advisor.Cabinet.StrategicProposalTitle".Translate(_pawn.Name?.ToStringShort ?? "Advisor").ToString()
                : $"[Cabinet Card] Strategic Plan by {_pawn.Name?.ToStringShort ?? "Advisor"}";

            var proposal = new CabinetProposal
            {
                Title = title,
                Summary = taskDriver.LastReasoningContent ?? ""
            };

            var callsToExecute = new List<ClientStructuredToolCall>();
            var reasonsToRecord = new List<string>();
            var approvedIndices = new HashSet<int>();

            for (int i = 0; i < deferredCalls.Count; i++)
            {
                var (toolCall, riskLevel) = deferredCalls[i];
                var reason = ExtractToolCallReason(toolCall) ?? toolCall.Name;
                var arguments = ParseToolCallArguments(toolCall.Arguments);
                arguments.TryGetValue("target", out var targetName);
                arguments.TryGetValue("param", out var parameter);

                proposal.Items.Add(new AdviceItem
                {
                    Action = toolCall.Name,
                    Target = targetName,
                    Param = parameter,
                    Reason = reason,
                    RiskLevel = riskLevel,
                    request_type = AdvisorApprovalPolicy.IsExplicitRequest(toolCall.Arguments) ? "request" : "normal"
                });

                callsToExecute.Add(toolCall);
                reasonsToRecord.Add(string.IsNullOrWhiteSpace(reason) ? "Cabinet Plan" : reason);

                int capturedIndex = i;
                proposal.ExecutionCallbacks.Add(() => approvedIndices.Add(capturedIndex));
            }

            RequestEntry? approvalEntry = null;
            try
            {
                approvalEntry = _approvalManager.SubmitCabinetProposal(
                    proposal,
                    _pawn,
                    onAllApproved: () =>
                    {
                        if (!IsCurrentCycle(taskDriver, requestCycle))
                            return;

                        try
                        {
                            if (_pawn == null || !RimMindPawnLookup.IsActiveSpawnedColonist(_pawn) || _pawn.Downed)
                                return;

                            var indicesToRun = approvedIndices.Count > 0
                                ? approvedIndices.OrderBy(idx => idx).ToList()
                                : Enumerable.Range(0, callsToExecute.Count).ToList();

                            for (int i = 0; i < callsToExecute.Count; i++)
                            {
                                if (!indicesToRun.Contains(i))
                                {
                                    var rejectedReason = !string.IsNullOrWhiteSpace(reasonsToRecord[i]) ? reasonsToRecord[i] : "Cabinet Plan";
                                    RecordToolHistory(callsToExecute[i].Name, rejectedReason, "rejected");
                                }
                            }

                            var selectedCalls = indicesToRun.Select(i => callsToExecute[i]).ToList();
                            var selectedReasons = indicesToRun.Select(i => !string.IsNullOrWhiteSpace(reasonsToRecord[i]) ? reasonsToRecord[i] : "Cabinet Plan").ToList();
                            var preApprovedCalls = selectedCalls.Select(StripPreApproval).ToList();
                            var results = ExecuteToolCallsSafely(preApprovedCalls, null, playerApproved: true);
                            for (int i = 0; i < selectedCalls.Count; i++)
                            {
                                var call = selectedCalls[i];
                                var rsn = selectedReasons[i];
                                taskDriver.BroadcastDecisionExecuted(call.Name, rsn);
                            }

                            for (int i = 0; i < results.Count; i++)
                            {
                                var result = results[i];
                                var callId = result.ToolCallId ?? result.ToolName ?? "tool";
                                var rsn = i < selectedReasons.Count && !string.IsNullOrWhiteSpace(selectedReasons[i])
                                    ? selectedReasons[i]
                                    : "Cabinet Plan";
                                RecordToolHistory(
                                    result.ToolName ?? callId,
                                    rsn,
                                    result.IsError ? result.Content : "approved");
                                if (result.IsError)
                                {
                                    RimMindErrors.Warn(
                                        $"[RimMind-Advisor] Cabinet tool '{result.ToolName ?? "unknown"}' failed for {_pawn.Name?.ToStringShort ?? _pawn.LabelShort}: {result.Content}");
                                }
                            }

                            string executedMote = "RimMind.Advisor.Cabinet.ExecutedMote".CanTranslate()
                                ? "RimMind.Advisor.Cabinet.ExecutedMote".Translate().ToString()
                                : "[RimMind] Cabinet plan approved & executed!";
                            ShowToolThoughtBubble(executedMote);
                            QueueFeedbackBatch(requestCycle, selectedCalls, results);
                        }
                        finally
                        {
                            FinishTrackedApproval(taskDriver, requestCycle, approvalEntry);
                        }
                    },
                    onRejected: () =>
                    {
                        if (!IsCurrentCycle(taskDriver, requestCycle))
                            return;

                        try
                        {
                            for (int i = 0; i < callsToExecute.Count; i++)
                            {
                                var rsn = !string.IsNullOrWhiteSpace(reasonsToRecord[i]) ? reasonsToRecord[i] : "Cabinet Plan";
                                RecordToolHistory(callsToExecute[i].Name, rsn, "rejected");
                            }
                        }
                        finally
                        {
                            FinishTrackedApproval(taskDriver, requestCycle, approvalEntry);
                        }
                    },
                    onDismissed: () =>
                        FinishTrackedApproval(taskDriver, requestCycle, approvalEntry),
                    beforeRegister: entry =>
                    {
                        approvalEntry = entry;
                        requestCycle.TrackPendingApproval(
                            entry,
                            () => RimMindAPI.DismissPendingRequest(entry));
                    });

                return true;
            }
            catch (Exception exception)
            {
                if (approvalEntry != null)
                {
                    requestCycle.TryFinishApproval(approvalEntry);
                    RimMindAPI.DismissPendingRequest(approvalEntry);
                }

                for (int i = 0; i < callsToExecute.Count; i++)
                {
                    var rsn = !string.IsNullOrWhiteSpace(reasonsToRecord[i]) ? reasonsToRecord[i] : "Cabinet Plan";
                    RecordToolHistory(callsToExecute[i].Name, rsn, "approval_error");
                }
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Failed to register cabinet approval for {_pawn.Name?.ToStringShort ?? _pawn.LabelShort}: {exception.Message}");
                return false;
            }
        }

        private void FinishTrackedApproval(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle,
            RequestEntry? approvalEntry)
        {
            if (approvalEntry == null ||
                !IsCurrentCycle(taskDriver, requestCycle) ||
                !requestCycle.TryFinishApproval(approvalEntry))
                return;

            TryAdvanceRequestCycle(taskDriver, requestCycle);
        }

        private void QueueFeedbackBatch(
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle,
            IReadOnlyList<ClientStructuredToolCall> calls,
            IReadOnlyList<ToolResult> results)
        {
            if (calls.Count != results.Count)
            {
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Skipping malformed feedback batch for {_pawn.Name.ToStringShort}: " +
                    $"{calls.Count} calls but {results.Count} results.");
                return;
            }

            var cleanCalls = calls.Select(StripPreApproval).ToList();
            requestCycle.QueueFeedback(cleanCalls, results);
        }

        internal static string? ExtractToolCallReason(ClientStructuredToolCall toolCall)
        {
            var arguments = ParseToolCallArguments(toolCall.Arguments);
            if (arguments.TryGetValue("reason", out var reason) && !reason.NullOrEmpty())
                return reason;

            var readableTool = FormatHumanReadableToolName(toolCall.Name);
            string[] detailKeys =
            {
                "building_def", "recipe_def", "zone_type", "work_type",
                "resource_type", "job_type", "item_def", "target", "operation"
            };
            foreach (var key in detailKeys)
            {
                if (arguments.TryGetValue(key, out var detail) && !detail.NullOrEmpty())
                {
                    return $"{readableTool}: {FormatHumanReadableToolName(detail)}";
                }
            }

            return readableTool;
        }

        internal static string FormatHumanReadableToolName(string? rawName) =>
            ApprovalManager.FormatHumanReadableToolName(rawName);

        private List<ToolResult> ExecuteToolCallsSafely(
            IReadOnlyList<ClientStructuredToolCall> calls,
            string? traceId,
            bool playerApproved = false)
        {
            try
            {
                // Approval travels via ToolCallArgs.IsPlayerApproved (trusted channel), never via LLM JSON.
                var executionTask = _toolExecutor.ExecuteAsync(
                    calls,
                    $"NPC-{_pawn.thingIDNumber}",
                    traceId,
                    CancellationToken.None,
                    playerApproved);

                if (!executionTask.IsCompleted)
                {
                    RimMind.Application.Common.Threading.SynchronousTaskGate.ObserveFault(executionTask);
                    RimMindErrors.Warn(
                        "[RimMind-Advisor][ToolCall][MainThreadWait] " +
                        $"Tool execution did not complete synchronously on main thread for {_pawn.Name.ToStringShort} " +
                        $"(trace={traceId ?? "unknown"}, tools={calls.Count}); failing fast to avoid main-thread freeze.");
                    return calls
                        .Select(call => ToolResult.Fail(
                            RimMind.Application.Common.Threading.SynchronousTaskGate.AsyncNotSupportedMessage,
                            call.Id,
                            call.Name))
                        .ToList();
                }

                return executionTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Tool execution failed for {_pawn.Name.ToStringShort}: {exception.Message}");
                return calls
                    .Select(call => ToolResult.Fail(
                        $"Tool execution failed: {exception.Message}",
                        call.Id,
                        call.Name))
                    .ToList();
            }
        }

        private static ClientStructuredToolCall StripPreApproval(ClientStructuredToolCall toolCall)
        {
            var stripped = StripPreApprovalArgument(toolCall.Arguments);
            return new ClientStructuredToolCall
            {
                Id = toolCall.Id,
                Name = toolCall.Name,
                Arguments = stripped
            };
        }

        internal static string StripPreApprovalArgument(string? argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return argumentsJson ?? string.Empty;

            try
            {
                var jobj = JObject.Parse(argumentsJson!);
                bool removed = false;
                if (jobj.Remove("pre_approved")) removed = true;
                if (jobj.Remove("is_approved")) removed = true;
                return removed ? jobj.ToString(Formatting.None) : argumentsJson!;
            }
            catch
            {
                return argumentsJson!;
            }
        }

        private static Dictionary<string, string> ParseToolCallArguments(string? arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                return new Dictionary<string, string>();

            try
            {
                var dict = new Dictionary<string, string>();
                var jobj = JObject.Parse(arguments!);
                foreach (var prop in jobj.Properties())
                {
                    if (prop.Value != null && prop.Value.Type != JTokenType.Null)
                    {
                        dict[prop.Name] = prop.Value.ToString();
                    }
                }
                return dict;
            }
            catch (Exception exception)
            {
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Failed to parse tool call arguments: {exception.Message}");
                return new Dictionary<string, string>();
            }
        }

        private void RecordToolHistory(string action, string? reason, string result)
        {
            var historyStore = AdvisorHistoryStore.Instance;
            if (historyStore == null)
                return;

            historyStore.AddRecord(_pawn, new AdvisorRequestRecord
            {
                action = action,
                reason = reason ?? string.Empty,
                result = result,
                tick = Find.TickManager.TicksGame
            });
        }

        private void ShowToolThoughtBubble(string? reason)
        {
            if (!Settings.showThoughtBubble || _pawn.Map == null || reason.NullOrEmpty())
                return;

            MoteMaker.ThrowText(
                _pawn.DrawPos,
                _pawn.Map,
                FormatAdvisorMoteText($"[RimMind] {reason}"),
                new Color(0.6f, 0.9f, 1f),
                5f);
        }

        internal static string FormatAdvisorMoteText(string text, int maxLineLength = 24, int maxTotalLength = 66)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (normalized.Length > maxTotalLength)
            {
                normalized = normalized.Substring(0, maxTotalLength - 1).TrimEnd() + "…";
            }

            if (normalized.Length <= maxLineLength)
                return normalized;

            var words = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder(normalized.Length + 4);
            int lineLen = 0;

            foreach (var word in words)
            {
                if (lineLen > 0 && lineLen + 1 + word.Length > maxLineLength)
                {
                    sb.Append('\n');
                    lineLen = 0;
                }

                if (word.Length > maxLineLength)
                {
                    int offset = 0;
                    while (offset < word.Length)
                    {
                        if (lineLen > 0)
                        {
                            sb.Append('\n');
                            lineLen = 0;
                        }
                        int take = Math.Min(maxLineLength, word.Length - offset);
                        sb.Append(word, offset, take);
                        lineLen = take;
                        offset += take;
                    }
                }
                else
                {
                    if (lineLen > 0)
                    {
                        sb.Append(' ');
                        lineLen++;
                    }
                    sb.Append(word);
                    lineLen += word.Length;
                }
            }

            return sb.ToString();
        }
    }
}
