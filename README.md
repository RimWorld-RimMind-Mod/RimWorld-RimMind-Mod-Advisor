<div align="center">

# RimMind-Advisor 💡
### Autonomous Colonist Advice, Player Approval & Feedback Loop for RimWorld 1.6

**English** | [简体中文](README_zh.md)

<p>
  <a href="https://rimworldgame.com/"><img src="https://img.shields.io/badge/RimWorld-1.6-brightgreen.svg" alt="RimWorld 1.6"></a>
  <a href="https://github.com/mcocdaa/RimWorld-RimMind-Mod-Core"><img src="https://img.shields.io/badge/Dependency-RimMind--Core-blue.svg" alt="Dependency: RimMind-Core"></a>
  <a href="#"><img src="https://img.shields.io/badge/Unit%20Tests-18%2B%20Passing-success.svg" alt="Unit Tests"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT"></a>
</p>

<p><em>Transform AI thoughts into meaningful colonist advice with player approval and real-time execution feedback.</em></p>

</div>

---

## 📖 Overview

**RimMind-Advisor** empowers colonists to formulate insightful suggestions based on their current psychological thoughts, environmental stresses, and personal goals. Instead of blindly overriding player commands, colonists present structured advice proposals with clear rationale and expected outcomes.

### Core Features
- **Player Approval Flow**: Advice surfaces via a non-intrusive floating HUD card displaying colonist reasoning, urgency, and proposed actions with one-click **[Approve]** and **[Reject]** buttons.
- **Smooth Mini-Pill Auto-Collapse**: When an advice proposal is decided or times out, the window automatically collapses into an elegant mini status pill `[Pending: 0]`, preventing screen clutter.
- **3-Round Feedback Loop**: Colonists track the execution outcome of approved proposals, learning whether their suggestions successfully alleviated crises or caused new setbacks.

---

## 🎮 In-Game Showcase

![RimMind-Advisor Showcase](docs/images/showcase.jpg)
*In-game advice approval card: A colonist presents a tactical proposal to harvest wild medicinal herbs before winter, with interactive approve/reject options and status indicator.*

---

## 🏛️ Architecture & Clean Dependencies

```mermaid
flowchart TD
    Pawn["Colonist Mental & Physical State"] --> Thought["Pawn Thoughts / Desires"]
    Thought --> Advisor["RimMind-Advisor Agent"]
    Advisor --> Proposal["Structured Advice Proposal"]
    Proposal --> Overlay["Advice Approval Window (UI)"]
    Overlay --> Decision{"Player Decision"}
    Decision -- Approve --> Execute["Dispatch via Core ToolCall"]
    Decision -- Reject --> Cooldown["Record Feedback & Enter Cooldown"]
    Execute --> Feedback["3-Round Execution Evaluation"]
```

---

## 🛠️ Installation & Load Order

```text
1. Harmony
2. Core (Vanilla RimWorld)
3. RimMind-Core
4. RimMind-Advisor
```

---

## 🧪 Developer Guide & Testing

Run unit tests directly:

```powershell
dotnet test RimMind-Advisor/Tests/RimMindAdvisor.Tests.csproj -c Release
```

---

## 📜 License

Licensed under the [MIT License](LICENSE).
