using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Llm;
using RimMind.Presentation.Api;

namespace RimMind.Advisor.Advisor
{
    internal sealed class AdvisorToolCallExecutor : IAdvisorToolCallExecutor
    {
        private readonly Func<string, IToolHandler?> _findTool;

        public AdvisorToolCallExecutor()
#if RIMMIND_ADVISOR_TESTS
            : this(_ => null)
#else
            : this(RimMindAPI.Tools.FindById)
#endif
        {
        }

        internal AdvisorToolCallExecutor(Func<string, IToolHandler?> findTool)
        {
            _findTool = findTool ?? throw new ArgumentNullException(nameof(findTool));
        }

        public async Task<List<ToolResult>> ExecuteAsync(
            IReadOnlyList<StructuredToolCall> calls,
            string? npcId,
            string? traceId,
            CancellationToken ct)
        {
            var results = new List<ToolResult>(calls.Count);

            foreach (var call in calls)
            {
                var handler = _findTool(call.Name);
                if (handler == null)
                {
                    results.Add(ToolResult.Fail($"Unknown tool: {call.Name}", call.Id, call.Name));
                    continue;
                }

                var args = new ToolCallArgs
                {
                    ToolCallId = call.Id,
                    ToolName = call.Name,
                    ArgumentsJson = string.IsNullOrEmpty(call.Arguments) ? "{}" : call.Arguments,
                    NpcId = npcId,
                    TraceId = traceId,
                    Ct = ct
                };

                var result = await handler.ExecuteAsync(args, ct).ConfigureAwait(false);
                results.Add(result.Match(
                    ok => ok with { ToolCallId = call.Id, ToolName = call.Name },
                    err => ToolResult.Fail(err.Message, call.Id, call.Name)));
            }

            return results;
        }
    }
}
