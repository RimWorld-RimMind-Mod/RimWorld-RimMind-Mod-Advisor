using System;
using RimMind.Domain.Enums;
using RimMind.Presentation;

namespace RimMind.Advisor.Advisor
{
    internal static class AdvisorToolRiskResolver
    {
        public static RiskLevel Resolve(string toolName)
        {
            if (!TryParseToolName(toolName, out var mechanismId, out var operation))
            {
                return RiskLevel.Low;
            }

            var mechanismRegistry = RimMindAPI.Mechanisms;
            if (mechanismRegistry == null)
            {
                return RiskLevel.Low;
            }

            var mechanism = RimMindAPI.Mechanisms.FindById(mechanismId);
            if (mechanism == null)
            {
                return RiskLevel.Low;
            }

            var mechanismRisk = mechanism.GetRiskForOperation(operation.Value);
            return mechanismRisk switch
            {
                MechanismRisk.Safe => RiskLevel.Low,
                MechanismRisk.Moderate => RiskLevel.Medium,
                MechanismRisk.Dangerous => RiskLevel.High,
                _ => RiskLevel.Low
            };
        }

        private static bool TryParseToolName(
            string toolName,
            out string mechanismId,
            out MechanismOperationType? operation)
        {
            mechanismId = string.Empty;
            operation = null;

            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            var lastDotIndex = toolName.LastIndexOf('.');
            if (lastDotIndex <= 0 || lastDotIndex == toolName.Length - 1)
            {
                return false;
            }

            mechanismId = toolName.Substring(0, lastDotIndex);
            var suffix = toolName.Substring(lastDotIndex + 1);
            operation = ResolveOperation(suffix);

            return operation.HasValue;
        }

        private static MechanismOperationType? ResolveOperation(string suffix)
        {
            switch (suffix.ToLowerInvariant())
            {
                case "query":
                    return MechanismOperationType.Query;
                case "list":
                    return MechanismOperationType.List;
                case "watch":
                    return MechanismOperationType.Watch;
                case "set":
                    return MechanismOperationType.Set;
                case "add":
                    return MechanismOperationType.Add;
                case "remove":
                    return MechanismOperationType.Remove;
                case "toggle":
                    return MechanismOperationType.Toggle;
                case "trigger":
                    return MechanismOperationType.Trigger;
                default:
                    return null;
            }
        }
    }
}
