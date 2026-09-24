using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Llm;
using RimMind.Presentation.Api;
using Verse;
using Xunit;

namespace RimMind.Advisor.Tests.Contracts
{
    [Collection("Advisor runtime")]
    public sealed class AdvisorContextIntegrationContracts
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Initial_and_feedback_envelopes_reach_registered_decision_providers(bool isFeedback)
        {
            RimMindAPI.Context.ContextKeys.Definitions.Clear();
            RimMindAPI.Tools.Definitions.Clear();
            RimMindAPI.Request.Sent.Clear();
            try
            {
                RimMindAPI.Tools.Definitions.Add(new ToolDefinition { Id = "test.action", Description = "fixture action" });
                AdvisorProviderRegistrar.RegisterAll();
                LlmRequestEnvelope envelope;
                if (isFeedback)
                {
                    var driver = new AdvisorTaskDriver(new Pawn { thingIDNumber = 7 }, new RimMindAdvisorSettings());
                    driver.RequestToolFeedback(new List<StructuredToolCall>(), new List<ToolResult>(), _ => { });
                    envelope = Assert.Single(RimMindAPI.Request.Sent);
                }
                else
                    envelope = AdvisorRequestAugmentationFactory.Create("NPC-7", null, false, null, null);

                var context = new ProviderContext(envelope.ScenarioId, "advisor-context") { PawnId = 7, NpcId = envelope.NpcId };
                var task = RimMindAPI.Context.ContextKeys.Definitions.Single(definition => definition.Key == "advisor_task");
                var actions = RimMindAPI.Context.ContextKeys.Definitions.Single(definition => definition.Key == "actions_list");
                Assert.Contains("TaskInstruction.Role", await task.Provider(context, CancellationToken.None));
                Assert.Contains("test.action", await actions.Provider(context, CancellationToken.None));
                Assert.Equal(ScenarioIds.Decision, envelope.ScenarioId);
                Assert.Null(await task.Provider(context with { Scenario = ScenarioIds.Dialogue }, CancellationToken.None));
                Assert.Null(await actions.Provider(context with { Scenario = ScenarioIds.Dialogue }, CancellationToken.None));
            }
            finally
            {
                RimMindAPI.Context.ContextKeys.Definitions.Clear();
                RimMindAPI.Tools.Definitions.Clear();
                RimMindAPI.Request.Sent.Clear();
            }
        }
    }
}
