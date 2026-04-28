# AGENTS.md — RimMind-Advisor

AI决策层，空闲/心情低落时LLM角色扮演选择最优行动，Tool Calling架构。

## 项目定位

AdvisorGameComponent 每隔 pawnScanIntervalTicks 扫描殖民者 → CompAIAdvisor 检查触发条件 → AdvisorTaskDriver 构建 ContextEngine Prompt + Tool Calling 请求 → 解析 StructuredToolCall → 审批高风险 → ExecuteBatchWithResults → 反馈循环(最多3层)。含双层冷却(Advisor层+Core层)、并发控制(Interlocked)、决策历史持久化、决策事件广播(PublishPerception)。

依赖: Core + Actions(编译期)，被其他模块消费(上下文Provider + Perception事件)。

## 构建

| 项 | 值 |
|----|-----|
| Target | net48, C#9.0, Nullable enable |
| Output | `../1.6/Assemblies/` |
| Assembly | RimMindAdvisor |
| 依赖 | RimMindCore.dll, RimMindActions.dll, Krafs.Rimworld.Ref, Lib.Harmony.Ref, Newtonsoft.Json |

## 源码结构

```
Source/
├── RimMindAdvisorMod.cs                Mod入口: Harmony + SettingsTab + Cooldown + ContextKey + PawnContextProvider
├── Settings/RimMindAdvisorSettings.cs  13项设置(全部有XML文档注释)
├── Comps/
│   ├── CompAIAdvisor.cs                核心ThingComp(状态判断/AI请求/ToolCall处理/审批/反馈循环/Gizmo)
│   └── CompProperties_AIAdvisor.cs     CompProperties(仅设compClass)
├── Advisor/
│   ├── AdvisorTaskDriver.cs            请求构建/ToolCall解析/反馈循环/决策广播
│   ├── AdvisorGameComponent.cs         GameComponent Tick循环: 扫描→触发→并发控制
│   ├── ApprovalManager.cs             ⚠️ 死代码(未被使用，CompAIAdvisor有内联审批逻辑)
│   ├── AdvisorResponse.cs             ⚠️ 死代码(AdviceItem + AdvisorResponse静态类未被使用)
│   └── JobCandidateBuilder.cs          候选任务列表(工作+9个即时动作)
├── Data/
│   ├── AdvisorRequestRecord.cs         单条决策记录(IExposable)
│   └── AdvisorHistoryStore.cs          WorldComponent按Pawn存储(每Pawn限50/全局限200)
├── Patches/AddCompToHumanlikePatch.cs  Humanlike注入Comp(Harmony Postfix on ThingDef.ResolveReferences)
├── Concurrency/AdvisorConcurrencyTracker.cs  全局原子计数器(Interlocked, 负值自纠正)
└── Debug/AdvisorDebugActions.cs        9个DebugAction(ShowState/ForceRequest/ShowPrompt/ListAll/ClearCooldown等)
```

## 核心流程

### 1. 触发 (AdvisorGameComponent.GameComponentTick)

```
enableAdvisor → IsConfigured → 每pawnScanIntervalTicks扫描 → EvaluateAllPawns
  → 遍历 Find.Maps → FreeColonists → GetComp<CompAIAdvisor>
  → 检查: IsEligible && !HasPendingRequest && IsEnabled && !CompPawnAgent.IsAgentActive
  → 触发: ShouldIdleTrigger || ShouldMoodTrigger
  → 冷却: Advisor层(requestCooldownTicks)已过
  → 并发: activeCount < maxConcurrentRequests
  → comp.RequestAdvice(settings)
```

### 2. 请求 (AdvisorTaskDriver.BuildAndSendRequest)

```
ContextRequest(Scenario=Decision, Budget=ContextBudget) → BuildContextSnapshot
  → advisorCustomPrompt: 插入到最后一个system消息之后(Insert(lastSysIdx+1))
  → BuildActionTools → RimMindActionsAPI.GetStructuredTools
  → AIRequest(ModId="Advisor", UseJsonMode=true, Schema=AdviceOutput)
  → RimMindAPI.RequestStructuredAsync(request, schema, callback, tools)
```

### 3. 响应处理 (CompAIAdvisor.OnAdviceReceived)

```
AIResponse.ToolCallsJson → TryParseToolCalls → List<StructuredToolCall>
  ├── (无ToolCalls) → Content Fallback: TryParseContentAsToolCalls({"advices":[...]})
  ├── 过滤: Name未supported / !IsAllowed → 跳过
  ├── 解析 Arguments → target/param/reason
  ├── 审批: enableRiskApproval && riskLevel >= autoBlockRiskLevel
  │   ├── 需审批 && enableRequestSystem → RegisterPendingRequest(批准/拒绝)
  │   └── 需审批 && !enableRequestSystem → 跳过
  └── 直接执行 → ExecuteBatchWithResults → BroadcastDecisionExecuted → AddRecord → 气泡
       └── ShouldRequestFeedback → RequestToolFeedback(最多3层反馈循环)
```

