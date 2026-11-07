using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Advisor.Advisor
{
    internal static class AdvisorToolRiskResolver
    {
        public static RiskLevel Resolve(string? toolName, ToolCallArgs? args = null)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return RiskLevel.Low;
            }

            if (TryParseToolName(toolName!, out var mechanismId, out var operation))
            {
                try
                {
                    var mechanism = RimMindAPI.Mechanisms?.FindById(mechanismId);
                    if (mechanism != null)
                    {
                        return MapMechanismRisk(mechanism.GetRiskForOperation(operation));
                    }
                }
                catch
                {
                }
            }

            try
            {
                var policy = RimMindAPI.Tools.ClassifyPolicy(toolName, args);
                if (policy.HasValue)
                {
                    if (policy.Value.RequiresApproval)
                    {
                        return RiskLevel.Critical;
                    }
                    if (policy.Value.Domain.HasValue)
                    {
                        return MapAutonomyDomainRisk(policy.Value.Domain.Value);
                    }
                    if (policy.Value.Risk != RiskLevel.Medium)
                    {
                        return policy.Value.Risk;
                    }
                }
            }
            catch
            {
            }

            if (TryGetAutonomyDomain(toolName, args, out var domain))
            {
                return MapAutonomyDomainRisk(domain);
            }

            try
            {
                var manifest = RimMindAPI.Tools.FindById(toolName!)?.Definition?.Manifest;
                if (manifest != null && (manifest.RequiresApproval || manifest.RiskLevel != RiskLevel.Medium))
                {
                    if (manifest.RequiresApproval)
                    {
                        return RiskLevel.Critical;
                    }
                    return manifest.RiskLevel;
                }
            }
            catch
            {
            }

            return RiskLevel.Low;
        }

        public static RiskLevel ResolveRiskLevel(string? toolName, ToolCallArgs? args = null)
            => Resolve(toolName, args);

        public static AutonomyDomain? ResolveAutonomyDomain(string? toolName, ToolCallArgs? args = null)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return null;
            }

            try
            {
                var policy = RimMindAPI.Tools.ClassifyPolicy(toolName!, args);
                if (policy.HasValue)
                {
                    if (policy.Value.Domain.HasValue)
                    {
                        return policy.Value.Domain.Value;
                    }
                    if (!string.IsNullOrWhiteSpace(policy.Value.DomainId) &&
                        Enum.TryParse<AutonomyDomain>(policy.Value.DomainId, ignoreCase: true, out var parsedDomain))
                    {
                        return parsedDomain;
                    }
                }
            }
            catch
            {
            }

            try
            {
                var manifest = RimMindAPI.Tools.FindById(toolName!)?.Definition?.Manifest;
                if (manifest != null)
                {
                    if (manifest.DefaultDomain.HasValue)
                    {
                        return manifest.DefaultDomain.Value;
                    }
                    if (!string.IsNullOrWhiteSpace(manifest.AutonomyDomainId) &&
                        Enum.TryParse<AutonomyDomain>(manifest.AutonomyDomainId, ignoreCase: true, out var manifestDomain))
                    {
                        return manifestDomain;
                    }
                }
            }
            catch
            {
            }

            if (TryParseToolName(toolName!, out var mechanismId, out var operation) &&
                operation != MechanismOperationType.Query &&
                operation != MechanismOperationType.List &&
                operation != MechanismOperationType.Watch)
            {
                if (mechanismId.StartsWith("pawn.", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mechanismId, "pawn", StringComparison.OrdinalIgnoreCase))
                {
                    return AutonomyDomain.EmergencySurvival;
                }
                if (mechanismId.StartsWith("map.", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mechanismId, "map", StringComparison.OrdinalIgnoreCase))
                {
                    return AutonomyDomain.ResourceMarking;
                }
                if (mechanismId.StartsWith("world.", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mechanismId, "world", StringComparison.OrdinalIgnoreCase))
                {
                    return AutonomyDomain.HighRiskOperations;
                }
            }

            return null;
        }

        public static bool TryGetAutonomyDomain(string? toolName, out AutonomyDomain domain)
            => TryGetAutonomyDomain(toolName, null, out domain);

        public static bool TryGetAutonomyDomain(string? toolName, ToolCallArgs? args, out AutonomyDomain domain)
        {
            var resolved = ResolveAutonomyDomain(toolName, args);
            if (resolved.HasValue)
            {
                domain = resolved.Value;
                return true;
            }

            domain = default;
            return false;
        }

        internal static string NormalizeToolId(string toolName) => toolName.Trim().Replace('.', '_');

        public static bool RequiresAutonomyApproval(Pawn? pawn, string? toolName, ToolCallArgs? args = null)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            if (TryGetAutonomyDomain(toolName, args, out var domain))
            {
                try
                {
                    if (RimMindAPI.Autonomy.RequiresApproval(pawn, domain))
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            try
            {
                var policy = RimMindAPI.Tools.ClassifyPolicy(toolName!, args);
                if (policy.HasValue && policy.Value.RequiresApproval)
                {
                    return true;
                }

                var manifest = RimMindAPI.Tools.FindById(toolName!)?.Definition?.Manifest;
                if (manifest != null && manifest.RequiresApproval)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public static bool RequiresPlayerApproval(Pawn? pawn, string? toolName, ToolCallArgs? args = null)
            => RequiresAutonomyApproval(pawn, toolName, args);

        public static bool IsAutonomyForbidden(Pawn? pawn, string? toolName, ToolCallArgs? args = null)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            if (TryGetAutonomyDomain(toolName, args, out var domain))
            {
                try
                {
                    return RimMindAPI.Autonomy.GetLevel(pawn, domain) == AutonomyLevel.Disabled;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        public static bool ShouldSkipAction(string? toolName, Pawn? pawn = null, ToolCallArgs? args = null)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return true;
            }

            if (RimMindAPI.ShouldSkipAction(toolName!))
            {
                return true;
            }

            if (IsAutonomyForbidden(pawn, toolName, args))
            {
                return true;
            }

            return false;
        }

        private static RiskLevel MapMechanismRisk(MechanismRisk mechanismRisk) =>
            mechanismRisk switch
            {
                MechanismRisk.Safe => RiskLevel.Low,
                MechanismRisk.Moderate => RiskLevel.Medium,
                MechanismRisk.Dangerous => RiskLevel.High,
                _ => RiskLevel.Low
            };

        private static RiskLevel MapAutonomyDomainRisk(AutonomyDomain domain) =>
            domain switch
            {
                AutonomyDomain.EmergencySurvival => RiskLevel.Low,
                AutonomyDomain.ResourceMarking => RiskLevel.Low,
                AutonomyDomain.WorktableBills => RiskLevel.Medium,
                AutonomyDomain.BlueprintPlacement => RiskLevel.Medium,
                AutonomyDomain.HighRiskOperations => RiskLevel.High,
                _ => RiskLevel.Low
            };

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
