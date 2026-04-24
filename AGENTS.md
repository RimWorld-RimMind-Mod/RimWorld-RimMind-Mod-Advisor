# AGENTS.md — RimMind-Advisor

本文件供 AI 编码助手阅读，描述 RimMind-Advisor 的架构、代码约定和扩展模式。

## 项目定位

RimMind-Advisor 是 RimMind AI 模组套件的 AI 决策层。它在小人空闲或心情低落时向 LLM 请求建议，决定下一步该做什么，并通过 RimMind-Actions 执行。

**核心职责**：
1. **空闲/心情检测**：扫描小人状态，在适当时机触发 AI 决策
2. **候选任务构建**：生成当前可行的任务列表（工作 + 即时动作）
3. **上下文构建**：使用 `ContextEngine` + `ContextRequest` 组装消息列表
4. **Tool Calling 响应处理**：解析 `StructuredToolCall`，转换为可执行的动作意图
5. **审批系统**：高风险动作通过 `RimMindAPI.RegisterPendingRequest` 请求玩家审批
6. **动作执行**：调用 `RimMindActionsAPI.ExecuteBatchWithResults` 执行决策
7. **反馈循环**：执行结果回传 LLM，最多 3 层 Tool Call 深度
8. **历史记录**：`AdvisorHistoryStore` 持久化决策历史，并注入 Prompt 上下文

**依赖关系**（编译期引用，运行时由 RimWorld 加载）：
- **RimMind-Core**：`RimMindAPI`（请求/上下文/审批）、`ContextEngine`、`ContextRequest`、`HistoryManager`、`SchemaRegistry`、`StructuredToolCall`、`SettingsUIHelper`、`AIRequestQueue`、`AIRequestPriority`、`CompPawnAgent`
- **RimMind-Actions**：`RimMindActionsAPI`（动作执行/查询）、`BatchActionIntent`、`ActionResult`、`RiskLevel`、`EatFoodAction`

## 源码结构

```
Source/
├── RimMindAdvisorMod.cs              Mod 入口：Harmony 注册、设置 Tab、PawnContextProvider、ModCooldown
├── Settings/
│   └── RimMindAdvisorSettings.cs     ModSettings（13 项设置，见下方设置表）
├── Comps/
│   ├── CompAIAdvisor.cs              核心 ThingComp：Tick 触发、AI 请求、Tool Call 处理、反馈循环、Gizmo
│   └── CompProperties_AIAdvisor.cs   ThingComp 属性，仅指定 compClass
├── Advisor/
│   ├── AdviceResponse.cs             空文件（DTO 已迁移到 Tool Calling 架构）
│   └── JobCandidateBuilder.cs        候选任务列表（工作 + 即时动作），含上下文提示和过滤
├── Data/
│   ├── AdvisorRequestRecord.cs       单条决策记录（IExposable）
│   └── AdvisorHistoryStore.cs        WorldComponent，按 Pawn 存储历史，全局日志上限 200 条
├── Patches/
│   └── AddCompToHumanlikePatch.cs    Harmony Postfix：为 Humanlike 种族注入 CompProperties_AIAdvisor
├── Concurrency/
│   └── AdvisorConcurrencyTracker.cs  全局原子计数器（Interlocked），负值自动修正
└── Debug/
    └── AdvisorDebugActions.cs        Dev 菜单 7 项调试动作

Tests/
├── RimMindAdvisor.Tests.csproj       xUnit 测试项目，net10.0，仅引用纯逻辑文件
├── AdviceResponseParseTests.cs       ⚠️ 引用已删除的 AdviceBatch/AdviceItem，编译失败
└── ConcurrencyTrackerTests.cs        并发计数器线程安全（5 个用例）
```

## 关键类与 API

### CompAIAdvisor（核心组件）

挂载到每个殖民者的 `ThingComp`，所有逻辑在主线程执行。

```csharp
// 字段
bool IsEnabled = false;              // 该小人是否启用 Advisor（序列化持久化）

// 属性
bool HasPendingRequest               // 有待响应请求
int  AdvisorCooldownTicksLeft        // Advisor 层剩余冷却 ticks

// 私有方法
bool IsEligible()                    // IsFreeNonSlaveColonist && !Dead && !Drafted && mood != null
bool IsIdle()                        // curJob 为 null 或 Wait/Wait_Wander/GotoWander/Wait_MaintainPosture，排除 playerForced
bool IsMoodBelowThreshold()          // mood.CurLevelPercentage < moodThreshold

// 公开方法
void ForceRequestAdvice()            // 绕过双层冷却 + 强制 IsEnabled=true（Dev 用）
```

