using System.Linq;
using RimMind.Advisor.Advisor;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Llm;
using Xunit;

namespace RimMind.Advisor.Tests
{
    public class AdvisorRequestAugmentationFactoryTests
    {
        [Fact]
        public void Create_PreservesInitialEmptyMessagesAndUsesOrderedKnownIds()
        {
            var envelope = AdvisorRequestAugmentationFactory.Create("npc-1", null, true, "custom", "rejections");
            var augmentations = envelope.SystemAugmentations!;

            Assert.Empty(envelope.Messages);
            Assert.Equal(new[] { "advisor.legacy-json-fallback", "advisor.custom-prompt", "advisor.rejected-decisions" },
                augmentations.Select(augmentation => augmentation.Id));
            Assert.Equal(new[] { 10, 20, 30 }, augmentations.Select(augmentation => augmentation.Order));
        }

        [Fact]
        public void Create_OmitsEmptyOptionalPromptAndRejections()
        {
            var envelope = AdvisorRequestAugmentationFactory.Create("npc-1", null, false, " ", "");

            Assert.Empty(envelope.Messages);
            Assert.Empty(envelope.SystemAugmentations!);
        }

        [Fact]
        public void CaptureFeedbackMessages_UsesFinalPipelineEnvelopeInsteadOfSnapshot()
        {
            var snapshot = new ContextSnapshot { NpcId = "npc-1" };
            snapshot.AddMessage(new ChatMessage { Role = "system", Content = "snapshot only" });
            var initialEnvelope = AdvisorRequestAugmentationFactory.Create("npc-1", null, true, "custom", null);
            var finalEnvelope = new RimMind.Domain.Llm.LlmRequestEnvelope
            {
                Messages = new System.Collections.Generic.List<ChatMessage>
                {
                    new() { Role = "system", Content = "context system" },
                    new() { Role = "system", Content = "middleware augmentation" },
                    new() { Role = "user", Content = "question" },
                },
            };
            var context = new LlmRequestContext { Envelope = finalEnvelope, Snapshot = snapshot };

            var captured = AdvisorRequestAugmentationFactory.CaptureFeedbackMessages(context, initialEnvelope.Messages);

            Assert.Equal(new[] { "context system", "middleware augmentation", "question" }, captured.Select(message => message.Content));
        }
    }
}
