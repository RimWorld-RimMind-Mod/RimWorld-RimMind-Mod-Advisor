using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
                var token = JToken.Parse(trimmed);
                if (token is JArray arrayToken)
                {
                    var arrayCalls = new List<ClientStructuredToolCall>();
                    foreach (var item in arrayToken)
                    {
                        var call = ParseSingleAdvice(item as JObject, arrayCalls.Count);
                        if (call != null)
                            arrayCalls.Add(call);
                    }
                    if (arrayCalls.Count > 0)
                        return arrayCalls;
                }
                else if (token is JObject root)
                {
                    if (root["advices"] is JArray advicesArray)
                    {
                        var toolCalls = new List<ClientStructuredToolCall>();
                        foreach (var adviceToken in advicesArray)
                        {
                            var call = ParseSingleAdvice(adviceToken as JObject, toolCalls.Count);
                            if (call != null)
                                toolCalls.Add(call);
                        }

                        if (toolCalls.Count > 0)
                            return toolCalls;
                    }
                    else
                    {
                        var singleCall = ParseSingleAdvice(root, 0);
                        if (singleCall != null)
                            return new List<ClientStructuredToolCall> { singleCall };
                    }
                }
            }
            catch (Exception)
            {
                // Fall through to pseudo-function-call extraction
            }

            return TryParsePseudoFunctionCall(nonBlankContent);
        }

        private ClientStructuredToolCall? ParseSingleAdvice(JObject? advice, int index)
        {
            if (advice == null)
                return null;

            string? actionName = advice.Value<string>("action")
                ?? advice.Value<string>("name")
                ?? advice.Value<string>("tool")
                ?? (advice["function"] as JObject)?.Value<string>("name");

            if (actionName.NullOrEmpty() || !_toolExists(actionName!))
                return null;

            var paramsToken = advice["parameters"]
                ?? advice["arguments"]
                ?? (advice["function"] as JObject)?["arguments"];

            if (paramsToken is JObject paramsObj)
            {
                var cloned = (JObject)paramsObj.DeepClone();
                string? topReason = advice.Value<string>("reason");
                if (!string.IsNullOrEmpty(topReason) && cloned["reason"] == null)
                    cloned["reason"] = topReason;
                string? topTarget = advice.Value<string>("target");
                if (!string.IsNullOrEmpty(topTarget) && cloned["target"] == null)
                    cloned["target"] = topTarget;
                return new ClientStructuredToolCall
                {
                    Id = $"fallback_{index}",
                    Name = actionName!,
                    Arguments = cloned.ToString(Formatting.None),
                };
            }

            if (paramsToken != null && paramsToken.Type == JTokenType.String)
            {
                string rawParams = paramsToken.ToString();
                if (rawParams.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    return new ClientStructuredToolCall
                    {
                        Id = $"fallback_{index}",
                        Name = actionName!,
                        Arguments = rawParams,
                    };
                }
            }

            var args = new Dictionary<string, string>();
            CopyNonBlank(advice, args, "target");
            CopyNonBlank(advice, args, "param");
            CopyNonBlank(advice, args, "reason");
            foreach (var prop in advice.Properties())
            {
                if (args.ContainsKey(prop.Name) ||
                    string.Equals(prop.Name, "action", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prop.Name, "name", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prop.Name, "tool", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prop.Name, "function", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prop.Name, "id", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prop.Name, "type", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (prop.Value != null && prop.Value.Type != JTokenType.Null)
                {
                    string val = prop.Value.Type == JTokenType.String
                        ? prop.Value.ToString()
                        : prop.Value.ToString(Formatting.None);
                    if (!val.NullOrEmpty())
                        args[prop.Name] = val;
                }
            }

            return new ClientStructuredToolCall
            {
                Id = $"fallback_{index}",
                Name = actionName!,
                Arguments = JsonConvert.SerializeObject(args),
            };
        }

        private List<ClientStructuredToolCall>? TryParsePseudoFunctionCall(string content)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                content.Trim(),
                @"\b(?<tool>[a-z][a-z0-9_.]{2,})\s*\((?<args>[\s\S]+?)\)?\s*$",
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = System.Text.RegularExpressions.Regex.Match(
                    content,
                    @"(?<tool>[a-z][a-z0-9_.]{2,})\s*\((?<args>[^)]*)\)",
                    System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            if (match.Success)
            {
                string toolName = match.Groups["tool"].Value.Trim();
                if (_toolExists(toolName))
                {
                    string argsBody = match.Groups["args"].Value;
                    var argsDict = new Dictionary<string, object>();
                    var kvMatches = System.Text.RegularExpressions.Regex.Matches(
                        argsBody,
                        @"(?<key>[a-zA-Z_][a-zA-Z0-9_]*)\)?\s*[:=]\s*(?:""(?<dq>(?:\\.|[^""])*)""|'(?<sq>(?:\\.|[^'])*)'|\((?<pq>[^)]*)\)|(?<raw>[^,)]+))");
                    foreach (System.Text.RegularExpressions.Match kv in kvMatches)
                    {
                        string key = kv.Groups["key"].Value.Trim();
                        string val = kv.Groups["dq"].Success
                            ? kv.Groups["dq"].Value
                            : kv.Groups["sq"].Success
                                ? kv.Groups["sq"].Value
                                : kv.Groups["pq"].Success
                                    ? kv.Groups["pq"].Value.Trim()
                                    : kv.Groups["raw"].Value.Trim();
                        argsDict[key] = val;
                    }

                    return new List<ClientStructuredToolCall>
                    {
                        new ClientStructuredToolCall
                        {
                            Id = "fallback_pseudo_0",
                            Name = toolName,
                            Arguments = JsonConvert.SerializeObject(argsDict),
                        }
                    };
                }
            }

            var toMatch = System.Text.RegularExpressions.Regex.Match(
                content,
                @"\bto=(?:functions\.|tools\.)?(?<tool>[a-z][a-z0-9_.]{2,})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (toMatch.Success)
            {
                string toolName = toMatch.Groups["tool"].Value.Trim();
                if (_toolExists(toolName))
                {
                    string afterMatch = content.Substring(toMatch.Index + toMatch.Length).Trim();
                    string argsJson = "{}";
                    int braceStart = afterMatch.IndexOf('{');
                    int braceEnd = afterMatch.LastIndexOf('}');
                    if (braceStart >= 0 && braceEnd > braceStart)
                    {
                        argsJson = afterMatch.Substring(braceStart, braceEnd - braceStart + 1);
                    }
                    return new List<ClientStructuredToolCall>
                    {
                        new ClientStructuredToolCall
                        {
                            Id = "fallback_pseudo_0",
                            Name = toolName,
                            Arguments = argsJson,
                        }
                    };
                }
            }

            return null;
        }

        private static string ExtractJsonObject(string content)
        {
            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = content.IndexOf('\n');
                string stripped = firstNewline >= 0 ? content.Substring(firstNewline + 1) : content.TrimStart('`');
                if (stripped.EndsWith("```", StringComparison.Ordinal))
                    stripped = stripped.Substring(0, stripped.Length - 3);
                content = stripped.Trim();
            }

            int firstBrace = content.IndexOf('{');
            int lastBrace = content.LastIndexOf('}');
            int firstBracket = content.IndexOf('[');
            int lastBracket = content.LastIndexOf(']');

            if (firstBracket >= 0 && (firstBrace < 0 || firstBracket < firstBrace) && lastBracket > firstBracket)
            {
                return content.Substring(firstBracket, lastBracket - firstBracket + 1);
            }

            return firstBrace >= 0 && lastBrace > firstBrace
                ? content.Substring(firstBrace, lastBrace - firstBrace + 1)
                : content;
        }

        private static void CopyNonBlank(
            JObject source,
            IDictionary<string, string> destination,
            string key)
        {
            string? value = source.Value<string>(key);
            if (!value.NullOrEmpty())
                destination[key] = value!;
        }
    }
}
