using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Threading;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using RimMind.Testing;
using Xunit;

namespace RimMind.Advisor.Tests.Contracts
{
    /// <summary>
    /// Regression contracts for queue identity (B7), per-call tool isolation (B10) and the single
    /// activity-scaled cooldown source (B11).
    /// </summary>
    [Collection("Advisor runtime")]
    public sealed class AdvisorQueueAndToolIsolationContracts
    {
        [Fact]
        public void Queue_cooldown_key_is_the_envelope_mod_id()
        {
            ContractCaseRunner.Run(
                ("initial request envelope uses the module-owned request id", () =>
                {
                    var envelope = AdvisorRequestAugmentationFactory.Create("NPC-1", null, false, null, null);
                    Assert.Equal(AdvisorRequestIds.RequestModId, envelope.ModId);
                }),
                ("registered queue cooldown resolves by exact envelope ModId", () =>
                {
                    var envelope = AdvisorRequestAugmentationFactory.Create("NPC-1", null, false, null, null);
                    var cooldown = new AdvisorModCooldown(new RimMindAdvisorSettings());
                    Assert.Equal(envelope.ModId, cooldown.Id);
                    Assert.Equal(envelope.ModId, cooldown.OwnerModId);
                }),
                ("source never clears or reads a cooldown with a literal key or diverging ModId", () =>
                {
                    var literalCooldownKey = new Regex(@"(ClearModCooldown|GetModCooldownTicksLeft)\(\s*""");
                    var literalModId = new Regex(@"WithModId\(\s*""(?<id>[^""]*)""");
                    foreach (var file in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories)
                                 .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
                    {
                        string text = File.ReadAllText(file);
                        Assert.False(literalCooldownKey.IsMatch(text),
                            $"{Path.GetFileName(file)} passes a literal cooldown key; use AdvisorRequestIds.RequestModId.");
                        foreach (Match match in literalModId.Matches(text))
                            Assert.Equal(AdvisorRequestIds.RequestModId, match.Groups["id"].Value);
                    }
                }),
                ("queue cooldown follows the same activity scaling as the pawn cooldown", () =>
                {
                    var settings = new RimMindAdvisorSettings { requestCooldownTicks = 6000 };
                    var cooldown = new AdvisorModCooldown(settings);
                    float previous = RimMindAPI.Settings.ActivityFrequencyScale;
                    try
                    {
                        RimMindAPI.Settings.ActivityFrequencyScale = 2f;
                        Assert.Equal(3000, cooldown.CooldownTicks);
                        RimMindAPI.Settings.ActivityFrequencyScale = 1f;
                        Assert.Equal(6000, cooldown.CooldownTicks);
                    }
                    finally
                    {
                        RimMindAPI.Settings.ActivityFrequencyScale = previous;
                    }
                }));
        }

        [Fact]
        public Task Throwing_tool_call_fails_alone_and_later_calls_still_run()
        {
            return ContractCaseRunner.RunAsync(
                ("executor isolates a synchronously throwing handler", async () =>
                {
                    var (executor, third) = CreateExecutor(new ThrowingHandler("tool.two", faulted: false));
                    var results = await executor.ExecuteAsync(ThreeCalls(), "npc", "trace", CancellationToken.None);

                    AssertOkFailOk(results);
                    Assert.Contains("InvalidOperationException", results[1].Content, StringComparison.Ordinal);
                    Assert.Equal(1, third.Calls);
                }),
                ("executor isolates a handler returning a faulted task", async () =>
                {
                    var (executor, third) = CreateExecutor(new ThrowingHandler("tool.two", faulted: true));
                    var results = await executor.ExecuteAsync(ThreeCalls(), "npc", "trace", CancellationToken.None);

                    AssertOkFailOk(results);
                    Assert.Equal(1, third.Calls);
                }),
                ("cancellation still propagates instead of becoming a tool result", async () =>
                {
                    using var cts = new CancellationTokenSource();
                    cts.Cancel();
                    var handler = new ThrowingHandler("tool.two", faulted: false, cancel: true);
                    var executor = new AdvisorToolCallExecutor(id => id == handler.Id ? handler : null);
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(
                        new[] { new StructuredToolCall { Id = "c2", Name = "tool.two" } }, null, null, cts.Token));
                }));
        }

