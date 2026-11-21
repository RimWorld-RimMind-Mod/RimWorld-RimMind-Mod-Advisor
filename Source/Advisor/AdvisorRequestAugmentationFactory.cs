using System.Collections.Generic;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Llm;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Builds Advisor's initial envelope using <see cref="ScenarioIds.AdvisorDecision"/>
    /// without synchronously constructing Core context.
    /// </summary>
    public static class AdvisorRequestAugmentationFactory
    {
        public const string LegacyJsonFallbackId = "advisor.legacy-json-fallback";
        public const string CustomPromptId = "advisor.custom-prompt";
        public const string RejectedDecisionsId = "advisor.rejected-decisions";
        public const string ColonyMacroStatusId = "advisor.colony-macro-status";

        private const string LegacyJsonFallbackContent =
            "[Legacy compatibility] Prefer native Tool Calling using each tool's declared JSON schema. Only if your model cannot emit native tool_calls, respond with a JSON object {\"advices\":[{\"name\":\"<tool_id>\",\"parameters\":{...}}]}.";

        public static LlmRequestEnvelope Create(
            string npcId,
            string? schema,
            bool enableLegacyJsonFallback,
            string? customPrompt,
            string? rejectedDecisions,
            List<StructuredTool>? tools = null,
            int maxTokens = 400,
            float temperature = 0.7f,
            int? expireAtTicks = null,
            string? colonyMacroStatus = null)
        {
            var augmentations = new List<PromptAugmentation>();
            if (!string.IsNullOrWhiteSpace(colonyMacroStatus))
                augmentations.Add(new PromptAugmentation(ColonyMacroStatusId, colonyMacroStatus!, 5));
            if (enableLegacyJsonFallback)
                augmentations.Add(new PromptAugmentation(LegacyJsonFallbackId, LegacyJsonFallbackContent, 10));
            if (!string.IsNullOrWhiteSpace(customPrompt))
                augmentations.Add(new PromptAugmentation(CustomPromptId, customPrompt!, 20));
            if (!string.IsNullOrWhiteSpace(rejectedDecisions))
                augmentations.Add(new PromptAugmentation(RejectedDecisionsId, rejectedDecisions!, 30));

            return LlmRequestEnvelopeBuilder
                .ForScenario(ScenarioIds.AdvisorDecision)
                .WithModId(AdvisorRequestIds.RequestModId)
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
