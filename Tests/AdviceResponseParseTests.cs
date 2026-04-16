using Newtonsoft.Json;
using RimMind.Advisor;
using Xunit;

// 测试 AdviceBatch 纯 JSON 解析，不依赖 RimWorld
namespace RimMind.Advisor.Tests
{
    public class AdviceBatchParseTests
    {
        // ── 1. 单条建议，字段完整 ──────────────────────────────────────────────

        [Fact]
        public void Parse_SingleAdvice_FullFields()
        {
            string json = "{\"advices\":[{\"action\":\"assign_work\",\"pawn\":\"Alice\",\"target\":null,\"param\":\"Mining\",\"reason\":\"矿石不足\"}]}";

            var batch = JsonConvert.DeserializeObject<AdviceBatch>(json);

            Assert.NotNull(batch);
            Assert.Single(batch!.advices);
            Assert.Equal("assign_work", batch.advices[0].action);
            Assert.Equal("Alice",       batch.advices[0].pawn);
            Assert.Null (               batch.advices[0].target);
            Assert.Equal("Mining",      batch.advices[0].param);
            Assert.Equal("矿石不足",    batch.advices[0].reason);
        }

        // ── 2. 多条建议（序列模式）────────────────────────────────────────────

        [Fact]
        public void Parse_MultipleAdvices_ReturnsAllInOrder()
        {
            string json = "{\"advices\":[" +
                          "{\"action\":\"move_to\",\"param\":\"45,32\",\"reason\":\"先到矿区\"}," +
                          "{\"action\":\"assign_work\",\"param\":\"Mining\",\"reason\":\"开始采矿\"}" +
                          "]}";

            var batch = JsonConvert.DeserializeObject<AdviceBatch>(json);

            Assert.NotNull(batch);
            Assert.Equal(2, batch!.advices.Count);
            Assert.Equal("move_to",     batch.advices[0].action);
            Assert.Equal("45,32",       batch.advices[0].param);
            Assert.Equal("assign_work", batch.advices[1].action);
            Assert.Equal("Mining",      batch.advices[1].param);
        }

        // ── 3. 多 Pawn 批量模式 ────────────────────────────────────────────────

        [Fact]
        public void Parse_MultiPawn_PawnFieldSet()
        {
            string json = "{\"advices\":[" +
                          "{\"pawn\":\"Alice\",\"action\":\"assign_work\",\"param\":\"Mining\",\"reason\":\"矿石不足\"}," +
                          "{\"pawn\":\"Bob\",\"action\":\"force_rest\",\"param\":null,\"reason\":\"体力仅30%\"}," +
                          "{\"pawn\":\"Charlie\",\"action\":\"tend_pawn\",\"target\":\"Diana\",\"reason\":\"Diana受伤\"}" +
                          "]}";

            var batch = JsonConvert.DeserializeObject<AdviceBatch>(json);

            Assert.NotNull(batch);
            Assert.Equal(3, batch!.advices.Count);
            Assert.Equal("Alice",   batch.advices[0].pawn);
            Assert.Equal("Bob",     batch.advices[1].pawn);
            Assert.Equal("Charlie", batch.advices[2].pawn);
            Assert.Equal("Diana",   batch.advices[2].target);
        }

        // ── 4. advices 为空数组 ────────────────────────────────────────────────

        [Fact]
        public void Parse_EmptyAdvices_ReturnsEmptyList()
        {
            string json = "{\"advices\":[]}";
            var batch = JsonConvert.DeserializeObject<AdviceBatch>(json);

            Assert.NotNull(batch);
            Assert.Empty(batch!.advices);
        }

        // ── 5. JSON 格式错误 → 抛异常 ─────────────────────────────────────────

        [Fact]
        public void Parse_MalformedJson_ThrowsException()
        {
            string json = "{not valid json}";
            Assert.Throws<JsonReaderException>(
                () => JsonConvert.DeserializeObject<AdviceBatch>(json));
        }

        // ── 6. 可选字段缺失时为 null / 默认值 ──────────────────────────────────

        [Fact]
        public void Parse_OnlyActionField_OptionalFieldsNull()
        {
            string json = "{\"advices\":[{\"action\":\"social_relax\"}]}";
            var batch = JsonConvert.DeserializeObject<AdviceBatch>(json);

            Assert.NotNull(batch);
            Assert.Single(batch!.advices);
            Assert.Equal("social_relax", batch.advices[0].action);
            Assert.Null(batch.advices[0].pawn);
            Assert.Null(batch.advices[0].target);
            Assert.Null(batch.advices[0].param);
            Assert.Null(batch.advices[0].reason);
        }

        // ── 7. advices 字段缺失时列表不为 null（默认初始化）─────────────────

        [Fact]
        public void Parse_MissingAdvicesField_DefaultsToEmptyList()
        {
            string json = "{}";
            var batch = JsonConvert.DeserializeObject<AdviceBatch>(json);

            Assert.NotNull(batch);
            // List<AdviceItem> 字段默认初始化为 new List<>()，不为 null
            Assert.NotNull(batch!.advices);
            Assert.Empty(batch.advices);
        }
    }
}
