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
    [Collection("Advisor runtime")]
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
                ("fallback accepts single tool JSON objects, arrays, and to= commentary", () =>
                {
                    var singleObj = parser.ParseLegacy(
                        "{\"name\":\"known.tool\",\"parameters\":{\"target\":\"pawn\",\"reason\":\"help\"}}");
                    var singleCall = Assert.Single(singleObj!);
                    Assert.Equal("known.tool", singleCall.Name);
                    Assert.Contains("\"target\":\"pawn\"", singleCall.Arguments, StringComparison.Ordinal);

                    var arrayObj = parser.ParseLegacy(
                        "[{\"tool\":\"known.tool\",\"arguments\":{\"reason\":\"batch\"}}]");
                    var arrayCall = Assert.Single(arrayObj!);
                    Assert.Equal("known.tool", arrayCall.Name);

                    var commentary = parser.ParseLegacy(
                        "Executing tool to=known.tool {\"target\":\"pawn\"}");
                    var commentaryCall = Assert.Single(commentary!);
                    Assert.Equal("known.tool", commentaryCall.Name);
                }),
                ("empty recommendations produce no executable calls", () =>
                {
                    Assert.Null(parser.ParseLegacy("{\"advices\":[]}"));
                    Assert.Null(parser.ParseLegacy("{\"advices\":[null]}"));
                    Assert.Null(parser.ParseLegacy("{\"other\":[]}"));
                    Assert.Null(parser.ParseLegacy("{broken"));
                }),
                ("fallback strips thinking tags and repairs trailing-comma JSON via Core LlmPayloadParser", () =>
                {
                    var calls = parser.ParseLegacy(
                        "<thinking>Need to rest</thinking>\n```json\n{\"advices\":[{\"action\":\"known.tool\",\"reason\":\"exhausted\",},]}\n```");
                    var call = Assert.Single(calls!);
                    Assert.Equal("known.tool", call.Name);
                    Assert.Contains("exhausted", call.Arguments, StringComparison.Ordinal);
                }));
        }

        [Fact]
        public void Summary_formatter_and_mote_presenter_use_manifest_keys_and_Core_mote_wrapping()
        {
            RimMind.Presentation.Api.RimMindAPI.Tools.Definitions.Clear();
            try
            {
                RimMind.Presentation.Api.RimMindAPI.Tools.Definitions.Add(new RimMind.Application.Common.Models.Tools.ToolDefinition
                {
                    Id = "custom.build",
                    Manifest = new RimMind.Application.Common.Models.Tools.ToolManifest
                    {
                        DisplayName = "Custom Build",
                        SummaryArgKeys = new[] { "blueprint_key" }
                    }
                });

                var byManifestKey = ToolCallSummaryFormatter.ExtractToolCallReason(new StructuredToolCall
                {
                    Name = "custom.build",
                    Arguments = "{\"blueprint_key\":\"solar_panel\"}"
                });
                Assert.Equal("Custom Build: Solar Panel", byManifestKey);

                var byNestedReason = ToolCallSummaryFormatter.ExtractToolCallReason(new StructuredToolCall
                {
                    Name = "custom.build",
                    Arguments = "{\"params\":{\"reason\":\"Need emergency power\"}}"
                });
                Assert.Equal("Need emergency power", byNestedReason);

                var stripped = ToolCallSummaryFormatter.StripPreApprovalArgument("{\"pre_approved\":true,\"x\":1}");
                Assert.DoesNotContain("pre_approved", stripped);
                Assert.Contains("\"x\":1", stripped);

                var wrapped = AdvisorMotePresenter.FormatAdvisorMoteText("This is a very long English advisor decision bubble message for colonist", maxLineLength: 20, maxTotalLength: 50);
                Assert.Contains("\n", wrapped);
                Assert.EndsWith("…", wrapped);
            }
            finally
            {
                RimMind.Presentation.Api.RimMindAPI.Tools.Definitions.Clear();
            }
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
        public void Prompt_helpers_preserve_risk_labels()
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
        }
    }
}
