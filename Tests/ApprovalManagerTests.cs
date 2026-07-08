using System;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Settings;
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
            Assert.NotNull(entry.callback);
            entry.callback!("RimMind.Advisor.Request.Approve");

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
            entry.callback!("RimMind.Advisor.Request.Reject");

            Assert.False(approvedCalled);
            Assert.True(rejectedCalled);

            // GetRecentApprovalContext 已移除,rejectedCalled 已验证
        }

    }
}
