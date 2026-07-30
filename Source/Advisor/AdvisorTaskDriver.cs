using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Tools;
using RimMind.Presentation.Api;
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

        private readonly AdvisorFeedbackSession _feedbackSession = new AdvisorFeedbackSession();
        private readonly AdvisorRecommendationParser _recommendationParser;

        public AdvisorTaskDriver(Pawn pawn, RimMindAdvisorSettings settings)
        {
            _pawn = pawn;
            _settings = settings;
            _recommendationParser = new AdvisorRecommendationParser(
                toolId => RimMindAPI.Tools.FindById(toolId) != null);
        }

        public bool HasPendingState => _feedbackSession.HasPendingState;

        public void BuildAndSendRequest(Action<Result<LlmResponse, RimMindError>> onComplete)
        {
            var npcId = $"NPC-{_pawn.thingIDNumber}";
            var schema = (string?)null;
            var tools = BuildActionTools();
            var reactionsText = GetRecentRejectedAdvisorDecisions(20);
            _feedbackSession.Begin(tools, schema);

            var expireAtTicks = Find.TickManager.TicksGame + _settings.requestExpireTicks;

            var envelope = AdvisorRequestAugmentationFactory.Create(
                npcId,
                schema,
                _settings.enableLegacyJsonFallback,
                _settings.advisorCustomPrompt,
                reactionsText,
                tools,
                400,
                0.7f,
                expireAtTicks);

            RimMindAPI.Request.Send(envelope, (result, context) =>
            {
                _feedbackSession.CaptureMessages(
                    AdvisorRequestAugmentationFactory.CaptureFeedbackMessages(
                        context,
                        envelope.Messages));
                onComplete(result);
            });
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
            _feedbackSession.SetReasoningContent(content);
        }

        public string? LastReasoningContent => _feedbackSession.ReasoningContent;

        public bool TryParseToolCalls(string toolCallsJson, out List<ClientStructuredToolCall> toolCalls)
        {
            if (!_recommendationParser.TryParseNative(toolCallsJson, out toolCalls))
            {
                RimMindErrors.Warn($"[RimMind-Advisor] ToolCalls parse failed for {_pawn.Name.ToStringShort}.");
                return false;
            }

            return true;
        }

        public List<ClientStructuredToolCall>? TryParseContentAsToolCallsIfEnabled(string content)
        {
            return _recommendationParser.ParseLegacyIfEnabled(
                content,
                _settings.enableLegacyJsonFallback);
        }

        public List<ClientStructuredToolCall>? TryParseContentAsToolCalls(string content)
            => _recommendationParser.ParseLegacy(content);

        public bool ShouldRequestFeedback()
        {
            return _feedbackSession.CanRequestFeedback(MaxToolCallDepth);
        }

        public void RequestToolFeedback(
            List<ClientStructuredToolCall> toolCalls,
            IReadOnlyList<ToolResult> results,
            Action<Result<LlmResponse, RimMindError>> onComplete)
        {
            var messages = new List<ChatMessage>(
                _feedbackSession.Messages ?? new List<ChatMessage>());

            messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = "",
                ReasoningContent = _feedbackSession.ReasoningContent,
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

            _feedbackSession.BeginFeedback(messages);

            var npcId = $"NPC-{_pawn.thingIDNumber}";
            var expireAtTicks = Find.TickManager.TicksGame + _settings.requestExpireTicks;

            var envelope = LlmRequestEnvelopeBuilder
                .ForScenario("Advisor")
                .WithModId("RimMind.Advisor")
                .WithNpcId(npcId)
                .WithSchema(_feedbackSession.Schema)
                .WithMessages(messages)
                .WithTools(_feedbackSession.Tools)
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
            _feedbackSession.Clear();
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
