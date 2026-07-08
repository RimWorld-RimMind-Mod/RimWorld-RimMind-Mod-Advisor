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
        private ApprovalManager? _approvalManager;
        private readonly AdvisorToolCallExecutor _toolExecutor = new AdvisorToolCallExecutor();

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

            _taskDriver = new AdvisorTaskDriver(Pawn, settings);
            _taskDriver.BuildAndSendRequest(OnAdviceReceived);
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

        private void OnAdviceReceived(Result<LlmResponse, RimMindError> result)
        {
            if (Pawn == null || Pawn.Dead || Pawn.Map == null)
            {
                CompleteRequestCycle();
                return;
            }

            if (result.IsErr)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] Request failed for {Pawn.Name.ToStringShort}: {result.Error}");
                CompleteRequestCycle();
                return;
            }

            var response = result.Value;

            if (_taskDriver == null)
            {
                CompleteRequestCycle();
                return;
            }

            List<ClientStructuredToolCall>? toolCalls = null;

            if (!string.IsNullOrEmpty(response.ToolCallsJson))
            {
                _taskDriver.SetReasoningContent(response.ReasoningContent);
                if (!_taskDriver.TryParseToolCalls(response.ToolCallsJson ?? string.Empty, out toolCalls))
                {
                    CompleteRequestCycle();
                    return;
                }
            }
            else if (!string.IsNullOrEmpty(response.Content))
            {
                _taskDriver.SetReasoningContent(response.ReasoningContent);
                toolCalls = _taskDriver.TryParseContentAsToolCallsIfEnabled(response.Content);
                if (toolCalls != null)
                {
                    Log.Message($"[RimMind-Advisor] Parsed {toolCalls.Count} action(s) from content fallback for {Pawn.Name.ToStringShort}");
                }
            }

            if (toolCalls == null || toolCalls.Count == 0)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] No actionable response for {Pawn.Name.ToStringShort} (no tool_calls, content unparseable)");
                CompleteRequestCycle();
                return;
            }

            var approvedCalls = new List<ClientStructuredToolCall>();
            var approvedReasons = new Dictionary<string, string?>();
            bool deferredForApproval = false;

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
                    deferredForApproval = true;
                    SubmitToolCallForApproval(tc, riskLevel);
                }
                else
                {
                    approvedCalls.Add(tc);
                    approvedReasons[tc.Id ?? tc.Name] = ExtractToolCallReason(tc) ?? tc.Name;
                }
            }

            if (deferredForApproval && approvedCalls.Count == 0)
            {
                CompleteRequestCycle();
                return;
            }

            if (approvedCalls.Count == 0)
            {
                CompleteRequestCycle();
                return;
            }

            var results = ExecuteToolCallsSafely(approvedCalls, response.RequestId);
            int succeeded = results.Count(r => !r.IsError);
            Log.Message($"[RimMind-Advisor] ToolCalls: executed {succeeded}/{approvedCalls.Count} tools for {Pawn.Name.ToStringShort}");

            foreach (var resultItem in results.Where(r => r.IsError))
            {
                RimMindErrors.Warn($"[RimMind-Advisor] Tool '{resultItem.ToolName ?? "unknown"}' failed for {Pawn.Name.ToStringShort}: {resultItem.Content}");
            }

            foreach (var call in approvedCalls)
            {
                _taskDriver?.BroadcastDecisionExecuted(call.Name, ExtractToolCallReason(call) ?? call.Name);
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

            if (_taskDriver.ShouldRequestFeedback())
            {
                _taskDriver.RequestToolFeedback(approvedCalls, results, OnAdviceReceived);
            }
            else
            {
                Log.Message($"[RimMind-Advisor] Max tool call depth ({AdvisorTaskDriver.MaxToolCallDepth}) reached for {Pawn.Name.ToStringShort}");
                CompleteRequestCycle();
            }
        }

        private void CompleteRequestCycle()
        {
            if (_hasPendingRequest)
            {
                _hasPendingRequest = false;
                _lastRequestTick = Find.TickManager.TicksGame;
                AdvisorConcurrencyTracker.Decrement();
            }
            _taskDriver?.ClearState();
            _taskDriver = null;
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
            if (string.IsNullOrWhiteSpace(arguments)) return false;

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

        private void SubmitToolCallForApproval(ClientStructuredToolCall toolCall, RiskLevel riskLevel)
        {
            var reason = ExtractToolCallReason(toolCall) ?? toolCall.Name;
            var args = ParseToolCallArguments(toolCall.Arguments);
            args.TryGetValue("target", out var targetName);
            args.TryGetValue("param", out var param);

            if (!Settings.enableRequestSystem)
            {
                Log.Message($"[RimMind-Advisor] Tool '{toolCall.Name}' blocked by risk level {riskLevel} (approval system disabled)");
                RecordToolHistory(toolCall.Name, reason, "blocked");
                return;
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

            _approvalManager.SubmitForApproval(adviceItem, Pawn,
                onApproved: () =>
                {
                    var results = ExecuteToolCallsSafely(
                        new List<ClientStructuredToolCall> { toolCall },
                        toolCall.Id);

                    _taskDriver?.BroadcastDecisionExecuted(toolCall.Name, reason);
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

                    // Unified: approval path also checks feedback loop (same as direct execution path)
                    if (_taskDriver != null && _taskDriver.ShouldRequestFeedback())
                    {
                        _taskDriver.RequestToolFeedback(
                            new List<ClientStructuredToolCall> { toolCall },
                            results,
                            OnAdviceReceived);
                    }
                    else
                    {
                        CompleteRequestCycle();
                    }
                },
                onRejected: () =>
                {
                    RecordToolHistory(toolCall.Name, reason, "rejected");
                });
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
                return _toolExecutor.ExecuteAsync(
                    calls,
                    $"NPC-{Pawn.thingIDNumber}",
                    traceId,
                    CancellationToken.None).GetAwaiter().GetResult();
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
            if (string.IsNullOrWhiteSpace(arguments)) return new Dictionary<string, string>();

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
