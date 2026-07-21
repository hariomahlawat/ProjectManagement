# Conference Review — Direction History Navigation

## Deployment

Apply this package on top of the current Conference Review implementation, including the sticky-header jitter correction.

Copy the supplied folders into the directory containing `ProjectManagement.csproj`, preserve the folder structure, and replace the matching files.

No database migration, NuGet package, npm package, service registration, or configuration change is required.

## Replacement files

- `Pages/Workspace/Conference.cshtml.cs`
- `Pages/Workspace/_ConferenceItemRow.cshtml`
- `Pages/Workspace/_ConferenceSection.cshtml`
- `Services/Workspace/IOfficerConferenceReadService.cs`
- `Services/Workspace/OfficerConferenceReadService.cs`
- `ViewModels/Workspace/OfficerConferenceViewModels.cs`
- `wwwroot/css/officer-conference.css`
- `wwwroot/js/pages/officer-conference.js`
- `wwwroot/js/pages/officer-conference.test.js`
- `ProjectManagement.Tests/OfficerConferencePageTests.cs`
- `ProjectManagement.Tests/OfficerConferenceReadServiceTests.cs`

## Implemented behaviour

- Renames the shared column heading to **Conference direction**.
- Shows compact older/newer navigation only when an item has more than one direction.
- Opens every row on the latest direction.
- Loads one item's history only when the user first requests an older direction.
- Caches the loaded history independently for each row.
- Displays `Latest · n of n` or `Historical · x of n` with accessible controls.
- Updates the direction and its associated progress together.
- Assigns progress to the correct direction cycle: after that direction and before the next direction.
- Applies the existing responsible-functionary semantics for projects, ideas, and other tasks.
- Keeps Action Queue, pending-direction counts, and closed-loop behaviour tied exclusively to the latest direction.
- Validates that the selected item belongs to the selected officer's authorised current workload.
- Uses row-local loading, retry, and error states; a failed history request does not disturb other rows.
- Returns to the true latest direction before opening **Issue further direction**.
- Invalidates cached history and advances the count after a new direction is saved.
- Preserves the invariant-height sticky-header jitter fix.

## Local validation

From the project root, stop the running application and execute:

```powershell
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
npm ci
npm test
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

Then reload Conference Review with `Ctrl+F5`.

## Functional verification

1. An item with one direction shows no navigation controls.
2. An item with multiple directions opens on `Latest · n of n`.
3. The left arrow shows the immediately older direction and its bounded progress.
4. The right arrow returns toward the latest direction.
5. Boundary arrows are disabled at the oldest and latest positions.
6. Different rows retain independent history positions.
7. A history-load failure leaves the latest direction visible and offers a local retry.
8. **Issue further direction** returns the row to latest before opening the editor.
9. After saving, the new direction is shown as latest and the historical cache is refreshed on next use.
10. Project Officer Action Queue and pending-direction counts remain based on the latest direction only.

## Validation completed while preparing this package

- Full repository JavaScript suite: **226/226 passed**.
- Conference Review JavaScript tests: **33/33 passed**.
- JavaScript syntax checks passed.
- CSS parsed successfully with no syntax errors.
- Modified C# files parsed successfully with the C# syntax parser.
- The preparation environment did not contain the .NET SDK; run the .NET build and test commands locally before publication.
