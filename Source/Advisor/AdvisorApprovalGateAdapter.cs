using System;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;
using Verse;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Adapter that bridges Advisor's approval flow to Core's IHumanApprovalGate contract.
    /// Enables future cross-mod approval orchestration via Core's agent framework.
    /// </summary>
    public sealed class AdvisorApprovalGateAdapter : IHumanApprovalGate
    {
        private readonly RimMindAdvisorSettings _settings;
        private readonly ApprovalManager _approvalManager;

        public AdvisorApprovalGateAdapter(RimMindAdvisorSettings settings, ApprovalManager approvalManager)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _approvalManager = approvalManager ?? throw new ArgumentNullException(nameof(approvalManager));
        }

        /// <summary>
        /// Centralized approval check. Mirrors CompAIAdvisor.ShouldDeferForApproval logic
        /// but works against AgentDecision so any mod can query the same rule.
        /// </summary>
        public bool RequiresApproval(AgentDecision decision, RiskLevel riskLevel)
        {
            if (decision == null) return false;

            bool systemBlocked = _settings.enableRiskApproval
                && riskLevel >= _settings.autoBlockRiskLevel;
            bool isRequest = IsToolCallRequest(decision.Param);
            return systemBlocked || isRequest;
        }

        /// <summary>
        /// Request player approval. Converts AgentDecision to AdviceItem and delegates
        /// to ApprovalManager.SubmitForApproval. The bool callback: true=approved, false=rejected.
        /// </summary>
        public void RequestApproval(AgentDecision decision, Action<bool> callback)
        {
            if (decision == null)
            {
                callback?.Invoke(false);
                return;
            }

            // Resolve target pawn if specified
            Pawn? targetPawn = null;
            if (decision.TargetPawnId is { Length: > 0 } targetPawnId)
            {
                targetPawn = FindTargetPawn(targetPawnId);
            }

            // Fall back to first colonist if no target specified
            if (targetPawn == null)
            {
                foreach (var map in Find.Maps)
                {
                    foreach (var pawn in map.mapPawns.FreeColonists)
                    {
                        targetPawn = pawn;
                        break;
                    }
                    if (targetPawn != null) break;
                }
            }

            if (targetPawn == null)
            {
                callback?.Invoke(false);
                return;
            }

            var item = new AdviceItem
            {
                Action = decision.ActionIntent,
                Reason = decision.Reason
            };

            _approvalManager.SubmitForApproval(
                item,
                targetPawn,
                onApproved: () => callback?.Invoke(true),
                onRejected: () => callback?.Invoke(false));
        }

        private static bool IsToolCallRequest(string? arguments)
        {
            if (arguments is null || arguments.Length == 0) return false;
            return arguments.Contains("request_type", StringComparison.OrdinalIgnoreCase);
        }

        private static Pawn? FindTargetPawn(string pawnId)
        {
            foreach (var map in Find.Maps)
            {
                foreach (var pawn in map.mapPawns.AllPawns)
                {
                    if (pawn.ThingID == pawnId || pawn.Name?.ToStringFull == pawnId)
                    {
                        return pawn;
                    }
                }
            }
            return null;
        }
    }
}
