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
4. `AdvisorTaskDriver.cs` and `AdvisorRequestAugmentationFactory.cs` — initial and feedback envelopes, both using Core's `ScenarioDecision`.
5. `AdvisorRequestCycleState.cs` — pure approval/feedback state machine.
6. `AdvisorApprovalPolicy.cs` and `ApprovalManager.cs` — risk/explicit-request rules, player approval registration, and terminal callbacks.
7. `AdvisorToolCallExecutor.cs` — main-thread tool execution boundary.

`AdvisorProviderRegistrar` also publishes a bounded synchronous Advisor history brief for optional consumers; Verse data is read on the main thread.

`advisor_task` and `actions_list` are registered for the same `ScenarioDecision` used by both request paths. `AdvisorHistoryStore` owns decision history; `ApprovalManager` does not duplicate it. The development-menu prompt preview in `../Debug/AdvisorDebugActions.cs` awaits Core's async snapshot builder, posts logging back through `LongEventHandler`, and discards results from an old game.

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

Use the full Advisor test project before committing behavior changes. Context integration contracts execute the real envelope factory, feedback driver, and registered provider callbacks; preview contracts invoke the registered debug command with a deferred context builder. See `../../Tests/README.md` for scope and limitations.

All Advisor test projects combined must stay below 1000 discovered cases (maximum 999), counting each parameterized row. Test real behavior, failure boundaries, and collaboration; use substitutes only at external boundaries. Do not test only mocks, copy production algorithms, pin private implementation shape, or combine unrelated scenarios to reduce the count. The game Autotester remains a separate runtime verification step.
