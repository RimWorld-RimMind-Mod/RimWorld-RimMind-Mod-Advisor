using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using RimMind.Actions;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Core;
using RimMind.Core.Client;
using RimMind.Core.UI;
using RimWorld;
using UnityEngine;
using Verse;

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

        public bool HasPendingRequest => _hasPendingRequest;
        public int LastRequestTick => _lastRequestTick;
        public AdvisorTaskDriver? TaskDriver => _taskDriver;

        public int AdvisorCooldownTicksLeft =>
            System.Math.Max(0, Settings.requestCooldownTicks - (Find.TickManager.TicksGame - _lastRequestTick));

        private Pawn Pawn => (Pawn)parent;
        private RimMindAdvisorSettings Settings => RimMindAdvisorMod.Settings;
        private bool DebugLogging => RimMind.Core.RimMindCoreMod.Settings.debugLogging;

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
                    Log.Warning($"[RimMind-Advisor] ForceRequest: {Pawn.Name.ToStringShort} pending request timed out, resetting.");
                    CompleteRequestCycle();
                }
                else
                {
                    Log.Warning($"[RimMind-Advisor] ForceRequest: {Pawn.Name.ToStringShort} already has a pending request, skipping.");
                    return;
                }
            }

            IsEnabled = true;
            _lastRequestTick = -9999;

            RimMind.Core.Internal.AIRequestQueue.Instance?.ClearCooldown("Advisor");
            Log.Message($"[RimMind-Advisor] ForceRequest: Core-layer cooldown cleared (Advisor), sending request...");

            RequestAdvice(Settings);
        }

        private void OnAdviceReceived(AIResponse response)
        {
            if (Pawn == null || Pawn.Dead || Pawn.Map == null)
            {
                CompleteRequestCycle();
                return;
            }

            if (!response.Success)
            {
                Log.Warning($"[RimMind-Advisor] Request failed for {Pawn.Name.ToStringShort}: {response.Error}");
                CompleteRequestCycle();
                return;
            }

            if (_taskDriver == null)
            {
                CompleteRequestCycle();
                return;
            }

            List<StructuredToolCall>? toolCalls = null;

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
                toolCalls = _taskDriver.TryParseContentAsToolCalls(response.Content);
                if (toolCalls != null)
                {
                    Log.Message($"[RimMind-Advisor] Parsed {toolCalls.Count} action(s) from content fallback for {Pawn.Name.ToStringShort}");
                }
            }

            if (toolCalls == null || toolCalls.Count == 0)
            {
                Log.Warning($"[RimMind-Advisor] No actionable response for {Pawn.Name.ToStringShort} (no tool_calls, content unparseable)");
                CompleteRequestCycle();
                return;
            }

            var supported = new HashSet<string>(RimMindActionsAPI.GetSupportedIntents());
            var intents = new List<BatchActionIntent>();

            foreach (var tc in toolCalls)
            {
                if (tc.Name.NullOrEmpty() || !supported.Contains(tc.Name)) continue;
                if (!RimMindActionsAPI.IsAllowed(tc.Name)) continue;

                string? targetName = null;
                string? param = tc.Arguments;
                string? reason = tc.Name;

                if (!tc.Arguments.NullOrEmpty())
                {
                    try
                    {
                        var args = JsonConvert.DeserializeObject<Dictionary<string, string>>(tc.Arguments);
                        if (args != null)
                        {
                            if (args.TryGetValue("target", out var t)) targetName = t;
                            if (args.TryGetValue("param", out var p)) param = p;
                            if (args.TryGetValue("reason", out var r)) reason = r;
                            args.TryGetValue("request_type", out var rt);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"[RimMind-Advisor] Failed to parse tool call arguments for {tc.Name}: {ex.Message}");
                    }
                }

                Pawn? targetPawn = null;
                if (!targetName.NullOrEmpty())
                    targetPawn = FindPawnByName(targetName!);

                var riskLevel = RimMindActionsAPI.GetRiskLevel(tc.Name);
                bool systemBlocked = Settings.enableRiskApproval
                    && riskLevel.HasValue
                    && riskLevel.GetValueOrDefault() >= Settings.autoBlockRiskLevel;

                bool isRequest = false;
                if (!tc.Arguments.NullOrEmpty())
                {
                    try
                    {
                        var args = JsonConvert.DeserializeObject<Dictionary<string, string>>(tc.Arguments);
                        if (args != null && args.TryGetValue("request_type", out var rt) && rt == "request")
                            isRequest = true;
                    }
                    catch { }
                }

                if (systemBlocked || isRequest)
                {
                    if (!Settings.enableRequestSystem)
                    {
                        Log.Message($"[RimMind-Advisor] Action '{tc.Name}' blocked by risk level {riskLevel.GetValueOrDefault()} (approval system disabled)");
                        continue;
                    }

                    if (_approvalManager == null)
                        _approvalManager = new ApprovalManager(Settings);

                    var capturedTc = tc;
                    var capturedTarget = targetPawn;
                    var capturedReason = reason;
                    var capturedParam = param;

                    var adviceItem = new AdviceItem
                    {
                        Action = capturedTc.Name,
                        Target = capturedTarget?.Name?.ToStringShort,
                        Param = capturedParam,
                        Reason = capturedReason,
                        RiskLevel = riskLevel.GetValueOrDefault(),
                    };

                    _approvalManager.SubmitForApproval(adviceItem, Pawn,
                        onApproved: () =>
                        {
                            var singleIntent = new List<BatchActionIntent>
                            {
                                new BatchActionIntent
                                {
                                    IntentId = capturedTc.Name,
                                    Actor = Pawn,
                                    Target = capturedTarget,
                                    Param = capturedParam,
                                    Reason = capturedReason,
                                }
                            };
                            var results = RimMindActionsAPI.ExecuteBatchWithResults(singleIntent);
                            _taskDriver?.BroadcastDecisionExecuted(capturedTc.Name, capturedReason);

                            var historyStore = AdvisorHistoryStore.Instance;
                            if (historyStore != null)
                            {
                                foreach (var r in results)
                                {
                                    historyStore.AddRecord(Pawn, new AdvisorRequestRecord
                                    {
                                        action = r.ActionName,
                                        reason = capturedReason ?? "",
                                        result = r.Success ? "approved" : r.Reason,
                                        tick = Find.TickManager.TicksGame
                                    });
                                }
                            }

                            if (Settings.showThoughtBubble && Pawn.Map != null)
                            {
                                string moteText = $"[RimMind] {capturedReason ?? capturedTc.Name}";
                                MoteMaker.ThrowText(Pawn.DrawPos, Pawn.Map, moteText,
                                    new Color(0.6f, 0.9f, 1f), 5f);
                            }
                        },
                        onRejected: () =>
                        {
                            var historyStore = AdvisorHistoryStore.Instance;
                            if (historyStore != null)
                            {
                                historyStore.AddRecord(Pawn, new AdvisorRequestRecord
                                {
                                    action = capturedTc.Name,
                                    reason = capturedReason ?? "",
                                    result = "rejected",
                                    tick = Find.TickManager.TicksGame
                                });
                            }
                        });
                }
                else
                {
                    intents.Add(new BatchActionIntent
                    {
                        IntentId = tc.Name,
                        Actor = Pawn,
                        Target = targetPawn,
                        Param = param,
                        Reason = reason,
                    });
                }
            }

            if (intents.Count == 0)
            {
                CompleteRequestCycle();
                return;
            }

            var results = RimMindActionsAPI.ExecuteBatchWithResults(intents);
            int succeeded = results.Count(r => r.Success);
            Log.Message($"[RimMind-Advisor] ToolCalls: executed {succeeded}/{intents.Count} actions for {Pawn.Name.ToStringShort}");

            // Broadcast decision events
            foreach (var intent in intents)
            {
                _taskDriver?.BroadcastDecisionExecuted(intent.IntentId, intent.Reason);
            }

            var historyStoreForBatch = AdvisorHistoryStore.Instance;
            if (historyStoreForBatch != null)
            {
                foreach (var r in results)
                {
                    historyStoreForBatch.AddRecord(Pawn, new AdvisorRequestRecord
                    {
                        action = r.ActionName,
                        reason = intents.FirstOrDefault(i => i.IntentId == r.ActionName)?.Reason ?? "",
                        result = r.Success ? "approved" : r.Reason,
                        tick = Find.TickManager.TicksGame
                    });
                }
            }

            if (Settings.showThoughtBubble && Pawn.Map != null)
            {
                var reasons = new List<string>();
                foreach (var intent in intents)
                    if (!intent.Reason.NullOrEmpty()) reasons.Add(intent.Reason!);

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
                _taskDriver.RequestToolFeedback(toolCalls, results, OnAdviceReceived);
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

        private void DebugSkip(string reason)
        {
            if (DebugLogging)
                Log.Message($"[RimMind-Advisor][Skip] {Pawn.Name.ToStringShort}: {reason}");
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

        private static Pawn? FindPawnByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (int.TryParse(name, out int thingId))
            {
                foreach (var map in Find.Maps)
                {
                    var pawn = map.mapPawns.AllPawns.FirstOrDefault(p => p.thingIDNumber == thingId);
                    if (pawn != null) return pawn;
                }
            }

            var matches = Find.Maps.SelectMany(m => m.mapPawns.AllPawns)
                .Where(p => p.Name?.ToStringShort == name)
                .ToList();

            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1)
                Log.Warning($"[RimMind-Advisor] FindPawnByName: '{name}' matches {matches.Count} pawns, using first. Consider using ThingID for disambiguation.");

            return matches.FirstOrDefault();
        }
    }
}
