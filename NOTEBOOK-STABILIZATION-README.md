# PRISM Notebook Stabilization & Consolidation

This package contains the complete, ready-to-paste source files for the agreed Notebook stabilization phase. Paths are relative to the PRISM ERP project root; copy the package contents over the project root and replace existing files when prompted.

## What is implemented

- Removed the 80-card board cap and made home-board reads/reordering work for the complete active owned section.
- Replaced the special SortOrder=0 behavior with one sparse ordering invariant (step 1024); new items go to the top and reorder does not mutate content timestamps/versions.
- Added defensive order normalization and exact reorder set/version validation.
- Removed the unsupported Critical priority option.
- Added owner reminder add/change/remove controls to the modern note/checklist editor using a dedicated versioned API mutation.
- Split Today and Overdue using one centralized IST day-boundary implementation. Today is the current IST calendar day; Overdue is before the start of today.
- Made Reminder metadata-driven rather than a persisted content type. Persisted content is normalized to Note or Checklist; legacy Idea/Draft semantics are preserved as labels during migration.
- Replaced per-request Todo import with a durable one-time migration state and database-level unique protection for legacy Todo identity.
- Removed legacy Razor POST mutation/editor paths; the modern Notebook API is the single UI write path.
- Centralized Notebook validation constants/rules for body, labels, checklist rows, priority, reminder and colour.
- Preserved optimistic concurrency and expanded concurrency/audit handling across lifecycle, checklist and reminder mutations.
- Restricted drag/reorder payloads to the current user's reorderable cards so shared cards cannot corrupt owned board order.
- Added quick-composer draft recovery and consolidated board-view storage with one-time migration of old local-storage keys.
- Added/extended regression tests for >80 items, repeated creates, ordering, date buckets, reminder mutation, migration hardening and frontend reminder API/scheduler behavior.

## Installation / deployment sequence

1. Back up the production database before applying the EF migration.
2. Copy every file in this package over the project root, preserving the folder structure.
3. From the project root, restore frontend dependencies and regenerate the committed Notebook bundle:

```bash
npm ci
npm run build:notebook
```

4. Build and run the automated tests:

```bash
dotnet build
dotnet test
npm test
npm run check:notebook-assets
```

5. Apply the database migration using your normal deployment process. If migrations are applied manually:

```bash
dotnet ef database update
```

6. Smoke-test the acceptance scenarios below before production rollout.

> The project already has an MSBuild target that regenerates Notebook assets before Build/Publish when Notebook inputs changed, provided `node_modules/esbuild` is installed. Running `npm run build:notebook` explicitly first is recommended so the generated `wwwroot/dist/notebook-index.bundle.js`, source map and manifest can be reviewed and committed together.

## Generated Notebook bundle

The generated files under `wwwroot/dist` are intentionally **not** included in this patch package. This execution environment did not have the repository frontend dependencies/esbuild available, so shipping a hand-written substitute would make `check:notebook-assets` fail and would not be a trustworthy production artifact. The source files in this package are the authoritative changes; the repository's existing build script produces the correct bundle.

## Database migration behavior

`20261207190000_StabilizeNotebookModule` performs the following database-side work:

- creates `NotebookMigrationStates`;
- imports any remaining non-deleted legacy Todo items into Notebook exactly once using deterministic IDs;
- preserves legacy Idea/Draft semantics as `Idea`/`Draft` labels;
- normalizes legacy Sticky/Reminder/Idea/Draft content types to Note;
- normalizes supported colour keys;
- normalizes active board order while preserving the old visible order;
- neutralizes duplicate legacy Todo identity before enforcing uniqueness;
- creates a unique filtered `(OwnerId, LegacyTodoItemId)` index;
- creates `(OwnerId, IsPinned, SortOrder)` index; and
- records `LegacyTodoImportV1` completion for current users.

The migration Down method intentionally does not reverse imported/normalized user data, because doing so could delete or misclassify Notebook content. It only restores the relevant schema/index state. Treat the data transformation as forward-only and restore from backup if a full rollback is required.

## Acceptance checks

Verify at minimum:

1. Create a normal note and confirm it appears at the top of Others.
2. Create a checklist, edit rows and toggle row completion.
3. Add a reminder to an existing note/checklist; change date/time/priority; remove it.
4. Confirm Today contains only the current IST calendar day and older reminders appear under Overdue.
5. Pin/unpin and rearrange multiple owned cards; confirm shared cards are not draggable/reordered.
6. Load a user with more than 80 active items and confirm all are visible and reorder succeeds.
7. Share a note with Editor and Viewer roles and confirm existing authorization behavior remains intact.
8. Open the same item in two browser tabs, save one, and confirm the stale tab receives conflict handling rather than silently overwriting.
9. Complete/reopen, archive/restore, trash/restore and permanently delete test items.
10. Refresh while text is still in the quick composer and confirm the draft is restored; confirm successful create clears the draft.

## Validation performed in this environment

- JavaScript syntax check: all Notebook/page JS source files checked successfully with Node.
- Notebook tests that do not require a DOM: **56 passed, 0 failed**.
- Full repository JS runner: **188 discovered; 168 passed; 20 failed to start because `jsdom` is not installed in this environment**. All 20 failures contain the same missing-jsdom startup error; no additional assertion failure was observed in those suites.
- C# lexical/static delimiter check: all **21 changed/new C# files** passed.
- Repository guard checks confirm no `.Take(80)`, Notebook `Critical`, `UseLegacyEditor`, Index Todo import call, special `SortOrder == 0`, or Razor Notebook POST handlers remain.
- The .NET SDK is not installed in this execution environment, so `dotnet build` / `dotnet test` could not be executed here. Run them in the normal PRISM development environment before deployment.

## Scope deliberately not added

This phase does not add rich text, attachments UI, recurring reminders, calendar/email integration, AI features, folders/sub-notebooks or a visual redesign. The attachment data model is retained for future use.
