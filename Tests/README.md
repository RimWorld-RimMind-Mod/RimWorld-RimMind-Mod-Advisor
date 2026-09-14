# RimMind Advisor contract tests

The active suite covers these boundaries:

- `AdvisorRecommendationContracts` — structured recommendation parsing, optional legacy fallback, prompt augmentation, and empty-response safety.
- `AdvisorActionBoundaryContracts` — approval/risk policy, Core Tool/Mechanism execution, and stable tool-result errors.
- `AdvisorLifecycleContracts` — concurrency ownership, request-cycle feedback, bounded history, and registered instant hints.
- `AdvisorContextIntegrationContracts` — real initial-envelope factory and feedback driver reaching the registered Decision providers, including foreign-scenario isolation.
- `AdvisorPreviewContracts` — real registered debug command returns before async context completion, publishes through the main-thread handoff, and ignores old-game results.

Current discovery count: 17 cases. All Advisor test projects combined allow at most 999 discovered cases (less than 1000), counting every parameterized data row. Do not merge unrelated scenarios merely to reduce discovery count.

The behavior contracts execute the production recommendation parser, approval policy, `ApprovalManager`, Core `RequestEntry`, feedback driver/session, provider registrar, request capacity, and tool executor. Approval coverage includes selected/rejected/expired/evicted/dismissed terminal outcomes without a live world clock, duplicate completion, and registration-failure cleanup. Tests no longer route approval checks through the unconnected `AdvisorApprovalGateAdapter`.

The Core request/registration interfaces and Verse world/UI services are test boundaries, not copied business algorithms. The deferred builder and queued `LongEventHandler` fixture exercise the actual preview entry point without running RimWorld. Some pre-existing lifecycle scenarios still contain source assertions; those assertions are not runtime integration evidence.

New or changed tests must verify real behavior, failure boundaries, and module collaboration. Substitutes isolate external dependencies only: do not test only mocks, copy production algorithms, or lock private implementation shape.

## Run

From the repository root:

```powershell
dotnet test RimMind-Advisor/Tests/RimMindAdvisor.Tests.csproj -c Release
```

This is not deployment or in-game verification.

## Cutover handoff

The active compile entry should be `Contracts/**/*.cs` plus:

```xml
<Compile Include="..\..\RimMind-Core\TestSupport\ContractCaseRunner.cs"
         Link="Support\ContractCaseRunner.cs" />
```

`RimMindAdvisor.Tests.csproj` is the authoritative production-source manifest. Keep its links and the `VerseStubs.cs` support include required by these contracts.
The legacy compile categories superseded at cutover are recommendation/parser tests, approval/risk/executor tests, and lifecycle/history/hint tests outside `Contracts/`.

## Retired legacy tests

Files outside `Contracts/` are retained on disk but excluded from compilation.
Their behavior mapping is recorded in the root contract mapping document.
Deletion requires explicit owner approval for each exact file path; directories are never deleted.
