# AGENTS.md — RimMind-Advisor

本文件供 AI 编码助手阅读，描述 RimMind-Advisor 的架构、代码约定和扩展模式。

## 项目定位

RimMind-Advisor 是 RimMind AI 模组套件的 AI 决策层。它在小人空闲或心情低落时向 LLM 请求建议，决定下一步该做什么，并通过 RimMind-Actions 执行。

**核心职责**：
1. **空闲/心情检测**：扫描小人状态，在适当时机触发 AI 决策
2. **候选任务构建**：生成当前可行的任务列表（工作 + 即时动作）
3. **Prompt 构建**：使用 `StructuredPromptBuilder` 组装 System Prompt + User Prompt
4. **响应解析**：解析 JSON 响应，转换为可执行的动作意图
5. **审批系统**：高风险动作通过 `RequestOverlay` 请求玩家审批
6. **动作执行**：调用 RimMindActionsAPI 执行决策
7. **历史记录**：`AdvisorHistoryStore` 持久化决策历史

**依赖关系**：
- RimMind-Core：API 客户端、上下文构建、设置框架
- RimMind-Actions：动作执行接口

## 源码结构

```
Source/
├── RimMindAdvisorMod.cs              Mod 入口，注册 Harmony 和设置 Tab
├── Settings/
│   └── RimMindAdvisorSettings.cs     模组设置（开关、冷却、并发、审批等）
├── Comps/
│   ├── CompAIAdvisor.cs              挂载到 Pawn 的核心组件，处理 Tick 和 AI 请求
│   └── CompProperties_AIAdvisor.cs   ThingComp 属性定义
├── Advisor/
│   ├── AdvisorPromptBuilder.cs       构建 System/User Prompt（使用 StructuredPromptBuilder）
│   ├── JobCandidateBuilder.cs        构建候选任务列表
│   └── AdviceResponse.cs             AI 响应 JSON DTO
├── Data/
│   ├── AdvisorRequestRecord.cs       单条决策记录
│   └── AdvisorHistoryStore.cs        WorldComponent，决策历史持久化
├── Patches/
│   └── AddCompToHumanlikePatch.cs    为所有人形种族注入 Comp
├── Concurrency/
│   └── AdvisorConcurrencyTracker.cs  全局并发计数器（线程安全）
└── Debug/
    └── AdvisorDebugActions.cs        Dev 菜单调试动作
```

## 关键类与 API

### CompAIAdvisor（核心组件）

挂载到每个殖民者的 ThingComp，负责：

```csharp
// 资格检查
bool IsEligible()  // 自由殖民者、未死亡、未征召、有心情
bool IsIdle()      // 当前任务为等待/闲逛类
bool IsMoodBelowThreshold()  // 心情低于阈值

// 触发 AI 请求
void RequestAIAdvice()      // 正常流程
void ForceRequestAdvice()   // 绕过冷却强制请求（Dev 用）

// 状态查询
bool HasPendingRequest              // 有待响应请求
int  AdvisorCooldownTicksLeft       // Advisor 层剩余冷却
bool IsEnabled                      // 该小人是否启用 Advisor
```

**触发条件**（需同时满足）：
1. 总开关 `enableAdvisor` 开启
2. API 已配置 `RimMindAPI.IsConfigured()`
3. 无待响应请求
4. 本小人顾问开关开启
5. 小人符合资格（`IsEligible`）
6. 小人空闲（`IsIdle`）或心情低于阈值（`enableMoodTrigger`）
7. Advisor 层冷却结束
8. 并发数未达上限

**审批流程**：
- AI 响应中 `request_type = "request"` 的建议 → 通过 `RimMindAPI.RegisterPendingRequest` 请求玩家审批
- AI 响应中 `request_type = "high_risk"` 且 `enableRiskApproval` 开启 → 同上
- 风险等级 >= `autoBlockRiskLevel` 的动作 → 自动拦截（`systemBlocked = true`）

### JobCandidateBuilder

构建候选任务列表，分两个区段：

```csharp
// 工作任务（action=assign_work）
[工作任务]（action 固定填 assign_work，param 填括号内的 WorkType defName）
1. 采矿（Mining）[低] — 3 个目标，最近 12 格（铁矿石）
2. 建造（Construction）[低] — 1 个目标，最近 8 格
...

// 即时动作（action=intentId）
[即时动作]（action 直接填括号内的 intentId）
11. 强制休息（force_rest）[低] — 体力 45%
12. 社交放松（social_relax）[低] — 心情 35%，建议放松
...
```

