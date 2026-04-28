using System;
using System.Collections.Generic;
using RimMind.Actions;
using RimMind.Advisor.Settings;
using RimMind.Core;
using RimMind.Core.UI;
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

        public bool RequiresApproval(RiskLevel riskLevel)
        {
            if (!_settings.enableRiskApproval) return false;
            return riskLevel >= _settings.autoBlockRiskLevel;
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

        public string GetRecentApprovalContext(int maxRecords = 5)
        {
            if (_records.Count == 0) return string.Empty;
            var sb = new System.Text.StringBuilder("[Recent approvals/rejections]\n");
            int count = 0;
            for (int i = _records.Count - 1; i >= 0 && count < maxRecords; i--, count++)
            {
                var r = _records[i];
                sb.AppendLine($"- {r.Action}: {(r.Approved ? "APPROVED" : "REJECTED")} ({r.Reason})");
            }
            return sb.ToString().TrimEnd();
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
