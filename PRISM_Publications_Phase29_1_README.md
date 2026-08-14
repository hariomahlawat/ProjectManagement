# PRISM Publications — Phase 29.1 Stabilization

This package is an incremental update for the Phase 29 Large-Compendium Structure Composer.
No database migration is required.

## What this phase fixes

1. **Floating Final Output command integrity**
   - The dock mirrors Preview/Download eligibility exactly.
   - Disabled Download is visibly disabled, not blue/enabled-looking.
   - The dock disappears as soon as the canonical Final Output card enters the viewport.

2. **Focus Review is proof-first**
   - On wide monitors, Focus Review hides the compact Publication Structure rail instead of squeezing it.
   - The dossier proof and inspector receive the reclaimed width.
   - The floating output command dock remains available while focused.

3. **Structure Editor is a true viewport workspace**
   - Desktop document scrolling is suppressed inside the editor.
   - Header/Save state remain visible.
   - Projects, canvas, and section navigator own their internal scrolling.
   - The normal PRISM footer no longer scrolls into the middle of an editing session.

4. **Clear Save/Back semantics**
   - Removed the redundant `Done` command.
   - `Back to Compendium` is the single exit path.
   - Persistent state indicator shows `Saved`, `Modified`, or `Session changes`.
   - Existing unsaved-change modal still offers Save and return / Return without saving / Cancel.

5. **Large-publication Structure Editor improvements**
   - `Select all N shown` for the current filtered project view.
   - Section navigator can be hidden/shown to reclaim canvas width.
   - Custom section rename affordance is visually explicit with a pencil cue.
   - Section/action hit targets are slightly larger.

6. **Project register large-screen refinement**
   - At >=1600px the register no longer forces its previous 1180px minimum width.
   - Columns fit the available full-width authoring surface more intelligently.

## Files changed

- `Pages/Projects/Publications/Compendium/Structure.cshtml`
- `wwwroot/js/pages/projects-compendium.js`
- `wwwroot/js/pages/projects-compendium-structure-editor.js`
- `wwwroot/css/pages/projects-publications.css`

## Validation files added

- `wwwroot/js/projects/publications-compendium-phase29-1-contract.test.js`
- `tools/Test-PrismPublicationsPhase29_1.ps1`

## Apply

Copy the contents of the ready-to-paste ZIP over the project root, preserving paths.

Then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase29_1.ps1
```

The current execution environment does not contain the .NET SDK, so `dotnet build` was not run here. The validation script runs it automatically on a workstation where the SDK is available.
