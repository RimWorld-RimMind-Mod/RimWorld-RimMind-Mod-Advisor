using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Advisor.Advisor;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Advisor.Tests
{
    public class AdvisorToolCallExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_Returns_Error_Result_For_Unknown_Tool()
        {
            var executor = new AdvisorToolCallExecutor(_ => null);
            var call = new StructuredToolCall
            {
                Id = "call-unknown",
                Name = "missing.tool",
                Arguments = "{\"value\":1}"
            };

            var results = await executor.ExecuteAsync(
                new[] { call },
                npcId: "npc-1",
                traceId: "trace-1",
                CancellationToken.None);

            var result = Assert.Single(results);
            Assert.True(result.IsError);
            Assert.Equal("Unknown tool: missing.tool", result.Content);
            Assert.Equal("call-unknown", result.ToolCallId);
            Assert.Equal("missing.tool", result.ToolName);
        }

        [Fact]
        public async Task ExecuteAsync_Passes_Args_Normalizes_Ok_Results_And_Preserves_Order()
        {
            using var cts = new CancellationTokenSource();
            var first = new CapturingToolHandler(
                "first.tool",
                ToolResult.Ok("first ok", "wrong-id", "wrong-name"));
            var second = new CapturingToolHandler(
                "second.tool",
                ToolResult.Ok("second ok", "wrong-id", "wrong-name"));

            var handlers = new Dictionary<string, IToolHandler>
            {
                [first.Id] = first,
                [second.Id] = second
            };
            var executor = new AdvisorToolCallExecutor(id => handlers.TryGetValue(id, out var handler) ? handler : null);
            var calls = new[]
            {
                new StructuredToolCall
                {
                    Id = "call-1",
                    Name = "first.tool",
                    Arguments = ""
                },
                new StructuredToolCall
                {
                    Id = "call-2",
                    Name = "second.tool",
                    Arguments = "{\"target\":\"bed\"}"
                }
            };

            var results = await executor.ExecuteAsync(calls, "npc-42", "trace-99", cts.Token);

            Assert.Equal(2, results.Count);
            Assert.False(results[0].IsError);
            Assert.Equal("first ok", results[0].Content);
            Assert.Equal("call-1", results[0].ToolCallId);
            Assert.Equal("first.tool", results[0].ToolName);
            Assert.False(results[1].IsError);
            Assert.Equal("second ok", results[1].Content);
            Assert.Equal("call-2", results[1].ToolCallId);
            Assert.Equal("second.tool", results[1].ToolName);

            Assert.NotNull(first.Args);
            Assert.Equal("call-1", first.Args.ToolCallId);
            Assert.Equal("first.tool", first.Args.ToolName);
            Assert.Equal("{}", first.Args.ArgumentsJson);
            Assert.Equal("npc-42", first.Args.NpcId);
            Assert.Equal("trace-99", first.Args.TraceId);
            Assert.Equal(cts.Token, first.Args.Ct);
            Assert.Equal(cts.Token, first.CancellationToken);

            Assert.NotNull(second.Args);
            Assert.Equal("call-2", second.Args.ToolCallId);
            Assert.Equal("second.tool", second.Args.ToolName);
            Assert.Equal("{\"target\":\"bed\"}", second.Args.ArgumentsJson);
            Assert.Equal("npc-42", second.Args.NpcId);
            Assert.Equal("trace-99", second.Args.TraceId);
            Assert.Equal(cts.Token, second.Args.Ct);
            Assert.Equal(cts.Token, second.CancellationToken);
        }

        private sealed class CapturingToolHandler : IToolHandler
        {
            private readonly ToolResult _result;

            public CapturingToolHandler(string id, ToolResult result)
            {
                Id = id;
                _result = result;
                Definition = new ToolDefinition { Id = id };
            }

            public string Id { get; }
            public string OwnerModId => "RimMind.Advisor.Tests";
            public ToolDefinition Definition { get; }
            public ToolCallArgs? Args { get; private set; }
            public CancellationToken CancellationToken { get; private set; }

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
            {
                Args = args;
                CancellationToken = ct;
                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(_result));
            }
        }
    }
}
