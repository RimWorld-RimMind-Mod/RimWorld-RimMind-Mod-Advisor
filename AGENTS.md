# AGENTS.md — RimMind-Advisor

AI决策层，空闲/心情低落时LLM角色扮演选择最优行动，Tool Calling架构。

## 项目定位

CompAIAdvisor 作为 ThingComp 挂载殖民者：空闲检测 → ContextEngine构建Prompt → RequestStructuredAsync(ToolCalling) → 解析StructuredToolCall → 审批高风险 → ExecuteBatchWithResults → 反馈循环(最多3层)。含双层冷却(Advisor层+Core层)、并发控制(Interlocked)、决策历史持久化。

依赖: Core + Actions(编译期)，被其他模块消费(上下文Provider)。

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
├── RimMindAdvisorMod.cs                Mod入口: Harmony + SettingsTab + Cooldown + ContextKey
├── Settings/RimMindAdvisorSettings.cs  13项设置
├── Comps/CompAIAdvisor.cs             核心ThingComp(Tick触发/AI请求/ToolCall处理/反馈循环/Gizmo)
├── Advisor/JobCandidateBuilder.cs      候选任务列表(工作+9个即时动作)
├── Data/
│   ├── AdvisorRequestRecord.cs         单条决策记录
│   └── AdvisorHistoryStore.cs          WorldComponent按Pawn存储
├── Patches/AddCompToHumanlikePatch.cs  Humanlike注入Comp
├── Concurrency/AdvisorConcurrencyTracker.cs  全局原子计数器
└── Debug/AdvisorDebugActions.cs
```

## 触发条件 (CompTick, 9项检查)

1. enableAdvisor总开关 → 2. API已配置 → 3. Agent未激活 → 4. 无待处理请求(超时60000tick重置) → 5. IsEnabled → 6. IsEligible(IsFreeNonSlaveColonist && !Dead && !Drafted) → 7. 空闲触发(enableIdleTrigger && IsIdle) 或 心情触发(enableMoodTrigger && mood<阈值) → 8. Advisor层冷却结束 → 9. 并发<上限

## Tool Calling 响应处理

```
AIResponse.ToolCallsJson → List<StructuredToolCall>
  ├── 过滤: Name未supported/!IsAllowed → 跳过
  ├── 解析 Arguments → target/param/reason
  ├── 审批: enableRiskApproval && riskLevel >= autoBlockRiskLevel
  │   ├── 需审批 && enableRequestSystem → RegisterPendingRequest(批准/拒绝)
  │   └── 需审批 && !enableRequestSystem → 跳过
  └── 直接执行 → ExecuteBatchWithResults → 气泡 → AddRecord
       └── _toolCallDepth < 3 → RequestToolFeedback(最多3层反馈循环)
```

Content Fallback: ToolCallsJson空时尝试解析 `{"advices":[...]}` 旧格式。

## 即时动作白名单 (9个)

| intentId | 过滤条件 |
|----------|---------|
| force_rest | 始终显示 |
| social_relax | 始终显示 |
| eat_food | 无JoyFood→过滤 |
| tend_pawn | 无受伤殖民者→过滤 |
| rescue_pawn | 无倒地殖民者→过滤 |
| inspire_work/shoot/trade | 已有灵感→过滤 |
| move_to | 始终显示 |

## 双层冷却

1. Advisor层: `requestCooldownTicks`(默认30000), 每Pawn独立, CompleteRequestCycle时更新
2. Core层: 全局共享, `RimMindAPI.RegisterModCooldown("Advisor",...)`

## 代码约定

- Harmony ID: `mcocdaa.RimMindAdvisor`, Postfix on `ThingDef.ResolveReferences`
- 序列化: ThingComp → `PostExposeData`; WorldComponent → `ExposeData`
- 翻译键前缀: `RimMind.Advisor.*`

## 操作边界

### ✅ 必须做
- 修改触发条件后更新本文件触发条件清单
- 修改审批逻辑后验证风险分级正确
- 新即时动作在 `BuildInstantHint` 添加case(返回null=过滤)

### ⚠️ 先询问
- 修改 `MaxToolCallDepth`(当前3)
- 修改 `maxConcurrentRequests` 默认值
- 重构 `CompAIAdvisor` 为独立 `GameComponent`

### 🚫 绝对禁止
- 后台线程调用 `RimMindActionsAPI.ExecuteBatchWithResults`
- 硬编码 `EatFoodAction.GetJoyFoodLabels` 直接引用
- 绕过 `RimMindAPI` 直接调用 `AIRequestQueue.Instance.ClearCooldown`
