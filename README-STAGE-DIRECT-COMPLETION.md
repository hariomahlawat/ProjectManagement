# PRISM ERP — Direct Stage Completion and Editable Start Date

## Scope

This is a project-relative replacement patch for the current PRISM ERP codebase. It implements direct completion of a `NotStarted` project stage and consistent, workflow-aware start-date suggestions across HoD direct updates, Project Officer proposals, and HoD approval.

## Behaviour implemented

### Start stage

- The **Started on** field remains editable.
- Its default is the day after the effective predecessor's completion date.
- The effective predecessor is the immediately preceding stage in the project's configured workflow.
- Consecutive `Skipped` stages are traversed backwards until the first non-skipped stage is reached.
- The search stops at that first non-skipped stage; it does not jump past an in-progress, blocked, not-started, or completed-without-date stage.
- If no usable predecessor completion date exists, the field defaults to the present date.

### Complete stage directly

- A `NotStarted` stage now offers **Complete stage directly**.
- The completion date defaults to the present date and remains editable.
- A separate optional **Start date** field is displayed.
- Where a usable effective predecessor completion exists, Start date is prefilled with predecessor completion + 1 day.
- The Start date remains editable.
- Where no usable predecessor completion date exists, Start date is left blank and may remain blank.
- Existing recorded start dates are preserved unless an authorised user supplies a replacement date.

### Project Officer and HoD workflow

- Project Officers receive the same semantic option in the stage-update proposal UI.
- Proposed start date is persisted separately from proposed completion date.
- The HoD approval screen shows both proposed dates.
- Approval and direct-apply paths use the same workflow and chronology rules.
- Multi-stage proposals retain projected-sequence validation.

### Chronology and safety

Server-side validation is authoritative. It rejects:

- future start or completion dates;
- a start date earlier than the effective predecessor boundary;
- a completion date earlier than the selected start date;
- a completion date earlier than the effective predecessor boundary when Start date is blank;
- unresolved mandatory predecessors, subject to the existing HoD backfill override workflow.

## Database migration

The patch includes:

`Migrations/20261201130000_AddRequestedStartDateToStageChangeRequests.cs`

The migration:

1. Adds nullable `RequestedStartDate` to `StageChangeRequests`.
2. Backfills existing completion proposals from the approved stage start or the latest earlier InProgress proposal where available.
3. Preserves backward compatibility for completion proposals already awaiting approval.

The current application already applies pending EF Core migrations at startup under a PostgreSQL advisory lock. Therefore, no manual SQL or `dotnet ef database update` is required for the current installation.

## Deployment

1. Back up the production PostgreSQL database.
2. Stop the IIS application pool.
3. Copy the contents of this folder into the project root, preserving paths and replacing existing files.
4. Build and publish the Release configuration.
5. Deploy the published output.
6. Start the application pool.
7. Confirm that the startup log records application of migration `20261201130000_AddRequestedStartDateToStageChangeRequests`.
8. Perform a hard browser refresh.

## Functional verification

Verify these scenarios after deployment:

1. Immediate predecessor completed: Start stage defaults to predecessor completion + 1 day.
2. Immediate predecessor skipped: the system walks back through the skipped chain only.
3. First non-skipped predecessor has no completion date: Start stage defaults to today; direct completion leaves Start date blank.
4. Direct completion allows the suggested Start date to be edited.
5. Direct completion allows a blank Start date where no usable predecessor completion exists.
6. Completion before Start date is rejected.
7. Project Officer proposal shows the proposed Start and completion dates in HoD approval.
8. Existing pending completion proposals continue to retain their historical proposed start where available.

## Build commands

```powershell
dotnet clean
dotnet restore
dotnet build -c Release
dotnet test -c Release
```
