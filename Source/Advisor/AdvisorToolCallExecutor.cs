using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Threading;
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
            CancellationToken ct,
            bool playerApproved = false)
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
                    Ct = ct,
                    IsPlayerApproved = playerApproved
                };

                results.Add(await ExecuteIsolatedAsync(handler, args, call, ct));
            }

            return results;
        }

        /// <summary>
        /// Runs one handler so that a throwing or faulted handler yields its own failed result
        /// instead of aborting the batch; earlier calls may already have mutated the game, so
        /// their real outcomes must still reach the feedback batch. Mirrors Core's
        /// ToolCallDispatchMiddleware isolation (cancellation still propagates).
        /// </summary>
        private static async Task<ToolResult> ExecuteIsolatedAsync(
            IToolHandler handler,
            ToolCallArgs args,
            StructuredToolCall call,
            CancellationToken ct)
        {
            try
            {
                var result = await handler.ExecuteAsync(args, ct);
                return result.Match(
                    ok => ok with { ToolCallId = call.Id, ToolName = call.Name },
                    err => ToolResult.Fail(err.Message, call.Id, call.Name));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return ToolResult.Fail(
                    $"Tool threw {exception.GetType().Name}: {exception.Message}",
                    call.Id,
                    call.Name);
            }
        }
    }

    /// <summary>
    /// Main-thread runner for an Advisor tool batch. Each call is dispatched and completed on its
    /// own, so a throwing executor, a malformed result or an asynchronous handler only fails that
    /// call; calls whose side effects already ran keep their real result in the feedback batch, and
    /// later calls never resume on a thread-pool continuation.
    /// </summary>
    internal static class AdvisorToolCallBatchRunner
    {
        internal static List<ToolResult> ExecuteOnMainThread(
            IAdvisorToolCallExecutor executor,
            IReadOnlyList<StructuredToolCall> calls,
            string? npcId,
            string? traceId,
            bool playerApproved,
            Action<string>? warn = null)
        {
            if (executor == null) throw new ArgumentNullException(nameof(executor));
            var results = new List<ToolResult>(calls.Count);
            foreach (var call in calls)
                results.Add(ExecuteOne(executor, call, npcId, traceId, playerApproved, warn));
            return results;
        }

        private static ToolResult ExecuteOne(
            IAdvisorToolCallExecutor executor,
            StructuredToolCall call,
            string? npcId,
            string? traceId,
            bool playerApproved,
            Action<string>? warn)
        {
            try
            {
                // Approval travels via ToolCallArgs.IsPlayerApproved (trusted channel), never via LLM JSON.
                var executionTask = executor.ExecuteAsync(
                    new[] { call },
                    npcId,
                    traceId,
                    CancellationToken.None,
                    playerApproved);

                if (!executionTask.IsCompleted)
                {
                    SynchronousTaskGate.ObserveFault(executionTask);
                    warn?.Invoke(
                        "[RimMind-Advisor][ToolCall][MainThreadWait] " +
                        $"Tool '{call.Name}' did not complete synchronously on main thread " +
                        $"(trace={traceId ?? "unknown"}, npc={npcId ?? "none"}); failing fast to avoid main-thread freeze.");
                    var asyncFail = ToolResult.Fail(SynchronousTaskGate.AsyncNotSupportedMessage, call.Id, call.Name);
                    RimMindAPI.Tools.RecordToolCallTrace(
                        traceId,
                        call.Name ?? string.Empty,
                        call.Arguments,
                        false,
                        asyncFail.Content);
                    return asyncFail;
                }

                var single = executionTask.GetAwaiter().GetResult();
                if (single == null || single.Count != 1 || single[0] == null)
                {
                    var countFail = ToolResult.Fail(
                        $"Tool executor returned {single?.Count ?? 0} results for one call.",
                        call.Id,
                        call.Name);
                    RimMindAPI.Tools.RecordToolCallTrace(
                        traceId,
                        call.Name ?? string.Empty,
                        call.Arguments,
                        false,
                        countFail.Content);
                    return countFail;
                }

                var toolResult = single[0];
                RimMindAPI.Tools.RecordToolCallTrace(
                    traceId,
                    toolResult.ToolName ?? call.Name ?? string.Empty,
                    call.Arguments,
                    !toolResult.IsError,
                    toolResult.Content);
                return toolResult;
            }
            catch (Exception exception)
            {
                warn?.Invoke(
                    $"[RimMind-Advisor] Tool '{call.Name}' execution failed (npc={npcId ?? "none"}): {exception.Message}");
                var exFail = ToolResult.Fail($"Tool execution failed: {exception.Message}", call.Id, call.Name);
                RimMindAPI.Tools.RecordToolCallTrace(
                    traceId,
                    call.Name ?? string.Empty,
                    call.Arguments,
                    false,
                    exFail.Content);
                return exFail;
            }
        }
    }
}
