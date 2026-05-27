using System.Collections.Generic;
using Newtonsoft.Json;
using Xunit;

namespace RimMind.Advisor.Tests
{
    /// <summary>
    /// AdvisorTaskDriver.TryParseContentAsToolCalls 单元测试
    /// 独立测试 Content 回退解析逻辑，验证 advices 格式解析和意图过滤
    /// </summary>
    public class AdvisorTaskDriverContentFallbackTests
    {
        /// <summary>
        /// 支持的意图集合，与 VerseStubs 中 RimMindActionsAPI.GetSupportedIntents 一致
        /// </summary>
        private static readonly HashSet<string> SupportedIntents = new HashSet<string>
        {
            "assign_job", "forbid_area", "social_relax", "add_thought"
        };

        /// <summary>
        /// 复制 AdvisorTaskDriver.TryParseContentAsToolCalls 的纯逻辑
        /// </summary>
        private static List<FallbackToolCall>? TryParseContentAsToolCalls(string content)
        {
            try
            {
                string trimmed = content.Trim();
                if (trimmed.StartsWith("```"))
                {
                    int firstBrace = trimmed.IndexOf('{');
                    int lastBrace = trimmed.LastIndexOf('}');
                    if (firstBrace >= 0 && lastBrace > firstBrace)
                        trimmed = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
                }

                var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(trimmed);
                if (parsed == null || !parsed.ContainsKey("advices")) return null;

                var advicesToken = parsed["advices"];
                string advicesJson = JsonConvert.SerializeObject(advicesToken);
                var advices = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(advicesJson);
                if (advices == null || advices.Count == 0) return null;

                var toolCalls = new List<FallbackToolCall>();
                int idx = 0;

                foreach (var adv in advices)
                {
                    if (!adv.TryGetValue("action", out var actionName) || string.IsNullOrEmpty(actionName)) continue;
                    if (!SupportedIntents.Contains(actionName)) continue;

                    var args = new Dictionary<string, string>();
                    if (adv.TryGetValue("target", out var target) && !string.IsNullOrEmpty(target)) args["target"] = target;
                    if (adv.TryGetValue("param", out var param) && !string.IsNullOrEmpty(param)) args["param"] = param;
                    if (adv.TryGetValue("reason", out var reason) && !string.IsNullOrEmpty(reason)) args["reason"] = reason;

                    toolCalls.Add(new FallbackToolCall
                    {
                        Id = $"fallback_{idx}",
                        Name = actionName,
                        Arguments = JsonConvert.SerializeObject(args),
                    });
                    idx++;
                }

                return toolCalls.Count > 0 ? toolCalls : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 模拟 ClientStructuredToolCall 的本地类型
        /// </summary>
        private class FallbackToolCall
        {
            public string Id = "";
            public string Name = "";
            public string Arguments = "";
        }

        [Fact]
        public void ContentFallback_ValidAdvicesWithSupportedAction_ReturnsToolCalls()
        {
            // 支持的动作返回 ToolCall
            string content = @"{""advices"":[{""action"":""assign_job"",""target"":""Pawn1"",""reason"":""good at crafting""}]}";
            var result = TryParseContentAsToolCalls(content);

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("assign_job", result[0].Name);
            Assert.Equal("fallback_0", result[0].Id);
            Assert.Contains("Pawn1", result[0].Arguments);
            Assert.Contains("good at crafting", result[0].Arguments);
        }

        [Fact]
        public void ContentFallback_UnsupportedAction_FilteredOut()
        {
            // 不支持的动作被过滤，返回 null
            string content = @"{""advices"":[{""action"":""unknown_action"",""target"":""Pawn1""}]}";
            var result = TryParseContentAsToolCalls(content);

            Assert.Null(result);
        }

        [Fact]
        public void ContentFallback_CodeBlockJson_ExtractsAndParses()
        {
            // 代码块包裹的 JSON 正确提取并解析
            string content = "```json\n{\"advices\":[{\"action\":\"social_relax\",\"target\":\"table\"}]}\n```";
            var result = TryParseContentAsToolCalls(content);

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("social_relax", result[0].Name);
        }

        [Fact]
        public void ContentFallback_NoAdvicesKey_ReturnsNull()
        {
            // 无 advices 键返回 null
            string content = @"{""other"":""data""}";
            var result = TryParseContentAsToolCalls(content);

            Assert.Null(result);
        }

        [Fact]
        public void ContentFallback_EmptyAdvices_ReturnsNull()
        {
            // 空 advices 数组返回 null
            string content = @"{""advices"":[]}";
            var result = TryParseContentAsToolCalls(content);

            Assert.Null(result);
        }

        [Fact]
        public void ContentFallback_InvalidJson_ReturnsNull()
        {
            // 无效 JSON 返回 null
            string content = "not json at all";
            var result = TryParseContentAsToolCalls(content);

            Assert.Null(result);
        }

        [Fact]
        public void ContentFallback_PartialFields_OnlyActionRequired()
        {
            // 只有 action 字段是必需的，target/param/reason 可选
            string content = @"{""advices"":[{""action"":""forbid_area""}]}";
            var result = TryParseContentAsToolCalls(content);

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("forbid_area", result[0].Name);
            // 无 target/param/reason 时 Arguments 为空 JSON 对象
            Assert.Equal("{}", result[0].Arguments);
        }

        [Fact]
        public void ContentFallback_MixedSupportedUnsupported_OnlySupportedReturned()
        {
            // 混合支持和不支持的动作，只返回支持的
            string content = @"{""advices"":[
                {""action"":""assign_job"",""target"":""Pawn1""},
                {""action"":""unknown_action"",""target"":""Pawn2""},
                {""action"":""add_thought"",""target"":""Pawn3""}
            ]}";
            var result = TryParseContentAsToolCalls(content);

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("assign_job", result[0].Name);
            Assert.Equal("add_thought", result[1].Name);
        }

