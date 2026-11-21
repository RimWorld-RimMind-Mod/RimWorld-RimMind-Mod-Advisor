using System.Collections.Generic;
using System.Linq;
using RimMind.Advisor.Settings;
using RimMind.Presentation.Api;
#if !RIMMIND_ADVISOR_TESTS
using RimWorld;
using UnityEngine;
#endif
using Verse;
using ClientStructuredToolCall = RimMind.Domain.Llm.StructuredToolCall;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Presents Advisor thought bubbles above colonists using Core <see cref="RimMindAPI.Text.WrapForMote"/>.
    /// </summary>
    internal static class AdvisorMotePresenter
    {
        internal static string FormatAdvisorMoteText(string text, int maxLineLength = 24, int maxTotalLength = 66)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (normalized.Length > maxTotalLength)
            {
                normalized = normalized.Substring(0, maxTotalLength - 1).TrimEnd() + "…";
            }

            return RimMindAPI.Text.WrapForMote(normalized, maxLineLength);
        }

#if !RIMMIND_ADVISOR_TESTS
        internal static void ShowToolThoughtBubble(Pawn pawn, RimMindAdvisorSettings settings, string? reason)
        {
            if (pawn == null || settings == null || !settings.showThoughtBubble || pawn.Map == null || reason.NullOrEmpty())
                return;

            string text = reason!.StartsWith("[RimMind]", System.StringComparison.Ordinal)
                ? reason
                : $"[RimMind] {reason}";
            MoteMaker.ThrowText(
                pawn.DrawPos,
                pawn.Map,
                FormatAdvisorMoteText(text),
                new Color(0.6f, 0.9f, 1f),
                5f);
        }

        internal static void ShowExecutedBatchMotes(
            Pawn pawn,
            RimMindAdvisorSettings settings,
            IReadOnlyList<ClientStructuredToolCall> calls)
        {
            if (pawn == null || settings == null || !settings.showThoughtBubble || pawn.Map == null || calls == null || calls.Count == 0)
                return;

            var reasons = calls
                .Select(call => ToolCallSummaryFormatter.ExtractToolCallReason(call) ?? call.Name)
                .Where(reason => !reason.NullOrEmpty())
                .ToList();
            if (reasons.Count == 0)
                return;

            string moteText = reasons.Count == 1
                ? $"[RimMind] {reasons[0]}"
                : $"[RimMind] {reasons[0]} / {reasons[1]}";
            MoteMaker.ThrowText(
                pawn.DrawPos,
                pawn.Map,
                FormatAdvisorMoteText(moteText),
                new Color(0.6f, 0.9f, 1f),
                5f);
        }
#endif
    }
}
