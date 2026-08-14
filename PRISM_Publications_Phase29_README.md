# PRISM Publications Phase 29 — Large-Compendium Structure Composer

## Purpose

Phase 29 scales Simulators Compendium authoring from small publications to large 50–100+ project catalogues without changing the existing first-class Compendium section model or PDF renderer.

The phase has two primary outcomes:

1. **Fast project selection** — the complete candidate row is now a selection target; checkbox and keyboard selection remain available, and Shift-click selects a visible range.
2. **Dedicated full-screen Publication Structure Editor** — a saved/loaded Compendium can be opened in a separate, full-width composer for section architecture, project movement, bulk operations and manual ordering.

No database migration is introduced in Phase 29.

## Ready-to-paste project files

- `Pages/Projects/Publications/Compendium/Index.cshtml`
- `Pages/Projects/Publications/Compendium/Structure.cshtml`
- `Pages/Projects/Publications/Compendium/Structure.cshtml.cs`
- `wwwroot/js/pages/projects-compendium.js`
- `wwwroot/js/pages/projects-compendium-structure-editor.js`
- `wwwroot/js/projects/compendium-structure-state.js`
- `wwwroot/css/pages/projects-publications.css`
- `wwwroot/js/projects/publications-compendium-phase29-contract.test.js`
- `ProjectManagement.Tests/Publications/CompendiumPhase29ContractTests.cs`
- `tools/Test-PrismPublicationsPhase29.ps1`

## Project-selection interaction

The candidate project table keeps the checkbox as the explicit selected-state indicator, but the entire non-interactive row now toggles selection.

- Click a row to select/deselect.
- Click the checkbox normally.
- Shift-click a row/checkbox to apply the target state across the currently visible filtered range.
- Space toggles a keyboard-focused row.
- Clicking an interactive child does not accidentally toggle the row.
- Selected rows use a restrained publication-green highlight and `aria-selected` state.
- Filter/search changes do not clear selected projects.
- Large `Clear selection` actions use a PRISM Bootstrap confirmation modal rather than native browser confirmation.

## Full-screen Structure Editor

The compact right-hand Publication Structure rail remains the normal navigator. Phase 29 adds **Structure editor** as a dedicated workspace for large publications.

Route:

`/Projects/Publications/Compendium/Structure?presetId=<id>`

The editor deliberately reuses the existing `CompendiumPreset`, `CompendiumPresetSectionConfiguration` and `CompendiumPresetProjectConfiguration` contracts. It does not create a parallel structure model.

### Workspace layout

On wide screens the editor uses three panes:

- **Projects** — search and quick filters across selected publication projects.
- **Publication canvas** — section cards and project order/assignment.
- **Sections** — a sticky document-outline navigator with counts and Unassigned visibility.

Responsive layouts collapse the third pane and then stack panes at narrower widths.

### Large-publication features

- Search selected projects.
- Quick filters: All, Unassigned, Warnings, Unreviewed.
- Local multi-selection independent of Compendium membership.
- Shift-range selection in the editor project finder.
- Bulk **Move to section**.
- Bulk **Remove from publication**.
- Add, rename, delete and reorder publication sections.
- Drag entire custom sections.
- Drag projects within a section in Manual mode.
- Drag projects between custom sections.
- Drop projects onto collapsed sections.
- Drag auto-scroll near the top/bottom of the canvas.
- Collapse/Expand individual sections and Collapse all/Expand all.
- Section navigator jumps directly to a section and highlights the active section as the canvas scrolls.
- Unassigned remains an explicit editorial queue.

## Manual versus automatic ordering

The editor preserves the existing Compendium semantics.

### Manual

Projects may be manually reordered. Custom-section assignment remains editable.

### Latest First / A–Z

Project sequence is automatic inside each section. Manual sequence ordering is therefore not represented as an authorable result. Custom-section assignment and section ordering remain editable.

## Browser-state handoff

The Structure Editor is a separate route, but it remains one Compendium authoring session.

A small shared client module, `compendium-structure-state.js`, transfers the current browser authoring state through `sessionStorage`, including:

- selected project IDs and order;
- current section definitions and assignments;
- publication narrative/grouping/sort choices;
- publication image/focal-point configuration;
- current review fingerprints and readiness/review state.

The handoff expires after four hours. Server save contracts still validate and normalise the final payload; browser state is never treated as trusted server data.

This allows a user to make unsaved changes in the normal workspace, open the Structure Editor, reorganise the publication, and return without losing the in-browser authoring state.

## Save / return behaviour

For HoD/Comdt users, the editor can atomically save the current project membership/order and section structure through the existing `ICompendiumPresetService.UpdateAsync` concurrency path.

- **Save** writes committed structure changes and updates the preset row version.
- **Done / Back** returns immediately if the editor is clean.
- If the editor has unsaved structure changes, a PRISM modal offers:
  - Save and return
  - Return without saving (changes remain in the current browser Compendium session)
  - Cancel
- Browser unload protection is active while the editor is dirty.
- If the preset was modified elsewhere, the existing concurrency conflict is surfaced rather than silently overwriting it.

Only HoD/Comdt may persist shared preset changes. Other authorised users may use local authoring state consistently with the existing Compendium permissions model.

## PDF / database scope

Phase 29 intentionally does **not** change:

- Compendium database schema;
- first-class custom section persistence introduced earlier;
- PDF project-page layout;
- PDF build identity;
- readiness policy;
- image rendering rules.

The PDF build identity remains `CompendiumPdf_2026-08-14_publication-review-v7` because Phase 29 changes authoring interaction, not publication rendering.

## Validation performed in this package

- `node --check wwwroot/js/pages/projects-compendium.js` — PASS
- `node --check wwwroot/js/pages/projects-compendium-structure-editor.js` — PASS
- `node --check wwwroot/js/projects/compendium-structure-state.js` — PASS
- Existing Compendium contract suite — **64/64 PASS**
- Phase 29 contract suite — **6/6 PASS**
- Combined Brochure + Compendium + Phase 29 contracts — **174/175**; the single failure is the same pre-existing Brochure Phase 9 `IBrochurePrintMeasurementService` DI expectation already present in the untouched Phase 28 baseline.
- Patch whitespace check — PASS
- Static C# delimiter sanity checks — PASS

The execution environment used to prepare the package does not contain the .NET SDK, so `dotnet build`/`dotnet test` could not be executed here.

## Workstation validation

After copying the ready-to-paste files over the Phase 28 project, run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase29.ps1
```

When the .NET SDK is available, the script also executes the project build and .NET test project.

## Recommended acceptance test

1. Load a saved Compendium.
2. Filter the candidate register and select several projects by clicking the rows.
3. Shift-click another visible row and verify range selection.
4. Save/load the Compendium if required, then open **Structure editor**.
5. Create several custom sections.
6. Multi-select projects and use **Move to section**.
7. Drag projects between sections and manually reorder them.
8. Collapse sections and drag a project to a collapsed target.
9. Drag a section to reorder the section sequence.
10. Save and return to Compendium.
11. Confirm the compact Publication Structure rail immediately reflects the authored result.
12. Preview the PDF and verify the existing publication structure/export behaviour remains unchanged.
