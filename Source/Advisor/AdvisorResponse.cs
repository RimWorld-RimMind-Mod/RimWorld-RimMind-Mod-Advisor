using System.Collections.Generic;
using RimMind.Actions;
using RimMind.Core.Client;

namespace RimMind.Advisor.Advisor
{
    public class AdviceItem
    {
        public string Action = null!;
        public string? Target;
        public string? Param;
        public string? Reason;
        public RiskLevel RiskLevel;
    }

    public static class AdvisorResponse
    {
        public static List<AdviceItem> ParseFromToolCalls(List<StructuredToolCall> toolCalls, HashSet<string> supportedIntents)
        {
            var items = new List<AdviceItem>();
            foreach (var tc in toolCalls)
            {
                if (!supportedIntents.Contains(tc.Name)) continue;
                var args = ParseArguments(tc.Arguments ?? "{}");
                items.Add(new AdviceItem
                {
                    Action = tc.Name,
                    Target = args.GetValueOrDefault("target"),
                    Param = args.GetValueOrDefault("param"),
                    Reason = args.GetValueOrDefault("reason"),
                    RiskLevel = RimMindActionsAPI.GetRiskLevel(tc.Name) ?? RiskLevel.Medium
                });
            }
            return items;
        }

        public static bool CheckAdviceAgainstIntent(AdviceItem item, string intentId)
        {
            return item.Action == intentId;
        }

        private static Dictionary<string, string> ParseArguments(string arguments)
        {
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(arguments)
                    ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }
    }
}