**触发条件**（CompTick 中按顺序检查，任一不满足即跳过）：
1. `enableAdvisor` 总开关开启
2. `RimMindAPI.IsConfigured()` API 已配置
3. `!CompPawnAgent.IsAgentActive(Pawn)` Agent 模式未激活
4. `_hasPendingRequest == false`（超时 60000 ticks 自动重置）
5. `IsEnabled` 本小人顾问开关开启
6. `IsEligible()` 小人符合资格
7. 空闲触发（`enableIdleTrigger && IsIdle()`）或心情触发（`enableMoodTrigger && IsMoodBelowThreshold()`）
8. Advisor 层冷却结束（`_lastRequestTick + requestCooldownTicks <= TicksGame`）
9. 并发数未达上限（`AdvisorConcurrencyTracker.ActiveCount < maxConcurrentRequests`）

**AIRequest 构建参数**（ContextEngine 架构）：
```csharp
var ctxRequest = new ContextRequest {
    NpcId     = $"NPC-{Pawn.ThingID}",
    Scenario  = ScenarioIds.Decision,
    Budget    = GetDecisionBudget(),  // 从 Core Settings 读取
    MaxTokens = 400,
    Temperature = 0.7f,
};
var schema = SchemaRegistry.AdviceOutput;
var tools = RimMindActionsAPI.GetStructuredTools();
var snapshot = RimMindAPI.BuildContextSnapshot(ctxRequest);

// advisorCustomPrompt 插入到消息列表最前（position 0）
if (!advisorCustomPrompt.NullOrEmpty())
    snapshot.Messages.Insert(0, new ChatMessage { Role = "system", Content = advisorCustomPrompt });

var aiRequest = new AIRequest {
    Messages      = snapshot.Messages,
    MaxTokens     = snapshot.MaxTokens,
    Temperature   = snapshot.Temperature,
    RequestId     = $"Structured_{npcId}",
    ModId         = "Advisor",
    ExpireAtTicks = TicksGame + requestExpireTicks,
    UseJsonMode   = true,
    Priority      = AIRequestPriority.Normal,
};
RimMindAPI.RequestStructuredAsync(aiRequest, schema, OnAdviceReceived, tools);
```

**Gizmo**（`CompGetGizmosExtra`）：
- 切换按钮：显示启用/禁用状态 + 冷却/等待子标签，图标 `UI/AdvisorIcon`
- Dev 模式额外按钮：Force Request Advice

### 响应处理流程（OnAdviceReceived → HandleToolCalls）

```
AIResponse → ToolCallsJson 解析 → List<StructuredToolCall>
    │
    ├── 解析失败/空 → Log.Warning，CompleteRequestCycle
    │
    └── 遍历 toolCalls：
        ├── Name 为空或不 supported → 跳过
        ├── !RimMindActionsAPI.IsAllowed(Name) → 跳过
        ├── 解析 Arguments → target/param/reason
        ├── FindPawnByName 查找 target Pawn
        │
        ├── 判断是否需要审批：
        │   ├── systemBlocked = enableRiskApproval && riskLevel >= autoBlockRiskLevel
        │   │
        │   ├── systemBlocked && enableRequestSystem → RegisterPendingRequest
        │   │   ├── 选项：批准/拒绝（无忽略）
        │   │   ├── 批准 → ExecuteBatch 单条 → AddRecord("approved")
        │   │   └── 拒绝 → AddRecord("system_blocked")
        │   │
        │   ├── systemBlocked && !enableRequestSystem → 跳过（动作被拦截）
        │   └── !systemBlocked → 加入直接执行列表
        │
        └── 直接执行列表 → ExecuteBatchWithResults(intents)
            → 气泡(0.6,0.9,1.0) 显示 reason
            → AddRecord(success/"reason")
            → 反馈循环（_toolCallDepth < 3 → RequestToolFeedback）
            → 否则 CompleteRequestCycle
```

### 反馈循环（RequestToolFeedback）

Tool Calling 架构支持多轮交互：执行结果回传 LLM，让 AI 根据结果调整决策。

