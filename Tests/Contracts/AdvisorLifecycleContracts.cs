using System;
using System.Collections.Generic;
using System.IO;
using RimMind.Advisor;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Data;
using RimMind.Application.Common.Models.UI;
using RimMind.Domain.Llm;
using RimMind.Testing;
using RimWorld.Planet;
using Verse;
using Xunit;

namespace RimMind.Advisor.Tests.Contracts
{
    public sealed class AdvisorLifecycleContracts
    {
        [Fact]
        public void Request_cycle_aggregates_feedback_and_reaches_one_terminal_state()
        {
            ContractCaseRunner.Run(
                ("feedback waits for every approval and starts once", () =>
                {
                    var cycle = new AdvisorRequestCycleState<string, string>();
                    cycle.AddPendingApproval();
                    cycle.AddPendingApproval();
                    cycle.QueueFeedback("approved", "result");
                    cycle.FinishApproval();

                    Assert.False(cycle.TryStartFeedback(out _, out _));
                    cycle.FinishApproval();
                    Assert.True(cycle.TryStartFeedback(out var calls, out var results));
                    Assert.Equal(new[] { "approved" }, calls);
                    Assert.Equal(new[] { "result" }, results);
                    Assert.False(cycle.TryStartFeedback(out _, out _));

                    cycle.FinishFeedback();
                    Assert.True(cycle.CanComplete);
                }),
                ("direct and approved results stay in one ordered feedback batch", () =>
                {
                    var cycle = new AdvisorRequestCycleState<string, string>();
                    cycle.AddPendingApproval();
                    cycle.QueueFeedback("direct", "direct-result");
                    cycle.QueueFeedback("approved", "approved-result");
                    cycle.FinishApproval();

                    Assert.True(cycle.TryStartFeedback(out var calls, out var results));
                    Assert.Equal(new[] { "direct", "approved" }, calls);
                    Assert.Equal(new[] { "direct-result", "approved-result" }, results);
                }),
                ("response batches cannot advance synchronously completed approvals", () =>
                {
                    var cycle = new AdvisorRequestCycleState<string, string>();
                    cycle.BeginResponseBatch();
                    cycle.AddPendingApproval();
                    cycle.QueueFeedback("direct", "result");
                    cycle.FinishApproval();

                    Assert.False(cycle.TryStartFeedback(out _, out _));
                    cycle.EndResponseBatch();
                    Assert.True(cycle.TryStartFeedback(out _, out _));
                }),
                ("cancellation errors do not strand remaining approvals", () =>
                {
                    var cycle = new AdvisorRequestCycleState<string, string>();
                    var first = new RequestEntry();
                    var second = new RequestEntry();
                    int secondCancellationCount = 0;
                    cycle.TrackPendingApproval(first, () => throw new InvalidOperationException("first failed"));
                    cycle.TrackPendingApproval(second, () => secondCancellationCount++);

                    var errors = cycle.CancelPendingApprovals();

                    Assert.Single(errors);
                    Assert.Equal(1, secondCancellationCount);
                    Assert.Equal(0, cycle.PendingApprovals);
                    Assert.True(cycle.CanComplete);
                }),
                ("Verse component delegates the complete request cycle", () =>
                {
                    var component = ReadSource("Comps/CompAIAdvisor.cs");
                    var coordinator = ReadSource("Advisor/AdvisorCycleCoordinator.cs");
                    Assert.Contains("AdvisorCycleCoordinator", component, StringComparison.Ordinal);
                    Assert.DoesNotContain("OnAdviceReceived", component, StringComparison.Ordinal);
                    Assert.DoesNotContain("SubmitToolCallForApproval", component, StringComparison.Ordinal);
                    Assert.Contains("OnAdviceReceived", coordinator, StringComparison.Ordinal);
                    Assert.Contains("SubmitToolCallForApproval", coordinator, StringComparison.Ordinal);
                    Assert.Contains("TryAdvanceRequestCycle", coordinator, StringComparison.Ordinal);
                }));
        }

