# RimMind - Advisor

你的私人 AI 殖民地顾问，为每个殖民者提供个性化的行动建议。

## 核心能力

**个性化决策** - AI 顾问会综合考虑殖民者的状态、性格、技能和当前可用工作，选择最符合角色特点的行动。

**深度角色扮演** - AI 深度扮演殖民者本人，决策理由显示在头顶气泡中，体现角色内心独白。

**候选工作展示** - 向 AI 展示当前可用的工作目标列表（按距离排序），让 AI 做出知情选择，而非盲目决策。

**审批系统** - 支持两层审批机制：殖民者主动请求需玩家批准，高风险动作自动拦截需玩家批准。审批历史注入 AI 上下文，让 AI 学习玩家偏好。

**风险控制** - 可配置自动拦截的风险等级阈值（Low/Medium/High/Critical），达到阈值的动作自动弹出审批窗口。

**并发控制** - 智能管理多个殖民者的顾问请求，限制同时等待响应数，避免 API 限流和游戏卡顿。

**请求过期** - AI 请求设有过期时间，超时未响应自动取消，避免请求堆积。

**自定义提示词** - 支持在设置中添加自定义规则，追加在系统 Prompt 末尾，让顾问按照你的偏好行事。

## 使用场景

- 不知道让哪个殖民者去做什么？Advisor 会给出建议
- 想让人物行为更符合其背景故事？AI 会考虑童年和成年经历
- 需要平衡工作效率和角色扮演？Advisor 兼顾两者
- 担心 AI 做出危险决策？高风险动作需要你审批
- 想让殖民者主动提出需求？开启请求系统，殖民者会向玩家提出行动请求

## 设置项

| 设置 | 默认值 | 说明 |
|------|--------|------|
| 启用 AI 顾问系统 | 开启 | 总开关 |
| 小人空闲时触发 | 开启 | 空闲时自动触发 |
| 小人扫描间隔 | 3600 ticks（~60s） | 检测空闲小人的频率 |
| 心情低时触发 | 开启 | 心情低于阈值额外触发 |
| 心情触发阈值 | 30% | 触发额外评估的心情线 |
| 请求冷却 | 12 游戏小时（30000 ticks） | 每个殖民者的独立冷却 |
| 最大并发请求数 | 3 | 同时等待响应的请求上限 |
| 请求过期 | 0.5 游戏天（30000 ticks） | 请求超时自动取消 |
| 显示 AI 决策气泡 | 开启 | 在头顶显示决策理由 |
| 启用审批系统 | 开启 | 殖民者请求需玩家批准 |
| 启用风险拦截 | 开启 | 高风险动作需玩家批准 |
| 自动拦截风险级别 | High | 此级别及以上自动拦截 |
| 自定义顾问 Prompt | 空 | 追加在系统 Prompt 末尾 |

## 建议配图

1. 顾问建议的气泡截图（展示 AI 决策理由）
2. 审批窗口截图（展示高风险动作审批流程）
3. 设置界面的自定义提示词区域

---

# RimMind - Advisor (English)

Your personal AI colony advisor, providing personalized action recommendations for each colonist.

## Key Features

**Personalized Decision Making** - AI advisor considers colonist state, personality, skills, and available work to choose actions that best fit the character.

**Deep Role-Playing** - AI deeply role-plays as the colonist, with decision reasoning shown as thought bubbles above their head.

**Candidate Work Display** - Shows AI a list of currently available work targets (sorted by distance), enabling informed decisions rather than blind choices.

**Approval System** - Two-tier approval: colonist initiative requests require player approval, and high-risk actions are automatically blocked for approval. Approval history feeds back into AI context.

**Risk Control** - Configurable auto-block risk level threshold (Low/Medium/High/Critical). Actions at or above the threshold automatically pop up an approval window.

**Concurrency Control** - Intelligently manages advisor requests from multiple colonists, limiting simultaneous requests to prevent API throttling.

**Request Expiry** - AI requests have an expiration time. Unanswered requests are automatically cancelled to prevent accumulation.

**Custom Prompts** - Supports adding custom rules in settings, appended to the end of the system prompt, to make the advisor behave according to your preferences.

## Use Cases

- Don't know which colonist should do what? Advisor gives suggestions
- Want character behavior to match their backstory? AI considers childhood and adulthood experiences
- Need to balance work efficiency with roleplay? Advisor handles both
- Worried about dangerous AI decisions? High-risk actions require your approval
- Want colonists to proactively request things? Enable the request system for colonist-initiated action requests

## Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Enable AI Advisor System | On | Master switch |
| Trigger on Colonist Idle | On | Auto-trigger when idle |
| Pawn Scan Interval | 3600 ticks (~60s) | Frequency of scanning for idle colonists |
| Trigger on Low Mood | On | Additional trigger when mood is low |
| Mood Trigger Threshold | 30% | Mood percentage line for triggering |
| Request Cooldown | 12 game hours (30000 ticks) | Per-colonist independent cooldown |
| Max Concurrent Requests | 3 | Upper limit for simultaneous pending requests |
| Request Expiry | 0.5 game days (30000 ticks) | Auto-cancel unanswered requests |
| Show AI Decision Bubble | On | Show reasoning above colonist |
| Enable Request System | On | Colonist requests require player approval |
| Enable Risk Blocking | On | High-risk actions require player approval |
| Auto-block Risk Level | High | Auto-block at this level and above |
| Custom Advisor Prompt | Empty | Appended to system prompt end |

## Suggested Screenshots

1. Thought bubble showing AI decision reasoning
2. Approval window for high-risk actions
3. Custom prompt area in settings
