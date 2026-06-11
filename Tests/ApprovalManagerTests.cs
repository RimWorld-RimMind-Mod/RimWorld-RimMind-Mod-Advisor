using System;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Settings;
using RimMind.Domain.Enums;
using RimMind.Application.Api;
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
        public void RequiresApproval_RiskApprovalDisabled_ReturnsFalse()
        {
            // 当 enableRiskApproval=false 时，任何风险等级都不需要审批
            var manager = CreateManager(enableRiskApproval: false);

            Assert.False(manager.RequiresApproval(RiskLevel.Low));
            Assert.False(manager.RequiresApproval(RiskLevel.Medium));
            Assert.False(manager.RequiresApproval(RiskLevel.High));
            Assert.False(manager.RequiresApproval(RiskLevel.Critical));
        }

        [Fact]
        public void RequiresApproval_LowRiskHighThreshold_ReturnsFalse()
        {
            // Low < High，不需要审批
            var manager = CreateManager(autoBlockRiskLevel: RiskLevel.High);
            Assert.False(manager.RequiresApproval(RiskLevel.Low));
        }

        [Fact]
        public void RequiresApproval_HighRiskHighThreshold_ReturnsTrue()
        {
            // High >= High，需要审批
            var manager = CreateManager(autoBlockRiskLevel: RiskLevel.High);
            Assert.True(manager.RequiresApproval(RiskLevel.High));
        }

        [Fact]
        public void RequiresApproval_CriticalRiskHighThreshold_ReturnsTrue()
        {
            // Critical >= High，需要审批
            var manager = CreateManager(autoBlockRiskLevel: RiskLevel.High);
            Assert.True(manager.RequiresApproval(RiskLevel.Critical));
        }

        [Fact]
        public void RequiresApproval_MediumRiskMediumThreshold_ReturnsTrue()
        {
            // Medium >= Medium，需要审批
            var manager = CreateManager(autoBlockRiskLevel: RiskLevel.Medium);
            Assert.True(manager.RequiresApproval(RiskLevel.Medium));
        }

        [Fact]
        public void RequiresApproval_LowRiskLowThreshold_ReturnsTrue()
        {
            // Low >= Low，需要审批
            var manager = CreateManager(autoBlockRiskLevel: RiskLevel.Low);
            Assert.True(manager.RequiresApproval(RiskLevel.Low));
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

            // 验证审批记录
            var context = manager.GetRecentApprovalContext(10);
            Assert.Contains("APPROVED", context);
            Assert.Contains("assign_job", context);
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

            // 验证审批记录
            var context = manager.GetRecentApprovalContext(10);
            Assert.Contains("REJECTED", context);
            Assert.Contains("forbid_area", context);
        }

        [Fact]
        public void GetRecentApprovalContext_NoRecords_ReturnsEmpty()
        {
            // 无记录时返回空字符串
            var manager = CreateManager();
            var context = manager.GetRecentApprovalContext();
            Assert.Equal(string.Empty, context);
        }

        [Fact]
        public void GetRecentApprovalContext_MultipleRecords_ReturnsMostRecent()
        {
            // 多条记录时返回最近的记录（按倒序）
            var manager = CreateManager();
            var pawn = new Pawn { thingIDNumber = 3 };
            RimMindAPI.ClearPendingRequests();

            // 提交两条审批
            var item1 = CreateAdviceItem("action_a", RiskLevel.High, "reason_a");
            manager.SubmitForApproval(item1, pawn, () => { }, () => { });
            RimMindAPI.PendingRequests[0].callback!("RimMind.Advisor.Request.Approve");

            var item2 = CreateAdviceItem("action_b", RiskLevel.Critical, "reason_b");
            manager.SubmitForApproval(item2, pawn, () => { }, () => { });
            RimMindAPI.PendingRequests[1].callback!("RimMind.Advisor.Request.Reject");

            // 获取最近1条记录
            var context = manager.GetRecentApprovalContext(1);
            Assert.Contains("action_b", context);
            Assert.Contains("REJECTED", context);
            Assert.DoesNotContain("action_a", context);
        }
    }
}
