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
            string readableAction = FormatHumanReadableToolName(item.Action);
            string titleText = "RimMind.Advisor.Request.RiskAction".CanTranslate()
                ? "RimMind.Advisor.Request.RiskAction".Translate(readableAction).ToString()
                : $"Advisor Action: {readableAction}";
            string descriptionText = !string.IsNullOrWhiteSpace(item.Reason) &&
                                     !string.Equals(item.Reason, item.Action, StringComparison.OrdinalIgnoreCase)
                ? item.Reason!
                : (!string.IsNullOrWhiteSpace(item.Target)
                    ? $"{readableAction}: {item.Target}"
                    : readableAction);

            RequestEntry? entryRef = null;
            var entry = new RequestEntry
            {
                source = "advisor",
                pawn = pawn,
                title = titleText,
                description = descriptionText,
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
                var readableItemAction = FormatHumanReadableToolName(it.Action);
                var itemSummary = !string.IsNullOrWhiteSpace(it.Reason) &&
                                  !string.Equals(it.Reason, it.Action, StringComparison.OrdinalIgnoreCase)
                    ? it.Reason
                    : (it.Target ?? it.Param ?? "");
                sb.AppendLine(string.IsNullOrWhiteSpace(itemSummary)
                    ? $"{i + 1}. [{readableItemAction}]"
                    : $"{i + 1}. [{readableItemAction}] {itemSummary}");
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

        internal static string FormatHumanReadableToolName(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return "Action";

            var trimmed = rawName!.Trim();
            string[] prefixes = { "actions_", "pawn_", "rimmind_" };
            foreach (var prefix in prefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    trimmed.Length > prefix.Length)
                {
                    trimmed = trimmed.Substring(prefix.Length);
                    break;
                }
            }

            var tokens = trimmed.Split(new[] { '_', '.', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return trimmed;

            var formatted = new string[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                formatted[i] = token.Length == 1
                    ? token.ToUpperInvariant()
                    : char.ToUpperInvariant(token[0]) + token.Substring(1);
            }

            return string.Join(" ", formatted);
        }
    }
}
