using System.Collections.Generic;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using Xunit;

namespace RimMind.Advisor.Tests
{
    public class AdvisorPromptHelperTests
    {
        [Theory]
        [InlineData(RiskLevel.Low, "RimMind.Advisor.Prompt.Risk.Low")]
        [InlineData(RiskLevel.Medium, "RimMind.Advisor.Prompt.Risk.Medium")]
        [InlineData(RiskLevel.High, "RimMind.Advisor.Prompt.Risk.High")]
        [InlineData(RiskLevel.Critical, "RimMind.Advisor.Prompt.Risk.Critical")]
        public void RiskTag_ReturnsExpectedKey(RiskLevel risk, string expectedKey)
        {
            var result = AdvisorPromptHelper.RiskTag(risk);
            Assert.Equal(expectedKey, result);
        }

        [Fact]
        public void RiskTag_InvalidValue_ReturnsEmpty()
        {
            var result = AdvisorPromptHelper.RiskTag((RiskLevel)999);
            Assert.Equal("", result);
        }

        [Fact]
        public void FindLastSystemIndex_EmptyList_ReturnsMinusOne()
        {
            var messages = new List<ChatMessage>();
            var result = AdvisorPromptHelper.FindLastSystemIndex(messages);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void FindLastSystemIndex_NoSystemMessages_ReturnsMinusOne()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user" },
                new ChatMessage { Role = "assistant" },
            };
            var result = AdvisorPromptHelper.FindLastSystemIndex(messages);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void FindLastSystemIndex_SystemPresent_ReturnsLastSystemIndex()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system" },
                new ChatMessage { Role = "user" },
                new ChatMessage { Role = "system" },
                new ChatMessage { Role = "assistant" },
            };
            var result = AdvisorPromptHelper.FindLastSystemIndex(messages);
            Assert.Equal(2, result);
        }

        [Fact]
        public void FindLastSystemIndex_OnlySystem_ReturnsLastIndex()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system" },
                new ChatMessage { Role = "system" },
            };
            var result = AdvisorPromptHelper.FindLastSystemIndex(messages);
            Assert.Equal(1, result);
        }

        [Fact]
        public void FindLastSystemIndex_SystemAtStart_ReturnsZero()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system" },
                new ChatMessage { Role = "user" },
            };
            var result = AdvisorPromptHelper.FindLastSystemIndex(messages);
            Assert.Equal(0, result);
        }
    }
}
