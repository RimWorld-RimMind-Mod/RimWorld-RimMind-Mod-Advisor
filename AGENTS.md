# AGENTS.md — RimMind-Advisor

本文件供 AI 编码助手阅读，描述 RimMind-Advisor 的架构、代码约定和扩展模式。

## 项目定位

RimMind-Advisor 是 RimMind AI 模组套件的 AI 决策层。它在小人空闲或心情低落时向 LLM 请求建议，决定下一步该做什么，并通过 RimMind-Actions 执行。

**核心职责**：
1. **空闲/心情检测**：扫描小人状态，在适当时机触发 AI 决策
2. **候选任务构建**：生成当前可行的任务列表（工作 + 即时动作）
3. **Prompt 构建**：使用 `StructuredPromptBuilder` + `PromptBudget` 组装 System/User Prompt
4. **响应解析**：解析 JSON 响应，转换为可执行的动作意图
5. **审批系统**：高风险/请求类动作通过 `RimMindAPI.RegisterPendingRequest` 请求玩家审批
6. **动作执行**：调用 `RimMindActionsAPI.ExecuteBatch` 执行决策
7. **历史记录**：`AdvisorHistoryStore` 持久化决策历史，并注入 Prompt 上下文

**依赖关系**（编译期引用，运行时由 RimWorld 加载）：
- **RimMind-Core**：`RimMindAPI`（请求/上下文/审批）、`StructuredPromptBuilder`、`PromptBudget`、`ContextComposer`、`SettingsUIHelper`、`AIRequestQueue`、`AIRequestPriority`
- **RimMind-Actions**：`RimMindActionsAPI`（动作执行/查询）、`BatchActionIntent`、`RiskLevel`、`EatFoodAction`

## 源码结构

```
Source/
├── RimMindAdvisorMod.cs              Mod 入口：Harmony 注册、设置 Tab、PawnContextProvider、ModCooldown
├── Settings/
│   └── RimMindAdvisorSettings.cs     ModSettings（13 项设置，见下方设置表）
├── Comps/
│   ├── CompAIAdvisor.cs              核心 ThingComp：Tick 触发、AI 请求、响应处理、Gizmo
│   └── CompProperties_AIAdvisor.cs   ThingComp 属性，仅指定 compClass
├── Advisor/
│   ├── AdvisorPromptBuilder.cs       System/User Prompt 构建（StructuredPromptBuilder + PromptBudget）
│   ├── JobCandidateBuilder.cs        候选任务列表（工作 + 即时动作），含上下文提示和过滤
│   └── AdviceResponse.cs             AI 响应 JSON DTO（AdviceBatch / AdviceItem）
├── Data/
│   ├── AdvisorRequestRecord.cs       单条决策记录（IExposable）
│   └── AdvisorHistoryStore.cs        WorldComponent，按 Pawn 存储历史，全局日志上限 200 条
├── Patches/
│   └── AddCompToHumanlikePatch.cs    Harmony Postfix：为 Humanlike 种族注入 CompProperties_AIAdvisor
├── Concurrency/
│   └── AdvisorConcurrencyTracker.cs  全局原子计数器（Interlocked）
└── Debug/
    └── AdvisorDebugActions.cs        Dev 菜单 7 项调试动作

Tests/
├── RimMindAdvisor.Tests.csproj       xUnit 测试项目，net10.0，仅引用纯逻辑文件
├── AdviceResponseParseTests.cs       AdviceBatch JSON 解析（7 个用例）
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
bool IsEligible()                    // IsFreeNonSlaveColonist && !Dead && !(drafter?.Drafted ?? false) && mood != null
bool IsIdle()                        // curJob 为 null 或 Wait/Wait_Wander/GotoWander/Wait_MaintainPosture，排除 playerForced
bool IsMoodBelowThreshold()          // mood.CurLevelPercentage < moodThreshold

// 公开方法
void RequestAIAdvice()               // 正常流程（内部调用）
void ForceRequestAdvice()            // 绕过双层冷却 + 强制 IsEnabled=true（Dev 用）
```

**触发条件**（CompTick 中按顺序检查，任一不满足即跳过）：
1. `enableAdvisor` 总开关开启
2. `RimMindAPI.IsConfigured()` API 已配置
3. `_hasPendingRequest == false`
4. `IsEnabled` 本小人顾问开关开启
5. `IsEligible()` 小人符合资格
6. 空闲触发（`enableIdleTrigger && IsIdle()`）或心情触发（`enableMoodTrigger && IsMoodBelowThreshold()`）
7. Advisor 层冷却结束（`_lastRequestTick + requestCooldownTicks <= TicksGame`）
8. 并发数未达上限（`AdvisorConcurrencyTracker.ActiveCount < maxConcurrentRequests`）

