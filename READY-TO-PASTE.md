# PRISM Publications Phase 26

## Publication Workspace + First-Class Editorial Structure

This package is intended to be pasted over the Phase 25 Compendium implementation.

### What changes

- Brochure and Compendium opt into the PRISM `workspace` shell and use wide/ultrawide monitors instead of the normal constrained ERP page width.
- Compendium Custom Sections are persisted as independent `CompendiumPresetSection` records rather than being inferred from a project's section-name string.
- Multiple empty custom sections can be created, ordered, saved and reloaded before any project is assigned.
- Custom section order is independent of project sort mode. `Latest first` and `A-Z` sort projects *inside* each section.
- Projects can be moved between custom sections by assignment control or drag-and-drop; section headers support rename, reorder and safe deletion.
- A dedicated PRISM modal confirms section deletion; populated sections move their projects to `Unassigned` without changing Project master data.
- Global narrative remains Project Brief by default, with per-project Project Brief / Capability Overview / Project Description override support.
- Review fingerprints are v3 and include publication section identity/name, so visible editorial changes require re-review.
- Readiness findings are aggregated by issue type. Missing Arm/Service is informational; the selected publication narrative being absent is a final-issue blocker.
- PDF dossier treatment removes duplicate status metadata and never prints internal authoring language for projects without photography.
- `No grouping` index output suppresses the artificial `Projects` section banner.

### Database migration

`20261208170000_AddCompendiumFirstClassSections`

The migration:

1. creates `CompendiumPresetSections`;
2. adds `CustomSectionId` and `NarrativeSourceOverride` to saved Compendium project rows;
3. migrates existing Phase 25 `CustomSectionName` values into first-class sections;
4. updates Compendium settings schema version from 4 to 5;
5. preserves the legacy section-name column for rollback/compatibility.

No Project Technical Category or other authoritative Project master data is changed.

### Paste / deploy

Copy the contents of the ready-to-paste package into the ProjectManagement root and overwrite the matching files.

Then run in PowerShell from the project root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase26.ps1
```

The script performs JavaScript syntax and Compendium contract tests, checks the migration/section/fingerprint/PDF contracts, and runs `dotnet build` plus `dotnet test` when the .NET SDK is available.

If your environment does not apply EF Core migrations automatically, apply the migration using your normal deployment procedure before testing saved Compendiums.

### Functional verification after build

1. Open Compendium on a 1600–1920 px monitor and confirm the Publications workspace uses nearly the full application canvas.
2. Choose **Custom sections**.
3. Create at least three sections without assigning projects. All three must remain visible.
4. Save and reload the Compendium. Empty sections must remain.
5. Assign projects to different sections and reorder the section headers.
6. Switch project order between Manual, Latest first and A-Z. Section order must not change.
7. Drag a project from one custom section to another.
8. Delete a populated section and confirm its projects move to **Unassigned**.
9. Override one project's narrative while the publication default remains Project Brief; save/reload and confirm persistence.
10. Preview PDF in Technical Category, No Grouping and Custom Sections modes and verify the index reflects the selected structure.
11. Verify a project with no photograph renders as a deliberate text-led Capability Dossier without internal authoring messages.

### Validation performed in this delivery environment

- `node --check wwwroot/js/pages/projects-compendium.js` — passed.
- `node --test wwwroot/js/projects/publications-compendium-contract.test.js` — **52/52 passed**.
- Brochure contract suite — **104/105 passed**; the single failure is the same pre-existing Phase 9 DI registration expectation present in the Phase 25 baseline and is unrelated to Phase 26.
- Static delimiter/lexical sanity check performed on all modified C# files — passed.
- .NET build/test could not be executed here because the .NET SDK is not installed in the execution environment. Run the supplied Phase 26 PowerShell validator on the development workstation.
