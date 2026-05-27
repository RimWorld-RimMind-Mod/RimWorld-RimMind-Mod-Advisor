using System;
using System.Collections.Generic;
using RimMind.Advisor.Data;
using RimWorld.Planet;
using Verse;
using Xunit;

namespace RimMind.Advisor.Tests
{
    /// <summary>
    /// AdvisorHistoryStore 单元测试：记录存储、逐 Pawn 限制、全局限制
    /// </summary>
    public class AdvisorHistoryStoreTests
    {
        /// <summary>
        /// 辅助方法：创建新的 AdvisorHistoryStore 实例
        /// </summary>
        private static AdvisorHistoryStore CreateStore()
        {
            return new AdvisorHistoryStore(new World());
        }

        /// <summary>
        /// 辅助方法：创建 Pawn 桩
        /// </summary>
        private static Pawn CreatePawn(int id)
        {
            return new Pawn { thingIDNumber = id };
        }

        /// <summary>
        /// 辅助方法：创建记录
        /// </summary>
        private static AdvisorRequestRecord CreateRecord(string action = "test_action", string result = "executed", int tick = 0)
        {
            return new AdvisorRequestRecord
            {
                action = action,
                reason = "test_reason",
                result = result,
                tick = tick
            };
        }

        [Fact]
        public void AddRecord_SingleRecord_StoredInPawnList()
        {
            // 单条记录正确存储到对应 Pawn 列表
            var store = CreateStore();
            var pawn = CreatePawn(1);
            var record = CreateRecord("assign_job", "executed", 100);

            store.AddRecord(pawn, record);

            var records = store.GetRecords(pawn);
            Assert.Single(records);
            Assert.Equal("assign_job", records[0].action);
            Assert.Equal("executed", records[0].result);
            Assert.Equal(100, records[0].tick);
        }

        [Fact]
        public void AddRecord_MultipleRecordsSamePawn_AllStored()
        {
            // 同一小人多次记录全部存储
            var store = CreateStore();
            var pawn = CreatePawn(2);

            for (int i = 0; i < 5; i++)
            {
                store.AddRecord(pawn, CreateRecord($"action_{i}", "executed", 100 + i));
            }

            var records = store.GetRecords(pawn);
            Assert.Equal(5, records.Count);
            Assert.Equal("action_0", records[0].action);
            Assert.Equal("action_4", records[4].action);
        }

        [Fact]
        public void AddRecord_PawnLimit50_EvictsOldest()
        {
            // 每 Pawn 限 50 条，超过时淘汰最旧记录
            var store = CreateStore();
            var pawn = CreatePawn(3);

            // 添加 55 条记录
            for (int i = 0; i < 55; i++)
            {
                store.AddRecord(pawn, CreateRecord($"action_{i}", "executed", 100 + i));
            }

            var records = store.GetRecords(pawn);
            // 应只剩 50 条，最早的 5 条被淘汰
            Assert.Equal(50, records.Count);
            // 最旧的是 action_5（action_0~4 被淘汰）
            Assert.Equal("action_5", records[0].action);
            // 最新的是 action_54
            Assert.Equal("action_54", records[49].action);
        }

        [Fact]
        public void AddRecord_GlobalLimit200_EvictsOldest()
        {
            // 全局限 200 条，超过时淘汰最旧记录
            var store = CreateStore();

            // 用 4 个 Pawn 各添加 55 条 = 220 条全局记录
            for (int pawnId = 10; pawnId < 14; pawnId++)
            {
                var pawn = CreatePawn(pawnId);
                for (int i = 0; i < 55; i++)
                {
                    store.AddRecord(pawn, CreateRecord($"p{pawnId}_a{i}", "executed", 100 + pawnId * 1000 + i));
                }
            }

            var globalLog = store.GlobalLog;
            // 应只剩 200 条
            Assert.Equal(200, globalLog.Count);
        }

        [Fact]
        public void GetRecords_NewPawn_ReturnsEmptyList()
        {
            // 新 Pawn 返回空列表
            var store = CreateStore();
            var pawn = CreatePawn(99);

            var records = store.GetRecords(pawn);
            Assert.NotNull(records);
            Assert.Empty(records);
        }

        [Fact]
        public void GetRecords_ExistingPawn_ReturnsSameList()
        {
            // 同一 Pawn 多次获取返回同一列表引用
            var store = CreateStore();
            var pawn = CreatePawn(5);

            var records1 = store.GetRecords(pawn);
            var records2 = store.GetRecords(pawn);

            Assert.Same(records1, records2);
        }

        [Fact]
        public void GlobalLog_ReflectsAllRecords()
        {
            // 全局日志反映所有 Pawn 的记录
            var store = CreateStore();
            var pawn1 = CreatePawn(20);
            var pawn2 = CreatePawn(21);

            store.AddRecord(pawn1, CreateRecord("action_p1", "executed", 100));
            store.AddRecord(pawn2, CreateRecord("action_p2", "executed", 200));

            var globalLog = store.GlobalLog;
            Assert.Equal(2, globalLog.Count);
            Assert.Equal("action_p1", globalLog[0].action);
            Assert.Equal("action_p2", globalLog[1].action);
        }

        [Fact]
        public void AddRecord_MultiplePawns_TrackedSeparately()
        {
            // 不同 Pawn 的记录独立追踪
            var store = CreateStore();
            var pawn1 = CreatePawn(30);
            var pawn2 = CreatePawn(31);

            store.AddRecord(pawn1, CreateRecord("pawn1_action", "executed", 100));
            store.AddRecord(pawn2, CreateRecord("pawn2_action", "executed", 200));
            store.AddRecord(pawn1, CreateRecord("pawn1_action2", "executed", 300));

            var records1 = store.GetRecords(pawn1);
            var records2 = store.GetRecords(pawn2);

            Assert.Equal(2, records1.Count);
            Assert.Single(records2);
            Assert.Equal("pawn1_action", records1[0].action);
            Assert.Equal("pawn1_action2", records1[1].action);
            Assert.Equal("pawn2_action", records2[0].action);
        }
    }
}
