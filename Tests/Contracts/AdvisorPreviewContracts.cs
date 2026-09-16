using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LudeonTK;
using RimMind.Advisor.Debug;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Models.Context;
using RimMind.Domain.Llm;
using RimMind.Presentation.Api;
using Verse;
using Xunit;

namespace RimMind.Advisor.Tests.Contracts
{
    [Collection("Advisor runtime")]
    public sealed class AdvisorPreviewContracts
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Prompt_preview_returns_before_completion_and_publishes_only_in_its_game(bool changeGame)
        {
            var builder = new DeferredContextBuilder();
            RimMindAPI.Settings.ContextEngine = builder;
            Find.Selector.SingleSelectedThing = new Pawn { thingIDNumber = 42 };
            Current.Game = new Game();
            Log.Messages.Clear();
            LongEventHandler.Reset();
            try
            {
                // Invoke the real registered debug command by its menu label, not a helper's name.
                var action = typeof(AdvisorDebugActions).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    .Single(method => method.GetCustomAttribute<DebugActionAttribute>()?.Name == "Show Full Prompt (selected)");
                action.Invoke(null, null);

                Assert.False(builder.Completion.Task.IsCompleted);
                Assert.Equal("NPC-42", builder.NpcId);
                Assert.Equal(ScenarioIds.Decision, builder.Scenario);
                Assert.Empty(Log.Messages);
                if (changeGame) Current.Game = new Game();

                var snapshot = new ContextSnapshot { NpcId = "NPC-42" };
                snapshot.AddMessage(new ChatMessage { Role = "system", Content = "fixture system" });
                snapshot.AddMessage(new ChatMessage { Role = "user", Content = "fixture user" });
                builder.Completion.SetResult(snapshot);
                await LongEventHandler.Queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Empty(Log.Messages);
                LongEventHandler.Drain();

                if (changeGame)
                    Assert.Empty(Log.Messages);
                else
                {
                    string output = Assert.Single(Log.Messages);
                    Assert.Contains("fixture system", output);
                    Assert.Contains("fixture user", output);
                }
            }
            finally
            {
                RimMindAPI.Settings.ContextEngine = null;
                Find.Selector.SingleSelectedThing = null;
                Current.Game = null;
                LongEventHandler.Reset();
                Log.Messages.Clear();
            }
        }

        private sealed class DeferredContextBuilder : IContextBuilder
        {
            public TaskCompletionSource<ContextSnapshot?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public string? NpcId { get; private set; }
            public string? Scenario { get; private set; }
            public Task<ContextSnapshot?> BuildSnapshotFromEnvelopeAsync(string npcId, string? currentQuery,
                int maxTokens = 800, float temperature = 0.7f, string? scenarioId = null,
                HashSet<string>? skipLayers = null, CancellationToken ct = default)
            {
                NpcId = npcId;
                Scenario = scenarioId;
                return Completion.Task;
            }
            public IBudgetScheduler? GetScheduler() => null;
            public EmbeddingSnapshotStore? GetEmbeddingSnapshotStore() => null;
        }
    }
}
