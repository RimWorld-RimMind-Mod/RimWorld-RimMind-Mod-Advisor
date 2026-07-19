using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Models.UI;
using RimMind.Domain.ValueObjects;
using RimMind.Domain.Llm;
using RimMind.Domain.Enums;
using RimMind.Presentation.Api;
using RimWorld;
using UnityEngine;
using Verse;

using ClientStructuredToolCall = RimMind.Domain.Llm.StructuredToolCall;

namespace RimMind.Advisor.Comps
{
    public class CompAIAdvisor : ThingComp
    {
        public bool IsEnabled = false;

        private bool _hasPendingRequest;
        private int _lastRequestTick = -9999;
        private int _pendingRequestTick;

        private AdvisorTaskDriver? _taskDriver;
        private AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult>? _requestCycle;
        private ApprovalManager? _approvalManager;
        private readonly IAdvisorToolCallExecutor _toolExecutor = new AdvisorToolCallExecutor();

        public bool HasPendingRequest => _hasPendingRequest;
        public int LastRequestTick => _lastRequestTick;
        public AdvisorTaskDriver? TaskDriver => _taskDriver;

        public int AdvisorCooldownTicksLeft =>
            System.Math.Max(0, Settings.requestCooldownTicks - (Find.TickManager.TicksGame - _lastRequestTick));

        private Pawn Pawn => (Pawn)parent;
        private RimMindAdvisorSettings Settings => RimMindAdvisorMod.Settings;
        private bool DebugLogging => RimMindAPI.Settings.DebugLogging;

        public bool IsEligible() =>
            Pawn.IsFreeNonSlaveColonist &&
            !Pawn.Dead &&
            !(Pawn.drafter?.Drafted ?? false) &&
            Pawn.needs?.mood != null;

        public bool IsIdle()
        {
            var job = Pawn.jobs?.curJob;
            if (job == null) return true;
            if (job.playerForced) return false;

            var def = job.def;
            return def == JobDefOf.Wait
                || def == JobDefOf.Wait_Wander
                || def == JobDefOf.GotoWander
                || def == JobDefOf.Wait_MaintainPosture;
        }

        public bool IsMoodBelowThreshold()
        {
            var mood = Pawn.needs?.mood;
            if (mood == null) return false;
            return mood.CurLevelPercentage < Settings.moodThreshold;
        }

        public bool ShouldIdleTrigger()
        {
            return Settings.enableIdleTrigger && IsIdle();
        }

        public bool ShouldMoodTrigger()
        {
            return Settings.enableMoodTrigger && IsMoodBelowThreshold();
        }

        public void RequestAdvice(RimMindAdvisorSettings settings)
        {
            _hasPendingRequest = true;
            _pendingRequestTick = Find.TickManager.TicksGame;
            AdvisorConcurrencyTracker.Increment();

            var taskDriver = new AdvisorTaskDriver(Pawn, settings);
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
                RimMindErrors.Warn($"[RimMind-Advisor] Failed to submit request for {Pawn.Name.ToStringShort}: {ex.Message}");
                CompleteRequestCycle(taskDriver, requestCycle);
            }
        }

