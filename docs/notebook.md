# My Notebook

Personal notes and checklists with labels, colours, pinning, reminders, trash, and per-note
sharing with other users. It is private per user and is **not** indexed by global search.

## Code map

| Layer | Location |
| --- | --- |
| Page | `Pages/Notebook/Index` (`[Authorize]`; the single-page board and editor modal). `Pages/Notebook/Edit` is a compatibility redirect to `Index?note={id}`. |
| JSON API | `Controllers/Api/NotebookController` (`api/notebook/...`), `Controllers/Api/NotebookSystemItemsController` (`api/notebook/system-items`), error mapping in `NotebookApiExceptionFilter` |
| Contracts | `Contracts/Notebook/NotebookContracts.cs`, `ViewModels/Notebook` |
| Services | `Services/Notebook/NotebookService` (all rules and access checks), `NotebookRules`, `NotebookLimits`, `NotebookQuickCaptureParser`, `NotebookCardModelFactory` + `RazorNotebookCardRenderer` (server-rendered card HTML), `NotebookNotificationService` (collaboration notifications), `NotebookSystemItemPreferenceService` |
| Worker | `Hosted/NotebookTrashRetentionWorker` |
| Frontend | `wwwroot/js/pages/notebook-index.js` (entry) and modules in `wwwroot/js/notebook/`, bundled to `wwwroot/dist/` |

## Data model (`ApplicationDbContext`)

- `NotebookItem`: `OwnerId`, `Title`, `BodyMarkdown`, `Type` (`Note`, `Checklist` are active;
  `Sticky`, `Reminder`, `Idea`, `Draft` are legacy values normalised to `Note`), `Status`
  (`Active`, `Completed`, `Archived`), `Priority`, `ReminderAtUtc`, `IsPinned`, `ColorKey`,
  `SortOrder`, `ClientRequestId` (idempotent create), `Version` (uuid concurrency token),
  `ArchivedAtUtc`, `DeletedAtUtc` (trash marker).
- `NotebookChecklistItem`, `NotebookTag` (labels are per owner) + `NotebookItemTag`,
  `NotebookItemCollaborator` (`Role`: `Editor` or `Viewer`), `NotebookAttachment` (model only;
  there is no attachment upload/download endpoint), `NotebookSystemItemPreference`,
  `NotebookMigrationState`.
- Limits (`NotebookLimits`): title 220 chars, body 20,000, checklist row 500 chars and 200 rows,
  label name 60 chars, 12 labels per item.
- Migrations: `20261125230000_AddNotebookModule` through `20261207200000_AddNotebookSystemItemPreferences`.
  See `docs/notebook-postgresql-deployment.md` for the `pgcrypto` requirement. At startup
  `Program.EnsureNotebookVersionSchemaAsync` fails fast if the `Version` column is missing or the
  migrations are out of order.

## API and authorization

All `NotebookController` actions require an authenticated user (`[Authorize]`) and an antiforgery
token for unsafe methods (`[AutoValidateAntiforgeryToken]`; the client sends it in the
`X-CSRF-TOKEN` header). Mutations are `[Consumes("application/json")]` and require the item's
current `Version`; a stale version returns 409 with the current item.

The user id always comes from the authentication cookie (`UserManager.GetUserId`), never from the
request. Every query in `NotebookService` filters by owner or collaborator:

| Access | Operations |
| --- | --- |
| Owner, editor or viewer | `GET items/{id}`, `GET items/{id}/card` (HTML), `GET items/{id}/collaborators`, `POST items/{id}/duplicate` (copy owned by caller) |
| Owner or editor | `PATCH items/{id}/content`, `PUT items/{id}/checklist`, `PATCH items/{id}/checklist-items/{rowId}` |
| Owner only | `PATCH items/{id}` (settings), pin, reminder, colour, labels, archive/restore, complete/reopen, trash (`POST items/{id}/trash` or `DELETE items/{id}`), restore-from-trash, permanent delete, show/hide checkboxes, collaborator search/add/role change/remove |
| Collaborator | `POST items/{id}/leave` |
| Caller's own data | `POST items`, `GET counts`, `PUT order`, `GET/POST labels`, `PATCH/DELETE labels/{labelId}`, `DELETE trash` (empty own trash) |

Items the user cannot see return 404 (`notebook_not_found`); insufficient role returns 403.

`NotebookSystemItemsController` (roles `Comdt`, `HoD`) stores per-user presentation preferences
(show on home, pin, colour, labels, placement) for PRISM-owned system cards such as the Conference
Review digest. It never modifies the source records.

## Trash retention

`NotebookTrashRetentionWorker` permanently deletes items whose `DeletedAtUtc` is older than
`Notebook:Trash:RetentionDays` (default 30), every `Notebook:Trash:SweepInterval` (default 6 hours;
waits 15 minutes after a failure). Its status is reported to the admin worker status registry.

## Frontend build pipeline

- `npm run build:notebook` (`tools/build-notebook.mjs`) bundles `wwwroot/js/pages/notebook-index.js`
  with esbuild (ESM, ES2022, source map) into `wwwroot/dist/notebook-index.bundle.js` and writes
  `wwwroot/dist/notebook-manifest.json` (bundle SHA-256) via `tools/write-notebook-manifest.js`.
  `npm run build:notebook:prod` minifies.
- The page loads `~/dist/notebook-index.bundle.js` with `asp-append-version`.
- **The generated files in `wwwroot/dist/` are committed.** After changing any JavaScript under
  `wwwroot/js/`, rebuild and commit the bundle, map and manifest.
- `npm run check:notebook-assets` (`tools/check-notebook-assets.mjs`) rebuilds and fails if
  `git diff -- wwwroot/dist` is not empty (stale committed assets).
- `ProjectManagement.csproj` target `BuildNotebookAssets` runs `npm run build:notebook` before
  `Build`/`Publish` when inputs changed, and `ValidateNotebookDependencies` fails the .NET build if
  `node_modules/esbuild` is missing, so run `npm ci` first on any build machine.
- JavaScript unit tests (`*.test.js` next to the modules) run with `npm test`.

## Notes
- `INotebookTodoImportService` (legacy To-do import) is registered but not called by any page or service.
- The Index page's search box uses `ILIKE` over title, body, labels and checklist rows, with
  `%`, `_` and `\` escaped.
