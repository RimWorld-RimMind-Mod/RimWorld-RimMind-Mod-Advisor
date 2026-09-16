using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Models.UI;
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
        public int LastRequestTick => _lastRequestTick;
        public AdvisorTaskDriver? TaskDriver => _taskDriver;
        public int CooldownTicksLeft =>
            Math.Max(0, Settings.requestCooldownTicks - (Find.TickManager.TicksGame - _lastRequestTick));

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
                if (Find.TickManager.TicksGame - _pendingRequestTick > 60000)
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

            if (_pawn.Dead || _pawn.Map == null)
            {
                CompleteRequestCycle(taskDriver, requestCycle);
                return;
            }

            if (result.IsErr)
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
                if (toolCalls != null)
                {
                    Log.Message(
                        $"[RimMind-Advisor] Parsed {toolCalls.Count} action(s) from content fallback for {_pawn.Name.ToStringShort}");
                }
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

                    var riskLevel = AdvisorToolRiskResolver.Resolve(toolCall.Name);
                    if (ShouldDeferForApproval(riskLevel, toolCall.Arguments))
                    {
                        SubmitToolCallForApproval(toolCall, riskLevel, taskDriver, requestCycle);
                    }
                    else
                    {
                        approvedCalls.Add(toolCall);
                        approvedReasons[toolCall.Id ?? toolCall.Name] =
                            ExtractToolCallReason(toolCall) ?? toolCall.Name;
                    }
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
            var results = ExecuteToolCallsSafely(approvedCalls, requestId);
            var succeeded = results.Count(result => !result.IsError);
            Log.Message(
                $"[RimMind-Advisor] ToolCalls: executed {succeeded}/{approvedCalls.Count} tools for {_pawn.Name.ToStringShort}");

            foreach (var result in results.Where(result => result.IsError))
            {
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Tool '{result.ToolName ?? "unknown"}' failed for {_pawn.Name.ToStringShort}: {result.Content}");
            }

            foreach (var call in approvedCalls)
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
                var reasons = approvedCalls
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
                        moteText,
                        new Color(0.6f, 0.9f, 1f),
                        5f);
                }
            }

            QueueFeedbackBatch(requestCycle, approvedCalls, results);
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
                    Log.Message(
                        $"[RimMind-Advisor] Max tool call depth ({AdvisorTaskDriver.MaxToolCallDepth}) reached for {_pawn.Name.ToStringShort}");
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
                Log.Message(
                    $"[RimMind-Advisor] Tool '{toolCall.Name}' blocked by risk level {riskLevel} (approval system disabled)");
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
                            if (_pawn.Dead || _pawn.Map == null)
                                return;

                            var calls = new List<ClientStructuredToolCall> { toolCall };
                            var results = ExecuteToolCallsSafely(calls, toolCall.Id);
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

            requestCycle.QueueFeedback(calls, results);
        }

        private static string? ExtractToolCallReason(ClientStructuredToolCall toolCall)
        {
            var arguments = ParseToolCallArguments(toolCall.Arguments);
            return arguments.TryGetValue("reason", out var reason) && !reason.NullOrEmpty()
                ? reason
                : null;
        }

        private List<ToolResult> ExecuteToolCallsSafely(
            IReadOnlyList<ClientStructuredToolCall> calls,
            string? traceId)
        {
            try
            {
                var executionTask = _toolExecutor.ExecuteAsync(
                    calls,
                    $"NPC-{_pawn.thingIDNumber}",
                    traceId,
                    CancellationToken.None);

                if (!executionTask.IsCompleted)
                {
                    RimMindErrors.Warn(
                        "[RimMind-Advisor][ToolCall][MainThreadWait] " +
                        $"Tool execution is asynchronous; preserving main-thread completion for {_pawn.Name.ToStringShort} " +
                        $"(trace={traceId ?? "unknown"}, tools={calls.Count}).");
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

        private static Dictionary<string, string> ParseToolCallArguments(string? arguments)
        {
            if (arguments == null || arguments.Trim().Length == 0)
                return new Dictionary<string, string>();

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(arguments)
                    ?? new Dictionary<string, string>();
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
                $"[RimMind] {reason}",
                new Color(0.6f, 0.9f, 1f),
                5f);
        }
    }
}