### 4. 反馈循环 (AdvisorTaskDriver.RequestToolFeedback)

```
_toolCallDepth++ → 构建assistant+tool消息 → RequestStructuredAsync → OnAdviceReceived
  → 深度检查: _toolCallDepth < MaxToolCallDepth(3)
  → 超过深度 → CompleteRequestCycle
```

### 5. 完成周期 (CompAIAdvisor.CompleteRequestCycle)

```
_hasPendingRequest=false → _lastRequestTick=now → Decrement → _taskDriver.ClearState → _taskDriver=null
```

## 即时动作白名单 (9个)

| intentId | BuildInstantHint 过滤条件 |
|----------|-------------------------|
| force_rest | 始终显示(体力%) |
| social_relax | 始终显示(心情%) |
| eat_food | GetActionHintData返回空→过滤 |
| tend_pawn | 无受伤殖民者→过滤 |
| rescue_pawn | 无倒地殖民者→过滤 |
| inspire_work | 已有灵感→过滤 |
| inspire_shoot | 已有灵感→过滤 |
| inspire_trade | 已有灵感→过滤 |
| move_to | 始终显示 |

## 双层冷却

1. Advisor层: `requestCooldownTicks`(默认30000), 每Pawn独立, CompleteRequestCycle时更新
2. Core层: 全局共享, `RimMindAPI.RegisterModCooldown("Advisor",...)`

## 决策广播

`AdvisorTaskDriver.BroadcastDecisionExecuted` → `RimMindAPI.PublishPerception(pawnId, "advisor_decision", summary, 0.5f)`

## 设置项 (13项，全部有XML文档注释+序列化+UI+重置)

| 字段 | 类型 | 默认 | UI控件 |
|------|------|------|--------|
| enableAdvisor | bool | true | Checkbox |
| enableIdleTrigger | bool | true | Checkbox |
| pawnScanIntervalTicks | int | 3600 | Slider(600~6000, 步进100) |
| enableMoodTrigger | bool | true | Checkbox |
| moodThreshold | float | 0.3 | Slider(0.25~0.6) |
| showThoughtBubble | bool | true | Checkbox |
| advisorCustomPrompt | string | "" | TextEntry(5行) |
| requestCooldownTicks | int | 30000 | Slider(3600~72000, 步进600) |
| maxConcurrentRequests | int | 3 | Slider(1~5) |
| requestExpireTicks | int | 30000 | Slider(3600~120000, 步进1500) |
| enableRequestSystem | bool | true | Checkbox |
| enableRiskApproval | bool | true | Checkbox |
| autoBlockRiskLevel | RiskLevel | High | Slider(0~3) |

## 代码约定

- Harmony ID: `mcocdaa.RimMindAdvisor`, Postfix on `ThingDef.ResolveReferences`
- 序列化: ThingComp → `PostExposeData`; WorldComponent → `ExposeData`; GameComponent → `ExposeData`
- 翻译键前缀: `RimMind.Advisor.*`
- TaskInstruction: `ContextKeyRegistry.Register("advisor_task", ...)` → `TaskInstructionBuilder.Build("RimMind.Advisor.Prompt.TaskInstruction", subKeys...)`
- PawnContextProvider: `RimMindAPI.RegisterPawnContextProvider("advisor_history", ...)` [Obsolete, 应迁移到ContextKeyRegistry]

## 操作边界

### ✅ 必须做
- 修改触发条件后更新本文件核心流程清单
- 修改审批逻辑后验证风险分级正确
- 新即时动作在 `BuildInstantHint` 添加case(返回null=过滤)
- 修改 AdvisorTaskDriver 后验证反馈循环深度逻辑

### ⚠️ 先询问
- 修改 `MaxToolCallDepth`(当前3)
- 修改 `maxConcurrentRequests` 默认值
- 重构 `CompAIAdvisor` 审批逻辑为使用 `ApprovalManager`
- 删除 `ApprovalManager.cs` / `AdvisorResponse.cs` 死代码

### 🚫 绝对禁止
- 后台线程调用 `RimMindActionsAPI.ExecuteBatchWithResults`
- 绕过 `RimMindAPI` 直接调用 `AIRequestQueue.Instance.ClearCooldown`
- 审批路径不调用 `BroadcastDecisionExecuted`（与直接执行路径必须一致）