        [Fact]
        public void Main_thread_batch_runner_keeps_real_results_of_other_calls()
        {
            ContractCaseRunner.Run(
                ("call 2 of 3 throwing in the executor fails only call 2", () =>
                {
                    var fake = new ScriptedExecutor(call => call.Name == "tool.two"
                        ? throw new InvalidOperationException("boom")
                        : Task.FromResult(new List<ToolResult> { ToolResult.Ok("done", call.Id, call.Name) }));
                    var warnings = new List<string>();

                    var results = AdvisorToolCallBatchRunner.ExecuteOnMainThread(
                        fake, ThreeCalls(), "NPC-1", "trace", playerApproved: true, warnings.Add);

                    AssertOkFailOk(results);
                    Assert.Contains("boom", results[1].Content, StringComparison.Ordinal);
                    Assert.Equal(new[] { "tool.one", "tool.two", "tool.three" }, fake.Executed);
                    Assert.All(fake.PlayerApproved, Assert.True);
                    Assert.Single(warnings);
                }),
                ("an asynchronous call fails fast without failing completed neighbours", () =>
                {
                    var pending = new TaskCompletionSource<List<ToolResult>>();
                    var fake = new ScriptedExecutor(call => call.Name == "tool.two"
                        ? pending.Task
                        : Task.FromResult(new List<ToolResult> { ToolResult.Ok("done", call.Id, call.Name) }));

                    var results = AdvisorToolCallBatchRunner.ExecuteOnMainThread(
                        fake, ThreeCalls(), "NPC-1", null, playerApproved: false);

                    AssertOkFailOk(results);
                    Assert.Equal(SynchronousTaskGate.AsyncNotSupportedMessage, results[1].Content);
                    Assert.Equal(3, fake.Executed.Count);
                }),
                ("a malformed executor result is reported for that call only", () =>
                {
                    var fake = new ScriptedExecutor(call => Task.FromResult(call.Name == "tool.two"
                        ? new List<ToolResult>()
                        : new List<ToolResult> { ToolResult.Ok("done", call.Id, call.Name) }));

                    AssertOkFailOk(AdvisorToolCallBatchRunner.ExecuteOnMainThread(
                        fake, ThreeCalls(), null, null, playerApproved: false));
                }));
        }

        [Fact]
        public void Effective_cooldown_is_the_single_activity_scaled_source()
        {
            ContractCaseRunner.Run(
                ("higher activity frequency shortens the effective cooldown", () =>
                {
                    Assert.Equal(15000, AdvisorCooldownCalculator.CalculateEffectiveCooldown(30000, false, false, false, 2f));
                    Assert.Equal(30000, AdvisorCooldownCalculator.CalculateEffectiveCooldown(30000, false, false, false, 1f));
                    Assert.Equal(60000, AdvisorCooldownCalculator.CalculateEffectiveCooldown(30000, false, false, false, 0.5f));
                }),
                ("scaling is clamped and invalid scales are neutral", () =>
                {
                    Assert.Equal(10500, AdvisorCooldownCalculator.CalculateEffectiveCooldown(30000, false, false, false, 100f));
                    Assert.Equal(105000, AdvisorCooldownCalculator.CalculateEffectiveCooldown(30000, false, false, false, 0.1f));
                    Assert.Equal(30000, AdvisorCooldownCalculator.CalculateEffectiveCooldown(30000, false, false, false, 0f));
                }),
                ("crisis safety floor still applies after scaling", () =>
                {
                    Assert.Equal(
                        AdvisorCooldownCalculator.MinCrisisCooldownTicks,
                        AdvisorCooldownCalculator.CalculateEffectiveCooldown(30000, false, true, false, 3f));
                }),
                ("a pawn is ready once the scaled cooldown elapsed even if the base one has not", () =>
                {
                    int effective = AdvisorCooldownCalculator.CalculateEffectiveCooldown(30000, false, false, false, 2f);
                    Assert.Equal(0, AdvisorCooldownCalculator.CooldownTicksLeft(effective, nowTicks: 116000, lastRequestTick: 100000));
                    int unscaled = AdvisorCooldownCalculator.CalculateEffectiveCooldown(30000, false, false, false, 1f);
                    Assert.Equal(14000, AdvisorCooldownCalculator.CooldownTicksLeft(unscaled, nowTicks: 116000, lastRequestTick: 100000));
                }));
        }

