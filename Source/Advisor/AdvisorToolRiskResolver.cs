using System;
using System.Collections.Generic;
using RimMind.Domain.Enums;
using RimMind.Presentation.Api;

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

            var mechanism = mechanismRegistry.FindById(mechanismId);
            if (mechanism == null)
            {
                return RiskLevel.Low;
            }

            var mechanismRisk = mechanism.GetRiskForOperation(operation);
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
            out MechanismOperationType operation)
        {
            mechanismId = string.Empty;
            operation = default;

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
            var resolvedOperation = ResolveOperation(suffix);
            if (!resolvedOperation.HasValue)
            {
                return false;
            }

            operation = resolvedOperation.Value;
            return true;
        }

        private static readonly Dictionary<string, MechanismOperationType> OperationSuffixMap
            = new Dictionary<string, MechanismOperationType>(StringComparer.OrdinalIgnoreCase)
            {
                { "query", MechanismOperationType.Query },
                { "list", MechanismOperationType.List },
                { "watch", MechanismOperationType.Watch },
                { "set", MechanismOperationType.Set },
                { "add", MechanismOperationType.Add },
                { "remove", MechanismOperationType.Remove },
                { "toggle", MechanismOperationType.Toggle },
                { "trigger", MechanismOperationType.Trigger },
            };

        internal static MechanismOperationType? ResolveOperation(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                return null;
            }

            return OperationSuffixMap.TryGetValue(suffix, out var operation)
                ? operation
                : (MechanismOperationType?)null;
        }
    }
}
