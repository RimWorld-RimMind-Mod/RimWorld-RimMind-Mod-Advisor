# RimMind - Advisor

AI 驱动的殖民者顾问，当小人空闲时自动分析状态和性格，通过 LLM 选择最符合角色的下一步行动。

## RimMind 是什么

RimMind 是一套 AI 驱动的 RimWorld 模组套件，通过接入大语言模型（LLM），让殖民者拥有人格、记忆、对话和自主决策能力。

## 子模组列表与依赖关系

| 模组 | 职责 | 依赖 | GitHub |
|------|------|------|--------|
| RimMind-Core | API 客户端、请求调度、上下文打包 | Harmony | [链接](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Core) |
| RimMind-Actions | AI 控制小人的动作执行库 | Core | [链接](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Actions) |
| **RimMind-Advisor** | **AI 扮演小人做出工作决策** | Core, Actions | [链接](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Advisor) |
| RimMind-Dialogue | AI 驱动的对话系统 | Core | [链接](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Dialogue) |
| RimMind-Memory | 记忆采集与上下文注入 | Core | [链接](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Memory) |
| RimMind-Personality | AI 生成人格与想法 | Core | [链接](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Personality) |
| RimMind-Storyteller | AI 叙事者，智能选择事件 | Core | [链接](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Storyteller) |

```
Core ── Actions ── Advisor
  ├── Dialogue
  ├── Memory
  ├── Personality
  └── Storyteller
```

## 安装步骤

### 从源码安装

**Linux/macOS:**
```bash
git clone git@github.com:mcocdaa/RimWorld-RimMind-Mod-Advisor.git
cd RimWorld-RimMind-Mod-Advisor
./script/deploy-single.sh <your RimWorld path>
```

**Windows:**
```powershell
git clone git@github.com:mcocdaa/RimWorld-RimMind-Mod-Advisor.git
cd RimWorld-RimMind-Mod-Advisor
./script/deploy-single.ps1 <your RimWorld path>
```

### 从 Steam 安装

1. 安装 [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) 前置模组
2. 安装 RimMind-Core
3. 安装 RimMind-Actions
4. 安装 RimMind-Advisor
5. 在模组管理器中确保加载顺序：Harmony → Core → Actions → Advisor

<!-- ![安装步骤](images/install-steps.png) -->

## 快速开始

### 填写 API Key

1. 启动游戏，进入主菜单
2. 点击 **选项 → 模组设置 → RimMind-Core**
3. 填写你的 **API Key** 和 **API 端点**
4. 填写 **模型名称**（如 `gpt-4o-mini`）
5. 点击 **测试连接**，确认显示"连接成功"

### 启用顾问

1. 进入游戏，选中一个殖民者
2. 在信息栏点击 **"AI顾问：关闭"** 切换为 **"AI顾问：开启"**
3. 小人空闲时自动触发 AI 决策
4. 开启"显示 AI 决策气泡"后，决策理由会显示在殖民者头顶

<!-- ![顾问开关](images/screenshot-advisor-toggle.png) -->

## 截图展示

<!-- ![AI决策气泡](images/screenshot-advisor-bubble.png) -->
<!-- ![审批窗口](images/screenshot-advisor-approval.png) -->
<!-- ![设置界面](images/screenshot-advisor-settings.png) -->

## 核心功能

### 智能空闲决策

殖民者空闲时，Advisor 自动触发：

1. **状态分析** — 收集小人的人格、技能、心情、健康等上下文
2. **候选生成** — 构建当前可行的任务列表（工作 + 即时动作）
3. **AI 决策** — 向 LLM 发送 Prompt，获取角色一致的行动建议
4. **动作执行** — 通过 RimMind-Actions 执行决策

### 角色扮演

AI 深度扮演殖民者本人，决策理由显示在头顶气泡中，体现角色内心独白。配合 RimMind-Personality 的人格档案，保持行为一致性。

### 审批系统

高风险动作自动弹出审批窗口，玩家可以批准、拒绝或忽略。审批历史记录注入后续 AI 请求的上下文，让 AI 学习玩家偏好。

### 并发控制

限制同时等待响应的请求数（默认 3 个），防止 API 限流和游戏卡顿。

## 设置项

| 设置 | 默认值 | 说明 |
|------|--------|------|
| 启用 AI 顾问系统 | 开启 | 总开关 |
| 小人空闲时触发 | 开启 | 空闲时自动触发 |
| 心情低时触发 | 开启 | 心情低于阈值额外触发 |
| 心情触发阈值 | 40% | 触发额外评估的心情线 |
| 请求冷却 | 1.4 游戏小时 | 每个殖民者的独立冷却 |
| 最大并发请求数 | 3 | 同时等待响应的请求上限 |
| 显示 AI 决策气泡 | 开启 | 在头顶显示决策理由 |
| 启用审批系统 | 开启 | 高风险动作需玩家审批 |
| 自动拦截风险级别 | High | 此级别及以上自动拦截 |
| 自定义顾问 Prompt | - | 追加在系统 Prompt 末尾 |

## 常见问题

**Q: 每个殖民者都要单独开启吗？**
A: 是的。选中殖民者 → 信息栏点击"AI顾问"按钮切换。默认关闭，避免不想要的殖民者被 AI 控制。

