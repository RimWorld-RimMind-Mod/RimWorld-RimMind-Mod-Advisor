using System;
using System.Collections.Generic;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Interfaces.UI;
using RimMind.Application.Common.Models.UI;
using RimMind.Domain.Enums;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Advisor.Advisor
{
    public class ApprovalManager
    {
        private readonly RimMindAdvisorSettings _settings;
        private readonly List<ApprovalRecord> _records = new List<ApprovalRecord>();

        public ApprovalManager(RimMindAdvisorSettings settings)
        {
            _settings = settings;
        }

        public RequestEntry SubmitForApproval(
            AdviceItem item,
            Pawn pawn,
            Action onApproved,
            Action onRejected,
            Action? onDismissed = null,
            Action<RequestEntry>? beforeRegister = null)
        {
            string approveLabel = "RimMind.Advisor.Request.Approve".Translate();
            string rejectLabel = "RimMind.Advisor.Request.Reject".Translate();

            var entry = new RequestEntry
            {
                source = "advisor",
                pawn = pawn,
                title = "RimMind.Advisor.Request.RiskAction".Translate(item.Action),
                description = item.Reason ?? item.Action,
                systemBlocked = true,
                expireTicks = _settings.requestExpireTicks,
                options = new[] { approveLabel, rejectLabel },
                callback = choice =>
                {
                    if (choice == approveLabel)
                    {
                        _records.Add(new ApprovalRecord { Action = item.Action, Reason = item.Reason, Approved = true, Tick = Find.TickManager.TicksGame });
                        onApproved();
                    }
                    else
                    {
                        _records.Add(new ApprovalRecord { Action = item.Action, Reason = item.Reason, Approved = false, Tick = Find.TickManager.TicksGame });
                        onRejected();
                    }
                },
                completionCallback = completionReason =>
                {
                    if (completionReason == RequestCompletionReason.Selected)
                        return;

                    if (completionReason == RequestCompletionReason.Dismissed)
                    {
                        onDismissed?.Invoke();
                        return;
                    }

                    _records.Add(new ApprovalRecord
                    {
                        Action = item.Action,
                        Reason = item.Reason,
                        Approved = false,
                        Tick = Find.TickManager.TicksGame
                    });
                    onRejected();
                },
            };
            beforeRegister?.Invoke(entry);
            try
            {
                RimMindAPI.RegisterPendingRequest(entry);
                return entry;
            }
            catch
            {
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

        public class ApprovalRecord
        {
            public string Action = null!;
            public string? Reason;
            public bool Approved;
            public int Tick;
        }
    }
}
