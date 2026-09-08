# RimMind Advisor contract tests

The retained suite is organized around three stable public boundaries:

- `AdvisorRecommendationContracts` — structured recommendation parsing, optional legacy fallback, prompt augmentation, and empty-response safety.
- `AdvisorActionBoundaryContracts` — approval/risk policy, Core Tool/Mechanism execution, and stable tool-result errors.
- `AdvisorLifecycleContracts` — concurrency ownership, request-cycle feedback, bounded history, and registered instant hints.

The compact suite contains 7 Facts and no Theory rows, below the Advisor budget of 48 discovered tests.

The active contracts execute the production recommendation parser, strict
approval policy, feedback session, request capacity and tool executor. They do
not inspect production source text.

## Cutover handoff

The active compile entry should be `Contracts/**/*.cs` plus:

```xml
<Compile Include="..\..\RimMind-Core\TestSupport\ContractCaseRunner.cs"
         Link="Support\ContractCaseRunner.cs" />
```

Keep the existing production-source links and `VerseStubs.cs` support include required by these contracts.
The legacy compile categories superseded at cutover are recommendation/parser tests, approval/risk/executor tests, and lifecycle/history/hint tests outside `Contracts/`.

## Retired legacy tests

Files outside `Contracts/` are retained on disk but excluded from compilation.
Their behavior mapping is recorded in the root contract mapping document.
Deletion requires explicit owner approval for each exact file path; directories are never deleted.
