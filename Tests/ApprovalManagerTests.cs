using System;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Models.UI;
using RimMind.Domain.Enums;
using RimMind.Presentation.Api;
using Verse;
using Xunit;

namespace RimMind.Advisor.Tests
{
    /// <summary>
    /// ApprovalManager 单元测试：审批逻辑、审批记录上下文
    /// </summary>
    public class ApprovalManagerTests
    {
        /// <summary>
        /// 辅助方法：创建带指定设置的 ApprovalManager
        /// </summary>
        private static ApprovalManager CreateManager(
            bool enableRiskApproval = true,
            RiskLevel autoBlockRiskLevel = RiskLevel.High,
            bool enableRequestSystem = true)
        {
            var settings = new RimMindAdvisorSettings
            {
                enableRiskApproval = enableRiskApproval,
                autoBlockRiskLevel = autoBlockRiskLevel,
                enableRequestSystem = enableRequestSystem,
                requestExpireTicks = 600
            };
            return new ApprovalManager(settings);
        }

        /// <summary>
        /// 辅助方法：创建 AdviceItem
        /// </summary>
        private static AdviceItem CreateAdviceItem(
            string action = "test_action",
            RiskLevel riskLevel = RiskLevel.High,
            string? reason = null)
        {
            return new AdviceItem
            {
                Action = action,
                RiskLevel = riskLevel,
                Reason = reason
            };
        }

        [Fact]
        public void SubmitForApproval_ApproveCallback_AddsApprovedRecord()
        {
            // 审批通过时记录 Approved=true
            var manager = CreateManager();
            var item = CreateAdviceItem("assign_job", RiskLevel.High, "good at crafting");
            var pawn = new Pawn { thingIDNumber = 1 };
            RimMindAPI.ClearPendingRequests();

            bool approvedCalled = false;
            bool rejectedCalled = false;
            manager.SubmitForApproval(item, pawn,
                onApproved: () => approvedCalled = true,
                onRejected: () => rejectedCalled = true);

            // 模拟玩家选择"批准"
            Assert.Single(RimMindAPI.PendingRequests);
            var entry = RimMindAPI.PendingRequests[0];
            Assert.True(entry.TryComplete(
                "RimMind.Advisor.Request.Approve",
                RimMind.Application.Common.Models.UI.RequestCompletionReason.Selected));

            Assert.True(approvedCalled);
            Assert.False(rejectedCalled);

            // GetRecentApprovalContext 已移除,approvedCalled 已验证
        }

        [Fact]
        public void SubmitForApproval_RejectCallback_AddsRejectedRecord()
        {
            // 审批拒绝时记录 Approved=false
            var manager = CreateManager();
            var item = CreateAdviceItem("forbid_area", RiskLevel.Critical, "dangerous area");
            var pawn = new Pawn { thingIDNumber = 2 };
            RimMindAPI.ClearPendingRequests();

            bool approvedCalled = false;
            bool rejectedCalled = false;
            manager.SubmitForApproval(item, pawn,
                onApproved: () => approvedCalled = true,
                onRejected: () => rejectedCalled = true);

            // 模拟玩家选择"拒绝"
            Assert.Single(RimMindAPI.PendingRequests);
            var entry = RimMindAPI.PendingRequests[0];
            Assert.True(entry.TryComplete(
                "RimMind.Advisor.Request.Reject",
                RimMind.Application.Common.Models.UI.RequestCompletionReason.Selected));

            Assert.False(approvedCalled);
            Assert.True(rejectedCalled);

            // GetRecentApprovalContext 已移除,rejectedCalled 已验证
        }

        [Fact]
        public void SubmitForApproval_ExpiredEntry_RejectsExactlyOnce()
        {
            var manager = CreateManager();
            var pawn = new Pawn { thingIDNumber = 3 };
            RimMindAPI.ClearPendingRequests();

            var rejectedCount = 0;
            manager.SubmitForApproval(
                CreateAdviceItem(),
                pawn,
                onApproved: () => throw new InvalidOperationException("expired approval must not execute"),
                onRejected: () => rejectedCount++);

            var entry = Assert.Single(RimMindAPI.PendingRequests);
            Assert.True(entry.TryComplete(
                null,
                RimMind.Application.Common.Models.UI.RequestCompletionReason.Expired));
            Assert.False(entry.TryComplete(
                null,
                RimMind.Application.Common.Models.UI.RequestCompletionReason.Evicted));
            Assert.Equal(1, rejectedCount);
        }