**Q: AI 会做出危险决策吗？**
A: AI 被提示优先选择低风险动作。高风险动作需要玩家审批，可在设置中调整自动拦截级别。

**Q: 配合 Personality 和 Memory 效果更好吗？**
A: 是的。Personality 提供人格档案，Memory 提供历史记忆，Advisor 会综合这些信息做出更符合角色的决策。

**Q: 会影响游戏帧率吗？**
A: 不会。所有 AI 请求异步执行，并发数有上限控制。

## 致谢

本项目开发过程中参考了以下优秀的 RimWorld 模组：

- [RimTalk](https://github.com/jlibrary/RimTalk.git) - 对话系统参考
- [RimTalk-ExpandActions](https://github.com/sanguodxj-byte/RimTalk-ExpandActions.git) - 动作扩展参考
- [NewRatkin](https://github.com/solaris0115/NewRatkin.git) - 种族模组架构参考
- [VanillaExpandedFramework](https://github.com/Vanilla-Expanded/VanillaExpandedFramework.git) - 框架设计参考

## 贡献

欢迎提交 Issue 和 Pull Request！如果你有任何建议或发现 Bug，请通过 GitHub Issues 反馈。


---

# RimMind - Advisor (English)

An AI-driven colonist advisor that automatically analyzes state and personality when colonists are idle, using LLM to choose the most character-appropriate next action.

## What is RimMind

RimMind is an AI-driven RimWorld mod suite that connects to Large Language Models (LLMs), giving colonists personality, memory, dialogue, and autonomous decision-making.

## Sub-Modules & Dependencies

| Module | Role | Depends On | GitHub |
|--------|------|------------|--------|
| RimMind-Core | API client, request dispatch, context packaging | Harmony | [Link](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Core) |
| RimMind-Actions | AI-controlled pawn action execution | Core | [Link](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Actions) |
| **RimMind-Advisor** | **AI role-plays colonists for work decisions** | Core, Actions | [Link](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Advisor) |
| RimMind-Dialogue | AI-driven dialogue system | Core | [Link](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Dialogue) |
| RimMind-Memory | Memory collection & context injection | Core | [Link](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Memory) |
| RimMind-Personality | AI-generated personality & thoughts | Core | [Link](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Personality) |
| RimMind-Storyteller | AI storyteller, smart event selection | Core | [Link](https://github.com/mcocdaa/RimWorld-RimMind-Mod-Storyteller) |

## Installation

### Install from Source

**Linux/macOS:**
```bash
git clone git@github.com:mcocdaa/RimWorld-RimMind-Mod-Advisor.git
cd RimWorld-RimMind-Mod-Advisor
./script/deploy-single.sh <your RimWorld path>
```

**Windows:**
```powershell
git clone git@github.com:mcocdaa/RimWorld-RimMind-Mod-Advisor.git
cd RimWorld-RimMind-Mod-Advisor
./script/deploy-single.ps1 <your RimWorld path>
```

### Install from Steam

1. Install [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
2. Install RimMind-Core
3. Install RimMind-Actions
4. Install RimMind-Advisor
5. Ensure load order: Harmony → Core → Actions → Advisor

## Quick Start

### API Key Setup

1. Launch the game, go to main menu
2. Click **Options → Mod Settings → RimMind-Core**
3. Enter your **API Key** and **API Endpoint**
4. Enter your **Model Name** (e.g., `gpt-4o-mini`)
5. Click **Test Connection** to confirm

### Enable Advisor

1. In-game, select a colonist
2. Click **"AI Advisor: OFF"** in the info panel to toggle **"AI Advisor: ON"**
3. AI decisions trigger automatically when the colonist is idle
4. Enable "Show AI Decision Bubbles" to see reasoning above colonists

## Key Features

- **Smart Idle Decisions**: Automatically triggers when colonists are idle — analyzes state, generates candidates, AI decides, Actions executes
- **Role-Playing**: AI deeply role-plays as the colonist, with decision reasoning shown as thought bubbles
- **Approval System**: High-risk actions require player approval; approval history feeds back into AI context
- **Concurrency Control**: Limits simultaneous AI requests (default: 3) to prevent API throttling

## FAQ

**Q: Do I need to enable advisor for each colonist?**
A: Yes. Select a colonist → click "AI Advisor" button in info panel. Default is OFF to avoid unwanted AI control.

**Q: Will AI make dangerous decisions?**
A: AI is prompted to prefer low-risk actions. High-risk actions require player approval. Auto-block level is configurable.

**Q: Does it work better with Personality and Memory?**
A: Yes. Personality provides character profiles, Memory provides history, and Advisor combines these for more character-appropriate decisions.

**Q: Will it affect game FPS?**
A: No. All AI requests are async with concurrency limits.

## Acknowledgments

This project references the following excellent RimWorld mods:

- [RimTalk](https://github.com/jlibrary/RimTalk.git) - Dialogue system reference
- [RimTalk-ExpandActions](https://github.com/sanguodxj-byte/RimTalk-ExpandActions.git) - Action expansion reference
- [NewRatkin](https://github.com/solaris0115/NewRatkin.git) - Race mod architecture reference
- [VanillaExpandedFramework](https://github.com/Vanilla-Expanded/VanillaExpandedFramework.git) - Framework design reference

## Contributing

Issues and Pull Requests are welcome! If you have any suggestions or find bugs, please feedback via GitHub Issues.