```csharp
// 最多 3 层深度
private const int MaxToolCallDepth = 3;

// 反馈请求构建：
// 1. 复制 _lastMessages
// 2. 追加 assistant 消息（含 ToolCalls）
// 3. 追加 tool 消息（含 ActionResult）
// 4. 发送新的 RequestStructuredAsync
```

**注意**：当前 `RequestToolFeedback` 未刷新 `_pendingRequestTick`，长时间反馈循环可能触发超时（已知问题）。

### JobCandidateBuilder

构建候选任务列表，分两个区段，所有文本使用翻译键：

```csharp
// 工作区段（action 固定 assign_work，param 为 WorkType defName）
// 格式：{序号}. {labelShort}({defName})[低] — {hint}
// hint 格式：单目标 "{N}个目标，最近{D}格"；多目标 "{N}个目标，最近{D}格({标签})"
// 最多 MaxWorkCandidates=10 条，仅包含已激活工作且有可用目标的

// 即时动作区段（action 为 intentId）
// 格式：{序号}. {displayName}({intentId}){风险标签} | {动作描述} — {上下文提示}
// 仅包含白名单内 + IsAllowed + !ShouldSkipAction + BuildInstantHint 返回非 null 的动作
```

**即时动作白名单**（`AdvisorInstantActions`）：
| intentId | BuildInstantHint 逻辑 | 过滤条件 |
|----------|----------------------|----------|
| `force_rest` | 体力百分比 | 始终显示（体力<90%提示低，否则提示充足） |
| `social_relax` | 心情百分比 | 始终显示（心情<60%提示低，否则提示正常） |
| `social_dining` | 同伴短名 | 地图无其他自由殖民者→返回 null 过滤 |
| `eat_food` | 可用美食列表 | 无 JoyFood→返回 null 过滤 |
| `tend_pawn` | 受伤殖民者短名 | 无受伤殖民者→返回 null 过滤 |
| `rescue_pawn` | 倒地殖民者短名 | 无倒地殖民者→返回 null 过滤 |
| `inspire_work` | 翻译文本 | 已有灵感→返回 null 过滤 |
| `inspire_fight` | 翻译文本 | 已有灵感→返回 null 过滤 |
| `inspire_trade` | 翻译文本 | 已有灵感→返回 null 过滤 |
| `move_to` | 翻译文本 | 始终显示 |

**风险等级**：`RiskLevel` 枚举（Low=0/Medium=1/High=2/Critical=3），由 `RimMindActionsAPI.GetRiskLevel` 返回。

### AdvisorHistoryStore / AdvisorRequestRecord

```csharp
public class AdvisorHistoryStore : WorldComponent {
    static AdvisorHistoryStore? Instance;                              // 单例，构造时赋值
    List<AdvisorRequestRecord> GetRecords(Pawn pawn);                 // 按 thingIDNumber 索引
    void AddRecord(Pawn pawn, AdvisorRequestRecord record);           // 同时追加到全局日志
    IReadOnlyList<AdvisorRequestRecord> GlobalLog { get; }           // 全局日志，上限 200 条
    // 序列化：Scribe_Collections.Look(ref _records, "advisorRecords", LookMode.Value, LookMode.Deep)
    //         Scribe_Collections.Look(ref _globalLog, "globalLog", LookMode.Deep)
    //         反序列化后 null-coalescing 兜底
}

public class AdvisorRequestRecord : IExposable {
    public string action = string.Empty;   // 动作 ID
    public string reason = string.Empty;   // AI 给出的理由
    public string result = string.Empty;   // approved / rejected / system_blocked / success / 失败原因
    public int    tick;                    // 游戏时刻
}
```

**调用点**：
- 直接执行路径：`HandleToolCalls` 中 `ExecuteBatchWithResults` 后，遍历 `ActionResult` 调用 `AddRecord`
- 审批回调路径：玩家批准后调用 `AddRecord`

### RimMindAdvisorMod（入口）

```csharp
// 构造函数中完成：
1. Settings = GetSettings<RimMindAdvisorSettings>()
2. new Harmony("mcocdaa.RimMindAdvisor").PatchAll()
3. RimMindAPI.RegisterSettingsTab("advisor", ...)     — 注册设置 Tab 到 Core
4. RimMindAPI.RegisterModCooldown("Advisor", ...)      — 注册 Mod 冷却到 Core
5. RimMindAPI.RegisterPawnContextProvider("advisor_history", ..., PriorityAuxiliary) — 注册历史上下文提供者
   // lambda 内：取最近 5 条记录，格式化为 "- action: reason → resultLabel"
   // resultLabel 根据 result 值翻译为 已批准/玩家拒绝/系统拦截/已忽略
```