        [Fact]
        public void ContentFallback_AdviceWithoutAction_Skipped()
        {
            // 缺少 action 字段的 advice 被跳过
            string content = @"{""advices"":[{""target"":""Pawn1"",""reason"":""no action""}]}";
            var result = TryParseContentAsToolCalls(content);

            Assert.Null(result);
        }

        [Fact]
        public void ContentFallback_EmptyAction_Skipped()
        {
            // action 为空字符串的 advice 被跳过
            string content = @"{""advices"":[{""action"":"""",""target"":""Pawn1""}]}";
            var result = TryParseContentAsToolCalls(content);

            Assert.Null(result);
        }

        [Fact]
        public void ContentFallback_TargetParamReason_AllIncludedInArguments()
        {
            // target、param、reason 全部包含在 Arguments 中
            string content = @"{""advices"":[{""action"":""assign_job"",""target"":""Pawn1"",""param"":""crafting"",""reason"":""skilled""}]}";
            var result = TryParseContentAsToolCalls(content);

            Assert.NotNull(result);
            Assert.Single(result);
            var args = JsonConvert.DeserializeObject<Dictionary<string, string>>(result[0].Arguments);
            Assert.NotNull(args);
            Assert.Equal("Pawn1", args["target"]);
            Assert.Equal("crafting", args["param"]);
            Assert.Equal("skilled", args["reason"]);
        }

        [Fact]
        public void ContentFallback_FallbackIdIncremented()
        {
            // 多个 ToolCall 的 fallback ID 递增
            string content = @"{""advices"":[
                {""action"":""assign_job"",""target"":""Pawn1""},
                {""action"":""social_relax"",""target"":""table""}
            ]}";
            var result = TryParseContentAsToolCalls(content);

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("fallback_0", result[0].Id);
            Assert.Equal("fallback_1", result[1].Id);
        }
    }
}
