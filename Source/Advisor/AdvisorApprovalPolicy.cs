using System;
using Newtonsoft.Json.Linq;
using RimMind.Domain.Enums;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Single approval rule used by both Advisor execution and Core's approval-gate adapter.
    /// </summary>
    internal static class AdvisorApprovalPolicy
    {
        public static bool RequiresApproval(
            bool enableRiskApproval,
            RiskLevel threshold,
            RiskLevel actualRisk,
            string? arguments)
            => (enableRiskApproval && actualRisk >= threshold)
               || IsExplicitRequest(arguments);

        public static bool IsExplicitRequest(string? arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                return false;

            string nonBlankArguments = arguments!;
            try
            {
                var json = JObject.Parse(nonBlankArguments);
                return json.TryGetValue(
                           "request_type",
                           StringComparison.Ordinal,
                           out JToken? requestType)
                       && requestType.Type == JTokenType.String
                       && string.Equals(
                           requestType.Value<string>(),
                           "request",
                           StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }
}
