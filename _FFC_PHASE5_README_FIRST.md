# FFC Phase 5 — Transaction and Persistence Reliability

## Apply this package when

FFC Phases 1–4 are already present. Extract this package into the **ProjectManagement project root**, preserve the folder structure, and replace matching files.

No database migration is required.

## Primary correction

The linked-project save failure was caused by transaction composition:

- `FfcProjectCommandService` opened the root transaction.
- `FfcProgressService` called `RemarkService` to create or edit the canonical external remark.
- `RemarkService` attempted to open another transaction on the same scoped `ApplicationDbContext`.

The transaction infrastructure is now composable:

- A root `RelationalTransactionScope` owns the database transaction.
- A nested scope participates through a uniquely named savepoint.
- Nested commit releases only its savepoint.
- Nested rollback rolls back only to its savepoint.
- Only the root scope commits, rolls back and disposes the database transaction.
- Post-commit callbacks execute only after the managed root transaction commits.
- Post-commit callback failures cannot convert a successful database commit into an apparent primary-operation failure.

This preserves the required atomic rule:

> The FFC project and its shared PRISM Project external remark are committed together, or neither is committed.

## Additional reliability improvements

- `FfcProjectCommandService` now uses the common transaction scope rather than direct `BeginTransactionAsync` handling.
- Failed composed operations roll back and clear EF Core tracked state so generated IDs and modified ghost entities are not retained.
- Unexpected failures include a short `FFC-...` correlation reference that is also written to the server log.
- Duplicate links, progress validation, permissions, concurrency conflicts and database consistency failures retain distinct user messages.
- Remark creation notifications and metrics are deferred until the root transaction has committed.
- Remark edit/delete success logging and metrics are similarly deferred.
- The owned transaction is disposed before post-commit callbacks execute, allowing notification services to use the same scoped `ApplicationDbContext` safely.

## Workspace refinements

- Pending IPA/GSL milestones no longer show the misleading text **No date recorded**.
- Completed milestones without dates show **Completion date missing** as an amber data-completeness warning.
- Empty project sections show one contextual **Add project** action rather than duplicate actions.
- Server-returned drawer validation errors are treated as the new clean baseline; discard confirmation appears only after the user changes those returned values.
- Reopened drawers scroll and focus the validation summary or first invalid field.
- The Create Record sticky action footer is slimmer and less visually dominant.

## Tests added

- Root and nested transaction ownership
- Savepoint commit and rollback behaviour
- Deferred post-commit callback execution
- Callback suppression after rollback
- Post-commit use of the same scoped DbContext
- Callback failure isolation after a committed transaction
- Atomic linked FFC project save with a nested progress operation
- Complete rollback after a nested progress failure
- Change-tracker ghost-state cleanup
- Milestone date semantics and contextual empty-state actions

## Protected assets

The following Detailed Table files remain unchanged:

- `Areas/ProjectOfficeReports/Pages/FFC/MapTableDetailed.cshtml`
- `Areas/ProjectOfficeReports/Pages/FFC/_DetailedTablePartial.cshtml`
- `wwwroot/js/pages/project-office-reports/ffc/ffc-map-table-detailed-inline-edit.js`

## Deployment

From the project root:

```powershell
Remove-Item -Recurse -Force .\bin, .\obj -ErrorAction SilentlyContinue

npm ci
npm test

dotnet restore
dotnet build ProjectManagement.csproj -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release
```

Restart the application and hard-refresh the browser after deployment.

## Verification completed while preparing the package

- `npm test`: **212 passed, 0 failed**
- JavaScript syntax check: passed
- Modified test project XML validation: passed
- Protected Detailed Table checksums: unchanged
- No database migration generated

The preparation environment does not contain the .NET SDK, so the actual .NET build and .NET test run must be completed on the development machine before deployment.
