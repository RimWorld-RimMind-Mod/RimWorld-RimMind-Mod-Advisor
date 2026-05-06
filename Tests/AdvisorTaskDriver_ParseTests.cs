using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace RimMind.Advisor.Tests
{
    public class AdvisorTaskDriverParseTests
    {
        private static List<StructuredAdvice>? TryParseContentAsToolCalls(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;

            string jsonStr = content.Trim();
            if (jsonStr.StartsWith("```"))
            {
                int start = jsonStr.IndexOf('\n') + 1;
                int end = jsonStr.LastIndexOf("```");
                if (start > 0 && end > start)
                    jsonStr = jsonStr[start..end].Trim();
            }

            if (!jsonStr.StartsWith("{")) return null;

            try
            {
                var obj = JObject.Parse(jsonStr);
                var advices = obj["advices"];
                if (advices == null || !advices.HasValues) return null;

                var result = new List<StructuredAdvice>();
                foreach (var adv in advices)
                {
                    string? action = adv["action"]?.ToString();
                    if (string.IsNullOrEmpty(action)) continue;

                    result.Add(new StructuredAdvice
                    {
                        action = action,
                        target = adv["target"]?.ToString(),
                        reason = adv["reason"]?.ToString()
                    });
                }
                return result.Count > 0 ? result : null;
            }
            catch { return null; }
        }

        [Fact]
        public void Parse_ValidAdvices_ReturnsAll()
        {
            string content = "{\"advices\":[{\"action\":\"assign_job\",\"target\":\"Pawn1\",\"reason\":\"good at crafting\"}]}";
            var result = TryParseContentAsToolCalls(content);
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("assign_job", result[0].action);
            Assert.Equal("Pawn1", result[0].target);
        }

        [Fact]
        public void Parse_NoAdvicesKey_ReturnsNull()
        {
            string content = "{\"something\":\"else\"}";
            var result = TryParseContentAsToolCalls(content);
            Assert.Null(result);
        }

        [Fact]
        public void Parse_EmptyAdvices_ReturnsNull()
        {
            string content = "{\"advices\":[]}";
            var result = TryParseContentAsToolCalls(content);
            Assert.Null(result);
        }

        [Fact]
        public void Parse_CodeBlockJson_ExtractsAndParses()
        {
            string content = "```json\n{\"advices\":[{\"action\":\"assign_job\",\"target\":\"Pawn1\"}]}\n```";
            var result = TryParseContentAsToolCalls(content);
            Assert.NotNull(result);
            Assert.Single(result);
        }

        [Fact]
        public void Parse_InvalidJson_ReturnsNull()
        {
            string content = "not json at all";
            var result = TryParseContentAsToolCalls(content);
            Assert.Null(result);
        }

        [Fact]
        public void Parse_AdviceWithoutAction_Skipped()
        {
            string content = "{\"advices\":[{\"target\":\"Pawn1\",\"reason\":\"no action field\"}]}";
            var result = TryParseContentAsToolCalls(content);
            Assert.Null(result);
        }

        [Fact]
        public void Parse_MultipleAdvices_ReturnsAll()
        {
            string content = "{\"advices\":[{\"action\":\"assign_job\",\"target\":\"Pawn1\"},{\"action\":\"forbid_area\",\"target\":\"Zone1\"}]}";
            var result = TryParseContentAsToolCalls(content);
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void Parse_NullContent_ReturnsNull()
        {
            var result = TryParseContentAsToolCalls(null!);
            Assert.Null(result);
        }

        [Fact]
        public void Parse_EmptyContent_ReturnsNull()
        {
            var result = TryParseContentAsToolCalls("");
            Assert.Null(result);
        }

        private class StructuredAdvice
        {
            public string action = "";
            public string? target;
            public string? reason;
        }
    }
}
