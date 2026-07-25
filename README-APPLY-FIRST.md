# PRISM ERP — ToT precision and workflow hardening

This package is ready to extract over the folder containing
`ProjectManagement.csproj`. Preserve the relative paths and replace existing
files when prompted. New files must also be copied.

## What this package changes

- Preserves year-only, month-and-year and exact-day precision for ToT start and
  completion dates.
- Displays `2026`, `Dec 2026` or `31 Dec 2026` according to what was entered.
- Adds precision fields to approved ToT records and pending approval requests.
- Conservatively backfills legacy boundary dates so an inferred boundary is not
  presented as an exact user-entered day.
- Enforces completed, non-archived, non-deleted project eligibility in
  `ProjectTotService.UpdateAsync`, `SubmitRequestAsync` and request approval.
- Applies the same lifecycle guard to Overview handlers and the dedicated ToT
  Edit page.
- Separates summary and edit actions in the drawer.
- Adds the inline ToT remark composer.
- Shows status-specific guidance and hides inapplicable dates and milestones.
- Shows AJAX success feedback inside the drawer.
- Replaces failed cover images with the designed neutral fallback.
- Carries partial-date precision through the ToT tracker and approval details.

## Database migration

The package adds:

`Migrations/20261206100000_AddProjectTotDatePrecision.cs`

The application’s existing automatic migration startup will apply it. The
migration is also registered in `Migrations/immutable-migration-ids.txt` and the
EF model snapshot is updated.

The conservative backfill uses these historical storage conventions:

- start date on 01 January → year precision;
- start date on the first of another month → month precision;
- completion date on 31 December → year precision;
- completion date on another month-end → month precision;
- other values → exact-day precision.

This deliberately avoids retaining known range-boundary values as invented
exact dates. A genuine exact boundary date may therefore be understated and can
be refined by an authorised user.

## Apply

1. Stop the IIS application pool or take the application offline.
2. Back up the application folder and PostgreSQL database.
3. Extract this package into the project source folder.
4. Rebuild and publish the application.
5. Start the application and confirm the migration completes.

## Required verification

Run:

```powershell
dotnet clean
dotnet restore
dotnet build
dotnet test
```

Then verify:

1. Completion year `2026` displays as `2026`.
2. Completion month December 2026 displays as `Dec 2026`.
3. Exact date 31 December 2026 displays as `31 Dec 2026`.
4. Active, archived and deleted projects cannot be changed through Overview,
   the dedicated Edit page, request submission or approval.
5. Summary mode shows `Close`, `Open tracker`, `Update details`.
6. Edit mode shows only `Cancel`, `Save details`.
7. An inline ToT remark saves without a page reload.
8. Status guidance and field visibility change correctly.
9. Success feedback does not cover the drawer header.
10. A failed cover-photo request shows `Cover photo unavailable`.

## Validation note

JavaScript syntax and source/migration consistency were checked while preparing
the package. The preparation environment did not contain the .NET SDK, so the
four `dotnet` commands above must be completed before deployment.
