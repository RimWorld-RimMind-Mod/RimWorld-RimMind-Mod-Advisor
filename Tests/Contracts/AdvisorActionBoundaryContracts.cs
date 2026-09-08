using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Models.UI;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using RimMind.Testing;
using Verse;
using Xunit;

namespace RimMind.Advisor.Tests.Contracts
{
    public sealed class AdvisorActionBoundaryContracts
    {
        [Fact]
        public void Approval_boundary_combines_risk_request_and_terminal_semantics()
        {
            ContractCaseRunner.Run(
                ("risk approval starts at the configured threshold", () =>
                {
                    var gate = CreateGate(enableRiskApproval: true, RiskLevel.High);
                    var decision = new AgentDecision(ActionIntent: "test.action");

                    Assert.False(gate.RequiresApproval(decision, RiskLevel.Medium));
                    Assert.True(gate.RequiresApproval(decision, RiskLevel.High));
                    Assert.True(gate.RequiresApproval(decision, RiskLevel.Critical));
                }),
                ("explicit request tools require approval even when risk gating is disabled", () =>
                {
                    var gate = CreateGate(enableRiskApproval: false, RiskLevel.Critical);
                    var request = new AgentDecision(
                        ActionIntent: "test.action",
                        Param: "{\"request_type\":\"request\"}");

                    Assert.True(gate.RequiresApproval(request, RiskLevel.Low));
                    Assert.False(gate.RequiresApproval(
                        new AgentDecision(
                            ActionIntent: "test.action",
                            Param: "{\"request_type\":\"system\"}"),
                        RiskLevel.Low));
                    Assert.False(gate.RequiresApproval(
                        new AgentDecision(
                            ActionIntent: "test.action",
                            Param: "{\"request_type\":\"REQUEST\"}"),
                        RiskLevel.Low));
                    Assert.False(gate.RequiresApproval(
                        new AgentDecision(
                            ActionIntent: "test.action",
                            Param: "{\"REQUEST_TYPE\":\"request\"}"),
                        RiskLevel.Low));
                    Assert.False(gate.RequiresApproval(
                        new AgentDecision(
                            ActionIntent: "test.action",
                            Param: "{\"request_type\":1}"),
                        RiskLevel.Low));
                    Assert.False(gate.RequiresApproval(
                        new AgentDecision(ActionIntent: "test.action", Param: "{\"other\":true}"),
                        RiskLevel.Low));
                }),
                ("selected and dismissed approvals have distinct terminal callbacks", () =>
                {
                    var settings = new RimMindAdvisorSettings { requestExpireTicks = 600 };
                    var manager = new ApprovalManager(settings);
                    RimMindAPI.ClearPendingRequests();
                    int approved = 0;
                    int rejected = 0;
                    int dismissed = 0;
                    try
                    {
                        var selected = manager.SubmitForApproval(
                            new AdviceItem { Action = "safe.action" },
                            new Pawn { thingIDNumber = 1 },
                            () => approved++,
                            () => rejected++,
                            () => dismissed++);
                        Assert.True(selected.TryComplete(
                            "RimMind.Advisor.Request.Approve",
                            RequestCompletionReason.Selected));

                        var cancelled = manager.SubmitForApproval(
                            new AdviceItem { Action = "cancelled.action" },
                            new Pawn { thingIDNumber = 2 },
                            () => approved++,
                            () => rejected++,
                            () => dismissed++);
                        Assert.True(RimMindAPI.DismissPendingRequest(cancelled));

                        Assert.Equal(1, approved);
                        Assert.Equal(0, rejected);
                        Assert.Equal(1, dismissed);
                    }
                    finally
                    {
                        RimMindAPI.ClearPendingRequests();
                    }
                }),
                ("risk suffixes resolve case-insensitively and unknown operations stay low risk", () =>
                {
                    Assert.Equal(MechanismOperationType.Query, AdvisorToolRiskResolver.ResolveOperation("QUERY"));
                    Assert.Equal(MechanismOperationType.Trigger, AdvisorToolRiskResolver.ResolveOperation("Trigger"));
                    Assert.Null(AdvisorToolRiskResolver.ResolveOperation("unknown"));
                    Assert.Equal(RiskLevel.Low, AdvisorToolRiskResolver.Resolve("missing-mechanism.set"));
                }));
        }

