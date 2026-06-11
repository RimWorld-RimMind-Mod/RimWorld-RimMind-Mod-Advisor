using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Api;
using RimWorld;
using Verse;

using ClientStructuredToolCall = RimMind.Domain.Llm.StructuredToolCall;

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

        public void BuildAndSendRequest(Action<Result<LlmResponse, RimMindError>> onComplete)
        {
            var npcId = $"NPC-{_pawn.thingIDNumber}";
            var engine = RimMindAPI.Settings.GetContextEngine();
            var snapshot = engine?.BuildSnapshotFromEnvelope(
                npcId, null, 400, 0.7f, RimMindAPI.Context.ScenarioDecision);

            var schema = (string?)null;
            var tools = BuildActionTools();
            var messages = snapshot != null
                ? new List<ChatMessage>(snapshot.Messages)
                : new List<ChatMessage>();

            if (_settings.enableLegacyJsonFallback)
            {
                int lastSysIdx = -1;
                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    if (messages[i].Role == "system") { lastSysIdx = i; break; }
                }
                messages.Insert(lastSysIdx + 1, new ChatMessage
                {
                    Role = "system",
                    Content = "[Legacy compatibility] Native ToolCall is preferred. If your model cannot emit tool_calls, respond with JSON object {\"advices\":[{\"action\":\"tool.id\",\"param\":\"{}\",\"reason\":\"short reason\"}]}."
                });
            }

            if (!_settings.advisorCustomPrompt.NullOrEmpty())
            {
                int lastSysIdx = -1;
                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    if (messages[i].Role == "system") { lastSysIdx = i; break; }
                }
                messages.Insert(lastSysIdx + 1, new ChatMessage { Role = "system", Content = _settings.advisorCustomPrompt });

                string reactionsText = GetRecentRejectedAdvisorDecisions(20);
                if (!string.IsNullOrEmpty(reactionsText))
                    messages.Insert(lastSysIdx + 2, new ChatMessage { Role = "system", Content = reactionsText });
            }
            else
            {
                string reactionsText = GetRecentRejectedAdvisorDecisions(20);
                if (!string.IsNullOrEmpty(reactionsText))
                {
                    int lastSysIdx = -1;
                    for (int i = messages.Count - 1; i >= 0; i--)
                    {
                        if (messages[i].Role == "system") { lastSysIdx = i; break; }
                    }
                    messages.Insert(lastSysIdx + 1, new ChatMessage { Role = "system", Content = reactionsText });
                }
            }

            _lastMessages = new List<ChatMessage>(messages);
            _lastTools = tools;
            _lastSchema = schema;
            _toolCallDepth = 0;
            _lastReasoningContent = null;

            var maxTokens = snapshot?.MaxTokens ?? 400;
            var temperature = snapshot?.Temperature ?? 0.7f;
            var expireAtTicks = Find.TickManager.TicksGame + _settings.requestExpireTicks;

            var envelope = LlmRequestEnvelopeBuilder
                .ForScenario("Advisor")
                .WithModId("RimMind.Advisor")
                .WithNpcId(npcId)
                .WithSchema(schema)
                .WithMessages(messages)
                .WithTools(tools)
                .WithToolDispatchMode(ToolCallDispatchMode.Manual)
                .WithMaxTokens(maxTokens)
                .WithTemperature(temperature)
                .WithExpireAtTicks(expireAtTicks)
                .Build();

            RimMindAPI.Request.Send(envelope, onComplete);
        }

        public List<StructuredTool>? BuildActionTools()
        {
            try
            {
                var defs = RimMindAPI.Tools.GetAllDefinitions();
                if (defs.Count == 0) return null;
                return defs.Select(d => new StructuredTool
                {
                    Name = d.Id,
                    Description = d.Description,
                    Parameters = d.ParametersSchema,
                }).ToList();
            }
            catch (Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] BuildActionTools failed: {ex.Message}");
                return null;
            }
        }

        public void SetReasoningContent(string? content)
        {
            _lastReasoningContent = content;
        }

        public string? LastReasoningContent => _lastReasoningContent;

        public bool TryParseToolCalls(string toolCallsJson, out List<ClientStructuredToolCall> toolCalls)
        {
            toolCalls = new List<ClientStructuredToolCall>();
            try
            {
                var parsed = JsonConvert.DeserializeObject<List<ClientStructuredToolCall>>(toolCallsJson);
                if (parsed != null) toolCalls = parsed;
                return true;
            }
            catch (Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] ToolCalls parse failed for {_pawn.Name.ToStringShort}: {ex.Message}");
                return false;
            }
        }

        public List<ClientStructuredToolCall>? TryParseContentAsToolCallsIfEnabled(string content)
        {
            return _settings.enableLegacyJsonFallback ? TryParseContentAsToolCalls(content) : null;
        }

        public List<ClientStructuredToolCall>? TryParseContentAsToolCalls(string content)
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

                var toolCalls = new List<ClientStructuredToolCall>();
                int idx = 0;

                foreach (var adv in advices)
                {
                    if (!adv.TryGetValue("action", out var actionName) || actionName.NullOrEmpty()) continue;
                    if (RimMindAPI.Tools.FindById(actionName) == null) continue;

                    var args = new Dictionary<string, string>();
                    if (adv.TryGetValue("target", out var target) && !target.NullOrEmpty()) args["target"] = target;
                    if (adv.TryGetValue("param", out var param) && !param.NullOrEmpty()) args["param"] = param;
                    if (adv.TryGetValue("reason", out var reason) && !reason.NullOrEmpty()) args["reason"] = reason;

                    toolCalls.Add(new ClientStructuredToolCall
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
                RimMindErrors.Warn($"[RimMind-Advisor] Content fallback parse failed: {ex.Message}");
                return null;
            }
        }

        public bool ShouldRequestFeedback()
        {
            return _toolCallDepth < MaxToolCallDepth
                && _lastMessages != null
                && _lastTools != null
                && _lastTools.Count > 0;
        }

        public void RequestToolFeedback(
            List<ClientStructuredToolCall> toolCalls,
            IReadOnlyList<ToolResult> results,
            Action<Result<LlmResponse, RimMindError>> onComplete)
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
                messages.Add(new ChatMessage
                {
                    Role = "tool",
                    Content = result.Content,
                    ToolCallId = result.ToolCallId ?? result.ToolName ?? "tool_result",
                });
            }

            _lastMessages = messages;

            var npcId = $"NPC-{_pawn.thingIDNumber}";
            var expireAtTicks = Find.TickManager.TicksGame + _settings.requestExpireTicks;

            var envelope = LlmRequestEnvelopeBuilder
                .ForScenario("Advisor")
                .WithModId("RimMind.Advisor")
                .WithNpcId(npcId)
                .WithSchema(_lastSchema)
                .WithMessages(messages)
                .WithTools(_lastTools)
                .WithToolDispatchMode(ToolCallDispatchMode.Manual)
                .WithMaxTokens(400)
                .WithTemperature(0.7f)
                .WithExpireAtTicks(expireAtTicks)
                .Build();

            RimMindAPI.Request.Send(envelope, onComplete);
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
                RimMindErrors.Warn($"[RimMind-Advisor] Failed to publish decision perception: {ex.Message}");
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

        private static string GetRecentRejectedAdvisorDecisions(int maxCount)
        {
            try
            {
                var store = AdvisorHistoryStore.Instance;
                if (store == null) return string.Empty;

                var globalLog = store.GlobalLog;
                if (globalLog == null || globalLog.Count == 0) return string.Empty;

                var rejected = globalLog
                    .Where(r => r.result == "rejected")
                    .OrderByDescending(r => r.tick)
                    .Take(maxCount)
                    .ToList();

                if (rejected.Count == 0) return string.Empty;

                var sb = new StringBuilder();
                sb.AppendLine("[RimMind-Advisor] Player reactions to previous AI advice:");
                foreach (var r in rejected)
                {
                    int day = r.tick / 60000 + 1;
                    sb.AppendLine($"[Day {day}] Action: {r.action}, Reason: {r.reason ?? "N/A"}, Player rejected");
                }
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] Failed to get rejected decisions: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
