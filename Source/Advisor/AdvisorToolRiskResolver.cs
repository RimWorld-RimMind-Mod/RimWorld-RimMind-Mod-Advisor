using System;
using System.Collections.Generic;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Advisor.Advisor
{
    internal static class AdvisorToolRiskResolver
    {
        private static readonly Dictionary<string, AutonomyDomain> ToolAutonomyDomainMap
            = new Dictionary<string, AutonomyDomain>(StringComparer.OrdinalIgnoreCase)
            {
                { "actions_place_blueprint", AutonomyDomain.BlueprintPlacement },
                { "actions_plan_room_project", AutonomyDomain.BlueprintPlacement },
                { "actions_designate_resources", AutonomyDomain.ResourceMarking },
                { "actions_manage_zone", AutonomyDomain.ResourceMarking },
                { "actions_add_worktable_bill", AutonomyDomain.WorktableBills },
                { "actions_manage_equipment", AutonomyDomain.EmergencySurvival },
                { "actions_emergency_flee", AutonomyDomain.EmergencySurvival },
                { "actions_stabilize_rest", AutonomyDomain.EmergencySurvival },
                { "actions_eat_and_recreation", AutonomyDomain.EmergencySurvival },
                { "actions_triage_patient", AutonomyDomain.EmergencySurvival },
            };

        public static RiskLevel Resolve(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return RiskLevel.Low;
            }

            if (TryParseToolName(toolName, out var mechanismId, out var operation))
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

            if (TryGetAutonomyDomain(toolName, out var domain))
            {
                return MapAutonomyDomainRisk(domain);
            }

            try
            {
                var manifest = RimMindAPI.Tools.FindById(toolName)?.Definition?.Manifest;
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

        public static bool TryGetAutonomyDomain(string? toolName, out AutonomyDomain domain)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                domain = default;
                return false;
            }

            return ToolAutonomyDomainMap.TryGetValue(toolName!.Trim(), out domain);
        }

        public static bool RequiresAutonomyApproval(Pawn? pawn, string? toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            if (TryGetAutonomyDomain(toolName, out var domain))
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

        public static bool IsAutonomyForbidden(Pawn? pawn, string? toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            if (TryGetAutonomyDomain(toolName, out var domain))
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