**AIRequest 构建参数**：
```csharp
new AIRequest {
    SystemPrompt  = AdvisorPromptBuilder.BuildSystemPrompt(pawn),
    UserPrompt    = AdvisorPromptBuilder.BuildUserPrompt(pawn),
    MaxTokens     = 400,
    Temperature   = 0.7f,
    RequestId     = "Advisor_{Pawn.ThingID}",
    ModId         = "Advisor",
    ExpireAtTicks = TicksGame + requestExpireTicks,
    UseJsonMode   = true,
    Priority      = AIRequestPriority.Normal,
}
```

**Gizmo**（`CompGetGizmosExtra`）：
- 切换按钮：显示启用/禁用状态 + 冷却/等待子标签，图标 `UI/AdvisorIcon`
- Dev 模式额外按钮：Force Request Advice

### 响应处理流程（OnAdviceReceived）

```
AIResponse → JSON 解析 → AdviceBatch
    │
    ├── 解析失败/空 → Log.Warning，return
    │
    └── 遍历 batch.advices：
        ├── action 不在 supported 集合 → 跳过
        ├── !RimMindActionsAPI.IsAllowed(action) → 跳过
        ├── 解析 actor/target Pawn（按 Name.ToStringShort 匹配）
        │
        ├── 判断是否需要审批：
        │   ├── systemBlocked = enableRiskApproval && riskLevel >= autoBlockRiskLevel
        │   ├── needsApproval = systemBlocked || request_type=="request" || request_type=="high_risk"
        │   │
        │   ├── needsApproval && enableRequestSystem → RegisterPendingRequest
        │   │   ├── systemBlocked → 选项：批准/拒绝（无忽略）
        │   │   └── 非系统拦截   → 选项：批准/拒绝/忽略
        │   │   ├── 批准 → ExecuteBatch 单条 → 绿色气泡(0.4,1,0.6) → 记录 "approved"
        │   │   ├── 拒绝 → 记录 systemBlocked?"system_blocked":"rejected"
        │   │   └── 忽略 → 记录 "ignored"
        │   │
        │   └── !needsApproval || !enableRequestSystem → 加入直接执行列表
        │
        └── 直接执行列表 → RimMindActionsAPI.ExecuteBatch(intents)
            → 青色气泡(0.6,0.9,1.0) 显示 reason
            → 更新 _lastRequestTick
```

### AdvisorPromptBuilder

```csharp
// System Prompt：StructuredPromptBuilder 链式构建
string BuildSystemPrompt(Pawn pawn)
// 实际调用链：
//   FromKeyPrefix("RimMind.Advisor.Prompt.System")
//     .Role(翻译键)              — 角色设定
//     .Goal(翻译键)              — 目标设定
//     .Constraint(翻译键)        — 字段规则
//     .ConstraintFromKey(翻译键) — 输出规则
//     .ConstraintFromKey(翻译键) — 风险控制
//     .ConstraintFromKey(翻译键) — 多样化提示（仅 enableRequestSystem 时追加）
//     .WithCustom(Settings.advisorCustomPrompt) — 玩家自定义追加

// User Prompt：PromptSection 列表 + PromptBudget 裁剪
string BuildUserPrompt(Pawn pawn)
// sections 按优先级排列：
//   1. "candidates" (PriorityCurrentInput) — 候选任务列表
//   2. RimMindAPI.BuildFullPawnSections(pawn) — Pawn 完整上下文（Core 提供，含 advisor_history）
// PromptBudget(5000, 600) 按优先级裁剪后拼合
// 注：advisor_history 通过 RegisterPawnContextProvider 注册，由 Core 的 BuildFullPawnSections 自动包含
```

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

### AdviceResponse（JSON DTO）

