using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Verse;

using ClientStructuredToolCall = RimMind.Domain.Llm.StructuredToolCall;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Pure recommendation payload boundary. Native tool calls and the optional
    /// legacy JSON fallback share one parser and one tool-existence rule.
    /// </summary>
    internal sealed class AdvisorRecommendationParser
    {
        private readonly Func<string, bool> _toolExists;

        public AdvisorRecommendationParser(Func<string, bool> toolExists)
        {
            _toolExists = toolExists ?? throw new ArgumentNullException(nameof(toolExists));
        }

        public bool TryParseNative(
            string? toolCallsJson,
            out List<ClientStructuredToolCall> toolCalls)
        {
            toolCalls = new List<ClientStructuredToolCall>();
            if (string.IsNullOrWhiteSpace(toolCallsJson))
                return false;

            string nonBlankJson = toolCallsJson!;
            try
            {
                var parsed = JsonConvert.DeserializeObject<List<ClientStructuredToolCall>>(nonBlankJson);
                if (parsed == null
                    || parsed.Exists(call => call == null || string.IsNullOrWhiteSpace(call.Name)))
                {
                    return false;
                }

                toolCalls = parsed;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public List<ClientStructuredToolCall>? ParseLegacyIfEnabled(
            string? content,
            bool enabled)
            => enabled ? ParseLegacy(content) : null;

        public List<ClientStructuredToolCall>? ParseLegacy(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            string nonBlankContent = content!;
            try
            {
                string trimmed = ExtractJsonObject(nonBlankContent.Trim());
                var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(trimmed);
                if (parsed == null || !parsed.TryGetValue("advices", out object? advicesToken))
                    return null;

                var advices = JsonConvert.DeserializeObject<List<Dictionary<string, string>?>>(
                    JsonConvert.SerializeObject(advicesToken));
                if (advices == null || advices.Count == 0)
                    return null;

                var toolCalls = new List<ClientStructuredToolCall>();
                foreach (var advice in advices)
                {
                    if (advice == null
                        || !advice.TryGetValue("action", out string? actionName)
                        || actionName.NullOrEmpty()
                        || !_toolExists(actionName))
                    {
                        continue;
                    }

                    var args = new Dictionary<string, string>();
                    CopyNonBlank(advice, args, "target");
                    CopyNonBlank(advice, args, "param");
                    CopyNonBlank(advice, args, "reason");
                    toolCalls.Add(new ClientStructuredToolCall
                    {
                        Id = $"fallback_{toolCalls.Count}",
                        Name = actionName,
                        Arguments = JsonConvert.SerializeObject(args),
                    });
                }

                return toolCalls.Count == 0 ? null : toolCalls;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ExtractJsonObject(string content)
        {
            if (!content.StartsWith("```", StringComparison.Ordinal))
                return content;

            int firstBrace = content.IndexOf('{');
            int lastBrace = content.LastIndexOf('}');
            return firstBrace >= 0 && lastBrace > firstBrace
                ? content.Substring(firstBrace, lastBrace - firstBrace + 1)
                : content;
        }

        private static void CopyNonBlank(
            IReadOnlyDictionary<string, string> source,
            IDictionary<string, string> destination,
            string key)
        {
            if (source.TryGetValue(key, out string? value) && !value.NullOrEmpty())
                destination[key] = value;
        }
    }
}
