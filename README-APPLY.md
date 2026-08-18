# PRISM Photos — Next Phase Implementation Pack

This pack contains only the files changed or added for the Photos operational-hardening phase. Copy the contents of this folder over the root of the existing `ProjectManagement` solution, preserving the relative paths.

## Implemented

- Canonical media visibility/access policy shared by catalogue queries, direct media serving, People review and bulk download.
- CollectionKey-based Collections query with independent pagination, collection detail browsing, source links, singleton-project suppression and visit-title normalisation.
- Completed Photos Select mode with sticky action bar, ZIP export for catalogue-backed media, Review People scope, Shift-range selection, Select All Visible and desktop lasso selection.
- Persistent “new media is available” refresh banner; background revision polling no longer silently reloads the page.
- Newest/oldest sorting and improved sparse final-row behaviour while retaining the justified Photos layout.
- People workload summary split into suggested identity groups and individual reviews instead of duplicating the unidentified count.
- Unidentified-face triage layout with source/year/matching filters, quality/date sorting, multi-select, bulk Leave Unidentified and Not a Face operations.
- Safer identity-group review: no pre-checked group members; candidate actions operate only on explicitly selected appearances; raw similarity is labelled as a ranking signal with Strong/Moderate/Weak review bands.
- Central fix for duplicated visit prefixes such as `Visit of VISIT OF ...`.
- Focused accessibility/state improvements and regression tests for title normalisation and media visibility policy.

## Deliberately not included

- Generic bulk Delete: Photos aggregates media owned by different source modules, so deletion requires an explicit source-ownership policy.
- Manual “Add to collection” albums: existing Collections are source/context collections; curated albums should remain a separate future concept.
- Masonry layout: the production Photos wall already uses a justified layout and this phase improves the sparse-row case instead.
- Automatic identity confirmation: all person assignments remain human-confirmed.

## Database/configuration

No EF Core migration is required.

The following optional People settings have safe defaults and therefore do not require an appsettings change:

```json
"ReviewTriageBatchLimit": 100,
"GroupingReviewModerateSimilarityThreshold": 0.50,
"GroupingReviewStrongSimilarityThreshold": 0.65
```

If these keys are already present in environment-specific configuration, review them against the validation rules before deployment.

## Apply

1. Back up or commit the current project.
2. Copy this implementation pack into the project root and overwrite matching files.
3. No database migration is needed.
4. Build and run the automated test suite.

Recommended verification commands:

```powershell
dotnet restore
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
node --check .\wwwroot\js\pages\photos-library.js
node --check .\wwwroot\js\pages\photos-people-review.js
```

## Production smoke test

- Photos: normal timeline, Newest/Oldest, search/filter state, sparse person/collection view.
- Collections: collection count, paging, singleton toggle, collection opening, source link, visit title.
- Select: click, Shift-click, lasso, Select All Visible, ZIP, Review People, Clear, Esc.
- Refresh: introduce a new media revision and verify the banner appears without losing current selection/viewer state.
- People: workload counts, unidentified Triage/Detailed switch, filters/sort, batch Leave Unidentified and Not a Face.
- Identity groups: zero initial selection, candidate Use/Reject for selected, Assign/Create for selected only.
- Direct media: archived/disabled/unavailable assets must not be retrievable and must not enter ZIP/People review.

## Validation performed in this implementation environment

- JavaScript syntax checks passed for both modified Photos scripts.
- Changed C# files passed structural delimiter/balance checks.
- Unified patch was generated from the supplied source archive and is validated separately before delivery.
- The .NET SDK is not installed in this execution environment, so the solution could not be compiled or the xUnit suite executed here. Run the commands above on the development machine before production deployment.