**即时动作白名单**（`AdvisorInstantActions`）：
- `force_rest` / `social_relax` / `social_dining`
- `eat_food` / `tend_pawn` / `rescue_pawn`
- `inspire_work` / `inspire_fight` / `inspire_trade`
- `move_to`

**风险等级**：Low/Medium/High/Critical，AI 被提示优先选择低风险动作。

### AdvisorPromptBuilder

```csharp
// System Prompt：使用 StructuredPromptBuilder 链式构建
string BuildSystemPrompt(Pawn pawn)
// 包含：Role/Goal/Process/Constraint/Example/Output/Fallback + 自定义 Prompt

// User Prompt：完整上下文 + 候选列表
string BuildUserPrompt(Pawn pawn)
// 包含：RimMindAPI.BuildFullPawnPrompt(pawn) + JobCandidateBuilder.Build(pawn)
```

### AdviceResponse（JSON DTO）

```csharp
class AdviceBatch {
    [JsonProperty("advices")] List<AdviceItem> advices;
}

class AdviceItem {
    [JsonProperty("action")]    string  action;        // 动作 ID
    [JsonProperty("pawn")]      string? pawn;          // 目标小人短名（多 Pawn 模式）
    [JsonProperty("target")]    string? target;        // 交互目标短名
    [JsonProperty("param")]     string? param;         // 参数
    [JsonProperty("reason")]    string? reason;        // 中文理由
    [JsonProperty("request_type")] string request_type = "normal"; // normal/request/high_risk
}
```

### AdvisorHistoryStore / AdvisorRequestRecord

```csharp
class AdvisorHistoryStore : WorldComponent {
    static AdvisorHistoryStore? Instance;
    List<AdvisorRequestRecord> GetRecords(Pawn pawn);
    void AddRecord(Pawn pawn, AdvisorRequestRecord record);
    IReadOnlyList<AdvisorRequestRecord> GlobalLog;  // 全局日志，上限 200 条
}

class AdvisorRequestRecord : IExposable {
    string action;
    string reason;
    string result;  // approved/rejected/system_blocked/ignored
    int tick;
}
```

### 设置项

| 设置 | 默认值 | 说明 |
|------|--------|------|
| enableAdvisor | true | 总开关 |
| requestCooldownTicks | 36000 | Advisor 层冷却（≈10 游戏小时） |
| maxConcurrentRequests | 3 | 最大并发请求数 |
| showThoughtBubble | true | 显示 AI 决策气泡 |
| enableIdleTrigger | true | 空闲时触发 |
| enableMoodTrigger | true | 心情低落时触发 |
| pawnScanIntervalTicks | 3600 | 扫描间隔（≈60 秒） |
| moodThreshold | 0.4 | 心情触发阈值 |
| advisorCustomPrompt | "" | 自定义追加 Prompt |
| requestExpireTicks | 30000 | 请求过期 ticks |
| enableRequestSystem | true | 启用审批系统 |
| enableRiskApproval | true | 启用风险审批 |
| autoBlockRiskLevel | High | 自动拦截风险等级 |
| injectMapAdvisorLog | false | 注入地图 Advisor 日志 |

## 数据流

```
游戏主线程 (CompTick)
    │
    ├── 间隔检查（pawnScanIntervalTicks）
    │       ▼
    ├── 条件检查（资格、空闲/心情、冷却、并发）
    │       ▼
    ├── RequestAIAdvice()
    │       ▼
    ├── 构建 AIRequest
    │   ├── SystemPrompt = BuildSystemPrompt()
    │   └── UserPrompt   = BuildUserPrompt()
    │       ▼
    ├── RimMindAPI.RequestAsync(request, OnAdviceReceived)
    │       ▼
    │   [Core 层异步处理...]
    │       ▼
    ├── OnAdviceReceived(AIResponse)
    │       ▼
    ├── 解析 JSON → AdviceBatch
    │       ▼
    ├── 遍历 advices：
    │   ├── request_type=request → RegisterPendingRequest（玩家审批）
    │   ├── request_type=high_risk + enableRiskApproval → RegisterPendingRequest
    │   ├── 风险 >= autoBlockRiskLevel → systemBlocked
    │   └── 其他 → 直接执行
    │       ▼
    ├── RimMindActionsAPI.ExecuteBatch(intents)
    │       ▼
    └── 显示决策气泡 + 记录历史
```

