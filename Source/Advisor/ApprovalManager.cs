using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Models.UI;
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
        private static readonly ConcurrentDictionary<RequestEntry, Pawn> _pendingRequests = new ConcurrentDictionary<RequestEntry, Pawn>();
        private readonly RimMindAdvisorSettings _settings;

        public ApprovalManager(RimMindAdvisorSettings settings)
        {
            _settings = settings;
        }

        public static int TrackedPendingCount => _pendingRequests.Count;

        public static void ClearPendingRequests()
        {
            _pendingRequests.Clear();
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
                var globalPending = RimMindAPI.Bus.GetPendingRequests();
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
                _pendingRequests.TryRemove(entry, out _);
                DismissOrCompleteSafely(entry);
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
            string approveLabel = RimMindAPI.Text.TrOr("RimMind.Advisor.Request.Approve", "Approve");
            string rejectLabel = RimMindAPI.Text.TrOr("RimMind.Advisor.Request.Reject", "Reject");
            string readableAction = ToolCallSummaryFormatter.FormatHumanReadableToolName(item.Action);
            string titleText = RimMindAPI.Text.TrOr(
                "RimMind.Advisor.Request.RiskAction",
                "Advisor Action: {0}",
                readableAction);
            string descriptionText = !string.IsNullOrWhiteSpace(item.Reason) &&
                                     !string.Equals(item.Reason, item.Action, StringComparison.OrdinalIgnoreCase)
                ? item.Reason!
                : (!string.IsNullOrWhiteSpace(item.Target)
                    ? $"{readableAction}: {item.Target}"
                    : readableAction);

            return RegisterTrackedEntry(
                source: "advisor",
                pawn: pawn,
                title: titleText,
                description: descriptionText,
                approveLabel: approveLabel,
                rejectLabel: rejectLabel,
                onApproved: onApproved,
                onRejected: onRejected,
                onDismissed: onDismissed,
                beforeRegister: beforeRegister);
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

            string approveAllLabel = RimMindAPI.Text.TrOr("RimMind.Advisor.Cabinet.ApproveAll", "Approve All");
            string dismissLabel = RimMindAPI.Text.TrOr("RimMind.Advisor.Cabinet.Dismiss", "Dismiss");
            string measuresHeader = RimMindAPI.Text.TrOr("RimMind.Advisor.Cabinet.ProposedMeasures", "Proposed Strategic Measures");

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
                var readableItemAction = ToolCallSummaryFormatter.FormatHumanReadableToolName(it.Action);
                var itemSummary = !string.IsNullOrWhiteSpace(it.Reason) &&
                                  !string.Equals(it.Reason, it.Action, StringComparison.OrdinalIgnoreCase)
                    ? it.Reason
                    : (it.Target ?? it.Param ?? "");
                sb.AppendLine(string.IsNullOrWhiteSpace(itemSummary)
                    ? $"{i + 1}. [{readableItemAction}]"
                    : $"{i + 1}. [{readableItemAction}] {itemSummary}");
            }

            return RegisterTrackedEntry(
                source: "advisor_cabinet",
                pawn: advisorPawn,
                title: proposal.Title,
                description: sb.ToString().TrimEnd(),
                approveLabel: approveAllLabel,
                rejectLabel: dismissLabel,
                onApproved: () =>
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
                },
                onRejected: onRejected,
                onDismissed: onDismissed,
                beforeRegister: beforeRegister);
        }

        private RequestEntry RegisterTrackedEntry(
            string source,
            Pawn pawn,
            string title,
            string description,
            string approveLabel,
            string rejectLabel,
            Action onApproved,
            Action onRejected,
            Action? onDismissed,
            Action<RequestEntry>? beforeRegister)
        {
            RequestEntry? entryRef = null;
            var entry = new RequestEntry
            {
                source = source,
                pawn = pawn,
                title = title,
                description = description,
                systemBlocked = true,
                expireTicks = _settings.requestExpireTicks,
                options = new[] { approveLabel, rejectLabel },
                callback = choice =>
                {
                    if (entryRef != null) _pendingRequests.TryRemove(entryRef, out _);
                    if (choice == approveLabel)
                        onApproved();
                    else
                        onRejected();
                },
                completionCallback = completionReason =>
                {
                    if (entryRef != null) _pendingRequests.TryRemove(entryRef, out _);
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
                RimMindAPI.Bus.RegisterPendingRequest(entry);
                return entry;
            }
            catch
            {
                _pendingRequests.TryRemove(entry, out _);
                DismissOrCompleteSafely(entry);
                throw;
            }
        }

        private static void DismissOrCompleteSafely(RequestEntry entry)
        {
            try
            {
                if (!RimMindAPI.Bus.DismissPendingRequest(entry))
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
                    // Preserve original error; RequestEntry remains idempotently terminal.
                }
            }
        }
    }
}
