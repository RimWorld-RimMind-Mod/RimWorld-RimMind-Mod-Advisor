using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Models.UI;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Verse;
using ClientStructuredToolCall = RimMind.Domain.Llm.StructuredToolCall;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Orchestrates a single colonist's Advisor request lifecycle: trigger/cooldown, LLM response triage,
    /// direct execution handoff, approval-pipeline routing, iterative tool feedback, and terminal cleanup.
    /// </summary>
    public sealed class AdvisorCycleCoordinator
    {
        private readonly Pawn _pawn;
        private readonly Func<RimMindAdvisorSettings> _settings;
        private readonly AdvisorDirectExecutionHandler _directExecution;
        private readonly ApprovalSubmissionPipeline _approvalPipeline;

        private bool _hasPendingRequest;
        private int _lastRequestTick = -9999;
        private int _pendingRequestTick;
        private AdvisorTaskDriver? _taskDriver;
        private AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult>? _requestCycle;

        public AdvisorCycleCoordinator(
            Pawn pawn,
            Func<RimMindAdvisorSettings> settings,
            IAdvisorToolCallExecutor? toolExecutor = null)
        {
            _pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _directExecution = new AdvisorDirectExecutionHandler(_pawn, _settings, toolExecutor ?? new AdvisorToolCallExecutor());
            _approvalPipeline = new ApprovalSubmissionPipeline(
                _pawn, _settings, IsCurrentCycle,
                _directExecution.ExecuteCallsSafely,
                _directExecution.RecordHistory,
                QueueFeedbackBatch,
                FinishTrackedApproval);
        }

        public bool HasPendingRequest => _hasPendingRequest;
        public int LastRequestTick { get => _lastRequestTick; set => _lastRequestTick = value; }
        public AdvisorTaskDriver? TaskDriver => _taskDriver;
        public int CooldownTicksLeft =>
            AdvisorCooldownCalculator.CooldownTicksLeft(
                AdvisorCooldownCalculator.CalculateCooldownForPawn(_pawn, Settings),
                Find.TickManager.TicksGame,
                _lastRequestTick);

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
            catch (Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] Failed to submit request for {_pawn.Name.ToStringShort}: {ex.Message}");
                CompleteRequestCycle(taskDriver, requestCycle);
            }
        }

        public void ForceRequestAdvice()
        {
            if (_hasPendingRequest)
            {
                if (Find.TickManager.TicksGame - _pendingRequestTick > RimMindTime.TicksPerDay)
                {
                    RimMindErrors.Warn($"[RimMind-Advisor] ForceRequest: {_pawn.Name.ToStringShort} pending request timed out, resetting.");
                    CompleteRequestCycle();
                }
                else
                {
                    RimMindErrors.Warn($"[RimMind-Advisor] ForceRequest: {_pawn.Name.ToStringShort} already has a pending request, skipping.");
                    return;
                }
            }

            _lastRequestTick = -9999;
            RimMindAPI.Request.ClearModCooldown(AdvisorRequestIds.RequestModId);
            Log.Message($"[RimMind-Advisor] ForceRequest: Core-layer cooldown cleared ({AdvisorRequestIds.RequestModId}), sending request...");
            RequestAdvice(Settings);
        }

        private void OnAdviceReceived(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle,
            bool isFeedbackResponse,
            Result<LlmResponse, RimMindError> result)
        {
            if (!IsCurrentCycle(taskDriver, requestCycle)) return;
            if (isFeedbackResponse)
            {
                if (!requestCycle.FeedbackInFlight) return;
                requestCycle.FinishFeedback();
            }

            if (!RimMindPawnLookup.IsActiveSpawnedColonist(_pawn) || _pawn.Downed)
            {
                CompleteRequestCycle(taskDriver, requestCycle);
                return;
            }

            if (result.IsErr)
            {
                if (result.Error.Code != RimMindErrorCode.Cancelled)
                    RimMindErrors.Warn($"[RimMind-Advisor] Request failed for {_pawn.Name.ToStringShort}: {result.Error}");
                CompleteRequestCycle(taskDriver, requestCycle);
                return;
            }

            var response = result.Value;
            List<ClientStructuredToolCall>? toolCalls = ExtractResponseToolCalls(taskDriver, response, out bool parseFailed);
            if (parseFailed)
            {
                CompleteRequestCycle(taskDriver, requestCycle);
                return;
            }

            if (toolCalls == null || toolCalls.Count == 0)
            {
                if (!isFeedbackResponse)
                    RimMindErrors.Warn($"[RimMind-Advisor] No actionable response for {_pawn.Name.ToStringShort} (no tool_calls, content unparseable)");
                CompleteRequestCycle(taskDriver, requestCycle);
                return;
            }

            DispatchResponseBatch(taskDriver, requestCycle, response.RequestId, toolCalls);
            TryAdvanceRequestCycle(taskDriver, requestCycle);
        }

        private static List<ClientStructuredToolCall>? ExtractResponseToolCalls(
            AdvisorTaskDriver taskDriver, LlmResponse response, out bool parseFailed)
        {
            parseFailed = false;
            if (!string.IsNullOrEmpty(response.ToolCallsJson))
            {
                taskDriver.SetReasoningContent(response.ReasoningContent);
                if (!taskDriver.TryParseToolCalls(response.ToolCallsJson ?? string.Empty, out var parsed))
                {
                    parseFailed = true;
                    return null;
                }
                return parsed;
            }

            if (!string.IsNullOrEmpty(response.Content))
            {
                taskDriver.SetReasoningContent(response.ReasoningContent);
                return taskDriver.TryParseContentAsToolCallsIfEnabled(response.Content);
            }

            return null;
        }

        private void DispatchResponseBatch(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle,
            string? requestId,
            List<ClientStructuredToolCall> toolCalls)
        {
            var approvedCalls = new List<ClientStructuredToolCall>();
            var approvedReasons = new Dictionary<string, string?>();
            var deferredCalls = new List<(ClientStructuredToolCall toolCall, RiskLevel riskLevel)>();

            requestCycle.BeginResponseBatch();
            try
            {
                foreach (var toolCall in toolCalls)
                {
                    if (string.IsNullOrEmpty(toolCall.Name)) continue;
                    string toolName = toolCall.Name!;
                    if (RimMindAPI.ToolSet.Registry.FindById(toolName) == null)
                    {
                        RimMindErrors.Warn($"[RimMind-Advisor] Unknown tool call '{toolName}' for {_pawn.Name.ToStringShort}, skipping.");
                        continue;
                    }

                    var preflightArgs = new ToolCallArgs
                    {
                        ToolCallId = toolCall.Id ?? string.Empty,
                        ToolName = toolName,
                        ArgumentsJson = string.IsNullOrWhiteSpace(toolCall.Arguments) ? "{}" : toolCall.Arguments,
                        PawnId = _pawn.thingIDNumber
                    };

                    if (AdvisorToolRiskResolver.IsAutonomyForbidden(_pawn, toolName, preflightArgs))
                    {
                        _directExecution.RecordHistory(toolName, ExtractToolCallReason(toolCall) ?? toolName, "blocked");
                        RimMindErrors.Warn($"[RimMind-Advisor] Tool '{toolName}' blocked by autonomy policy for {_pawn.Name.ToStringShort}.");
                        continue;
                    }

                    var riskLevel = AdvisorToolRiskResolver.Resolve(toolName, preflightArgs);
                    if (ShouldDeferForApproval(toolCall, riskLevel, preflightArgs))
                    {
                        deferredCalls.Add((toolCall, riskLevel));
                    }
                    else
                    {
                        approvedCalls.Add(toolCall);
                        approvedReasons[toolCall.Id ?? toolName] = ExtractToolCallReason(toolCall) ?? toolName;
                    }
                }

                if (deferredCalls.Count > 1)
                    _approvalPipeline.SubmitCabinet(deferredCalls, taskDriver, requestCycle, requestId);
                else if (deferredCalls.Count == 1)
                    SubmitToolCallForApproval(deferredCalls[0].toolCall, deferredCalls[0].riskLevel, taskDriver, requestCycle, requestId);

                if (approvedCalls.Count > 0)
                {
                    var (executedCalls, results) = _directExecution.ExecuteDirectBatch(taskDriver, requestId, approvedCalls, approvedReasons);
                    QueueFeedbackBatch(requestCycle, executedCalls, results);
                }
            }
            catch (Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] Failed to process response for {_pawn.Name.ToStringShort}: {ex.Message}");
            }
            finally
            {
                if (IsCurrentCycle(taskDriver, requestCycle) && requestCycle.ResponseBatchOpen)
                    requestCycle.EndResponseBatch();
            }
        }

        private bool SubmitToolCallForApproval(
            ClientStructuredToolCall toolCall, RiskLevel riskLevel,
            AdvisorTaskDriver taskDriver, AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle,
            string? requestId = null)
            => _approvalPipeline.SubmitSingle(toolCall, riskLevel, taskDriver, requestCycle, requestId);

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
                if (!requestCycle.TryStartFeedback(out var calls, out var results)) return;

                try
                {
                    taskDriver.RequestToolFeedback(calls, results, result =>
                        OnAdviceReceived(taskDriver, requestCycle, isFeedbackResponse: true, result));
                }
                catch (Exception ex)
                {
                    requestCycle.FinishFeedback();
                    RimMindErrors.Warn($"[RimMind-Advisor] Failed to submit feedback request for {_pawn.Name.ToStringShort}: {ex.Message}");
                    CompleteRequestCycle(taskDriver, requestCycle);
                }
                return;
            }

            if (requestCycle.CanComplete) CompleteRequestCycle(taskDriver, requestCycle);
        }

        private bool IsCurrentCycle(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle)
            => ReferenceEquals(_taskDriver, taskDriver) && ReferenceEquals(_requestCycle, requestCycle);

        private void CompleteRequestCycle(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle)
        {
            if (IsCurrentCycle(taskDriver, requestCycle)) CompleteRequestCycle();
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
            if (cancellationErrors == null) return;

            foreach (var error in cancellationErrors)
                RimMindErrors.Warn($"[RimMind-Advisor] Failed to cancel a pending approval for {_pawn.Name.ToStringShort}: {error.Message}");
        }

        private bool ShouldDeferForApproval(ClientStructuredToolCall call, RiskLevel riskLevel, ToolCallArgs preflightArgs) =>
            AdvisorApprovalPolicy.RequiresApproval(Settings.enableRiskApproval, Settings.autoBlockRiskLevel, riskLevel, call.Arguments) ||
            AdvisorToolRiskResolver.RequiresAutonomyApproval(_pawn, call.Name ?? string.Empty, preflightArgs);

        private void FinishTrackedApproval(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle,
            RequestEntry? approvalEntry)
        {
            if (approvalEntry == null || !IsCurrentCycle(taskDriver, requestCycle) || !requestCycle.TryFinishApproval(approvalEntry))
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
                RimMindErrors.Warn($"[RimMind-Advisor] Skipping malformed feedback batch for {_pawn.Name.ToStringShort}: {calls.Count} calls but {results.Count} results.");
                return;
            }
            requestCycle.QueueFeedback(calls.Select(ToolCallSummaryFormatter.StripPreApproval).ToList(), results);
        }

        internal static string? ExtractToolCallReason(ClientStructuredToolCall toolCall)
            => ToolCallSummaryFormatter.ExtractToolCallReason(toolCall);

        internal static string FormatHumanReadableToolName(string? rawName)
            => ToolCallSummaryFormatter.FormatHumanReadableToolName(rawName);

        internal static string StripPreApprovalArgument(string? argumentsJson)
            => ToolCallSummaryFormatter.StripPreApprovalArgument(argumentsJson);

        internal static string FormatAdvisorMoteText(string text, int maxLineLength = 24, int maxTotalLength = 66)
            => AdvisorMotePresenter.FormatAdvisorMoteText(text, maxLineLength, maxTotalLength);
    }
}
