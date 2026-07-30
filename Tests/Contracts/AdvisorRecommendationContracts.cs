using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Advisor.Advisor;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Testing;
using Xunit;

namespace RimMind.Advisor.Tests.Contracts
{
    public sealed class AdvisorRecommendationContracts
    {
        [Fact]
        public void Recommendation_payload_boundaries_are_explicit_and_safe()
        {
            var parser = new AdvisorRecommendationParser(
                toolId => toolId == "known.tool");

            ContractCaseRunner.Run(
                ("native tool-call arrays are parsed into structured calls", () =>
                {
                    bool parsed = parser.TryParseNative(
                        "[{\"id\":\"c1\",\"name\":\"known.tool\",\"arguments\":\"{}\"}]",
                        out var calls);
                    Assert.True(parsed);
                    Assert.Equal("c1", Assert.Single(calls).Id);
                    Assert.Equal("known.tool", calls[0].Name);
                }),
                ("malformed native tool calls are rejected without escaping the boundary", () =>
                {
                    Assert.False(parser.TryParseNative("{broken", out var calls));
                    Assert.Empty(calls);
                    Assert.False(parser.TryParseNative("[null]", out calls));
                    Assert.Empty(calls);
                    Assert.False(parser.TryParseNative(
                        "[{\"id\":\"ok\",\"name\":\"known.tool\"},null]",
                        out calls));
                    Assert.Empty(calls);
                    Assert.False(parser.TryParseNative("[1]", out calls));
                    Assert.Empty(calls);
                }),
                ("legacy content fallback is opt-in", () =>
                {
                    const string content = "{\"advices\":[{\"action\":\"known.tool\"}]}";
                    Assert.Null(parser.ParseLegacyIfEnabled(content, enabled: false));
                    Assert.Single(parser.ParseLegacyIfEnabled(content, enabled: true)!);
                }),
                ("fallback extracts advices and rejects unknown tools", () =>
                {
                    var calls = parser.ParseLegacy(
                        "```json\n{\"advices\":["
                        + "{\"action\":\"unknown.tool\"},"
                        + "{\"action\":\"known.tool\",\"target\":\"pawn\",\"reason\":\"help\"}"
                        + "]}\n```");

                    var call = Assert.Single(calls!);
                    Assert.Equal("fallback_0", call.Id);
                    Assert.Equal("known.tool", call.Name);
                    Assert.Contains("\"target\":\"pawn\"", call.Arguments, StringComparison.Ordinal);
                }),
                ("empty recommendations produce no executable calls", () =>
                {
                    Assert.Null(parser.ParseLegacy("{\"advices\":[]}"));
                    Assert.Null(parser.ParseLegacy("{\"advices\":[null]}"));
                    Assert.Null(parser.ParseLegacy("{\"other\":[]}"));
                    Assert.Null(parser.ParseLegacy("{broken"));
                }));
        }

        [Fact]
        public void Prompt_augmentation_is_ordered_optional_and_feedback_safe()
        {
            ContractCaseRunner.Run(
                ("known augmentations preserve deterministic order", () =>
                {
                    var envelope = AdvisorRequestAugmentationFactory.Create(
                        "npc-1",
                        schema: null,
                        enableLegacyJsonFallback: true,
                        customPrompt: "custom",
                        rejectedDecisions: "rejected");

                    Assert.Empty(envelope.Messages);
                    Assert.Equal(
                        new[]
                        {
                            AdvisorRequestAugmentationFactory.LegacyJsonFallbackId,
                            AdvisorRequestAugmentationFactory.CustomPromptId,
                            AdvisorRequestAugmentationFactory.RejectedDecisionsId
                        },
                        envelope.SystemAugmentations!.Select(item => item.Id));
                    Assert.Equal(new[] { 10, 20, 30 }, envelope.SystemAugmentations!.Select(item => item.Order));
                }),
                ("blank optional augmentations are omitted", () =>
                {
                    var envelope = AdvisorRequestAugmentationFactory.Create(
                        "npc-1",
                        schema: null,
                        enableLegacyJsonFallback: false,
                        customPrompt: " ",
                        rejectedDecisions: "");

                    Assert.Empty(envelope.SystemAugmentations!);
                }),
                ("feedback captures the final pipeline envelope", () =>
                {
                    var fallback = new List<ChatMessage>
                    {
                        new ChatMessage { Role = "system", Content = "fallback" }
                    };
                    var finalEnvelope = new LlmRequestEnvelope
                    {
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", Content = "final system" },
                            new ChatMessage { Role = "user", Content = "final user" }
                        }
                    };
                    var context = new LlmRequestContext
                    {
                        Envelope = finalEnvelope,
                        Snapshot = new ContextSnapshot { NpcId = "npc-1" }
                    };

                    var captured = AdvisorRequestAugmentationFactory.CaptureFeedbackMessages(context, fallback);

                    Assert.Equal(new[] { "final system", "final user" }, captured.Select(message => message.Content));
                }));
        }

        [Fact]
        public void Prompt_helpers_preserve_risk_labels_and_system_insertion_boundary()
        {
            ContractCaseRunner.Run(
                ("all public risk levels map to stable localization keys", () =>
                {
                    var expected = new Dictionary<RiskLevel, string>
                    {
                        [RiskLevel.Low] = "RimMind.Advisor.Prompt.Risk.Low",
                        [RiskLevel.Medium] = "RimMind.Advisor.Prompt.Risk.Medium",
                        [RiskLevel.High] = "RimMind.Advisor.Prompt.Risk.High",
                        [RiskLevel.Critical] = "RimMind.Advisor.Prompt.Risk.Critical"
                    };

                    foreach (var pair in expected)
                        Assert.Equal(pair.Value, AdvisorPromptHelper.RiskTag(pair.Key));
                    Assert.Equal(string.Empty, AdvisorPromptHelper.RiskTag((RiskLevel)999));
                }),
                ("custom prompt insertion targets the last system message", () =>
                {
                    var messages = new List<ChatMessage>
                    {
                        new ChatMessage { Role = "system" },
                        new ChatMessage { Role = "user" },
                        new ChatMessage { Role = "system" },
                        new ChatMessage { Role = "assistant" }
                    };

                    Assert.Equal(2, AdvisorPromptHelper.FindLastSystemIndex(messages));
                    Assert.Equal(-1, AdvisorPromptHelper.FindLastSystemIndex(new List<ChatMessage>()));
                }));
        }
    }
}
