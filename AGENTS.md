# AGENTS.md — RimMind-Advisor

Advisor 在小人空闲或心情低落时请求角色化建议，通过 Core ToolCall 执行动作，并将直接执行、玩家审批和反馈收敛到一个有界周期。

## Start here

先读 `Source/Advisor/README.md`。通常只需要继续打开：

- `Source/Comps/CompAIAdvisor.cs`：Verse 适配与持久化开关。
- `Source/Advisor/AdvisorGameComponent.cs`：节流扫描和触发。
- `Source/Advisor/AdvisorCycleCoordinator.cs`：请求周期与唯一终态。
- `Source/Advisor/AdvisorTaskDriver.cs`：请求和反馈消息构建。
- `Source/Advisor/AdvisorRequestAugmentationFactory.cs`：初始 Decision envelope 与可选提示词增量。
- `Source/Advisor/AdvisorRequestCycleState.cs`：纯审批/反馈状态机。
- `Source/Advisor/AdvisorApprovalPolicy.cs`、`ApprovalManager.cs`：审批条件与 Core RequestEntry 终态回调。
- `Source/RimMindAdvisorMod.cs`：组合根与设置入口。
- `Source/Advisor/AdvisorProviderRegistrar.cs`：Context Provider 注册与工具列表格式化。
- `Source/Settings/AdvisorSettingsDrawer.cs`：原生/Core 设置页共享绘制实现。

## Main flow

```text
AdvisorGameComponent
  → CompAIAdvisor trigger checks
  → AdvisorCycleCoordinator
  → AdvisorTaskDriver
  → RimMindAPI.Request.Send
  → approval/direct execution
  → one aggregated feedback batch
  → terminal cleanup
```

## Public boundary

Advisor 只通过 `RimMindAPI` 使用 Core 请求、ToolRegistry、ContextKey 和审批入口。Actions 不是编译期依赖。对外数据通过 Thought、上下文 Provider 和 perception 事件暴露。

## Local invariants

- 初始请求、feedback 和审批回调必须捕获并验证原 driver/cycle。
- 初始和 feedback 请求都使用 `RimMindAPI.Context.ScenarioDecision`，与 `advisor_task` / `actions_list` provider 守卫一致。
- 一个响应批次的直接和审批结果只发送一次聚合 feedback。
- Tool handler 可能修改 Verse 状态，必须在主线程完成。
- 每个开始的周期只释放一次并发槽，并清理审批和 driver 状态。
- 保持序列化键 `aiAdvisorEnabled` 不变。
- `MaxToolCallDepth` 当前为 3；修改前先获得批准。
- Mod 入口只组合和转发；Provider 注册集中在 `AdvisorProviderRegistrar`，设置控件集中在 `AdvisorSettingsDrawer`。
- 决策历史由 `AdvisorHistoryStore` 持有；`ApprovalManager` 只负责审批入口与终态回调，不另存重复历史。
- 调试预览使用 `BuildSnapshotFromEnvelopeAsync`；通过 `LongEventHandler` 回主线程输出，忽略旧游戏的完成结果。

## Smallest useful verification

```powershell
dotnet test Tests/RimMindAdvisor.Tests.csproj -c Release --filter FullyQualifiedName~AdvisorLifecycleContracts
dotnet build Source/RimMindAdvisor.csproj -c Release
```

提交前运行完整 `RimMindAdvisor.Tests.csproj`。Advisor 全部测试项目累计最多 999 个发现用例（少于 1000）；参数化测试每个数据行计入总数。验证真实行为、失败边界和模块协作；替身只隔离外部依赖，不只验证 mock、不复制生产算法、不锁定私有实现形状，不为压低数量合并无关场景。重点入口见 `Tests/README.md`。

## Do not

- 不从后台线程执行 `AdvisorToolCallExecutor`。
- 不绕过 `RimMindAPI` 访问 Core 内部、设置实例或具体请求队列。
- 不为单一周期再创建 workflow 接口、DTO 层或并行 feedback 通道。
- 不把触发检查、Gizmo 或 `PostExposeData` 搬进周期协调器。