```csharp
public class AdviceBatch {
    [JsonProperty("advices")] public List<AdviceItem> advices = new List<AdviceItem>();  // 默认空列表
}

public class AdviceItem {
    [JsonProperty("action")]        public string  action = "";         // 必填，动作 ID
    [JsonProperty("pawn")]          public string? pawn;                // 多 Pawn 模式：目标小人短名
    [JsonProperty("target")]        public string? target;              // 社交类动作的交互对象短名
    [JsonProperty("param")]         public string? param;               // 参数（如 WorkType defName）
    [JsonProperty("reason")]        public string? reason;              // 理由（中文）
    [JsonProperty("request_type")]  public string  request_type = "normal"; // normal/request/high_risk
}
```

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
    public string result = string.Empty;   // approved / rejected / system_blocked / ignored
    public int    tick;                    // 游戏时刻
}
```

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
| moodThreshold | float | 0.3 | 0.25~0.6 | 心情触发阈值（30%） |
| advisorCustomPrompt | string | "" | — | 自定义追加 System Prompt |
| requestExpireTicks | int | 30000 | 3600~120000, 步进1500 | 请求过期 ticks（≈0.5 游戏天） |
| enableRequestSystem | bool | true | — | 启用审批系统（关闭则所有需审批动作直接执行） |
| enableRiskApproval | bool | true | — | 启用风险拦截（达到阈值自动拦截） |
| autoBlockRiskLevel | RiskLevel | High | Low~Critical | 自动拦截风险等级阈值 |

## 数据流

```
游戏主线程 (CompTick)
    │
    ├── Pawn.IsHashIntervalTick(pawnScanIntervalTicks)
    │       ▼
    ├── 8 项条件检查（见触发条件）
    │       ▼
    ├── RequestAIAdvice()
    │   ├── _hasPendingRequest = true
    │   ├── AdvisorConcurrencyTracker.Increment()
    │   ├── 构建 AIRequest（MaxTokens=400, Temp=0.7, UseJsonMode=true）
    │   └── RimMindAPI.RequestAsync(request, OnAdviceReceived)
    │       ▼
    │   [Core 层异步：排队 → 冷却检查 → HTTP → JSON 返回]
    │       ▼
    ├── OnAdviceReceived(AIResponse)
    │   ├── _hasPendingRequest = false
    │   ├── AdvisorConcurrencyTracker.Decrement()
    │   ├── JsonConvert.DeserializeObject<AdviceBatch>(response.Content)
    │   ├── 遍历 advices：过滤 → 审批判断 → 执行/注册请求
    │   └── 直接执行 → ExecuteBatch → 气泡 → _lastRequestTick 更新
    │       ▼
    └── 审批回调 → 单条 ExecuteBatch → 气泡 → AddRecord
