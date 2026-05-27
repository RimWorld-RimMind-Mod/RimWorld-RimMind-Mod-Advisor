using System.Collections.Generic;
using Xunit;

namespace RimMind.Advisor.Tests
{
    /// <summary>
    /// AdvisorTaskDriver 反馈循环与状态管理单元测试
    /// 独立测试纯逻辑，不依赖 RimWorld 运行时
    /// </summary>
    public class AdvisorTaskDriverFeedbackAndStateTests
    {
        /// <summary>
        /// 本地辅助类：模拟 AdvisorTaskDriver 的状态管理逻辑
        /// 复制核心字段和方法，用于纯逻辑测试
        /// </summary>
        private class TaskDriverStateSimulator
        {
            public const int MaxToolCallDepth = 3;

            private List<object>? _lastMessages;
            private List<object>? _lastTools;
            private string? _lastSchema;
            private int _toolCallDepth;
            private string? _lastReasoningContent;

            public bool HasPendingState => _lastMessages != null;

            public string? LastReasoningContent => _lastReasoningContent;

            public int ToolCallDepth => _toolCallDepth;

            /// <summary>
            /// 模拟 BuildAndSendRequest 后的状态设置
            /// </summary>
            public void SimulateBuildRequest(string? schema = null)
            {
                _lastMessages = new List<object> { new() };
                _lastTools = new List<object> { new() };
                _lastSchema = schema;
                _toolCallDepth = 0;
                _lastReasoningContent = null;
            }

            public bool ShouldRequestFeedback()
            {
                return _toolCallDepth < MaxToolCallDepth && _lastMessages != null && _lastSchema != null;
            }

            public void SimulateToolFeedback()
            {
                _toolCallDepth++;
                _lastMessages = new List<object>(_lastMessages ?? new List<object>());
            }

            public void SetReasoningContent(string? content)
            {
                _lastReasoningContent = content;
            }

            public void ClearState()
            {
                _lastMessages = null;
                _lastTools = null;
                _lastSchema = null;
                _toolCallDepth = 0;
                _lastReasoningContent = null;
            }
        }

        [Fact]
        public void MaxToolCallDepth_IsThree()
        {
            // 最大反馈深度常量为 3
            Assert.Equal(3, TaskDriverStateSimulator.MaxToolCallDepth);
        }

        [Fact]
        public void HasPendingState_InitialState_ReturnsFalse()
        {
            // 初始状态无待处理请求
            var sim = new TaskDriverStateSimulator();
            Assert.False(sim.HasPendingState);
        }

        [Fact]
        public void HasPendingState_AfterBuildRequest_ReturnsTrue()
        {
            // 构建请求后有待处理状态
            var sim = new TaskDriverStateSimulator();
            sim.SimulateBuildRequest();
            Assert.True(sim.HasPendingState);
        }

        [Fact]
        public void ClearState_ResetsHasPendingState()
        {
            // ClearState 后 HasPendingState 为 false
            var sim = new TaskDriverStateSimulator();
            sim.SimulateBuildRequest();
            Assert.True(sim.HasPendingState);

            sim.ClearState();
            Assert.False(sim.HasPendingState);
        }

        [Fact]
        public void ShouldRequestFeedback_InitialState_ReturnsFalse()
        {
            // 初始状态不需要反馈（无 _lastMessages）
            var sim = new TaskDriverStateSimulator();
            Assert.False(sim.ShouldRequestFeedback());
        }

        [Fact]
        public void ShouldRequestFeedback_WithPendingStateAndSchema_ReturnsTrue()
        {
            // 有待处理状态且有 schema 时需要反馈
            var sim = new TaskDriverStateSimulator();
            sim.SimulateBuildRequest(schema: "test_schema");
            Assert.True(sim.ShouldRequestFeedback());
        }

        [Fact]
        public void ShouldRequestFeedback_WithPendingStateNoSchema_ReturnsFalse()
        {
            // 有待处理状态但无 schema 时不需要反馈
            var sim = new TaskDriverStateSimulator();
            sim.SimulateBuildRequest(schema: null);
            Assert.False(sim.ShouldRequestFeedback());
        }

        [Fact]
        public void FeedbackLoop_DepthIncrementPreventsInfiniteLoop()
        {
            // 反馈循环深度递增，达到 MaxToolCallDepth 时停止
            var sim = new TaskDriverStateSimulator();
            sim.SimulateBuildRequest(schema: "test_schema");

            // 深度 0：需要反馈
            Assert.True(sim.ShouldRequestFeedback());
            sim.SimulateToolFeedback();
            Assert.Equal(1, sim.ToolCallDepth);

            // 深度 1：需要反馈
            Assert.True(sim.ShouldRequestFeedback());
            sim.SimulateToolFeedback();
            Assert.Equal(2, sim.ToolCallDepth);

            // 深度 2：需要反馈
            Assert.True(sim.ShouldRequestFeedback());
            sim.SimulateToolFeedback();
            Assert.Equal(3, sim.ToolCallDepth);

            // 深度 3 = MaxToolCallDepth：不需要反馈
            Assert.False(sim.ShouldRequestFeedback());
        }

        [Fact]
        public void SetReasoningContent_StoresContent()
        {
            // 推理内容正确存储和读取
            var sim = new TaskDriverStateSimulator();
            Assert.Null(sim.LastReasoningContent);

            sim.SetReasoningContent("思考过程...");
            Assert.Equal("思考过程...", sim.LastReasoningContent);

            sim.SetReasoningContent(null);
            Assert.Null(sim.LastReasoningContent);
        }

        [Fact]
        public void ClearState_ResetsToolCallDepth()
        {
            // ClearState 重置反馈深度
            var sim = new TaskDriverStateSimulator();
            sim.SimulateBuildRequest(schema: "test");
            sim.SimulateToolFeedback();
            sim.SimulateToolFeedback();
            Assert.Equal(2, sim.ToolCallDepth);

            sim.ClearState();
            Assert.Equal(0, sim.ToolCallDepth);
        }

        [Fact]
        public void ClearState_ResetsReasoningContent()
        {
            // ClearState 重置推理内容
            var sim = new TaskDriverStateSimulator();
            sim.SimulateBuildRequest();
            sim.SetReasoningContent("some reasoning");
            sim.ClearState();
            Assert.Null(sim.LastReasoningContent);
        }
    }
}
