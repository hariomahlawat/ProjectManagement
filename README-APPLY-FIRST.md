# Project Overview Proliferation Profile — Ready-to-replace implementation

## Apply

1. Back up the current source tree.
2. Extract this bundle into the directory containing `ProjectManagement.csproj`.
3. Preserve the included folder structure and overwrite matching files.
4. Add the new files when prompted.
5. Build and run the test project before deployment.
6. Deploy normally. The application’s existing automatic EF Core migration process will apply:
   `20261205090000_AddProjectProliferationProfileFields`.

Do not manually alter the database and do not create a second migration for the same change.

## User-visible result

- The separate **Next stage** card is removed.
- The current-stage card now includes the next stage as supporting text.
- A new **Proliferation** card shows indicative proliferation cost and a clear tri-state availability position:
  - Available for proliferation
  - Not available for proliferation
  - Availability not assessed
- Admin, HoD and the assigned Project Officer can edit proliferation details in a right-side editor without leaving the project overview.
- Other authorised viewers can open the same card in read-only mode and see cost, availability, reason, remarks and update information.
- Cost, availability and remarks are saved to the existing authoritative project cost and technology-status records; downstream completed-project, compendium, dashboard and briefing queries continue to use the same data.

## Data-model change

- `ProjectTechStatus.AvailableForProliferation` is now nullable so that **Not assessed** is distinct from **Not available**.
- `ProjectTechStatus.ProliferationRemarks` is added as a dedicated optional field.
- Existing true/false records are preserved by the migration.

## Validation and safeguards

- Proliferation cost may be blank or greater than zero, with at most two decimal places.
- A reason is mandatory when the project is marked **Not available**.
- Reason and remarks are limited to 500 characters.
- Updates are permission checked, anti-forgery protected and audit logged.
- The offcanvas editor uses a static backdrop to avoid loss through an inadvertent outside click.

## Verification after replacement

Run from the solution root:

```powershell
dotnet build ProjectManagement.csproj
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj
```

Then verify one project in each state:

1. No cost and not assessed.
2. Cost recorded and available.
3. Cost recorded and not available with a reason.
4. Save optional proliferation remarks and reopen the card.
5. Confirm an unassigned Project Officer has read-only access.
6. Confirm Admin, HoD and the assigned Project Officer can edit.
7. Confirm completed-project and briefing filters still return only `AvailableForProliferation == true` projects.

## Environment validation performed while preparing this bundle

- `wwwroot/js/projects/overview.js` passed `node --check`.
- Migration lineage and migration-file presence validation passed.
- The replacement diff passed whitespace/error checks.
- The full JavaScript test runner could not complete because `jsdom` is not installed in this execution environment; unaffected tests that do not require `jsdom` ran, while `jsdom`-dependent suites failed at module loading.
- The .NET SDK is not installed in this execution environment, so C# compilation and xUnit execution must be completed on the development machine before deployment.