```

## 冷却机制

双层冷却设计：

1. **Advisor 层冷却**（`requestCooldownTicks`，默认 30000）：
   - 每个 Pawn 独立，由 `CompAIAdvisor._lastRequestTick` 控制
   - 仅在直接执行成功后更新 `_lastRequestTick`
   - 审批类动作不更新冷却（回调中未设置 `_lastRequestTick`）

2. **Core 层冷却**（由 `AIRequestQueue` 按 ModId="Advisor" 控制）：
   - 全局共享，通过 `RimMindAPI.RegisterModCooldown("Advisor", ...)` 注册
   - 冷却时长由 `RimMindCoreMod.Settings.globalCooldownTicks` 控制

**强制请求**（`ForceRequestAdvice`）：
- 设置 `IsEnabled = true`
- 重置 `_lastRequestTick = -9999`（清除 Advisor 层冷却）
- 调用 `RimMind.Core.Internal.AIRequestQueue.Instance?.ClearCooldown("Advisor")`（清除 Core 层冷却）

## 并发控制

```csharp
// 全局原子计数器
AdvisorConcurrencyTracker.ActiveCount  // int，当前等待响应的请求数
AdvisorConcurrencyTracker.Increment()  // Interlocked.Increment
AdvisorConcurrencyTracker.Decrement()  // Interlocked.Decrement
```

- 请求发起时 Increment，响应收到时 Decrement（无论成功/失败）
- 上限由 `maxConcurrentRequests` 控制（默认 3）

## 线程安全

- `CompAIAdvisor` 所有逻辑在主线程执行（CompTick / OnAdviceReceived 回调）
- `AdvisorConcurrencyTracker` 使用 `Interlocked` 保证原子性
- HTTP 请求和 JSON 解析在 Core 层的后台线程完成，结果通过回调回主线程

## 代码约定

### 命名空间

| 命名空间 | 职责 |
|----------|------|
| `RimMind.Advisor` | Mod 入口（RimMindAdvisorMod）、DTO（AdviceBatch/AdviceItem） |
| `RimMind.Advisor.Settings` | 设置（RimMindAdvisorSettings） |
| `RimMind.Advisor.Comps` | ThingComp（CompAIAdvisor、CompProperties_AIAdvisor） |
| `RimMind.Advisor.Advisor` | 核心逻辑（AdvisorPromptBuilder、JobCandidateBuilder） |
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
  - `Source/Advisor/AdviceResponse.cs` — DTO
  - `Source/Concurrency/AdvisorConcurrencyTracker.cs` — 计数器
- 已有测试类：`AdviceBatchParseTests`（7 用例）、`ConcurrencyTrackerTests`（5 用例）

## 扩展指南

### 添加新的即时动作

1. 在 RimMind-Actions 中实现新动作（注册 intentId + 风险等级）
2. 在 `JobCandidateBuilder.AdvisorInstantActions` 中添加 intentId
3. 在 `BuildInstantHint` 中添加 switch case，返回上下文提示或 null（null=过滤掉）
4. 调整风险等级（如有必要，在 Actions 侧修改）

### 修改 Prompt

- System Prompt：修改 `AdvisorPromptBuilder.BuildSystemPrompt` 中的链式调用
- User Prompt：修改 `BuildUserPrompt` 中的 PromptSection 列表
- 玩家自定义：通过 `advisorCustomPrompt` 设置项追加到 System Prompt 末尾
- 翻译键前缀：`RimMind.Advisor.Prompt.*`

### 调试

Dev 菜单（RimMind Advisor 分类）提供 7 项动作：
| 动作 | 功能 |
|------|------|
| Show Advisor State (selected) | 选中 Pawn 的完整状态（开关/冷却/并发/API） |
| Force Request Advice (selected) | 强制请求建议（清除双层冷却） |
| Show Job Candidates (selected) | 查看候选任务列表文本 |
| Show Full Prompt (selected) | 查看 System + User Prompt 全文 |
| List All Advisor States | 列出地图所有殖民者状态 |
| Clear ALL Cooldowns | 清除 Core 层所有冷却 |
| Reset Concurrency Count | 强制归零并发计数器 |

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
      "pawn": "Alice",
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
- 即时动作 intentId — 见 `AdvisorInstantActions` 白名单（10 个）

**request_type 值域**：
- `normal` — 直接执行
- `request` — 请求玩家审批（选项：批准/拒绝/忽略）
- `high_risk` — 高风险动作，需审批（受 `enableRiskApproval` 控制）

**多 Pawn 模式**：`pawn` 字段填目标小人 `Name.ToStringShort`，为 null 时使用当前 Pawn。`target` 字段填交互对象短名（如 tend_pawn 的伤员）。

## RimMind-Core API 使用汇总

| API | 调用位置 | 用途 |
|-----|----------|------|
| `RimMindAPI.IsConfigured()` | CompAIAdvisor.CompTick | 检查 API 是否已配置 |
| `RimMindAPI.RequestAsync(request, callback)` | CompAIAdvisor.RequestAIAdvice | 发送 AI 请求 |
| `RimMindAPI.RegisterPendingRequest(entry)` | CompAIAdvisor.OnAdviceReceived | 注册审批请求 |
| `RimMindAPI.RegisterSettingsTab(id, title, draw)` | RimMindAdvisorMod 构造 | 注册设置 Tab |
| `RimMindAPI.RegisterModCooldown(modId, ticksFunc)` | RimMindAdvisorMod 构造 | 注册 Mod 冷却 |
| `RimMindAPI.RegisterPawnContextProvider(id, func, priority)` | RimMindAdvisorMod 构造 | 注册 Pawn 上下文提供者 |
| `RimMindAPI.BuildFullPawnSections(pawn)` | AdvisorPromptBuilder.BuildUserPrompt | 获取 Pawn 完整上下文 PromptSection |
| `RimMindAPI.ShouldSkipAction(intentId)` | JobCandidateBuilder.BuildInstantCandidates | 检查动作是否应被跳过（Bridge 门控） |
| `RimMindActionsAPI.GetSupportedIntents()` | CompAIAdvisor.OnAdviceReceived | 获取所有已注册 intentId |
| `RimMindActionsAPI.IsAllowed(intentId)` | CompAIAdvisor/JobCandidateBuilder | 检查动作是否被允许 |
| `RimMindActionsAPI.GetRiskLevel(intentId)` | CompAIAdvisor.OnAdviceReceived | 获取动作风险等级 |
| `RimMindActionsAPI.ExecuteBatch(intents)` | CompAIAdvisor.OnAdviceReceived | 批量执行动作意图 |
| `RimMindActionsAPI.GetWorkTargets(pawn, defName, max)` | JobCandidateBuilder | 获取工作任务目标列表 |
| `RimMindActionsAPI.GetActionDescriptions()` | JobCandidateBuilder | 获取所有动作描述（intentId/displayName/riskLevel） |
| `AIRequestQueue.Instance.ClearCooldown(modId)` | CompAIAdvisor.ForceRequestAdvice | 清除 Core 层冷却 |
| `AIRequestQueue.Instance.GetCooldownTicksLeft(modId)` | AdvisorDebugActions | 查询 Core 层剩余冷却 |
