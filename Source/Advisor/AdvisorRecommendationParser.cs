using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Presentation.Api;
using Verse;

using ClientStructuredToolCall = RimMind.Domain.Llm.StructuredToolCall;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Pure recommendation payload boundary. Delegates LLM output cleaning, JSON extraction/repair,
    /// and pseudo-function-call parsing to Core <see cref="RimMindAPI.Json"/>, keeping only
    /// Advisor's recommendation schema mapping and tool-existence filtering.
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

            try
            {
                var parsed = JsonConvert.DeserializeObject<List<ClientStructuredToolCall>>(toolCallsJson!);
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

            string raw = content!;
            string? extracted = RimMindAPI.Json.ExtractJsonPayload(raw);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                var parsedToken = TryParseTokenWithRepair(extracted!);
                if (parsedToken is JArray arrayToken)
                {
                    var arrayCalls = ParseAdviceArray(arrayToken);
                    if (arrayCalls.Count > 0)
                        return arrayCalls;
                }
                else if (parsedToken is JObject root)
                {
                    if (root["advices"] is JArray advicesArray)
                    {
                        var adviceCalls = ParseAdviceArray(advicesArray);
                        if (adviceCalls.Count > 0)
                            return adviceCalls;
                    }
                    else
                    {
                        var singleCall = ParseSingleAdvice(root, 0);
                        if (singleCall != null)
                            return new List<ClientStructuredToolCall> { singleCall };
                    }
                }
            }

            // Delegate pseudo-function-call & commentary (`tool(k=v)` / `to=tool {...}`) parsing to Core
            var pseudoCalls = RimMindAPI.Json.ParsePseudoFunctionCalls(raw);
            if (pseudoCalls != null && pseudoCalls.Count > 0)
            {
                var filtered = new List<ClientStructuredToolCall>();
                for (int i = 0; i < pseudoCalls.Count; i++)
                {
                    var candidate = pseudoCalls[i];
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.Name) || !_toolExists(candidate.Name))
                        continue;

                    filtered.Add(new ClientStructuredToolCall
                    {
                        Id = string.IsNullOrWhiteSpace(candidate.Id) ? $"fallback_pseudo_{filtered.Count}" : candidate.Id!,
                        Name = candidate.Name,
                        Arguments = string.IsNullOrWhiteSpace(candidate.ArgumentsJson) ? "{}" : candidate.ArgumentsJson
                    });
                }

                if (filtered.Count > 0)
                    return filtered;
            }

            return null;
        }

        private static JToken? TryParseTokenWithRepair(string json)
        {
            try
            {
                return JToken.Parse(json);
            }
            catch
            {
                string? repaired = RimMindAPI.Json.TryRepairJson(json);
                if (string.IsNullOrWhiteSpace(repaired))
                    return null;
                try
                {
                    return JToken.Parse(repaired!);
                }
                catch
                {
                    return null;
                }
            }
        }

        private List<ClientStructuredToolCall> ParseAdviceArray(JArray arrayToken)
        {
            var calls = new List<ClientStructuredToolCall>();
            foreach (var item in arrayToken)
            {
                var call = ParseSingleAdvice(item as JObject, calls.Count);
                if (call != null)
                    calls.Add(call);
            }
            return calls;
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
