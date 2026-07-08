using RimMind.Advisor.Advisor;
using RimMind.Advisor.Settings;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;
using Xunit;

namespace RimMind.Advisor.Tests
{
    public class AdvisorApprovalGateAdapterTests
    {
        private static AdvisorApprovalGateAdapter CreateSut(
            bool enableRiskApproval = true,
            RiskLevel autoBlockRiskLevel = RiskLevel.High)
        {
            var settings = new RimMindAdvisorSettings
            {
                enableRiskApproval = enableRiskApproval,
                autoBlockRiskLevel = autoBlockRiskLevel
            };
            var approvalManager = new ApprovalManager(settings);
            return new AdvisorApprovalGateAdapter(settings, approvalManager);
        }

        [Fact]
        public void RequiresApproval_NullDecision_ReturnsFalse()
        {
            var sut = CreateSut();
            Assert.False(sut.RequiresApproval(null!, RiskLevel.High));
        }

        [Fact]
        public void RequiresApproval_RiskDisabled_ReturnsFalse()
        {
            var sut = CreateSut(enableRiskApproval: false);
            var decision = new AgentDecision(ActionIntent: "test.action");
            Assert.False(sut.RequiresApproval(decision, RiskLevel.Critical));
        }

        [Fact]
        public void RequiresApproval_RiskBelowThreshold_ReturnsFalse()
        {
            var sut = CreateSut(enableRiskApproval: true, autoBlockRiskLevel: RiskLevel.High);
            var decision = new AgentDecision(ActionIntent: "test.action");
            Assert.False(sut.RequiresApproval(decision, RiskLevel.Medium));
        }

        [Fact]
        public void RequiresApproval_RiskAtThreshold_ReturnsTrue()
        {
            var sut = CreateSut(enableRiskApproval: true, autoBlockRiskLevel: RiskLevel.High);
            var decision = new AgentDecision(ActionIntent: "test.action");
            Assert.True(sut.RequiresApproval(decision, RiskLevel.High));
        }

        [Fact]
        public void RequiresApproval_RiskAboveThreshold_ReturnsTrue()
        {
            var sut = CreateSut(enableRiskApproval: true, autoBlockRiskLevel: RiskLevel.High);
            var decision = new AgentDecision(ActionIntent: "test.action");
            Assert.True(sut.RequiresApproval(decision, RiskLevel.Critical));
        }

        [Fact]
        public void RequiresApproval_IsRequestType_ReturnsTrue_EvenWhenRiskLow()
        {
            var sut = CreateSut(enableRiskApproval: true, autoBlockRiskLevel: RiskLevel.Critical);
            var decision = new AgentDecision(ActionIntent: "test.action", Param: "{\"request_type\":\"system\"}");
            Assert.True(sut.RequiresApproval(decision, RiskLevel.Low));
        }

        [Fact]
        public void RequiresApproval_IsRequestType_DisabledRisk_ReturnsTrue()
        {
            var sut = CreateSut(enableRiskApproval: false);
            var decision = new AgentDecision(ActionIntent: "test.action", Param: "{\"request_type\":\"system\"}");
            Assert.True(sut.RequiresApproval(decision, RiskLevel.Low));
        }

        [Fact]
        public void RequiresApproval_NoRequestTypeParam_ReturnsFalse_WhenRiskLow()
        {
            var sut = CreateSut(enableRiskApproval: true, autoBlockRiskLevel: RiskLevel.High);
            var decision = new AgentDecision(ActionIntent: "test.action", Param: "{\"foo\":\"bar\"}");
            Assert.False(sut.RequiresApproval(decision, RiskLevel.Low));
        }
    }
}
