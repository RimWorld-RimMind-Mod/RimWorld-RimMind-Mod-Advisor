# Advisor request cycle

## Responsibility

This slice turns an eligible pawn state into one bounded AI decision cycle. It owns
request submission, approval aggregation, tool execution, feedback, and terminal
cleanup. Verse scanning and save data stay at the edges.

## Start here

Read the files in this order:

1. `../Comps/CompAIAdvisor.cs` — Verse adapter and persisted enable toggle.
2. `AdvisorGameComponent.cs` — throttled pawn trigger scan.
3. `AdvisorCycleCoordinator.cs` — cycle state and terminal semantics.
4. `AdvisorTaskDriver.cs` — request envelope and feedback construction.
5. `AdvisorRequestCycleState.cs` — pure approval/feedback state machine.
6. `ApprovalManager.cs` — player approval registration.
7. `AdvisorToolCallExecutor.cs` — main-thread tool execution boundary.

## Invariants

- Every callback captures and validates its original driver and cycle.
- Direct and approved tool results form one ordered feedback batch per response.
- Tool execution remains on the main thread, even when a handler returns a task.
- Every started cycle reaches one terminal cleanup and releases concurrency once.

## Focused verification

From the repository root:

```powershell
dotnet test RimMind-Advisor/Tests/RimMindAdvisor.Tests.csproj -c Release --filter FullyQualifiedName~AdvisorLifecycleContracts
```

Use the full Advisor test project before committing behavior changes. The game
Autotester remains a separate runtime verification step.
