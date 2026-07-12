# Conference Remarks — Phase 2 Source-Module Integration

This bundle contains only the files added or changed for Phase 2. Apply it **after Phase 1** by extracting it over the root of the existing `ProjectManagement` solution and replacing matching files while preserving the folder structure.

## Implemented

- Integrated `Conference` into the existing Project remarks stream, filters and inline composer.
- Integrated `Conference` into the Project Idea discussion stream and inline comment composer.
- Integrated `Conference` into the Action Task update timeline and inline update composer.
- Kept one authoritative source record in each operational module; no duplicate conference-remarks table or synchronisation process was introduced.
- Restricted creation of Conference remarks to `Comdt` and `HoD` at both UI and server/service levels.
- Preserved read visibility for every user already authorised to view the underlying Project, Idea or Action Task.
- Added restrained, consistent visual treatment for Conference entries while keeping them in the same chronological feed as other remarks and updates.
- Added task notification wording specific to Conference directions.
- Extended Project remark summaries to include Conference counts.
- Added regression coverage for role permissions, typed task posting, notification metadata, Project composer permissions and browser-side typed-composer behaviour.

## Database impact

Phase 2 introduces **no new migration**. It uses the schema and native types delivered in Phase 1, including migration `20261201170000_AddConferenceRemarkFoundation`.

## Deployment sequence

1. Confirm that the Phase 1 files and migration are already present.
2. Back up the application and PostgreSQL database.
3. Extract this bundle over the project root and replace matching files.
4. From the project root, run:
   - `npm ci`
   - `npm test`
   - `dotnet restore`
   - `dotnet build`
   - `dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj`
5. Publish and deploy through the existing production process.

## Validation completed in this environment

- JavaScript syntax checks passed for all changed scripts and tests.
- The complete JavaScript suite passed: **144 tests, 0 failures**.
- The modified Razor views passed targeted guardrail checks for inline scripts, inline event handlers and inline style attributes.
- ZIP integrity, file manifest and patch integrity were checked before delivery.

The .NET SDK is not installed in this execution environment, so the .NET build and xUnit suite could not be executed here. Run the commands above before publishing.

## Scope boundary

This phase covers source-module integration only. The compact officer-specific Conference page and the conference icon on Officer Workload cards belong to Phase 3 and are not included in this bundle.