### 设置项

| 设置 | 类型 | 默认值 | 范围/步进 | 说明 |
|------|------|--------|-----------|------|
| enableAdvisor | bool | true | — | 总开关 |
| requestCooldownTicks | int | 30000 | 3600~72000, 步进600 | Advisor 层冷却（≈12 游戏小时） |
| maxConcurrentRequests | int | 3 | 1~5 | 最大并发请求数 |
| showThoughtBubble | bool | true | — | 显示 AI 决策气泡 |
| enableIdleTrigger | bool | true | — | 空闲时触发 |
| enableMoodTrigger | bool | true | — | 心情低落时触发 |
| pawnScanIntervalTicks | int | 3600 | 600~6000, 步进100 | CompTick 检查间隔（≈60 秒） |
| moodThreshold | float | 0.3 | 0.25~0.6 | 心情触发阈值 |
| requestExpireTicks | int | 30000 | 3600~120000, 步进1500 | 审批请求过期 ticks |
| enableRequestSystem | bool | true | — | 启用审批系统（关闭则高风险动作被跳过） |
| enableRiskApproval | bool | true | — | 启用风险拦截 |
| autoBlockRiskLevel | RiskLevel | High | Low~Critical | 自动拦截风险等级阈值 |
| advisorCustomPrompt | string | "" | — | 自定义 System Prompt（插入到消息列表最前） |

## 数据流

```
游戏主线程 (CompTick)
    │
    ├── Pawn.IsHashIntervalTick(pawnScanIntervalTicks)
    │       ▼
    ├── 9 项条件检查（见触发条件）
    │       ▼
    ├── RequestAIAdvice()
    │   ├── _hasPendingRequest = true, _pendingRequestTick = now
    │   ├── AdvisorConcurrencyTracker.Increment()
    │   ├── ContextEngine.BuildSnapshot(ctxRequest) → Messages
    │   ├── advisorCustomPrompt → Insert(0, system message)
    │   └── RimMindAPI.RequestStructuredAsync(aiRequest, schema, callback, tools)
    │       ▼
    │   [Core 层异步：排队 → 冷却检查 → HTTP → Tool Call JSON 返回]
    │       ▼
    ├── OnAdviceReceived(AIResponse)
    │   ├── Pawn 无效/失败/空 → CompleteRequestCycle
    │   └── HandleToolCalls(ToolCallsJson)
    │       ├── 解析 StructuredToolCall 列表
    │       ├── 遍历：过滤 → 审批判断 → 执行/注册请求
    │       ├── 直接执行 → ExecuteBatchWithResults → 气泡 → AddRecord
    │       └── _toolCallDepth < 3 → RequestToolFeedback
    │           └── 构建反馈消息 → RequestStructuredAsync（循环回 OnAdviceReceived）
    │       ▼
    └── CompleteRequestCycle()
        ├── if (_hasPendingRequest) { Decrement; _lastRequestTick = now; }
        ├── _toolCallDepth = 0
        └── _lastMessages = null
```

## 冷却机制

双层冷却设计：

1. **Advisor 层冷却**（`requestCooldownTicks`，默认 30000）：
   - 每个 Pawn 独立，由 `CompAIAdvisor._lastRequestTick` 控制
   - 仅在 `CompleteRequestCycle()` 中更新 `_lastRequestTick`（`_hasPendingRequest` 为 true 时）
   - 审批回调不更新冷却（回调独立于请求周期）

2. **Core 层冷却**（由 `AIRequestQueue` 按 ModId="Advisor" 控制）：
   - 全局共享，通过 `RimMindAPI.RegisterModCooldown("Advisor", ...)` 注册
   - 冷却时长由 `RimMindCoreMod.Settings.globalCooldownTicks` 控制

**强制请求**（`ForceRequestAdvice`）：
- 设置 `IsEnabled = true`
- 重置 `_lastRequestTick = -9999`（清除 Advisor 层冷却）
- 调用 `RimMind.Core.Internal.AIRequestQueue.Instance?.ClearCooldown("Advisor")`（清除 Core 层冷却）

## 并发控制

