using System.Collections.Generic;
using Newtonsoft.Json;
using RimMind.Domain.Llm;
using Xunit;

namespace RimMind.Advisor.Tests
{
    /// <summary>
    /// AdvisorTaskDriver.TryParseToolCalls 单元测试
    /// 独立测试 JSON 解析逻辑，验证 ToolCall 解析的正确性
    /// </summary>
    public class AdvisorTaskDriverTryParseToolCallsTests
    {
        /// <summary>
        /// 复制 AdvisorTaskDriver.TryParseToolCalls 的纯逻辑
        /// </summary>
        private static bool TryParseToolCalls(string toolCallsJson, out List<StructuredToolCall> toolCalls)
        {
            toolCalls = new List<StructuredToolCall>();
            try
            {
                var parsed = JsonConvert.DeserializeObject<List<StructuredToolCall>>(toolCallsJson);
                if (parsed != null) toolCalls = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        [Fact]
        public void TryParseToolCalls_ValidJson_ReturnsTrue()
        {
            // 有效 JSON 返回 true 并正确解析
            string json = @"[{""Id"":""call_1"",""Name"":""assign_job"",""Arguments"":""{\""target\"":\""Pawn1\""}""}]";
            bool result = TryParseToolCalls(json, out var toolCalls);

            Assert.True(result);
            Assert.Single(toolCalls);
            Assert.Equal("call_1", toolCalls[0].Id);
            Assert.Equal("assign_job", toolCalls[0].Name);
        }

        [Fact]
        public void TryParseToolCalls_InvalidJson_ReturnsFalse()
        {
            // 无效 JSON 返回 false，toolCalls 为空列表
            string json = "not valid json";
            bool result = TryParseToolCalls(json, out var toolCalls);

            Assert.False(result);
            Assert.Empty(toolCalls);
        }

        [Fact]
        public void TryParseToolCalls_EmptyArray_ReturnsTrueAndEmptyList()
        {
            // 空数组返回 true 但列表为空
            string json = "[]";
            bool result = TryParseToolCalls(json, out var toolCalls);

            Assert.True(result);
            Assert.Empty(toolCalls);
        }

        [Fact]
        public void TryParseToolCalls_MultipleCalls_ReturnsAll()
        {
            // 多个 ToolCall 全部返回
            string json = @"[
                {""Id"":""call_1"",""Name"":""assign_job"",""Arguments"":""{}""},
                {""Id"":""call_2"",""Name"":""forbid_area"",""Arguments"":""{}""},
                {""Id"":""call_3"",""Name"":""social_relax"",""Arguments"":""{}""}
            ]";
            bool result = TryParseToolCalls(json, out var toolCalls);

            Assert.True(result);
            Assert.Equal(3, toolCalls.Count);
            Assert.Equal("assign_job", toolCalls[0].Name);
            Assert.Equal("forbid_area", toolCalls[1].Name);
            Assert.Equal("social_relax", toolCalls[2].Name);
        }

        [Fact]
        public void TryParseToolCalls_MalformedJson_ReturnsFalse()
        {
            // 畸形 JSON（缺少括号）返回 false
            string json = @"[{""Id"":""call_1"",""Name"":""assign_job""";
            bool result = TryParseToolCalls(json, out var toolCalls);

            Assert.False(result);
            Assert.Empty(toolCalls);
        }

        [Fact]
        public void TryParseToolCalls_JsonWithNullFields_ParsedCorrectly()
        {
            // JSON 中字段为 null 时正确解析
            string json = @"[{""Id"":""call_1"",""Name"":""assign_job"",""Arguments"":null}]";
            bool result = TryParseToolCalls(json, out var toolCalls);

            Assert.True(result);
            Assert.Single(toolCalls);
            Assert.Equal("assign_job", toolCalls[0].Name);
            Assert.Null(toolCalls[0].Arguments);
        }

        [Fact]
        public void TryParseToolCalls_EmptyString_ReturnsTrueWithEmptyList()
        {
            // 空字符串反序列化返回 null，不抛异常，方法返回 true 但列表为空
            bool result = TryParseToolCalls("", out var toolCalls);
            Assert.True(result);
            Assert.Empty(toolCalls);
        }

        [Fact]
        public void TryParseToolCalls_SingleCallWithComplexArguments_ParsedCorrectly()
        {
            // 包含复杂参数的 ToolCall 正确解析
            string json = @"[{""Id"":""call_42"",""Name"":""assign_job"",""Arguments"":""{\""target\"":\""Pawn1\"",\""param\"":\""crafting\"",\""reason\"":\""good at crafting\""}""}]";
            bool result = TryParseToolCalls(json, out var toolCalls);

            Assert.True(result);
            Assert.Single(toolCalls);
            Assert.Equal("call_42", toolCalls[0].Id);
            Assert.Contains("Pawn1", toolCalls[0].Arguments);
            Assert.Contains("crafting", toolCalls[0].Arguments);
        }
    }
}