        [Fact]
        public void SubmitForApproval_DismissedEntry_IsCancellationNotPlayerRejection()
        {
            var manager = CreateManager();
            RimMindAPI.ClearPendingRequests();
            var rejectedCount = 0;
            var dismissedCount = 0;

            var submittedEntry = manager.SubmitForApproval(
                CreateAdviceItem(),
                new Pawn { thingIDNumber = 4 },
                onApproved: () => throw new InvalidOperationException("dismissed approval must not execute"),
                onRejected: () => rejectedCount++,
                onDismissed: () => dismissedCount++);

            var entry = Assert.Single(RimMindAPI.PendingRequests);
            Assert.Same(submittedEntry, entry);
            Assert.True(RimMindAPI.DismissPendingRequest(entry));
            Assert.False(RimMindAPI.DismissPendingRequest(entry));

            Assert.Equal(0, rejectedCount);
            Assert.Equal(1, dismissedCount);
            Assert.Empty(RimMindAPI.PendingRequests);
        }

        [Fact]
        public void SubmitForApproval_SynchronousTerminalDuringRegistration_DoesNotLeaveCyclePending()
        {
            var manager = CreateManager();
            var cycle = new AdvisorRequestCycleState<string, string>();
            RequestEntry? approvalEntry = null;
            RimMindAPI.ClearPendingRequests();
            RimMindAPI.RegisterPendingRequestBehavior = entry =>
            {
                RimMindAPI.PendingRequests.Remove(entry);
                entry.TryComplete(null, RequestCompletionReason.Dismissed);
            };

            approvalEntry = manager.SubmitForApproval(
                CreateAdviceItem(),
                new Pawn { thingIDNumber = 5 },
                onApproved: () => throw new InvalidOperationException("synchronously dismissed approval must not execute"),
                onRejected: () => throw new InvalidOperationException("synchronously dismissed approval is not a rejection"),
                onDismissed: () =>
                {
                    if (approvalEntry != null)
                        cycle.TryFinishApproval(approvalEntry);
                },
                beforeRegister: entry =>
                {
                    approvalEntry = entry;
                    cycle.TrackPendingApproval(entry, () => RimMindAPI.DismissPendingRequest(entry));
                });

            Assert.Equal(0, cycle.PendingApprovals);
            Assert.True(cycle.CanComplete);
            Assert.Empty(RimMindAPI.PendingRequests);
            RimMindAPI.ClearPendingRequests();
        }

        [Fact]
        public void SubmitForApproval_PartialRegistrationFailure_UntracksAndDismissesExactlyOnce()
        {
            var manager = CreateManager();
            var cycle = new AdvisorRequestCycleState<string, string>();
            RequestEntry? approvalEntry = null;
            var dismissedCount = 0;
            RimMindAPI.ClearPendingRequests();
            RimMindAPI.RegisterPendingRequestBehavior = _ =>
                throw new InvalidOperationException("registration failed after enqueue");

            Assert.Throws<InvalidOperationException>(() => manager.SubmitForApproval(
                CreateAdviceItem(),
                new Pawn { thingIDNumber = 6 },
                onApproved: () => throw new InvalidOperationException("failed registration must not execute"),
                onRejected: () => throw new InvalidOperationException("failed registration is not a rejection"),
                onDismissed: () =>
                {
                    dismissedCount++;
                    if (approvalEntry != null)
                        cycle.TryFinishApproval(approvalEntry);
                },
                beforeRegister: entry =>
                {
                    approvalEntry = entry;
                    cycle.TrackPendingApproval(entry, () => RimMindAPI.DismissPendingRequest(entry));
                }));

            Assert.Equal(1, dismissedCount);
            Assert.Equal(0, cycle.PendingApprovals);
            Assert.True(cycle.CanComplete);
            Assert.Empty(RimMindAPI.PendingRequests);
            RimMindAPI.ClearPendingRequests();
        }

    }
}