```csharp
// 全局原子计数器，负值自动修正
AdvisorConcurrencyTracker.ActiveCount  // int，当前等待响应的请求数
AdvisorConcurrencyTracker.Increment()  // Interlocked.Increment
AdvisorConcurrencyTracker.Decrement()  // Interlocked.Decrement + 负值修正
```

- 请求发起时 Increment，`CompleteRequestCycle` 中 Decrement（仅当 `_hasPendingRequest` 为 true）
- 超时路径（60000 ticks）也会 Decrement
- 上限由 `maxConcurrentRequests` 控制（默认 3）

## 线程安全

- `CompAIAdvisor` 所有逻辑在主线程执行（CompTick / OnAdviceReceived 回调）
- `AdvisorConcurrencyTracker` 使用 `Interlocked` 保证原子性
- HTTP 请求和 JSON 解析在 Core 层的后台线程完成，结果通过回调回主线程

## 代码约定

### 命名空间

| 命名空间 | 职责 |
|----------|------|
| `RimMind.Advisor` | Mod 入口（RimMindAdvisorMod） |
| `RimMind.Advisor.Settings` | 设置（RimMindAdvisorSettings） |
| `RimMind.Advisor.Comps` | ThingComp（CompAIAdvisor、CompProperties_AIAdvisor） |
| `RimMind.Advisor.Advisor` | 核心逻辑（JobCandidateBuilder） |
| `RimMind.Advisor.Data` | 数据持久化（AdvisorHistoryStore、AdvisorRequestRecord） |
| `RimMind.Advisor.Patches` | Harmony 补丁（AddAdvisorCompPatch） |
| `RimMind.Advisor.Concurrency` | 并发控制（AdvisorConcurrencyTracker） |
| `RimMind.Advisor.Debug` | 调试动作（AdvisorDebugActions） |

### 序列化

- ThingComp 使用 `PostExposeData` + `Scribe_Values.Look`
- WorldComponent 使用 `ExposeData` + `Scribe_Collections.Look`
- 默认值必须与字段初始化值一致

### Harmony

- Harmony ID：`mcocdaa.RimMindAdvisor`
- 补丁类：`AddAdvisorCompPatch`，Postfix on `ThingDef.ResolveReferences`
- 过滤条件：`race.intelligence == Intelligence.Humanlike`
- 去重：检查 `comps.Any(c => c is CompProperties_AIAdvisor)`
- 原因：XML Patch 在继承解析前运行，race/intelligence 字段尚未继承

### 构建

| 项目 | 目标框架 | 语言版本 | 输出路径 |
|------|----------|----------|----------|
| Source | net48 | C# 9.0 | `../1.6/Assemblies/` |
| Tests | net10.0 | C# 9.0 | — |

- NuGet：`Krafs.Rimworld.Ref(1.6.*)`, `Lib.Harmony.Ref(2.*)`, `Newtonsoft.Json(13.0.*)`
- 编译期引用：`RimMindCore.dll`, `RimMindActions.dll`（Private=false，运行时加载）
- 部署：设置 `RIMWORLD_DIR` 环境变量后构建自动 robocopy

### 测试

- 测试项目仅引用纯逻辑文件（无 RimWorld 依赖）：
  - `Source/Concurrency/AdvisorConcurrencyTracker.cs` — 计数器
- 已有测试类：`ConcurrencyTrackerTests`（5 用例）
- ⚠️ `AdviceResponseParseTests` 引用已删除的 `AdviceBatch`/`AdviceItem`，编译失败

## 扩展指南

### 添加新的即时动作

1. 在 RimMind-Actions 中实现新动作（注册 intentId + 风险等级）
2. 在 `JobCandidateBuilder.AdvisorInstantActions` 中添加 intentId
3. 在 `BuildInstantHint` 中添加 switch case，返回上下文提示或 null（null=过滤掉）
4. 调整风险等级（如有必要，在 Actions 侧修改）

### 修改上下文

- 上下文构建由 Core 的 `ContextEngine` 管理，Advisor 通过 `ContextRequest` 配置
- `advisorCustomPrompt` 设置项插入到消息列表最前（position 0），在 ContextEngine 生成的消息之前
- 历史上下文通过 `RegisterPawnContextProvider("advisor_history", ...)` 注册，由 Core 自动包含
- 翻译键前缀：`RimMind.Advisor.Prompt.*`

### 调试

