using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Verse;
using ClientStructuredToolCall = RimMind.Domain.Llm.StructuredToolCall;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Formats human-readable tool labels, extracts display reasons honoring <c>Manifest.SummaryArgKeys</c>,
    /// parses tool call JSON arguments via Core <see cref="RimMindAPI.Json"/>, and strips spoofed pre-approval fields.
    /// </summary>
    internal static class ToolCallSummaryFormatter
    {
        private static readonly string[] GenericFallbackDetailKeys = { "target", "operation" };

        internal static string? ExtractToolCallReason(ClientStructuredToolCall toolCall)
        {
            if (toolCall == null)
                return null;

            string? extractedReason = RimMindAPI.Json.ExtractReasonFromArgumentsJson(toolCall.Arguments);
            if (!extractedReason.NullOrEmpty())
                return extractedReason;

            var arguments = ParseToolCallArguments(toolCall.Arguments);
            if (arguments.TryGetValue("reason", out var directReason) && !directReason.NullOrEmpty())
                return directReason;

            var readableTool = FormatHumanReadableToolName(toolCall.Name);

            try
            {
                var summaryKeys = RimMindAPI.ToolSet.Registry
                    .FindById(toolCall.Name ?? string.Empty)?.Definition?.Manifest?.SummaryArgKeys;
                if (summaryKeys != null)
                {
                    for (int i = 0; i < summaryKeys.Count; i++)
                    {
                        var key = summaryKeys[i];
                        if (!string.IsNullOrWhiteSpace(key) &&
                            arguments.TryGetValue(key, out var summaryVal) &&
                            !summaryVal.NullOrEmpty())
                        {
                            return $"{readableTool}: {FormatHumanReadableToolName(summaryVal)}";
                        }
                    }
                }
            }
            catch
            {
            }

            foreach (var key in GenericFallbackDetailKeys)
            {
                if (arguments.TryGetValue(key, out var detail) && !detail.NullOrEmpty())
                {
                    return $"{readableTool}: {FormatHumanReadableToolName(detail)}";
                }
            }

            return readableTool;
        }

        internal static string FormatHumanReadableToolName(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return "Action";

            var trimmed = rawName!.Trim();
            try
            {
                var displayName = RimMindAPI.ToolSet.Registry.FindById(trimmed)?.Definition?.Manifest?.DisplayName;
                if (!string.IsNullOrWhiteSpace(displayName))
                    return displayName!;
            }
            catch
            {
            }

            string[] prefixes = { "pawn_", "rimmind_" };
            foreach (var prefix in prefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    trimmed.Length > prefix.Length)
                {
                    trimmed = trimmed.Substring(prefix.Length);
                    break;
                }
            }

            var tokens = trimmed.Split(new[] { '_', '.', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return trimmed;

            var formatted = new string[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                formatted[i] = token.Length == 1
                    ? token.ToUpperInvariant()
                    : char.ToUpperInvariant(token[0]) + token.Substring(1);
            }

            return string.Join(" ", formatted);
        }

        internal static ClientStructuredToolCall StripPreApproval(ClientStructuredToolCall toolCall)
        {
            var stripped = StripPreApprovalArgument(toolCall.Arguments);
            return new ClientStructuredToolCall
            {
                Id = toolCall.Id,
                Name = toolCall.Name,
                Arguments = stripped
            };
        }

        internal static string StripPreApprovalArgument(string? argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return argumentsJson ?? string.Empty;

            try
            {
                var jobj = JObject.Parse(argumentsJson!);
                bool removed = false;
                if (jobj.Remove("pre_approved")) removed = true;
                if (jobj.Remove("is_approved")) removed = true;
                return removed ? jobj.ToString(Formatting.None) : argumentsJson!;
            }
            catch
            {
                return argumentsJson!;
            }
        }

        internal static Dictionary<string, string> ParseToolCallArguments(string? arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                return new Dictionary<string, string>();

            try
            {
                var dict = new Dictionary<string, string>();
                if (!RimMindAPI.Json.TryParseJObjectWithRepair(arguments, out var jobj) || jobj == null)
                    return dict;

                foreach (var prop in jobj.Properties())
                {
                    if (prop.Value != null && prop.Value.Type != JTokenType.Null)
                    {
                        dict[prop.Name] = prop.Value.ToString();
                    }
                }
                return dict;
            }
            catch (Exception exception)
            {
                RimMindErrors.Warn(
                    $"[RimMind-Advisor] Failed to parse tool call arguments: {exception.Message}");
                return new Dictionary<string, string>();
            }
        }
    }
}
