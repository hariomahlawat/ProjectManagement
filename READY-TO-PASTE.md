# PRISM Publications Phase 25 — Compendium Editorial Composer

This package is a **ready-to-paste delta** for the uploaded PRISM source. Copy the project-relative files over the existing solution, preserving folder structure.

## What is implemented

- **Publication narrative source**: Project Brief (new default), Capability Overview, or Project Description.
- Source availability/counts are visible during selection and review. Changing the narrative source invalidates prior review fingerprints because the publication content changed.
- **Direct cover hero workflow**: Automatic hero, Choose hero, or No imagery. The existing “Use as cover hero” shortcut remains available during project review.
- **Grouping is separate from ordering**:
  - Technical Category (default)
  - No Grouping
  - Custom Sections (publication-only; never modifies the Project Technical Category)
  - Manual, Latest First, or A–Z ordering
- **Custom sections** can be created, renamed, reordered and assigned per project in the publication structure rail.
- “Latest First” uses `YearOfDevelopment` first, then completion year/date, then record creation year as a deterministic fallback.
- Saved Compendiums use **schema v4** and persist narrative, grouping, sort, custom section, image, crop and cover decisions.
- Existing schema-v3 Compendiums are migration-preserved as **Project Description** publications so an old saved publication does not silently change content after deployment. New v4 Compendiums default to **Project Brief**.
- Project PDF pages are redesigned as a consistent **Capability Dossier**: stronger editorial hierarchy, compact factual metadata, prominent imagery, dynamic narrative heading, designed no-photo state, and continuation treatment.
- The factual Technical Category remains visible in the project dossier even when the publication is arranged by custom sections.
- The browser review workspace exposes the narrative source/availability and uses the same publication-image/crop contract as the final PDF.

## Database migration

New migration:

`Migrations/20261208160000_AddCompendiumEditorialComposer.cs`

It adds:

- `CompendiumPresets.NarrativeSource`
- `CompendiumPresets.GroupingMode`
- `CompendiumPresets.SortMode`
- `CompendiumPresetProjects.CustomSectionName`
- schema default upgrade from v3 to v4

The migration ID deliberately follows the repository's existing `2026120815...` Compendium migration sequence.

## Apply

1. Back up the database/source branch.
2. Copy all files from this package into the project root, preserving paths.
3. Apply the EF Core migration using your normal deployment process (`dotnet ef database update` if that is how this solution is managed).
4. Build the solution.
5. Run the validation script:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase25.ps1
```

## Validation performed in this environment

- `node --check wwwroot/js/pages/projects-compendium.js` — **passed**.
- `node --test wwwroot/js/projects/publications-compendium-contract.test.js` — **45/45 passed**.
- Brochure + Compendium contract run: **149/150 passed**. The sole Brochure failure already exists unchanged in the uploaded baseline (`IBrochurePrintMeasurementService` test expects Singleton while the baseline DI registration is Scoped); it is unrelated to this Compendium phase.
- A .NET SDK is not installed in this execution environment, so `dotnet build` / `dotnet test` could not be executed here. The included Phase 25 PowerShell validator runs them automatically when `dotnet` is available on the development workstation.

## Smoke-test scenarios after paste

1. Create a new Compendium: **Project Brief / Technical Category / Manual** should be selected by default.
2. Switch narrative to Capability Overview: all prior project review confirmations should require re-review.
3. Switch to **Latest First** and **No Grouping**: the rail/index/project sequence should be newest-first.
4. Switch to **Custom Sections**: create/rename sections, move projects, preview PDF, and confirm the Project Technical Category shown in the dossier remains unchanged.
5. Use **Choose hero** in Publication Settings and confirm the selected project image becomes the cover hero without changing that project's primary image record.
6. Preview before all reviews is allowed; final download remains gated until the selected publication state is fully reviewed.
7. Load an older saved v3 Compendium and confirm it remains Description-led after migration.

## File inventory

See `CHANGED-FILES.txt` for the complete repo-relative list.
