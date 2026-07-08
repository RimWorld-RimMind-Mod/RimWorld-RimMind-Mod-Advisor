using RimMind.Advisor;
using Xunit;

namespace RimMind.Advisor.Tests
{
    public class InstantHintRegistryTests
    {
        [Theory]
        [InlineData("force_rest")]
        [InlineData("social_relax")]
        [InlineData("eat_food")]
        [InlineData("tend_pawn")]
        [InlineData("rescue_pawn")]
        [InlineData("inspire_work")]
        [InlineData("inspire_shoot")]
        [InlineData("inspire_trade")]
        [InlineData("move_to")]
        public void IsKnownAction_RegisteredAction_ReturnsTrue(string action)
        {
            Assert.True(InstantHintRegistry.IsKnownAction(action));
        }

        [Fact]
        public void IsKnownAction_UnknownAction_ReturnsFalse()
        {
            Assert.False(InstantHintRegistry.IsKnownAction("nonexistent_action"));
        }

        [Fact]
        public void GetKnownActions_ReturnsAllNine()
        {
            var actions = InstantHintRegistry.GetKnownActions();
            Assert.Equal(9, actions.Count);
        }
    }
}
