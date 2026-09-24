using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Llm;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// ToolCall batch executor interface for Advisor and future submodules.
    /// </summary>
    public interface IAdvisorToolCallExecutor
    {
        Task<List<ToolResult>> ExecuteAsync(
            IReadOnlyList<StructuredToolCall> calls,
            string? npcId,
            string? traceId,
            CancellationToken ct);
    }
}
