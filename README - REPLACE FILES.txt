PRISM Production Readiness + Notifications Fix - Version 5
==========================================================

Purpose
-------
This is the consolidated, ready-to-replace package for the latest PRISM code state. It combines:
- mandatory production startup migrations and migration-lineage validation;
- application/media schema readiness checks;
- the DatabaseStartupMigrator nullable-host warning correction;
- the complete production notification API/browser/SignalR repair.

Why this package is consolidated
--------------------------------
Program.cs depends on production-readiness types introduced by the earlier hardening package. This
package contains the complete dependency set, so there is no replacement-order ambiguity and no risk
of restoring an older Program.cs while fixing notifications.

Replacement method
------------------
1. Stop the IIS application pool.
2. Back up the application database and current source tree.
3. Copy every file/folder from this package into the ProjectManagement solution root, preserving
   relative paths and replacing existing files when prompted.
4. Do not copy source files directly into the IIS publish directory.
5. Build, test and publish the complete application from the solution root.
6. Deploy the complete publish output, then start the IIS application pool.

Required build validation
-------------------------
npm ci
npm test
dotnet clean
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet publish -c Release -o .\publish

Notification defects corrected
-------------------------------
1. The authenticated layout now emits a request-specific antiforgery token and stores the matching
   antiforgery cookie.
2. Antiforgery middleware is registered at the correct point after authentication/authorization.
3. Every notification state-changing endpoint requires antiforgery validation.
4. The complete JavaScript/server contract is implemented:
   GET    /api/notifications
   GET    /api/notifications/count
   POST   /api/notifications/read
   POST   /api/notifications/unread
   POST   /api/notifications/read-all
   POST   /api/notifications/seen
   POST   /api/notifications/projects/{projectId}/mute
   DELETE /api/notifications/projects/{projectId}/mute
5. Legacy single-notification read/unread routes remain temporarily for cached-browser compatibility.
6. Page, read, unread, seen, read-all and project-mute operations return stable typed DTOs.
7. SignalR synchronises state across open tabs.
8. A post-commit SignalR/backplane failure is logged but cannot convert a successful database
   mutation into HTTP 500; polling reconciles the client state.
9. Inputs are bounded and validated; read responses are marked no-store.
10. End-to-end endpoint tests cover authentication, antiforgery, paging, mutations, mute/unmute,
    legacy compatibility and cache headers.

Database impact of the notification repair
------------------------------------------
No new notification migration is required. The existing 20261201110000_HardenNotifications
migration already supplies the required fields and indexes. The package retains the separate
production migration-lineage and schema-reconciliation corrections from Version 4.1/4.2.

Production configuration prerequisites
--------------------------------------
- DP_KEYS_DIR must be an absolute, durable and writable directory outside the publish folder.
- The production PostgreSQL connection string must identify a valid Host and Database.
- The IIS identity must have the filesystem and database rights documented in the production
  readiness notes.
- The production HTTPS certificate must be trusted by client devices.

Post-deployment smoke test
--------------------------
1. Sign in as a normal user and load Dashboard.
2. Open the notification bell and confirm unseen rows are recorded without an error.
3. Mark one notification read, then unread.
4. Use Mark all read.
5. Open Notification Centre and test folder, status, search, module and project filters.
6. Mute and unmute a project.
7. Open the same account in a second tab and confirm state synchronises through SignalR or polling.
8. In browser Network tools confirm mutation requests carry X-CSRF-TOKEN and return JSON HTTP 200.
9. Confirm anonymous /api/notifications requests return HTTP 401, not an HTML sign-in redirect.
10. Review application logs for migration completion, schema readiness and any SignalR warnings.

Validation performed in the preparation environment
---------------------------------------------------
- Notification JavaScript tests: 10 passed, 0 failed.
- Complete JavaScript suite: 128 passed, 0 failed.
- Static route/DTO/SignalR/antiforgery dependency review completed.
- The preparation environment did not contain the .NET SDK; therefore Release build and xUnit
  execution must be completed using the commands above before deployment.