        private static StructuredToolCall[] ThreeCalls() => new[]
        {
            new StructuredToolCall { Id = "c1", Name = "tool.one", Arguments = "{}" },
            new StructuredToolCall { Id = "c2", Name = "tool.two", Arguments = "{}" },
            new StructuredToolCall { Id = "c3", Name = "tool.three", Arguments = "{}" }
        };

        private static void AssertOkFailOk(IReadOnlyList<ToolResult> results)
        {
            Assert.Equal(3, results.Count);
            Assert.False(results[0].IsError);
            Assert.True(results[1].IsError);
            Assert.False(results[2].IsError);
            Assert.Equal(new[] { "c1", "c2", "c3" }, results.Select(result => result.ToolCallId));
            Assert.Equal(new[] { "tool.one", "tool.two", "tool.three" }, results.Select(result => result.ToolName));
        }

        private static (AdvisorToolCallExecutor executor, CountingHandler third) CreateExecutor(IToolHandler second)
        {
            var first = new CountingHandler("tool.one");
            var third = new CountingHandler("tool.three");
            var handlers = new Dictionary<string, IToolHandler>
            {
                [first.Id] = first,
                [second.Id] = second,
                [third.Id] = third
            };
            return (new AdvisorToolCallExecutor(id => handlers.TryGetValue(id, out var handler) ? handler : null), third);
        }

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

        private sealed class ScriptedExecutor : IAdvisorToolCallExecutor
        {
            private readonly Func<StructuredToolCall, Task<List<ToolResult>>> _script;
            public ScriptedExecutor(Func<StructuredToolCall, Task<List<ToolResult>>> script) => _script = script;
            public List<string> Executed { get; } = new();
            public List<bool> PlayerApproved { get; } = new();

            public Task<List<ToolResult>> ExecuteAsync(
                IReadOnlyList<StructuredToolCall> calls,
                string? npcId,
                string? traceId,
                CancellationToken ct,
                bool playerApproved = false)
            {
                var call = Assert.Single(calls);
                Executed.Add(call.Name);
                PlayerApproved.Add(playerApproved);
                return _script(call);
            }
        }

        private sealed class CountingHandler : IToolHandler
        {
            public CountingHandler(string id)
            {
                Id = id;
                Definition = new ToolDefinition { Id = id };
            }

            public string Id { get; }
            public string OwnerModId => "RimMind.Advisor.Tests";
            public ToolDefinition Definition { get; }
            public int Calls { get; private set; }

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
            {
                Calls++;
                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("done")));
            }
        }

        private sealed class ThrowingHandler : IToolHandler
        {
            private readonly bool _faulted;
            private readonly bool _cancel;

            public ThrowingHandler(string id, bool faulted, bool cancel = false)
            {
                Id = id;
                _faulted = faulted;
                _cancel = cancel;
                Definition = new ToolDefinition { Id = id };
            }

            public string Id { get; }
            public string OwnerModId => "RimMind.Advisor.Tests";
            public ToolDefinition Definition { get; }

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
            {
                if (_cancel)
                    ct.ThrowIfCancellationRequested();
                var exception = new InvalidOperationException("handler exploded after partial side effects");
                if (_faulted)
                    return Task.FromException<Result<ToolResult, RimMindError>>(exception);
                throw exception;
            }
        }
    }
}