        [Fact]
        public Task Tool_execution_preserves_order_context_and_error_identity()
        {
            return ContractCaseRunner.RunAsync(
                ("unknown tools return a named error result", async () =>
                {
                    var executor = new AdvisorToolCallExecutor(_ => null);
                    var result = Assert.Single(await executor.ExecuteAsync(
                        new[]
                        {
                            new StructuredToolCall
                            {
                                Id = "call-missing",
                                Name = "missing.tool",
                                Arguments = "{}"
                            }
                        },
                        "npc-1",
                        "trace-1",
                        CancellationToken.None));

                    Assert.True(result.IsError);
                    Assert.Equal("call-missing", result.ToolCallId);
                    Assert.Equal("missing.tool", result.ToolName);
                }),
                ("known tools receive normalized arguments and caller context", async () =>
                {
                    using var cts = new CancellationTokenSource();
                    var handler = new CapturingHandler(
                        "known.tool",
                        Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("done")));
                    var executor = new AdvisorToolCallExecutor(id => id == handler.Id ? handler : null);

                    var result = Assert.Single(await executor.ExecuteAsync(
                        new[]
                        {
                            new StructuredToolCall
                            {
                                Id = "call-known",
                                Name = handler.Id,
                                Arguments = ""
                            }
                        },
                        "npc-42",
                        "trace-42",
                        cts.Token));

                    Assert.False(result.IsError);
                    Assert.Equal("call-known", result.ToolCallId);
                    Assert.Equal("known.tool", result.ToolName);
                    Assert.NotNull(handler.Args);
                    Assert.Equal("{}", handler.Args!.ArgumentsJson);
                    Assert.Equal("npc-42", handler.Args.NpcId);
                    Assert.Equal("trace-42", handler.Args.TraceId);
                    Assert.Equal(cts.Token, handler.CancellationToken);
                }),
                ("handler errors become tool results without aborting the batch", async () =>
                {
                    var failing = new CapturingHandler(
                        "failing.tool",
                        Result<ToolResult, RimMindError>.Err(RimMindErrors.Internal("handler failed")));
                    var succeeding = new CapturingHandler(
                        "succeeding.tool",
                        Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("second")));
                    var handlers = new Dictionary<string, IToolHandler>
                    {
                        [failing.Id] = failing,
                        [succeeding.Id] = succeeding
                    };
                    var executor = new AdvisorToolCallExecutor(
                        id => handlers.TryGetValue(id, out var handler) ? handler : null);

                    var results = await executor.ExecuteAsync(
                        new[]
                        {
                            new StructuredToolCall { Id = "call-1", Name = failing.Id },
                            new StructuredToolCall { Id = "call-2", Name = succeeding.Id }
                        },
                        null,
                        null,
                        CancellationToken.None);

                    Assert.Equal(2, results.Count);
                    Assert.True(results[0].IsError);
                    Assert.Contains("handler failed", results[0].Content, StringComparison.Ordinal);
                    Assert.False(results[1].IsError);
                    Assert.Equal("call-2", results[1].ToolCallId);
                }));
        }

        private static AdvisorApprovalGateAdapter CreateGate(
            bool enableRiskApproval,
            RiskLevel threshold)
        {
            var settings = new RimMindAdvisorSettings
            {
                enableRiskApproval = enableRiskApproval,
                autoBlockRiskLevel = threshold
            };
            return new AdvisorApprovalGateAdapter(settings, new ApprovalManager(settings));
        }

        private sealed class CapturingHandler : IToolHandler
        {
            private readonly Result<ToolResult, RimMindError> _result;

            public CapturingHandler(string id, Result<ToolResult, RimMindError> result)
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

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(
                ToolCallArgs args,
                CancellationToken ct)
            {
                Args = args;
                CancellationToken = ct;
                return Task.FromResult(_result);
            }
        }
    }
}