        public void ForceRequestAdvice()
        {
            if (_hasPendingRequest)
            {
                if (Find.TickManager.TicksGame - _pendingRequestTick > 60000)
                {
                    RimMindErrors.Warn($"[RimMind-Advisor] ForceRequest: {Pawn.Name.ToStringShort} pending request timed out, resetting.");
                    CompleteRequestCycle();
                }
                else
                {
                    RimMindErrors.Warn($"[RimMind-Advisor] ForceRequest: {Pawn.Name.ToStringShort} already has a pending request, skipping.");
                    return;
                }
            }

            IsEnabled = true;
            _lastRequestTick = -9999;

            RimMindAPI.ClearModCooldown("Advisor");
            Log.Message($"[RimMind-Advisor] ForceRequest: Core-layer cooldown cleared (Advisor), sending request...");

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

            if (Pawn == null || Pawn.Dead || Pawn.Map == null)
            {
                CompleteRequestCycle(taskDriver, requestCycle);
                return;
            }

            if (result.IsErr)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] Request failed for {Pawn.Name.ToStringShort}: {result.Error}");
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
                    Log.Message($"[RimMind-Advisor] Parsed {toolCalls.Count} action(s) from content fallback for {Pawn.Name.ToStringShort}");
                }
            }

            if (toolCalls == null || toolCalls.Count == 0)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] No actionable response for {Pawn.Name.ToStringShort} (no tool_calls, content unparseable)");
                CompleteRequestCycle(taskDriver, requestCycle);
                return;
            }

            var approvedCalls = new List<ClientStructuredToolCall>();
            var approvedReasons = new Dictionary<string, string?>();
            requestCycle.BeginResponseBatch();
            try
            {
                foreach (var tc in toolCalls)
                {
                    if (tc.Name.NullOrEmpty()) continue;

                    if (RimMindAPI.Tools.FindById(tc.Name) == null)
                    {
                        RimMindErrors.Warn($"[RimMind-Advisor] Unknown tool call '{tc.Name}' for {Pawn.Name.ToStringShort}, skipping.");
                        continue;
                    }

                    var riskLevel = AdvisorToolRiskResolver.Resolve(tc.Name);

                    if (ShouldDeferForApproval(riskLevel, tc.Arguments))
                    {
                        SubmitToolCallForApproval(tc, riskLevel, taskDriver, requestCycle);
                    }
                    else
                    {
                        approvedCalls.Add(tc);
                        approvedReasons[tc.Id ?? tc.Name] = ExtractToolCallReason(tc) ?? tc.Name;
                    }
                }

                if (approvedCalls.Count > 0)
                {
                    var results = ExecuteToolCallsSafely(approvedCalls, response.RequestId);
                    int succeeded = results.Count(r => !r.IsError);
                    Log.Message($"[RimMind-Advisor] ToolCalls: executed {succeeded}/{approvedCalls.Count} tools for {Pawn.Name.ToStringShort}");

                    foreach (var resultItem in results.Where(r => r.IsError))
                    {
                        RimMindErrors.Warn($"[RimMind-Advisor] Tool '{resultItem.ToolName ?? "unknown"}' failed for {Pawn.Name.ToStringShort}: {resultItem.Content}");
                    }

                    foreach (var call in approvedCalls)
                    {
                        taskDriver.BroadcastDecisionExecuted(call.Name, ExtractToolCallReason(call) ?? call.Name);
                    }

                    var historyStoreForBatch = AdvisorHistoryStore.Instance;
                    if (historyStoreForBatch != null)
                    {
                        foreach (var r in results)
                        {
                            var resultKey = r.ToolCallId ?? r.ToolName ?? "";
                            approvedReasons.TryGetValue(resultKey, out var reason);
                            historyStoreForBatch.AddRecord(Pawn, new AdvisorRequestRecord
                            {
                                action = r.ToolName ?? "tool",
                                reason = reason ?? "",
                                result = r.IsError ? r.Content : "approved",
                                tick = Find.TickManager.TicksGame
                            });
                        }
                    }

                    if (Settings.showThoughtBubble && Pawn.Map != null)
                    {
                        var reasons = new List<string>();
                        foreach (var call in approvedCalls)
                        {
                            var reason = ExtractToolCallReason(call) ?? call.Name;
                            if (!reason.NullOrEmpty()) reasons.Add(reason);
                        }

                        if (reasons.Count > 0)
                        {
                            string moteText = reasons.Count == 1
                                ? $"[RimMind] {reasons[0]}"
                                : $"[RimMind] {reasons[0]} / {reasons[1]}";
                            MoteMaker.ThrowText(Pawn.DrawPos, Pawn.Map, moteText,
                                new Color(0.6f, 0.9f, 1f), 5f);
                        }
                    }

                    QueueFeedbackBatch(requestCycle, approvedCalls, results);
                }
            }
            catch (Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] Failed to process response for {Pawn.Name.ToStringShort}: {ex.Message}");
            }
            finally
            {
                if (IsCurrentCycle(taskDriver, requestCycle) && requestCycle.ResponseBatchOpen)
                    requestCycle.EndResponseBatch();
            }

            TryAdvanceRequestCycle(taskDriver, requestCycle);
        }

        private bool IsCurrentCycle(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle)
        {
            return ReferenceEquals(_taskDriver, taskDriver)
                && ReferenceEquals(_requestCycle, requestCycle);
        }

        private void TryAdvanceRequestCycle(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle)
        {
            if (!IsCurrentCycle(taskDriver, requestCycle)
                || requestCycle.ResponseBatchOpen
                || requestCycle.PendingApprovals > 0
                || requestCycle.FeedbackInFlight)
                return;

            if (requestCycle.HasQueuedFeedback)
            {
                if (!taskDriver.ShouldRequestFeedback())
                {
                    requestCycle.DiscardQueuedFeedback();
                    Log.Message($"[RimMind-Advisor] Max tool call depth ({AdvisorTaskDriver.MaxToolCallDepth}) reached for {Pawn.Name.ToStringShort}");
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
                        result => OnAdviceReceived(taskDriver, requestCycle, isFeedbackResponse: true, result));
                }
                catch (Exception ex)
                {
                    requestCycle.FinishFeedback();
                    RimMindErrors.Warn($"[RimMind-Advisor] Failed to submit feedback request for {Pawn.Name.ToStringShort}: {ex.Message}");
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
            if (cancellationErrors != null)
            {
                foreach (var error in cancellationErrors)
                {
                    RimMindErrors.Warn(
                        $"[RimMind-Advisor] Failed to cancel a pending approval for {Pawn.Name.ToStringShort}: {error.Message}");
                }
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            string label = IsEnabled ? "RimMind.Advisor.UI.Gizmo.Enabled".Translate() : "RimMind.Advisor.UI.Gizmo.Disabled".Translate();
            string subLabel = "";

            if (IsEnabled)
            {
                int cooldownLeft = Settings.requestCooldownTicks - (Find.TickManager.TicksGame - _lastRequestTick);
                if (cooldownLeft > 0)
                    subLabel = "RimMind.Advisor.UI.Gizmo.Cooldown".Translate($"{cooldownLeft / 2500f:F1}");
                else if (_hasPendingRequest)
                    subLabel = "RimMind.Advisor.UI.Gizmo.Waiting".Translate();
            }

            yield return new Command_Action
            {
                defaultLabel = label,
                defaultDesc = subLabel.NullOrEmpty()
                    ? "RimMind.Advisor.UI.Gizmo.Desc".Translate()
                    : subLabel,
                icon = ContentFinder<Texture2D>.Get("UI/AdvisorIcon", reportFailure: false),
                action = () => IsEnabled = !IsEnabled,
            };

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "RimMind.Advisor.UI.Gizmo.ForceRequest".Translate(),
                    defaultDesc = "RimMind.Advisor.UI.Gizmo.ForceRequestDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/AdvisorIcon", reportFailure: false),
                    action = ForceRequestAdvice,
                };
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref IsEnabled, "aiAdvisorEnabled", false);
        }

        private static bool IsToolCallRequest(string? arguments)
        {
            if (arguments is null || arguments.Trim().Length == 0) return false;

            try
            {
                var args = JsonConvert.DeserializeObject<Dictionary<string, string>>(arguments);
                return args != null
                    && args.TryGetValue("request_type", out var requestType)
                    && requestType == "request";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Centralized check: should this ToolCall be deferred for human approval?
        /// Combines risk-level check and request-type check.
        /// </summary>
        private bool ShouldDeferForApproval(RiskLevel riskLevel, string? arguments)
        {
            bool systemBlocked = Settings.enableRiskApproval
                && riskLevel >= Settings.autoBlockRiskLevel;
            bool isRequest = IsToolCallRequest(arguments);
            return systemBlocked || isRequest;
        }

        private bool SubmitToolCallForApproval(
            ClientStructuredToolCall toolCall,
            RiskLevel riskLevel,
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle)
        {
            var reason = ExtractToolCallReason(toolCall) ?? toolCall.Name;
            var args = ParseToolCallArguments(toolCall.Arguments);
            args.TryGetValue("target", out var targetName);
            args.TryGetValue("param", out var param);

            if (!Settings.enableRequestSystem)
            {
                Log.Message($"[RimMind-Advisor] Tool '{toolCall.Name}' blocked by risk level {riskLevel} (approval system disabled)");
                RecordToolHistory(toolCall.Name, reason, "blocked");
                return false;
            }

            if (_approvalManager == null)
                _approvalManager = new ApprovalManager(Settings);

            var adviceItem = new AdviceItem
            {
                Action = toolCall.Name,
                Target = targetName,
                Param = param,
                Reason = reason,
                RiskLevel = riskLevel,
                request_type = IsToolCallRequest(toolCall.Arguments) ? "request" : "normal",
            };

            RequestEntry? approvalEntry = null;
            try
            {
                approvalEntry = _approvalManager.SubmitForApproval(adviceItem, Pawn,
                    onApproved: () =>
                    {
                        if (!IsCurrentCycle(taskDriver, requestCycle))
                            return;

                        try
                        {
                            if (Pawn == null || Pawn.Dead || Pawn.Map == null)
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
                                    RimMindErrors.Warn($"[RimMind-Advisor] Approved tool '{result.ToolName ?? toolCall.Name}' failed for {Pawn.Name.ToStringShort}: {result.Content}");
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
            catch (Exception ex)
            {
                if (approvalEntry != null)
                {
                    requestCycle.TryFinishApproval(approvalEntry);
                    RimMindAPI.DismissPendingRequest(approvalEntry);
                }

                RecordToolHistory(toolCall.Name, reason, "approval_error");
                RimMindErrors.Warn($"[RimMind-Advisor] Failed to register approval for '{toolCall.Name}': {ex.Message}");
                return false;
            }
        }

        private void FinishTrackedApproval(
            AdvisorTaskDriver taskDriver,
            AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle,
            RequestEntry? approvalEntry)
        {
            if (approvalEntry == null
                || !IsCurrentCycle(taskDriver, requestCycle)
                || !requestCycle.TryFinishApproval(approvalEntry))
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
                    $"[RimMind-Advisor] Skipping malformed feedback batch for {Pawn.Name.ToStringShort}: " +
                    $"{calls.Count} calls but {results.Count} results.");
                return;
            }

            requestCycle.QueueFeedback(calls, results);
        }

        private static string? ExtractToolCallReason(ClientStructuredToolCall toolCall)
        {
            var args = ParseToolCallArguments(toolCall.Arguments);
            return args.TryGetValue("reason", out var reason) && !reason.NullOrEmpty()
                ? reason
                : null;
        }

        private List<ToolResult> ExecuteToolCallsSafely(
            IReadOnlyList<ClientStructuredToolCall> calls,
            string? traceId)
        {
            try
            {
                // OnAdviceReceived is invoked by Core's main-thread queue tick. Tool handlers may
                // ultimately mutate Verse state through MainOnly mechanism interfaces, so moving this
                // await to Task.Run or an unconstrained continuation would be unsafe. Keep the result
                // available for the feedback request, but make any future truly-asynchronous handler
                // visible instead of silently turning a main-thread stall into an unexplained freeze.
                var executionTask = _toolExecutor.ExecuteAsync(
                    calls,
                    $"NPC-{Pawn.thingIDNumber}",
                    traceId,
                    CancellationToken.None);

                if (!executionTask.IsCompleted)
                {
                    RimMindErrors.Warn(
                        $"[RimMind-Advisor][ToolCall][MainThreadWait] " +
                        $"Tool execution is asynchronous; preserving main-thread completion for {Pawn.Name.ToStringShort} " +
                        $"(trace={traceId ?? "unknown"}, tools={calls.Count}).");
                }

                return executionTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] Tool execution failed for {Pawn.Name.ToStringShort}: {ex.Message}");
                return calls
                    .Select(call => ToolResult.Fail(
                        $"Tool execution failed: {ex.Message}",
                        call.Id,
                        call.Name))
                    .ToList();
            }
        }

        private static Dictionary<string, string> ParseToolCallArguments(string? arguments)
        {
            if (arguments is null || arguments.Trim().Length == 0) return new Dictionary<string, string>();

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(arguments)
                    ?? new Dictionary<string, string>();
            }
            catch (System.Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] Failed to parse tool call arguments: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        private void RecordToolHistory(string action, string? reason, string result)
        {
            var historyStore = AdvisorHistoryStore.Instance;
            if (historyStore == null) return;

            historyStore.AddRecord(Pawn, new AdvisorRequestRecord
            {
                action = action,
                reason = reason ?? "",
                result = result,
                tick = Find.TickManager.TicksGame
            });
        }

        private void ShowToolThoughtBubble(string? reason)
        {
            if (!Settings.showThoughtBubble || Pawn.Map == null || reason.NullOrEmpty()) return;

            string moteText = $"[RimMind] {reason}";
            MoteMaker.ThrowText(Pawn.DrawPos, Pawn.Map, moteText,
                new Color(0.6f, 0.9f, 1f), 5f);
        }
    }
}