Dev 菜单（RimMind Advisor 分类）提供 7 项动作：
| 动作 | 功能 |
|------|------|
| Show Advisor State (selected) | 选中 Pawn 的完整状态（开关/冷却/并发/API） |
| Force Request Advice (selected) | 强制请求建议（清除双层冷却） |
| Show Job Candidates (selected) | 查看候选任务列表文本 |
| Show Full Prompt (selected) | 查看 ContextEngine 构建的 System + User Prompt 全文 |
| List All Advisor States | 列出地图所有殖民者状态 |
| Clear ALL Cooldowns | 清除 Core 层所有冷却 |
| Reset Concurrency Count | 强制归零并发计数器 |

## Tool Calling 响应格式

AI 通过 Tool Calling 返回动作决策，而非旧版 JSON `advices` 数组。

**StructuredToolCall 结构**（由 Core 定义）：
```json
[
  {
    "id": "call_001",
    "name": "social_relax",
    "arguments": "{\"target\":null,\"param\":null,\"reason\":\"心情低落，需要放松\"}"
  },
  {
    "id": "call_002",
    "name": "assign_work",
    "arguments": "{\"target\":null,\"param\":\"Mining\",\"reason\":\"擅长采矿且附近有铁矿石\"}"
  }
]
```

**arguments 字段**：JSON 字符串，解析后包含 `target`、`param`、`reason` 等键。

**action（name）值域**：
- `assign_work` — 工作任务，param 为 WorkType defName
- 即时动作 intentId — 见 `AdvisorInstantActions` 白名单（10 个）

**审批逻辑**：
- 仅基于风险等级判断：`enableRiskApproval && riskLevel >= autoBlockRiskLevel`
- 旧版 `request_type` 字段不再使用

## RimMind-Core API 使用汇总

| API | 调用位置 | 用途 |
|-----|----------|------|
| `RimMindAPI.IsConfigured()` | CompAIAdvisor.CompTick | 检查 API 是否已配置 |
| `RimMindAPI.BuildContextSnapshot(ctxRequest)` | CompAIAdvisor.RequestAIAdvice | 构建 ContextEngine 快照 |
| `RimMindAPI.RequestStructuredAsync(req, schema, cb, tools)` | CompAIAdvisor.RequestAIAdvice | 发送 Tool Calling 请求 |
| `RimMindAPI.RegisterPendingRequest(entry)` | CompAIAdvisor.HandleToolCalls | 注册审批请求 |
| `RimMindAPI.RegisterSettingsTab(id, title, draw)` | RimMindAdvisorMod 构造 | 注册设置 Tab |
| `RimMindAPI.RegisterModCooldown(modId, ticksFunc)` | RimMindAdvisorMod 构造 | 注册 Mod 冷却 |
| `RimMindAPI.RegisterPawnContextProvider(id, func, priority)` | RimMindAdvisorMod 构造 | 注册 Pawn 上下文提供者 |
| `RimMindAPI.ShouldSkipAction(intentId)` | JobCandidateBuilder.BuildInstantCandidates | 检查动作是否应被跳过（Bridge 门控） |
| `RimMindActionsAPI.GetSupportedIntents()` | CompAIAdvisor.HandleToolCalls | 获取所有已注册 intentId |
| `RimMindActionsAPI.IsAllowed(intentId)` | CompAIAdvisor/JobCandidateBuilder | 检查动作是否被允许 |
| `RimMindActionsAPI.GetRiskLevel(intentId)` | CompAIAdvisor.HandleToolCalls | 获取动作风险等级 |
| `RimMindActionsAPI.GetStructuredTools()` | CompAIAdvisor.BuildActionTools | 获取 Tool Calling 工具定义 |
| `RimMindActionsAPI.ExecuteBatchWithResults(intents)` | CompAIAdvisor.HandleToolCalls | 批量执行动作意图（返回结果） |
| `RimMindActionsAPI.GetWorkTargets(pawn, defName, max)` | JobCandidateBuilder | 获取工作任务目标列表 |
| `RimMindActionsAPI.GetActionDescriptions()` | JobCandidateBuilder | 获取所有动作描述 |
| `AIRequestQueue.Instance.ClearCooldown(modId)` | CompAIAdvisor.ForceRequestAdvice | 清除 Core 层冷却 |
| `AIRequestQueue.Instance.GetCooldownTicksLeft(modId)` | AdvisorDebugActions | 查询 Core 层剩余冷却 |
| `CompPawnAgent.IsAgentActive(pawn)` | CompAIAdvisor.CompTick | 检查 Agent 模式是否激活 |
