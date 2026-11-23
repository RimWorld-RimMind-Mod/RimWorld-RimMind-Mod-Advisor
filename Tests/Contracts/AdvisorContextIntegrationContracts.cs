using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Enums;
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
        public async Task Initial_and_feedback_envelopes_use_AdvisorDecision_and_isolate_AgentDecision(bool isFeedback)
        {
            RimMindAPI.Context.ContextKeys.Definitions.Clear();
            RimMindAPI.Context.RegisteredScenarios.Clear();
            RimMindAPI.Tools.Definitions.Clear();
            RimMindAPI.Request.Sent.Clear();
            RimMindAPI.MainThreadSnapshot.CallCount = 0;
            try
            {
                RimMindAPI.Tools.Definitions.Add(new ToolDefinition
                {
                    Id = "test.action",
                    Description = "fixture action",
                    ParametersSchema = "{\"type\":\"object\",\"properties\":{\"pawn_id\":{\"type\":\"integer\",\"description\":\"Pawn thing ID\"},\"map_id\":{\"type\":\"integer\"}}}",
                    Manifest = new ToolManifest
                    {
                        Scopes = ToolScope.ColonistSelf,
                        RiskLevel = RiskLevel.Low
                    }
                });
                RimMindAPI.Tools.Definitions.Add(new ToolDefinition
                {
                    Id = "world.admin_only",
                    Description = "world admin tool",
                    Manifest = new ToolManifest
                    {
                        Scopes = ToolScope.WorldAdmin,
                        RiskLevel = RiskLevel.High
                    }
                });
                RimMindAPI.Tools.Definitions.Add(new ToolDefinition
                {
                    Id = "colony.high_risk",
                    Description = "high risk build action",
                    Manifest = new ToolManifest
                    {
                        Scopes = ToolScope.ColonyBuild,
                        RiskLevel = RiskLevel.High
                    }
                });

                AdvisorProviderRegistrar.RegisterAll();

                // Dedicated AdvisorDecision scenario descriptor must be registered
                Assert.Contains(RimMindAPI.Context.RegisteredScenarios, s => s.Id == ScenarioIds.AdvisorDecision);

                LlmRequestEnvelope envelope;
                if (isFeedback)
                {
                    var driver = new AdvisorTaskDriver(new Pawn { thingIDNumber = 7 }, new RimMindAdvisorSettings());
                    driver.BuildAndSendRequest(_ => { });
                    RimMindAPI.Request.Sent.Clear();
                    driver.RequestToolFeedback(
                        new List<StructuredToolCall> { new StructuredToolCall { Id = "call_1", Name = "test.action", Arguments = "{}" } },
                        new List<ToolResult> { ToolResult.Ok("done", "call_1", "test.action") },
                        _ => { });
                    envelope = Assert.Single(RimMindAPI.Request.Sent);
                    // Round-2 feedback preserves the compact scoped toolset for Anthropic / OpenAI tool_result compatibility
                    Assert.NotNull(envelope.Tools);
                    Assert.Contains(envelope.Tools!, t => t.Name == "test.action");
                    Assert.DoesNotContain(envelope.Tools!, t => t.Name == "world.admin_only");
                }
                else
                {
                    var driver = new AdvisorTaskDriver(new Pawn { thingIDNumber = 7 }, new RimMindAdvisorSettings());
                    var scopedTools = driver.BuildActionTools();
                    Assert.NotNull(scopedTools);
                    Assert.Contains(scopedTools!, t => t.Name == "test.action");
                    Assert.Contains(scopedTools!, t => t.Name == "colony.high_risk");
                    // WorldAdmin tools must be excluded from AdvisorDefaultScope
                    Assert.DoesNotContain(scopedTools!, t => t.Name == "world.admin_only");
                    // Compact schema strips boilerplate parameter descriptions and optional map_id noise
                    var testTool = scopedTools!.Single(t => t.Name == "test.action");
                    Assert.DoesNotContain("Pawn thing ID", testTool.Parameters ?? string.Empty);
                    Assert.DoesNotContain("map_id", testTool.Parameters ?? string.Empty);

                    envelope = AdvisorRequestAugmentationFactory.Create("NPC-7", null, false, null, null, scopedTools);
                    Assert.NotNull(envelope.Tools);
                }

                Assert.Equal(ScenarioIds.AdvisorDecision, envelope.ScenarioId);

                var context = new ProviderContext(envelope.ScenarioId, "advisor-context") { PawnId = 7, NpcId = envelope.NpcId };
                var task = RimMindAPI.Context.ContextKeys.Definitions.Single(definition => definition.Key == "advisor_task");
                var actions = RimMindAPI.Context.ContextKeys.Definitions.Single(definition => definition.Key == "actions_list");

                var taskText = await task.Provider(context, CancellationToken.None);
                Assert.Contains("TaskInstruction.Role", taskText);
                Assert.Contains("TaskInstruction.LanguageRule", taskText);

                var actionsText = await actions.Provider(context, CancellationToken.None);
                Assert.NotNull(actionsText);
                // P0-2: L3 actions_list is concise guidance, NOT a full plain-text dump of every tool & description
                Assert.DoesNotContain("fixture action", actionsText);
                Assert.DoesNotContain("test.action", actionsText);
                Assert.Contains("RimMind.Advisor.Prompt.ActionsGuidanceHeader", actionsText);
                Assert.Contains("RimMind.Advisor.Prompt.ActionsElevatedRisk", actionsText);

                // MainThreadSnapshot must be used by providers
                Assert.True(RimMindAPI.MainThreadSnapshot.CallCount >= 2);

                // P0-1: Advisor providers MUST NOT leak into AgentDecision, Decision, or Dialogue scenarios
                Assert.Null(await task.Provider(context with { Scenario = ScenarioIds.AgentDecision }, CancellationToken.None));
                Assert.Null(await actions.Provider(context with { Scenario = ScenarioIds.AgentDecision }, CancellationToken.None));
                Assert.Null(await task.Provider(context with { Scenario = ScenarioIds.Decision }, CancellationToken.None));
                Assert.Null(await actions.Provider(context with { Scenario = ScenarioIds.Decision }, CancellationToken.None));
                Assert.Null(await task.Provider(context with { Scenario = ScenarioIds.Dialogue }, CancellationToken.None));
                Assert.Null(await actions.Provider(context with { Scenario = ScenarioIds.Dialogue }, CancellationToken.None));
            }
            finally
            {
                RimMindAPI.Context.ContextKeys.Definitions.Clear();
                RimMindAPI.Context.RegisteredScenarios.Clear();
                RimMindAPI.Tools.Definitions.Clear();
                RimMindAPI.Request.Sent.Clear();
            }
        }

        [Fact]
        public async Task Advisor_history_formats_errors_without_collapsing_to_ignored_and_uses_MainThreadSnapshot()
        {
            RimMindAPI.Context.ContextKeys.Definitions.Clear();
            RimMindAPI.MainThreadSnapshot.CallCount = 0;
            var historyStore = new AdvisorHistoryStore(new RimWorld.Planet.World());

            var pawn = new Pawn { thingIDNumber = 15 };
            Find.CurrentMap = new Map();
            Find.CurrentMap.mapPawns.FreeColonists.Add(pawn);

            historyStore.AddRecord(pawn, new AdvisorRequestRecord
            {
                action = "actions_place_blueprint",
                reason = "build shelter",
                result = "Insufficient steel: need 50, have 10",
                tick = 100
            });

            try
            {
                AdvisorProviderRegistrar.RegisterAll();
                var historyDef = RimMindAPI.Context.ContextKeys.Definitions.Single(d => d.Key == "advisor_history");
                var context = new ProviderContext(ScenarioIds.AdvisorDecision, "trace") { PawnId = 15 };
                var text = await historyDef.Provider(context, CancellationToken.None);

                Assert.NotNull(text);
                Assert.Contains("RimMind.Advisor.Prompt.ResultFailed", text);
                Assert.Contains("Insufficient steel: need 50, have 10", text);
                Assert.DoesNotContain("Ignored", text);
                Assert.True(RimMindAPI.MainThreadSnapshot.CallCount >= 1);
            }
            finally
            {
                RimMindAPI.Context.ContextKeys.Definitions.Clear();
                AdvisorHistoryStore.ResetCache();
            }
        }
    }
}
