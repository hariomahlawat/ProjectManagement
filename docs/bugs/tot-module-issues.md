# ToT module critical issues

Status of the three issues raised in the earlier ToT module review, checked against the code on 2026-09-27. The ToT tracker page is `Areas/ProjectOfficeReports/Pages/Tot/Index.cshtml.cs`. Earlier versions of this note gave the path as `Areas/Projects/...`, which is wrong.

## 1. Tracker degrades gracefully when ToT request metadata columns are missing — **Fixed**

- `ProjectTotTrackerReadService.GetAsync` tries up to three snapshot queries in turn: full columns, then without request-detail columns, then without ToT-detail columns. `TryLoadSnapshotsAsync` catches `PostgresException` with `SqlState == PostgresErrorCodes.UndefinedColumn`.
- `BuildProjectSnapshotQuery` projects `TotRequest.RowVersion` only when `includeRequestDetailColumns` is true, and `null` otherwise.
- Test evidence: `ProjectManagement.Tests/ProjectTotTrackerReadServiceTests.cs` overrides `ShouldSimulateUndefinedColumn` and asserts that `RequestRowVersion` is null on the fallback path.

## 2. Zero-length request row versions block approval — **Fixed**

- `IndexModel` sets `DecideInput.RowVersion` only when `RequestRowVersion is { Length: > 0 }`. Empty tokens are left out.
- `OnPostDecideAsync` checks whether the `DecideInput.RowVersion` form field is present at all ("no request selected"). An empty value is sent as a `null` expected row version.
- `ProjectTotService.DecideRequestAsync` compares row versions only when `expectedRowVersion is not null`. `ProjectTotRequest.RowVersion` is also an EF concurrency token (`ApplicationDbContext.ConfigureRowVersion`), so concurrent saves are still detected at `SaveChangesAsync`. However, a `DbUpdateConcurrencyException` there is not caught and would surface as a server error.

## 3. Chronological validation of MET and first-production dates — **Fixed**

- `ProjectTotService.ValidateRequest` rejects MET completion or first-production-model dates earlier than the ToT start date, and later than the ToT completion date when one is given. Messages include "MET completion date cannot be earlier than the ToT start date." and "First production model date cannot be later than the ToT completion date."
- Test evidence: `ProjectManagement.Tests/ProjectTotServiceTests.cs`.

## Related current rule: Repeat Build projects

ToT applies only to projects that are not deleted, not archived, not Repeat Build (`Project.IsBuild`) and have `LifecycleStatus == Completed` (`ProjectTotApplicabilityPolicy`). `ProjectTotService` enforces this rule on submit, on direct update and on approval. Rejection is not blocked. The tracker (`ProjectTotTrackerReadService`) and exports list only eligible projects. See `docs/ProjectOfficeReports_Directions.md` for the complete Repeat Build rules and the gaps that remain open.
