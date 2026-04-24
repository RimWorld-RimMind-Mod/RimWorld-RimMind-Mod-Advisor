using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using RimMind.Actions;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Core;
using RimMind.Core.Client;
using RimMind.Core.Comps;
using RimMind.Core.Context;
using RimMind.Core.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Advisor.Comps
{
    public class CompAIAdvisor : ThingComp
    {
        public bool IsEnabled = false;

        private bool    _hasPendingRequest;
        private int     _lastRequestTick = -9999;
        private int     _pendingRequestTick;

        private const int MaxToolCallDepth = 3;

        private List<ChatMessage>?       _lastMessages;
        private List<StructuredTool>?    _lastTools;
        private string?                  _lastSchema;
        private int                      _toolCallDepth;

        public bool HasPendingRequest => _hasPendingRequest;
        public int  AdvisorCooldownTicksLeft =>
            System.Math.Max(0, Settings.requestCooldownTicks - (Find.TickManager.TicksGame - _lastRequestTick));

        private Pawn Pawn => (Pawn)parent;
        private RimMindAdvisorSettings Settings => RimMindAdvisorMod.Settings;
        private bool DebugLogging => RimMind.Core.RimMindCoreMod.Settings.debugLogging;

        public override void CompTick()
        {
            if (Pawn.Map == null) return;

            var s = Settings;
            if (!Pawn.IsHashIntervalTick(s.pawnScanIntervalTicks)) return;

            if (!s.enableAdvisor)
            {
                return;
            }
            if (!RimMindAPI.IsConfigured())
            {
                DebugSkip("API not configured");
                return;
            }
            if (CompPawnAgent.IsAgentActive(Pawn))
            {
                return;
            }
            if (_hasPendingRequest)
            {
                if (Find.TickManager.TicksGame - _pendingRequestTick > 60000)
                {
                    Log.Warning($"[RimMind-Advisor] Pending request timeout for {Pawn.Name.ToStringShort}, resetting.");
                    _hasPendingRequest = false;
                    AdvisorConcurrencyTracker.Decrement();
                }
                else
                {
                    DebugSkip("Pending request exists");
                    return;
                }
            }
            if (!IsEnabled)
            {
                DebugSkip("Advisor toggle off for this colonist");
                return;
            }
            if (!IsEligible())
            {
                DebugSkip("Colonist ineligible (dead/slave/drafted/no mood)");
                return;
            }

            bool idleTriggered = s.enableIdleTrigger && IsIdle();
            bool moodTriggered = s.enableMoodTrigger && IsMoodBelowThreshold();

            if (!idleTriggered && !moodTriggered)
            {
                DebugSkip($"No trigger met (idle={idleTriggered}, mood={moodTriggered})");
                return;
            }

            int ticksGame = Find.TickManager.TicksGame;
            int advisorCooldownLeft = s.requestCooldownTicks - (ticksGame - _lastRequestTick);
            if (advisorCooldownLeft > 0)
            {
                if (DebugLogging)
                    Log.Message($"[RimMind-Advisor][Advisor Cooldown] {Pawn.Name.ToStringShort} cooling down, {advisorCooldownLeft} ticks remaining (~{advisorCooldownLeft / 2500f:F2} game hours). requestCooldownTicks={s.requestCooldownTicks}");
                return;
            }

            if (AdvisorConcurrencyTracker.ActiveCount >= s.maxConcurrentRequests)
            {
                if (DebugLogging)
                    Log.Message($"[RimMind-Advisor][Concurrency Limit] Current concurrent {AdvisorConcurrencyTracker.ActiveCount}/{s.maxConcurrentRequests}, {Pawn.Name.ToStringShort} skipped.");
                return;
            }

            RequestAIAdvice();
        }

        private void DebugSkip(string reason)
        {
            if (DebugLogging)
                Log.Message($"[RimMind-Advisor][Skip] {Pawn.Name.ToStringShort}: {reason}");
        }

        private bool IsEligible() =>
            Pawn.IsFreeNonSlaveColonist &&
            !Pawn.Dead &&
            !(Pawn.drafter?.Drafted ?? false) &&
            Pawn.needs?.mood != null;

        private bool IsIdle()
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

        private bool IsMoodBelowThreshold()
        {
            var mood = Pawn.needs?.mood;
            if (mood == null) return false;
            return mood.CurLevelPercentage < Settings.moodThreshold;
        }

        private void RequestAIAdvice()
        {
            _hasPendingRequest = true;
            _pendingRequestTick = Find.TickManager.TicksGame;
            AdvisorConcurrencyTracker.Increment();

            var npcId = $"NPC-{Pawn.ThingID}";
            var ctxRequest = new ContextRequest
            {
                NpcId = npcId,
                Scenario = ScenarioIds.Decision,
                Budget = GetDecisionBudget(),
                MaxTokens = 400,
                Temperature = 0.7f,
            };

            var schema = RimMind.Core.Context.SchemaRegistry.AdviceOutput;

            var tools = BuildActionTools();

            var snapshot = RimMindAPI.BuildContextSnapshot(ctxRequest);

            if (!Settings.advisorCustomPrompt.NullOrEmpty())
            {
                snapshot.Messages.Insert(0, new ChatMessage { Role = "system", Content = Settings.advisorCustomPrompt });
            }

            _lastMessages = new List<ChatMessage>(snapshot.Messages);
            _lastTools = tools;
            _lastSchema = schema;
            _toolCallDepth = 0;

            var aiRequest = new AIRequest
            {
                SystemPrompt = null,
                Messages = snapshot.Messages,
                MaxTokens = snapshot.MaxTokens,
                Temperature = snapshot.Temperature,
                RequestId = $"Structured_{npcId}",
                ModId = "Advisor",
                ExpireAtTicks = Find.TickManager.TicksGame + Settings.requestExpireTicks,
                UseJsonMode = true,
                Priority = AIRequestPriority.Normal,
            };

            RimMindAPI.RequestStructuredAsync(aiRequest, schema, OnAdviceReceived, tools);
        }

        private float GetDecisionBudget()
        {
            var settings = RimMindCoreMod.Settings?.Context;
            if (settings == null) return 0.5f;
            return settings.ContextBudget;
        }

        private List<StructuredTool>? BuildActionTools()
        {
            try
            {
                var tools = RimMindActionsAPI.GetStructuredTools();
                return tools.Count > 0 ? tools : null;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMind-Advisor] BuildActionTools failed: {ex.Message}");
                return null;
            }
        }

        public void ForceRequestAdvice()
        {
            if (_hasPendingRequest)
            {
                Log.Warning($"[RimMind-Advisor] ForceRequest: {Pawn.Name.ToStringShort} already has a pending request, skipping.");
                return;
            }

            IsEnabled        = true;
            _lastRequestTick = -9999;

            RimMind.Core.Internal.AIRequestQueue.Instance?.ClearCooldown("Advisor");
            Log.Message($"[RimMind-Advisor] ForceRequest: Core-layer cooldown cleared (Advisor), sending request...");

            RequestAIAdvice();
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

            if (string.IsNullOrEmpty(response.ToolCallsJson))
            {
                Log.Warning($"[RimMind-Advisor] No tool calls in response for {Pawn.Name.ToStringShort}");
                CompleteRequestCycle();
                return;
            }

            HandleToolCalls(response.ToolCallsJson);
        }

        private void CompleteRequestCycle()
        {
            if (_hasPendingRequest)
            {
                _hasPendingRequest = false;
                _lastRequestTick = Find.TickManager.TicksGame;
                AdvisorConcurrencyTracker.Decrement();
            }
            _toolCallDepth = 0;
            _lastMessages = null;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            string label    = IsEnabled ? "RimMind.Advisor.UI.Gizmo.Enabled".Translate() : "RimMind.Advisor.UI.Gizmo.Disabled".Translate();
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
                defaultDesc  = subLabel.NullOrEmpty()
                    ? "RimMind.Advisor.UI.Gizmo.Desc".Translate()
                    : subLabel,
                icon   = ContentFinder<Texture2D>.Get("UI/AdvisorIcon", reportFailure: false),
                action = () => IsEnabled = !IsEnabled,
            };

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "RimMind.Advisor.UI.Gizmo.ForceRequest".Translate(),
                    defaultDesc  = "RimMind.Advisor.UI.Gizmo.ForceRequestDesc".Translate(),
                    icon   = ContentFinder<Texture2D>.Get("UI/AdvisorIcon", reportFailure: false),
                    action = ForceRequestAdvice,
                };
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref IsEnabled, "aiAdvisorEnabled", false);
        }

        private void HandleToolCalls(string toolCallsJson)
        {
            List<StructuredToolCall>? toolCalls;
            try
            {
                toolCalls = JsonConvert.DeserializeObject<List<StructuredToolCall>>(toolCallsJson);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMind-Advisor] ToolCalls parse failed for {Pawn.Name.ToStringShort}: {ex.Message}");
                CompleteRequestCycle();
                return;
            }

            if (toolCalls == null || toolCalls.Count == 0)
            {
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
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"[RimMind-Advisor] Failed to parse tool call arguments for {tc.Name}: {ex.Message}");
                    }
                }

                Pawn? targetPawn = null;
                if (!targetName.NullOrEmpty())
                    targetPawn = FindPawnByName(Pawn.Map, targetName);

                var riskLevel = RimMindActionsAPI.GetRiskLevel(tc.Name);
                bool systemBlocked = Settings.enableRiskApproval
                    && riskLevel.HasValue
                    && riskLevel.Value >= Settings.autoBlockRiskLevel;

                if (systemBlocked)
                {
                    if (!Settings.enableRequestSystem)
                    {
                        Log.Message($"[RimMind-Advisor] Action '{tc.Name}' blocked by risk level {riskLevel.Value} (approval system disabled)");
                        continue;
                    }
                    string title = "RimMind.Advisor.Request.RiskAction".Translate(tc.Name);
                    var capturedTc = tc;
                    var capturedTarget = targetPawn;
                    var capturedReason = reason;
                    string approveLabel = "RimMind.Advisor.Request.Approve".Translate();
                    string rejectLabel = "RimMind.Advisor.Request.Reject".Translate();

                    var entry = new RequestEntry
                    {
                        source = "advisor",
                        pawn = Pawn,
                        title = title,
                        description = capturedReason ?? tc.Name,
                        systemBlocked = systemBlocked,
                        expireTicks = Settings.requestExpireTicks,
                        options = new[] { approveLabel, rejectLabel },
                        callback = choice =>
                        {
                            if (choice == approveLabel)
                            {
                                var singleIntent = new List<BatchActionIntent>
                                {
                                    new BatchActionIntent
                                    {
                                        IntentId = capturedTc.Name,
                                        Actor = Pawn,
                                        Target = capturedTarget,
                                        Param = capturedTc.Arguments,
                                        Reason = capturedReason,
                                    }
                                };
                                RimMindActionsAPI.ExecuteBatch(singleIntent);
                                var historyStore = AdvisorHistoryStore.Instance;
                                if (historyStore != null)
                                {
                                    historyStore.AddRecord(Pawn, new AdvisorRequestRecord
                                    {
                                        action = capturedTc.Name,
                                        reason = capturedReason ?? "",
                                        result = "approved",
                                        tick = Find.TickManager.TicksGame
                                    });
                                }
                            }
                        }
                    };
                    RimMindAPI.RegisterPendingRequest(entry);
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

            var historyStore = AdvisorHistoryStore.Instance;
            if (historyStore != null)
            {
                foreach (var r in results)
                {
                    historyStore.AddRecord(Pawn, new AdvisorRequestRecord
                    {
                        action = r.ActionName,
                        reason = intents.FirstOrDefault(i => i.IntentId == r.ActionName)?.Reason ?? "",
                        result = r.Success ? "success" : r.Reason,
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

            if (_toolCallDepth < MaxToolCallDepth)
            {
                RequestToolFeedback(toolCallsJson, results);
            }
            else
            {
                Log.Message($"[RimMind-Advisor] Max tool call depth ({MaxToolCallDepth}) reached for {Pawn.Name.ToStringShort}");
                CompleteRequestCycle();
            }
        }

        private void RequestToolFeedback(string toolCallsJson, List<ActionResult> results)
        {
            _toolCallDepth++;

            List<StructuredToolCall>? toolCalls;
            try
            {
                toolCalls = JsonConvert.DeserializeObject<List<StructuredToolCall>>(toolCallsJson);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMind-Advisor] RequestToolFeedback deserialization failed for {Pawn.Name.ToStringShort}: {ex.Message}");
                CompleteRequestCycle();
                return;
            }

            var messages = new List<ChatMessage>(_lastMessages ?? new List<ChatMessage>());

            messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = "",
                ToolCalls = toolCalls?.Select(tc => new ChatToolCall
                {
                    Id = tc.Id,
                    Name = tc.Name,
                    Arguments = tc.Arguments,
                }).ToList() ?? new List<ChatToolCall>()
            });

            foreach (var result in results)
            {
                var matchingTc = toolCalls?.FirstOrDefault(tc => tc.Name == result.ActionName);
                messages.Add(new ChatMessage
                {
                    Role = "tool",
                    Content = result.ToString(),
                    ToolCallId = matchingTc?.Id ?? result.ActionName,
                });
            }

            _lastMessages = messages;

            var followUpRequest = new AIRequest
            {
                Messages = messages,
                MaxTokens = 400,
                Temperature = 0.7f,
                RequestId = $"Structured_NPC-{Pawn.ThingID}_fb{_toolCallDepth}",
                ModId = "Advisor",
                ExpireAtTicks = Find.TickManager.TicksGame + Settings.requestExpireTicks,
                UseJsonMode = true,
                Priority = AIRequestPriority.Normal,
            };

            RimMindAPI.RequestStructuredAsync(followUpRequest, _lastSchema, OnAdviceReceived, _lastTools);
        }

        private static Pawn? FindPawnByName(Map? map, string name)
        {
            if (map == null || string.IsNullOrEmpty(name)) return null;

            if (int.TryParse(name, out int thingId))
            {
                var pawn = map.mapPawns.AllPawns.FirstOrDefault(p => p.thingIDNumber == thingId);
                if (pawn != null) return pawn;
            }

            var matches = map.mapPawns.AllPawns
                .Where(p => p.Name?.ToStringShort == name)
                .ToList();

            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1)
                Log.Warning($"[RimMind-Advisor] FindPawnByName: '{name}' matches {matches.Count} pawns, using first. Consider using ThingID for disambiguation.");

            return matches.FirstOrDefault();
        }
    }
}