## 冷却机制

双层冷却设计：

1. **Advisor 层冷却**（`requestCooldownTicks`）：
   - 每个 Pawn 独立
   - 防止同一小人过于频繁请求
   - 由 `CompAIAdvisor._lastRequestTick` 控制

2. **Core 层冷却**（`globalCooldownTicks`）：
   - 按 ModId 独立
   - 防止同一目的过于频繁请求
   - 由 `AIRequestQueue` 控制

**强制请求**（`ForceRequestAdvice`）会同时清除两层冷却。

## 并发控制

```csharp
// 全局原子计数器
AdvisorConcurrencyTracker.ActiveCount  // 当前等待响应的请求数
AdvisorConcurrencyTracker.Increment()  // 发起请求时
AdvisorConcurrencyTracker.Decrement()  // 收到响应时
```

## 线程安全

- `CompAIAdvisor` 所有逻辑在主线程执行
- `AdvisorConcurrencyTracker` 使用 `Interlocked` 保证原子性
- HTTP 请求和 JSON 解析在 Core 层的后台线程完成

## 代码约定

### 命名空间

- `RimMind.Advisor` — 顶层（Mod 入口、DTO）
- `RimMind.Advisor.Settings` — 设置
- `RimMind.Advisor.Comps` — ThingComp
- `RimMind.Advisor.Advisor` — 核心逻辑（Prompt、Candidate）
- `RimMind.Advisor.Data` — 数据持久化
- `RimMind.Advisor.Patches` — Harmony 补丁
- `RimMind.Advisor.Concurrency` — 并发控制
- `RimMind.Advisor.Debug` — 调试动作

### 序列化

```csharp
public override void PostExposeData()
{
    base.PostExposeData();
    Scribe_Values.Look(ref IsEnabled, "aiAdvisorEnabled", false);
}
```

### Harmony

- Harmony ID：`mcocdaa.RimMindAdvisor`
- 使用 Postfix 动态注入 CompProperties
- 原因：XML Patch 在继承解析前运行，无法正确过滤 race/intelligence

### 构建

- 目标框架：`net48`
- C# 语言版本：9.0
- RimWorld 版本：1.6
- 输出路径：`../1.6/Assemblies/`

### 测试

- 单元测试项目：`Tests/`，使用 xUnit，目标 `net10.0`
- 已有测试：`AdviceResponseParseTests`、`ConcurrencyTrackerTests`

## 扩展指南

### 添加新的即时动作

1. 在 RimMind-Actions 中实现新动作
2. 在 `JobCandidateBuilder.AdvisorInstantActions` 中添加 intentId
3. 在 `BuildInstantHint` 中添加上下文提示逻辑
4. 调整风险等级（如有必要）

### 修改 Prompt

- 修改 `AdvisorPromptBuilder.BuildSystemPrompt` 调整角色设定
- 通过设置页的 `advisorCustomPrompt` 让玩家自定义追加内容

### 调试

Dev 菜单（开发模式）提供以下动作：
- Show Advisor State (selected) — 查看选中 Pawn 的顾问状态
- Force Request Advice (selected) — 强制请求建议
- Show Job Candidates (selected) — 查看候选任务列表
- Show Full Prompt (selected) — 查看完整 Prompt
- List All Advisor States — 列出所有殖民者状态
- Clear ALL Cooldowns — 清除所有冷却
- Reset Concurrency Count — 重置并发计数

## AI 响应格式标准

```json
{
  "advices": [
    {
      "action": "social_relax",
      "pawn": null,
      "target": null,
      "param": null,
      "reason": "心情低落，需要放松",
      "request_type": "normal"
    },
    {
      "action": "assign_work",
      "pawn": null,
      "target": null,
      "param": "Mining",
      "reason": "擅长采矿且附近有铁矿石",
      "request_type": "normal"
    }
  ]
}
```

**action 值域**：
- `assign_work` — 工作任务，param 为 WorkType defName
- 即时动作 intentId — 见 `AdvisorInstantActions` 白名单

**request_type 值域**：
- `normal` — 直接执行
- `request` — 请求玩家审批
- `high_risk` — 高风险动作，需审批（受 `enableRiskApproval` 控制）
