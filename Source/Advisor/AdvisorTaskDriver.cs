using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using RimMind.Actions;
using RimMind.Advisor.Settings;
using RimMind.Core;
using RimMind.Core.Client;
using RimMind.Core.Context;
using RimWorld;
using Verse;

namespace RimMind.Advisor.Advisor
{
    public class AdvisorTaskDriver
    {
        public const int MaxToolCallDepth = 3;

        private readonly Pawn _pawn;
        private readonly RimMindAdvisorSettings _settings;

        private List<ChatMessage>? _lastMessages;
        private List<StructuredTool>? _lastTools;
        private string? _lastSchema;
        private int _toolCallDepth;
        private string? _lastReasoningContent;

        public AdvisorTaskDriver(Pawn pawn, RimMindAdvisorSettings settings)
        {
            _pawn = pawn;
            _settings = settings;
        }

        public bool HasPendingState => _lastMessages != null;

        public void BuildAndSendRequest(Action<AIResponse> onComplete)
        {
            var npcId = $"NPC-{_pawn.thingIDNumber}";
            var ctxRequest = new ContextRequest
            {
                NpcId = npcId,
                Scenario = ScenarioIds.Decision,
                Budget = GetDecisionBudget(),
                MaxTokens = 400,
                Temperature = 0.7f,
            };

            var schema = SchemaRegistry.AdviceOutput;
            var tools = BuildActionTools();
            var snapshot = RimMindAPI.BuildContextSnapshot(ctxRequest);

            if (!_settings.advisorCustomPrompt.NullOrEmpty())
            {
                int lastSysIdx = -1;
                for (int i = snapshot.Messages.Count - 1; i >= 0; i--)
                {
                    if (snapshot.Messages[i].Role == "system") { lastSysIdx = i; break; }
                }
                snapshot.Messages.Insert(lastSysIdx + 1, new ChatMessage { Role = "system", Content = _settings.advisorCustomPrompt });
            }

            _lastMessages = new List<ChatMessage>(snapshot.Messages);
            _lastTools = tools;
            _lastSchema = schema;
            _toolCallDepth = 0;
            _lastReasoningContent = null;

            var aiRequest = new AIRequest
            {
                SystemPrompt = null!,
                Messages = snapshot.Messages,
                MaxTokens = snapshot.MaxTokens,
                Temperature = snapshot.Temperature,
                RequestId = $"Structured_{npcId}",
                ModId = "Advisor",
                ExpireAtTicks = Find.TickManager.TicksGame + _settings.requestExpireTicks,
                UseJsonMode = true,
                Priority = AIRequestPriority.Normal,
            };

            RimMindAPI.RequestStructuredAsync(aiRequest, schema, onComplete, tools);
        }

        public List<StructuredTool>? BuildActionTools()
        {
            try
            {
                var tools = RimMindActionsAPI.GetStructuredTools();
                return tools.Count > 0 ? tools : null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Advisor] BuildActionTools failed: {ex.Message}");
                return null;
            }
        }

        public void SetReasoningContent(string? content)
        {
            _lastReasoningContent = content;
        }

        public string? LastReasoningContent => _lastReasoningContent;

        public bool TryParseToolCalls(string toolCallsJson, out List<StructuredToolCall> toolCalls)
        {
            toolCalls = new List<StructuredToolCall>();
            try
            {
                var parsed = JsonConvert.DeserializeObject<List<StructuredToolCall>>(toolCallsJson);
                if (parsed != null) toolCalls = parsed;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Advisor] ToolCalls parse failed for {_pawn.Name.ToStringShort}: {ex.Message}");
                return false;
            }
        }

        public List<StructuredToolCall>? TryParseContentAsToolCalls(string content)
        {
            try
            {
                string trimmed = content.Trim();
                if (trimmed.StartsWith("```"))
                {
                    int firstBrace = trimmed.IndexOf('{');
                    int lastBrace = trimmed.LastIndexOf('}');
                    if (firstBrace >= 0 && lastBrace > firstBrace)
                        trimmed = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
                }

                var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(trimmed);
                if (parsed == null || !parsed.ContainsKey("advices")) return null;

                var advicesToken = parsed["advices"];
                string advicesJson = JsonConvert.SerializeObject(advicesToken);
                var advices = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(advicesJson);
                if (advices == null || advices.Count == 0) return null;

                var supported = new HashSet<string>(RimMindActionsAPI.GetSupportedIntents());
                var toolCalls = new List<StructuredToolCall>();
                int idx = 0;

                foreach (var adv in advices)
                {
                    if (!adv.TryGetValue("action", out var actionName) || actionName.NullOrEmpty()) continue;
                    if (!supported.Contains(actionName)) continue;

                    var args = new Dictionary<string, string>();
                    if (adv.TryGetValue("target", out var target) && !target.NullOrEmpty()) args["target"] = target;
                    if (adv.TryGetValue("param", out var param) && !param.NullOrEmpty()) args["param"] = param;
                    if (adv.TryGetValue("reason", out var reason) && !reason.NullOrEmpty()) args["reason"] = reason;

                    toolCalls.Add(new StructuredToolCall
                    {
                        Id = $"fallback_{idx}",
                        Name = actionName,
                        Arguments = JsonConvert.SerializeObject(args),
                    });
                    idx++;
                }

                return toolCalls.Count > 0 ? toolCalls : null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Advisor] Content fallback parse failed: {ex.Message}");
                return null;
            }
        }

        public bool ShouldRequestFeedback()
        {
            return _toolCallDepth < MaxToolCallDepth && _lastMessages != null && _lastSchema != null;
        }

        public void RequestToolFeedback(List<StructuredToolCall> toolCalls, List<ActionResult> results, Action<AIResponse> onComplete)
        {
            _toolCallDepth++;

            var messages = new List<ChatMessage>(_lastMessages ?? new List<ChatMessage>());

            messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = "",
                ReasoningContent = _lastReasoningContent,
                ToolCalls = toolCalls.Select(tc => new ChatToolCall
                {
                    Id = tc.Id,
                    Name = tc.Name,
                    Arguments = tc.Arguments,
                }).ToList()
            });

            foreach (var result in results)
            {
                var matchingTc = toolCalls.FirstOrDefault(tc => tc.Name == result.ActionName);
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
                RequestId = $"Structured_NPC-{_pawn.thingIDNumber}_fb{_toolCallDepth}",
                ModId = "Advisor",
                ExpireAtTicks = Find.TickManager.TicksGame + _settings.requestExpireTicks,
                UseJsonMode = true,
                Priority = AIRequestPriority.Normal,
            };

            RimMindAPI.RequestStructuredAsync(followUpRequest, _lastSchema!, onComplete, _lastTools);
        }

        public void BroadcastDecisionExecuted(string actionName, string? reason)
        {
            try
            {
                var summary = $"action={actionName}";
                if (!string.IsNullOrEmpty(reason)) summary += $",reason={reason}";
                RimMindAPI.PublishPerception(_pawn.thingIDNumber, "advisor_decision", summary, 0.5f);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind-Advisor] Failed to publish decision perception: {ex.Message}");
            }
        }

        public void ClearState()
        {
            _lastMessages = null;
            _lastTools = null;
            _lastSchema = null;
            _toolCallDepth = 0;
            _lastReasoningContent = null;
        }

        private float GetDecisionBudget()
        {
            var coreSettings = RimMindCoreMod.Settings?.Context;
            if (coreSettings == null) return 0.5f;
            return coreSettings.ContextBudget;
        }
    }
}
