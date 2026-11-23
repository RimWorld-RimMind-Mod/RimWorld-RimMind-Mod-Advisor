using System;
using System.Collections.Generic;
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
    [Collection("Advisor runtime")]
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
                }));
        }

        [Fact]
        public void Concurrency_history_and_provider_state_remain_bounded()
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
                ("history appends by pawn, prunes inactive pawns, and uses IExposable wrapper", () =>
                {
                    Assert.True(typeof(IExposable).IsAssignableFrom(typeof(PawnAdvisorHistory)));
                    var wrapper = new PawnAdvisorHistory { pawnId = 7 };
                    wrapper.records.Add(new AdvisorRequestRecord { action = "test", tick = 1 });
                    wrapper.ExposeData();
                    Assert.Equal(7, wrapper.pawnId);
                    Assert.Single(wrapper.records);

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

                    int trackedBefore = store.TrackedPawnCount;
                    var unreadPawn = new Pawn { thingIDNumber = 999 };
                    Assert.Empty(store.GetRecords(unreadPawn));
                    Assert.Empty(store.GetRecentRecords(unreadPawn, 5));
                    Assert.Equal(trackedBefore, store.TrackedPawnCount);

                    Find.CurrentMap = new Map();
                    Find.CurrentMap.mapPawns.FreeColonists.Clear();
                    Find.CurrentMap.mapPawns.FreeColonists.Add(pawn);
                    store.ExposeData();
                    Assert.Equal(1, store.TrackedPawnCount);
                    Assert.Equal(50, store.GetRecords(pawn).Count);
                }),
                ("approval manager clears pending pawn references on game lifecycle reset", () =>
                {
                    ApprovalManager.ClearPendingRequests();
                    RimMind.Presentation.Api.RimMindAPI.ClearPendingRequests();
                    try
                    {
                        var manager = new ApprovalManager(new RimMind.Advisor.Settings.RimMindAdvisorSettings());
                        var pawn = new Pawn { thingIDNumber = 88 };
                        manager.SubmitForApproval(
                            new AdviceItem { Action = "actions_place_blueprint", Reason = "test" },
                            pawn,
                            onApproved: () => { },
                            onRejected: () => { });
                        Assert.Equal(1, ApprovalManager.TrackedPendingCount);
                        ApprovalManager.ClearPendingRequests();
                        Assert.Equal(0, ApprovalManager.TrackedPendingCount);
                    }
                    finally
                    {
                        ApprovalManager.ClearPendingRequests();
                        RimMind.Presentation.Api.RimMindAPI.ClearPendingRequests();
                    }
                }),
                ("Advisor publishes a bounded 5-item history brief via public pawn provider", () =>
                {
                    RimMind.Presentation.Api.RimMindAPI.Providers.RegisteredPawnProviders.Clear();
                    var store = new AdvisorHistoryStore(new World());
                    var pawn = new Pawn { thingIDNumber = 33 };
                    for (int i = 0; i < 8; i++)
                    {
                        store.AddRecord(pawn, new AdvisorRequestRecord
                        {
                            action = $"act-{i}",
                            reason = $"why-{i}",
                            result = "approved",
                            tick = i
                        });
                    }

                    try
                    {
                        AdvisorProviderRegistrar.RegisterAll();
                        var registered = Assert.Single(RimMind.Presentation.Api.RimMindAPI.Providers.RegisteredPawnProviders);
                        Assert.Equal("advisor.history_brief", registered.Id);
                        Assert.Equal("RimMind.Advisor", registered.Owner);
                        Assert.Equal(100, registered.Priority);
                        Assert.True(registered.OverrideExisting);

                        string brief = registered.Provider(pawn);
                        Assert.StartsWith("[RimMind Advisor]", brief, StringComparison.Ordinal);
                        Assert.DoesNotContain("act-0", brief);
                        Assert.DoesNotContain("act-2", brief);
                        Assert.Contains("- act-3: why-3 (approved)", brief);
                        Assert.Contains("- act-7: why-7 (approved)", brief);
                        Assert.Equal(string.Empty, registered.Provider(new Pawn { thingIDNumber = 999 }));
                    }
                    finally
                    {
                        RimMind.Presentation.Api.RimMindAPI.Providers.RegisteredPawnProviders.Clear();
                        AdvisorHistoryStore.ResetCache();
                    }
                }),
                ("pre-approval stripping removes spoofed approval flags while preserving real args", () =>
                {
                    var sanitized = ToolCallSummaryFormatter.StripPreApproval(new StructuredToolCall
                    {
                        Id = "c1",
                        Name = "actions_place_blueprint",
                        Arguments = "{\"building_def\":\"Bed\",\"pre_approved\":true,\"is_approved\":true}"
                    });

                    Assert.DoesNotContain("pre_approved", sanitized.Arguments);
                    Assert.DoesNotContain("is_approved", sanitized.Arguments);
                    Assert.Contains("Bed", sanitized.Arguments);
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

        [Fact]
        public void Advisor_cooldown_calculator_adapts_to_danger_and_pawn_crisis()
        {
            ContractCaseRunner.Run(
                ("normal peaceful state preserves base cooldown", () =>
                {
                    int cooldown = AdvisorCooldownCalculator.CalculateDynamicCooldown(
                        baseCooldownTicks: 30000,
                        isMapHighDanger: false,
                        isPawnInCrisis: false,
                        isPawnInExtremeBreakRisk: false);
                    Assert.Equal(30000, cooldown);
                }),
                ("map high danger applies 0.20x reduction", () =>
                {
                    int cooldown = AdvisorCooldownCalculator.CalculateDynamicCooldown(
                        baseCooldownTicks: 30000,
                        isMapHighDanger: true,
                        isPawnInCrisis: false,
                        isPawnInExtremeBreakRisk: false);
                    Assert.Equal(6000, cooldown);
                }),
                ("pawn extreme mental break risk applies 0.15x reduction", () =>
                {
                    int cooldown = AdvisorCooldownCalculator.CalculateDynamicCooldown(
                        baseCooldownTicks: 30000,
                        isMapHighDanger: false,
                        isPawnInCrisis: false,
                        isPawnInExtremeBreakRisk: true);
                    Assert.Equal(4500, cooldown);
                }),
                ("pawn physical crisis applies 0.10x reduction", () =>
                {
                    int cooldown = AdvisorCooldownCalculator.CalculateDynamicCooldown(
                        baseCooldownTicks: 30000,
                        isMapHighDanger: false,
                        isPawnInCrisis: true,
                        isPawnInExtremeBreakRisk: false);
                    Assert.Equal(3000, cooldown);
                }),
                ("compound crisis prioritizes physical crisis over map high danger", () =>
                {
                    int cooldown = AdvisorCooldownCalculator.CalculateDynamicCooldown(
                        baseCooldownTicks: 30000,
                        isMapHighDanger: true,
                        isPawnInCrisis: true,
                        isPawnInExtremeBreakRisk: true);
                    Assert.Equal(3000, cooldown);
                }),
                ("minimum crisis cooldown guards against excessive flood", () =>
                {
                    int cooldown = AdvisorCooldownCalculator.CalculateDynamicCooldown(
                        baseCooldownTicks: 5000,
                        isMapHighDanger: true,
                        isPawnInCrisis: true,
                        isPawnInExtremeBreakRisk: true);
                    Assert.Equal(AdvisorCooldownCalculator.MinCrisisCooldownTicks, cooldown);
                }));
        }
    }
}
