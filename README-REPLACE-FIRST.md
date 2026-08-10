# PRISM Publications Phase 3 — Integration, Runtime & Acceptance Hardening

## Why this package exists

The Brochure page currently fails with:

`InvalidOperationException: Unable to resolve service for type 'ProjectManagement.Services.Publications.IBrochurePublicationService'`

The Publications workspace files are present, but the earlier narrow `Program.cs` DI merge and the Projects navigation merge were not incorporated into the running application. This package removes that deployment ambiguity.

## What this phase changes

### P0 runtime integration
- Replaces `Program.cs` with the exact uploaded PRISM baseline plus Publications integration.
- Adds `using ProjectManagement.Services.Publications;`.
- Calls `builder.Services.AddProjectPublications();` immediately after the existing Compendium registrations.
- Protects the complete `/Projects/Publications/**` Razor Pages folder through Razor Pages conventions.
- Adds a startup validator that resolves the full Brochure and retained Compendium service graphs at startup.

### P0 navigation integration
- Replaces `ProjectModuleNavDefinition.cs` with the exact current definition plus one `Publications` destination after Analytics and before Industry directory.
- Preserves ARPP/PPP, Industry directory, Create project and Pending approvals semantics.
- Replaces the navigation regression test with coverage for Publications presence, order and active-prefix behaviour.

### Publications UX consistency
- Adds `Preview PDF` to the Compendium workspace using the same authoritative Compendium exporter.
- Preview opens inline in a new browser tab; final `Download PDF` remains a normal attachment download.
- Adds an explicit eligibility statement: completed, active project records marked available for proliferation.
- Updates Compendium JavaScript so Preview does not incorrectly put the Download button into a generating state.

### Route/security hardening
- Adds explicit `[Authorize]` to the Publications landing Razor page in addition to the folder-level convention.
- Adds route integration tests for Publications overview, Brochure and Compendium.

### Deployment verification
- Adds `tools/Test-PrismPublicationsIntegration.ps1`.
- It checks the critical service registration, route authorization, navigation contract, required feature files and optional offline font package before browser testing.

## Which package to use

If the Phase 2 Brochure Quality Hardening files are already in your PRISM project, use the **Phase 3 incremental package**.

If you want one complete Publications package containing Phase 1 + Phase 2 + Phase 3, use the **Brochure Builder v3 Full package** instead.

## Installation — incremental package

Copy the contents of this package over the `ProjectManagement` project root and replace files when prompted.

This phase intentionally supplies **complete replacement files** for both `Program.cs` and `ProjectModuleNavDefinition.cs`; there are no separate manual merge steps.

The two replacement files were built from the exact `ProjectManagement-master (2)(4).zip` source supplied in this conversation, preserving its existing ARPP, Industry Directory, Compendium, Project Briefing, notification and navigation code.

## After replacement

Run from the project root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsIntegration.ps1

Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

Then restart IIS Express / IIS application pool and hard-refresh the browser.

## Acceptance checks

1. `/Projects/Publications/Brochure` opens without the DI exception.
2. Projects secondary navigation contains **Publications** after Analytics and before Industry directory.
3. Publications remains active on Overview, Brochure and Compendium child routes.
4. `/Projects/Publications` redirects unauthenticated users to login.
5. Brochure project list and preflight load normally.
6. Brochure Preview and Generate work using the Phase 2 renderer.
7. Compendium page still shows the existing authoritative readiness metrics/warnings.
8. `Preview PDF` opens the Compendium inline in a new tab.
9. `Download PDF` downloads the Compendium normally.
10. Existing `/Projects/Compendium` compatibility route continues to work.
11. `Test-PrismPublicationsIntegration.ps1` returns `PUBLICATIONS INTEGRATION CHECK PASSED`.
12. If DM Sans is absent, the checker reports only a warning and brochure generation continues with Lato fallback.

## Database / migrations

No database migration is required.

## Build note

The preparation environment does not contain the .NET SDK. JavaScript and structural/static integration checks were run here, but the authoritative C# build and xUnit execution must be run in your normal PRISM development environment.
