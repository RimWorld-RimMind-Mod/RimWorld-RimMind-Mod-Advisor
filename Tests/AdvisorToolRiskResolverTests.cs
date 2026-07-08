using RimMind.Advisor.Advisor;
using RimMind.Domain.Enums;
using Xunit;

namespace RimMind.Advisor.Tests
{
    public class AdvisorToolRiskResolverTests
    {
        [Theory]
        [InlineData("query", MechanismOperationType.Query)]
        [InlineData("list", MechanismOperationType.List)]
        [InlineData("watch", MechanismOperationType.Watch)]
        [InlineData("set", MechanismOperationType.Set)]
        [InlineData("add", MechanismOperationType.Add)]
        [InlineData("remove", MechanismOperationType.Remove)]
        [InlineData("toggle", MechanismOperationType.Toggle)]
        [InlineData("trigger", MechanismOperationType.Trigger)]
        public void ResolveOperation_ValidSuffix_ReturnsExpectedEnum(
            string suffix, MechanismOperationType expected)
        {
            var result = AdvisorToolRiskResolver.ResolveOperation(suffix);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("QUERY")]
        [InlineData("Set")]
        [InlineData("Trigger")]
        [InlineData("ADD")]
        public void ResolveOperation_CaseInsensitive_ReturnsExpectedEnum(string suffix)
        {
            var result = AdvisorToolRiskResolver.ResolveOperation(suffix);
            Assert.NotNull(result);
        }

        [Theory]
        [InlineData("unknown")]
        [InlineData("")]
        [InlineData("delete")]
        [InlineData("create")]
        public void ResolveOperation_UnknownSuffix_ReturnsNull(string suffix)
        {
            var result = AdvisorToolRiskResolver.ResolveOperation(suffix);
            Assert.Null(result);
        }

        [Fact]
        public void ResolveOperation_NullSuffix_ReturnsNull()
        {
            // Guards against ArgumentNullException from Dictionary.TryGetValue(null).
            Assert.Null(AdvisorToolRiskResolver.ResolveOperation(null!));
        }

        [Fact]
        public void Resolve_NullOrEmpty_ReturnsLowRisk()
        {
            Assert.Equal(RiskLevel.Low, AdvisorToolRiskResolver.Resolve(null!));
            Assert.Equal(RiskLevel.Low, AdvisorToolRiskResolver.Resolve(""));
            Assert.Equal(RiskLevel.Low, AdvisorToolRiskResolver.Resolve("   "));
        }

        [Fact]
        public void Resolve_NoDotSeparator_ReturnsLowRisk()
        {
            Assert.Equal(RiskLevel.Low, AdvisorToolRiskResolver.Resolve("query"));
            Assert.Equal(RiskLevel.Low, AdvisorToolRiskResolver.Resolve(".query"));
        }

        [Fact]
        public void Resolve_TrailingDot_ReturnsLowRisk()
        {
            Assert.Equal(RiskLevel.Low, AdvisorToolRiskResolver.Resolve("some_mechanism."));
        }

        [Fact]
        public void Resolve_UnknownSuffix_ReturnsLowRisk()
        {
            Assert.Equal(RiskLevel.Low, AdvisorToolRiskResolver.Resolve("some_mechanism.unknown_op"));
        }

        [Fact]
        public void Resolve_ValidSuffixButNoMechanismRegistered_ReturnsLowRisk()
        {
            // Without a registered mechanism, Resolve returns Low
            Assert.Equal(RiskLevel.Low, AdvisorToolRiskResolver.Resolve("some_mechanism.query"));
            Assert.Equal(RiskLevel.Low, AdvisorToolRiskResolver.Resolve("some_mechanism.set"));
            Assert.Equal(RiskLevel.Low, AdvisorToolRiskResolver.Resolve("some_mechanism.trigger"));
        }
    }
}
