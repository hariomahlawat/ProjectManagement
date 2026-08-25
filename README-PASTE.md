# PRISM Compendium Phase 46.1 — Live-State Synchronisation Hotfix

## Scope

This is a focused hotfix for the Compendium **Dossier presentation defaults** controls introduced in Phase 46.

The server state/persistence and PDF output were already correct. The browser defect was caused by three default-change handlers calling `renderComposer()`, a function that does not exist in `projects-compendium.js`. The state was therefore saved to the in-memory/hidden form model, but JavaScript execution stopped before the selected button and related review/preflight UI could repaint. A page refresh then reconstructed the correct visual state from the already-updated values.

## Production file to replace

Paste this file over the file at the same project-relative path:

- `wwwroot/js/pages/projects-compendium.js`

Optional regression test file (recommended for source control):

- `wwwroot/js/projects/publications-compendium-phase46-1-live-state.test.js`

No C# file, database schema, EF migration, Razor markup, CSS, QuestPDF code, or Compendium review fingerprint changes are required for this hotfix.

## Behaviour after the fix

Changing any of these Compendium-level controls now repaints immediately without refresh:

- Page layout
- Text flow
- Publication image Fill/Fit
- Narrative alignment remains on the same working render path

The hotfix also keeps `aria-pressed` synchronized with the visible active state for Narrative alignment, Page layout, Text flow, and Fill/Fit buttons.

Each Phase 46 default change now completes the same authoritative UI pipeline used by Narrative alignment:

1. update editorial state;
2. propagate the new effective value only to inheriting dossiers;
3. invalidate only affected project reviews;
4. synchronize hidden form state;
5. render dirty state;
6. repaint editorial controls and publication structure via `renderOrder()`;
7. refresh review progress and navigation;
8. schedule preflight;
9. reload the active Focus Review dossier when applicable.

## Paste / deployment

1. Back up your current `wwwroot/js/pages/projects-compendium.js`.
2. Replace it with the file from this package.
3. Add the regression test file if you keep the repository test suite under source control.
4. Rebuild/republish the application normally. The Razor page already loads `projects-compendium.js` with `asp-append-version="true"`, so a new static-asset version is generated on publish.
5. In development, hard-refresh the browser once if the old asset remains cached.

## Verification on your development machine

From the project root:

```powershell
node --check wwwroot/js/pages/projects-compendium.js
node --test wwwroot/js/projects/publications-compendium-phase46-1-live-state.test.js
node --test wwwroot/js/projects/publications-compendium*.test.js
```

Then manually verify:

1. Open a saved Compendium.
2. Click **Automatic → Balanced** under Dossier presentation defaults. `Balanced` must become visibly active immediately.
3. Toggle **Flow below image ↔ Side column**. The selected button must update immediately.
4. Toggle **Fill ↔ Fit**. The selected button must update immediately.
5. Confirm the review count/status and right-side structure state refresh without reloading the browser.
6. Open Focus Review and confirm an inheriting dossier resolves to the newly selected publication default.
7. Refresh the page and confirm the same selections persist.

## Database / PDF impact

- **No migration required.**
- **No PDF renderer change.** The physical PDF reviewed for Phase 46 was already honoring the persisted defaults; this hotfix only repairs immediate browser feedback and related live-state refresh.
