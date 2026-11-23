# RimMind Advisor contract tests

The active suite covers these boundaries:

- `AdvisorRecommendationContracts` — structured recommendation parsing, optional legacy fallback, summary formatting, mote wrapping, prompt augmentation, and empty-response safety.
- `AdvisorActionBoundaryContracts` — approval/risk policy, manifest-driven risk resolution, Core Tool/Mechanism execution, and stable tool-result errors.
- `AdvisorLifecycleContracts` — concurrency ownership, request-cycle feedback, bounded history & `PawnAdvisorHistory` Scribe wrapper, public history brief provider, and spoofed pre-approval stripping.
- `AdvisorContextIntegrationContracts` — real initial-envelope factory and feedback driver reaching the registered `ScenarioIds.AdvisorDecision` providers, compact scoped tool projection, and foreign-scenario isolation.
- `AdvisorPreviewContracts` — real registered debug command returns before async context completion, publishes through `RimMindAPI.MainThread`, and ignores old-game results.
- `AdvisorQueueAndToolIsolationContracts` — queue cooldown identity, per-call exception isolation, main-thread batch execution, and single activity-scaled cooldown source.
- `AdvisorToolDomainMapContracts` — manifest & dynamic `IToolPolicyClassifier` domain/risk resolution with zero hardcoded `actions_*` lookup tables.
- `AdvisorAutonomousPerceptionContracts` & `ColonyMacroPerceiverTests` — active pawn/animal filtering, macro bottleneck evaluation (including colonist comfy-temp bounds), and `CabinetProposal` approval callbacks.

All Advisor test projects combined allow at most 999 discovered cases (less than 1000), counting every parameterized data row. Do not merge unrelated scenarios merely to reduce discovery count.

New or changed tests must verify real behavior, failure boundaries, and module collaboration. Substitutes isolate external dependencies only: do not test only mocks, copy production algorithms, or lock private implementation shape.

## Run

From the repository root:

```powershell
dotnet test RimMind-Advisor/Tests/RimMindAdvisor.Tests.csproj -c Release
```

This is not deployment or in-game verification.

## Manifest

`RimMindAdvisor.Tests.csproj` is the authoritative production-source manifest (`<EnableDefaultCompileItems>false</EnableDefaultCompileItems>`), compiling `Contracts/**/*.cs`, `VerseStubs.cs`, and `..\..\RimMind-Core\TestSupport\ContractCaseRunner.cs` under `C# 9.0` compatibility.

Deletion of any test file requires explicit owner approval for that exact file path; directories are never deleted.
