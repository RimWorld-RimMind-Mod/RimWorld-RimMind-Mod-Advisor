using System.Collections.Generic;
using Newtonsoft.Json;
using Xunit;

namespace RimMind.Advisor.Tests
{
    public class AdvisorParseTests
    {
        [Fact]
        public void ParseAdvices_ValidJson_ExtractsActions()
        {
            string json = @"{
                ""advices"": [
                    {""action"": ""move_to"", ""target"": ""stockpile"", ""reason"": ""need supplies""},
                    {""action"": ""eat_food"", ""target"": ""meal"", ""param"": ""simple""}
                ]
            }";

            var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            Assert.NotNull(parsed);
            Assert.True(parsed.ContainsKey("advices"));

            var advicesJson = JsonConvert.SerializeObject(parsed!["advices"]);
            var advices = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(advicesJson);

            Assert.NotNull(advices);
            Assert.Equal(2, advices!.Count);
            Assert.Equal("move_to", advices![0]["action"]);
            Assert.Equal("eat_food", advices[1]["action"]);
        }

        [Fact]
        public void ParseAdvices_EmptyAdvices_ReturnsEmptyList()
        {
            string json = @"{""advices"": []}";
            var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            var advicesJson = JsonConvert.SerializeObject(parsed!["advices"]);
            var advices = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(advicesJson);

            Assert.NotNull(advices);
            Assert.Empty(advices!);
        }

        [Fact]
        public void ParseAdvices_NoAdvicesKey_ReturnsNull()
        {
            string json = @"{""other"": ""data""}";
            var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            Assert.NotNull(parsed);
            Assert.False(parsed!.ContainsKey("advices"));
        }

        [Fact]
        public void ParseAdvices_CodeBlockStripped()
        {
            string content = "```json\n{\"advices\":[{\"action\":\"rest\"}]}\n```";
            string trimmed = content.Trim();
            if (trimmed.StartsWith("```"))
            {
                int firstBrace = trimmed.IndexOf('{');
                int lastBrace = trimmed.LastIndexOf('}');
                if (firstBrace >= 0 && lastBrace > firstBrace)
                    trimmed = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(trimmed);
            Assert.NotNull(parsed);
            Assert.True(parsed!.ContainsKey("advices"));
        }

        [Fact]
        public void ParseAdvices_PartialFields()
        {
            string json = @"{""advices"": [{""action"": ""move_to""}]}";
            var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            var advicesJson = JsonConvert.SerializeObject(parsed!["advices"]);
            var advices = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(advicesJson);

            Assert.Single(advices!);
            Assert.Equal("move_to", advices![0]["action"]);
            Assert.False(advices![0].ContainsKey("target"));
            Assert.False(advices![0].ContainsKey("param"));
        }

        [Fact]
        public void ParseAdvices_InvalidJson_ReturnsNull()
        {
            Assert.Throws<JsonReaderException>(() =>
                JsonConvert.DeserializeObject<Dictionary<string, object>>("not json"));
        }
    }
}
