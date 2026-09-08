using System.Collections.Generic;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Llm;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Builds Advisor's initial envelope without synchronously constructing Core context.
    /// </summary>
    public static class AdvisorRequestAugmentationFactory
    {
        public const string LegacyJsonFallbackId = "advisor.legacy-json-fallback";
        public const string CustomPromptId = "advisor.custom-prompt";
        public const string RejectedDecisionsId = "advisor.rejected-decisions";

        private const string LegacyJsonFallbackContent =
            "[Legacy compatibility] Native ToolCall is preferred. If your model cannot emit tool_calls, respond with JSON object {\"advices\":[{\"action\":\"tool.id\",\"param\":\"{}\",\"reason\":\"short reason\"}]}.";

        public static LlmRequestEnvelope Create(
            string npcId,
            string? schema,
            bool enableLegacyJsonFallback,
            string? customPrompt,
            string? rejectedDecisions,
            List<StructuredTool>? tools = null,
            int maxTokens = 400,
            float temperature = 0.7f,
            int? expireAtTicks = null)
        {
            var augmentations = new List<PromptAugmentation>();
            if (enableLegacyJsonFallback)
                augmentations.Add(new PromptAugmentation(LegacyJsonFallbackId, LegacyJsonFallbackContent, 10));
            if (!string.IsNullOrWhiteSpace(customPrompt))
                augmentations.Add(new PromptAugmentation(CustomPromptId, customPrompt!, 20));
            if (!string.IsNullOrWhiteSpace(rejectedDecisions))
                augmentations.Add(new PromptAugmentation(RejectedDecisionsId, rejectedDecisions!, 30));

            return LlmRequestEnvelopeBuilder
                .ForScenario("Advisor")
                .WithModId("RimMind.Advisor")
                .WithNpcId(npcId)
                .WithSchema(schema)
                .WithSystemAugmentations(augmentations)
                .WithTools(tools)
                .WithToolDispatchMode(ToolCallDispatchMode.Manual)
                .WithMaxTokens(maxTokens)
                .WithTemperature(temperature)
                .WithExpireAtTicks(expireAtTicks)
                .Build();
        }

        public static List<ChatMessage> CaptureFeedbackMessages(
            LlmRequestContext? context,
            IReadOnlyList<ChatMessage> fallbackMessages)
        {
            return context?.Envelope?.Messages != null
                ? new List<ChatMessage>(context.Envelope.Messages)
                : new List<ChatMessage>(fallbackMessages);
        }
    }
}
