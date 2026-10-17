using System;
using System.Collections.Generic;
using System.Text;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Models.UI;
using RimMind.Domain.Common;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Advisor.Advisor
{
    public class CabinetProposal
    {
        public string Title { get; set; } = "Colony Strategic Proposal";
        public string Summary { get; set; } = "";
        public List<AdviceItem> Items { get; set; } = new List<AdviceItem>();
        public List<Action> ExecutionCallbacks { get; set; } = new List<Action>();
    }

    public class ApprovalManager
    {
        private static readonly Dictionary<RequestEntry, Pawn> _pendingRequests = new Dictionary<RequestEntry, Pawn>();
        private readonly RimMindAdvisorSettings _settings;

        public ApprovalManager(RimMindAdvisorSettings settings)
        {
            _settings = settings;
        }

        public static void CancelPendingForPawn(Pawn? pawn)
        {
            if (pawn == null) return;

            var toCancel = new List<RequestEntry>();
            foreach (var kvp in _pendingRequests)
            {
                if (ReferenceEquals(kvp.Value, pawn) || (kvp.Value != null && kvp.Value.thingIDNumber == pawn.thingIDNumber && pawn.thingIDNumber > 0))
                {
                    toCancel.Add(kvp.Key);
                }
            }

            try
            {
                var globalPending = RimMindAPI.GetPendingRequests();
                if (globalPending != null)
                {
                    foreach (var entry in globalPending)
                    {
                        if (entry != null && (ReferenceEquals(entry.pawn, pawn) || (entry.pawn is Pawn entryPawn && entryPawn.thingIDNumber == pawn.thingIDNumber && pawn.thingIDNumber > 0)))
                        {
                            if (!toCancel.Contains(entry))
                                toCancel.Add(entry);
                        }
                    }
                }
            }
            catch
            {
            }

            foreach (var entry in toCancel)
            {
                _pendingRequests.Remove(entry);
                try
                {
                    if (!RimMindAPI.DismissPendingRequest(entry))
                        entry.TryComplete(null, RequestCompletionReason.Dismissed);
                }
                catch
                {
                    try
                    {
                        entry.TryComplete(null, RequestCompletionReason.Dismissed);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public RequestEntry SubmitForApproval(
            AdviceItem item,
            Pawn pawn,
            Action onApproved,
            Action onRejected,
            Action? onDismissed = null,
            Action<RequestEntry>? beforeRegister = null)
        {
            string approveLabel = "RimMind.Advisor.Request.Approve".CanTranslate()
                ? "RimMind.Advisor.Request.Approve".Translate().ToString()
                : "Approve";
            string rejectLabel = "RimMind.Advisor.Request.Reject".CanTranslate()
                ? "RimMind.Advisor.Request.Reject".Translate().ToString()
                : "Reject";
            string titleText = "RimMind.Advisor.Request.RiskAction".CanTranslate()
                ? "RimMind.Advisor.Request.RiskAction".Translate(item.Action).ToString()
                : $"Advisor Action: {item.Action}";

            RequestEntry? entryRef = null;
            var entry = new RequestEntry
            {
                source = "advisor",
                pawn = pawn,
                title = titleText,
                description = item.Reason ?? item.Action,
                systemBlocked = true,
                expireTicks = _settings.requestExpireTicks,
                options = new[] { approveLabel, rejectLabel },
                callback = choice =>
                {
                    if (entryRef != null) _pendingRequests.Remove(entryRef);
                    if (choice == approveLabel)
                    {
                        onApproved();
                    }
                    else
                    {
                        onRejected();
                    }
                },
                completionCallback = completionReason =>
                {
                    if (entryRef != null) _pendingRequests.Remove(entryRef);
                    if (completionReason == RequestCompletionReason.Selected)
                        return;

                    if (completionReason == RequestCompletionReason.Dismissed)
                    {
                        onDismissed?.Invoke();
                        return;
                    }

                    onRejected();
                },
            };
            entryRef = entry;
            _pendingRequests[entry] = pawn;
            beforeRegister?.Invoke(entry);
            try
            {
                RimMindAPI.RegisterPendingRequest(entry);
                return entry;
            }
            catch
            {
                _pendingRequests.Remove(entry);
                try
                {
                    if (!RimMindAPI.DismissPendingRequest(entry))
                        entry.TryComplete(null, RequestCompletionReason.Dismissed);
                }
                catch
                {
                    try
                    {
                        entry.TryComplete(null, RequestCompletionReason.Dismissed);
                    }
                    catch
                    {
                        // Preserve the registration exception; RequestEntry remains idempotently terminal.
                    }
                }

                throw;
            }
        }

        public RequestEntry SubmitCabinetProposal(
            CabinetProposal proposal,
            Pawn advisorPawn,
            Action onAllApproved,
            Action onRejected,
            Action? onDismissed = null,
            Action<RequestEntry>? beforeRegister = null)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));

            string approveAllLabel = "RimMind.Advisor.Cabinet.ApproveAll".CanTranslate()
                ? "RimMind.Advisor.Cabinet.ApproveAll".Translate().ToString()
                : "Approve All";
            string dismissLabel = "RimMind.Advisor.Cabinet.Dismiss".CanTranslate()
                ? "RimMind.Advisor.Cabinet.Dismiss".Translate().ToString()
                : "Dismiss";
            string measuresHeader = "RimMind.Advisor.Cabinet.ProposedMeasures".CanTranslate()
                ? "RimMind.Advisor.Cabinet.ProposedMeasures".Translate().ToString()
                : "Proposed Strategic Measures";

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(proposal.Summary))
            {
                sb.AppendLine(proposal.Summary);
                sb.AppendLine();
            }
            sb.AppendLine(measuresHeader + ":");
            for (int i = 0; i < proposal.Items.Count; i++)
            {
                var it = proposal.Items[i];
                sb.AppendLine($"{i + 1}. [{it.Action}] {it.Reason ?? it.Target ?? it.Param ?? ""}");
            }

            RequestEntry? entryRef = null;
            var entry = new RequestEntry
            {
                source = "advisor_cabinet",
                pawn = advisorPawn,
                title = proposal.Title,
                description = sb.ToString().TrimEnd(),
                systemBlocked = true,
                expireTicks = _settings.requestExpireTicks,
                options = new[] { approveAllLabel, dismissLabel },
                callback = choice =>
                {
                    if (entryRef != null) _pendingRequests.Remove(entryRef);
                    if (choice == approveAllLabel)
                    {
                        foreach (var cb in proposal.ExecutionCallbacks)
                        {
                            try
                            {
                                cb();
                            }
                            catch (Exception ex)
                            {
                                Log.Warning($"[ApprovalManager] Cabinet sub-action failed: {ex.Message}");
                            }
                        }
                        onAllApproved();
                    }
                    else
                    {
                        onRejected();
                    }
                },
                completionCallback = completionReason =>
                {
                    if (entryRef != null) _pendingRequests.Remove(entryRef);
                    if (completionReason == RequestCompletionReason.Selected)
                        return;

                    if (completionReason == RequestCompletionReason.Dismissed)
                    {
                        onDismissed?.Invoke();
                        return;
                    }

                    onRejected();
                },
            };
            entryRef = entry;
            _pendingRequests[entry] = advisorPawn;
            beforeRegister?.Invoke(entry);
            try
            {
                RimMindAPI.RegisterPendingRequest(entry);
                return entry;
            }
            catch
            {
                _pendingRequests.Remove(entry);
                try
                {
                    if (!RimMindAPI.DismissPendingRequest(entry))
                        entry.TryComplete(null, RequestCompletionReason.Dismissed);
                }
                catch
                {
                    try
                    {
                        entry.TryComplete(null, RequestCompletionReason.Dismissed);
                    }
                    catch
                    {
                        // Preserve the registration exception; RequestEntry remains idempotently terminal.
                    }
                }

                throw;
            }
        }
    }
}