        [Fact]
        public void Concurrency_history_and_hint_state_remain_bounded()
        {
            ContractCaseRunner.Run(
                ("concurrency count round-trips without leaking a slot", () =>
                {
                    int before = AdvisorConcurrencyTracker.ActiveCount;
                    AdvisorConcurrencyTracker.Increment();
                    try
                    {
                        Assert.Equal(before + 1, AdvisorConcurrencyTracker.ActiveCount);
                    }
                    finally
                    {
                        AdvisorConcurrencyTracker.Decrement();
                    }
                    Assert.Equal(before, AdvisorConcurrencyTracker.ActiveCount);
                }),
                ("scan path enforces the configured concurrency ceiling", () =>
                {
                    var capacity = new AdvisorRequestCapacity(active: 2, maximum: 3);
                    Assert.Equal(1, capacity.Remaining);
                    Assert.True(capacity.TryReserve());
                    Assert.Equal(0, capacity.Remaining);
                    Assert.False(capacity.TryReserve());
                }),
                ("history appends by pawn and evicts beyond both limits", () =>
                {
                    var store = new AdvisorHistoryStore(new World());
                    var pawn = new Pawn { thingIDNumber = 7 };
                    for (var index = 0; index < 55; index++)
                    {
                        store.AddRecord(
                            pawn,
                            new AdvisorRequestRecord { action = $"action-{index}", tick = index });
                    }

                    Assert.Equal(50, store.GetRecords(pawn).Count);
                    Assert.Equal("action-5", store.GetRecords(pawn)[0].action);

                    for (var pawnId = 10; pawnId < 14; pawnId++)
                    {
                        var otherPawn = new Pawn { thingIDNumber = pawnId };
                        for (var index = 0; index < 50; index++)
                        {
                            store.AddRecord(
                                otherPawn,
                                new AdvisorRequestRecord { action = $"{pawnId}-{index}", tick = index });
                        }
                    }
                    Assert.Equal(200, store.GlobalLog.Count);
                }),
                ("instant hints expose exactly the registered action boundary", () =>
                {
                    var expected = new HashSet<string>
                    {
                        "force_rest",
                        "social_relax",
                        "eat_food",
                        "tend_pawn",
                        "rescue_pawn",
                        "inspire_work",
                        "inspire_shoot",
                        "inspire_trade",
                        "move_to"
                    };

                    Assert.True(expected.SetEquals(InstantHintRegistry.GetKnownActions()));
                    Assert.False(InstantHintRegistry.IsKnownAction("unknown-action"));
                }),
                ("Advisor publishes a bounded history brief without exposing its store", () =>
                {
                    string source = ReadSource("Advisor/AdvisorProviderRegistrar.cs")
                        .Replace("\r\n", "\n");
                    Assert.Contains(
                        "            RegisterPublicProviders();\n" +
                        "        }\n\n" +
                        "        private static void RegisterPublicProviders()",
                        source,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "private const string PublicProviderOwner = \"RimMind.Advisor\";",
                        source,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "            RimMindAPI.Providers.RegisterPawnProvider(\n" +
                        "                \"advisor.history_brief\",\n" +
                        "                PublicProviderOwner,\n" +
                        "                pawn =>",
                        source,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "                },\n" +
                        "                priority: 100,\n" +
                        "                overrideExisting: true);",
                        source,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "var text = new StringBuilder(\"[RimMind Advisor]\");",
                        source,
                        StringComparison.Ordinal);
                    Assert.DoesNotContain(
                        "text.AppendLine(\"[RimMind Advisor]\");",
                        source,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "foreach (var record in history.Take(5))",
                        source,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "$\"- {record.action}: {record.reason} ({record.result})\");",
                        source,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "return text.ToString().TrimEnd();",
                        source,
                        StringComparison.Ordinal);
                }),
                ("driver reset clears feedback depth reasoning and request state", () =>
                {
                    var session = new AdvisorFeedbackSession();
                    session.Begin(
                        new List<StructuredTool> { new StructuredTool { Name = "tool" } },
                        "schema");
                    session.CaptureMessages(
                        new List<ChatMessage> { new ChatMessage { Role = "system" } });
                    session.SetReasoningContent("reasoning");
                    Assert.True(session.CanRequestFeedback(3));
                    session.BeginFeedback(session.Messages!);

                    session.Clear();

                    Assert.False(session.HasPendingState);
                    Assert.Null(session.Messages);
                    Assert.Null(session.Tools);
                    Assert.Null(session.Schema);
                    Assert.Null(session.ReasoningContent);
                    Assert.Equal(0, session.ToolCallDepth);
                }));
        }

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(Path.Combine(
                SourceRoot(),
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string SourceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null &&
                   !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Advisor", "Source")))
                directory = directory.Parent;

            return Path.Combine(
                directory?.FullName ?? throw new InvalidOperationException("Repository root not found."),
                "RimMind-Advisor",
                "Source");
        }
    }
}
