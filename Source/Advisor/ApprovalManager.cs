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

        public void SubmitForApproval(AdviceItem item, Pawn pawn, Action onApproved, Action onRejected)
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
                }
            };
            RimMindAPI.RegisterPendingRequest(entry);
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
