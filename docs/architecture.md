# Architecture and configuration

This guide explains how the application is wired together: which layers exist, how dependencies are registered, and which configuration switches control behaviour. Use it alongside [docs/data-domain.md](data-domain.md) and [docs/infrastructure-services.md](infrastructure-services.md) for a complete picture of the platform.

## Solution structure

```
Contracts/                  Shared DTOs used by APIs, services, and SignalR hubs
Configuration/              Options classes bound from appsettings
Data/                       EF Core DbContext, migrations, and seeders
Application/                Vertical slices (e.g., IPR) that orchestrate persistence and storage beyond Razor Pages
Features/                   Feature-specific helpers (e.g., remarks, user lookup)
Helpers/                    Stateless helpers (quick parsers, celebration date logic)
Infrastructure/             Cross-cutting filters, clocks, security helpers
Models/                     Entity types and supporting enums/value objects
Pages/                      Razor Pages for end-user features
Areas/Admin/                Razor Pages for admin centre modules
Services/                   Business services, background workers, notification stack
Utilities/                  Low-level helpers (CSV exports, HTML sanitisation)
wwwroot/                    Static assets and JavaScript modules
```

Most Razor Pages depend on interfaces defined in `Services/`. These interfaces are registered in `Program.cs` so they can be swapped with mocks during tests or alternative implementations in production. (see Program.cs lines 82-236) Dependency injection keeps page models light and focussed on HTTP concerns.

## Startup pipeline

`Program.cs` bootstraps the application and configures the middleware pipeline. (see Program.cs lines 1-760) Key steps:

1. **Logging & diagnostics** – Adds console logging, suppresses noisy EF logs, and registers metrics. (see Program.cs lines 21-74)
2. **Data protection** – Persists key rings to `DP_KEYS_DIR` (env override) so authentication cookies survive restarts. (see Program.cs lines 29-45)
3. **Database** – Configures `ApplicationDbContext` with PostgreSQL and applies migrations automatically at startup. (see Program.cs lines 47-244)
4. **Identity** – Registers ASP.NET Core Identity with relaxed password rules, username-based accounts, and custom policies (`Project.Create`, `Checklist.View`, `Checklist.Edit`). Cookie settings enforce secure, HttpOnly cookies scoped to the host. (see Program.cs lines 82-148)
5. **Security headers** – Enables HSTS, rate limiting for the login endpoint, forwarded headers for proxy scenarios, and a global middleware that sets CSP/COOP/CORP headers. Document preview routes relax these headers to allow embedded PDF viewers. (see Program.cs lines 125-310)
6. **Services** – Wires every service, options binding, and hosted worker. Notable registrations include plan/timeline services, document pipeline, remark notifications, SignalR hub, and upload providers. (see Program.cs lines 108-236)
7. **Razor Pages** – Applies global authorization conventions, enables antiforgery, and attaches `EnforcePasswordChangeFilter` so users flagged with `MustChangePassword` are redirected to the change-password page. (see Program.cs lines 215-236) (see Infrastructure/EnforcePasswordChangeFilter.cs lines 6-42)
8. **Middleware** – After building the app, the pipeline enforces HTTPS, security headers, static files, routing, rate limiting, authentication, and authorization. Minimal APIs handle calendar events, notifications, and process checklists alongside the Razor Pages endpoints. (see Program.cs lines 229-760)

### Environment awareness

- Development builds call `IdentitySeeder.SeedAsync` which creates an `admin` user plus sample HoD/Project Officer accounts. (see Data/IdentitySeeder.cs lines 12-78)
- Additional Content Security Policy connect sources can be supplied per environment via `SecurityHeaders:ContentSecurityPolicy:ConnectSources` (see [configuration reference](configuration-reference.md)). (see Program.cs lines 229-310)

## Authentication and authorization

- **Identity store** – `ApplicationUser` extends `IdentityUser` with `FullName`, `Rank`, and a `MustChangePassword` flag. (see Models/ApplicationUser.cs lines 6-34)
- **Roles** – Seeded roles include Admin, HoD, Project Officer, TA, Comdt, MCO, Project Office, and Main Office. (see Data/IdentitySeeder.cs lines 14-32)
- **Policies** – `Project.Create` restricts project creation to Admin/HoD; `Checklist.Edit` grants stage checklist edits to MCO/HoD; `Checklist.View` requires authentication. (see Program.cs lines 108-148)
- **Filters** – `EnforcePasswordChangeFilter` redirects any authenticated user with `MustChangePassword = true` to the change-password screen, excluding login/logout routes. (see Infrastructure/EnforcePasswordChangeFilter.cs lines 6-42)
- **SignalR** – The notifications hub (`/hubs/notifications`) requires authentication and streams notification updates to connected clients. (see Program.cs lines 229-356) (see Hubs/NotificationsHub.cs lines 1-62)

## Background services

Hosted services run alongside the web host:

- `LoginAggregationWorker` – Aggregates daily login counts for analytics. (see Services/LoginAggregationWorker.cs lines 13-110)
- `TodoPurgeWorker` – Deletes completed tasks after `Todo:RetentionDays` and logs warnings when operations fail. (see Services/TodoPurgeWorker.cs lines 14-108)
- `UserPurgeWorker` – Enforces the user lifecycle (undo window and hard delete). (see Services/UserPurgeWorker.cs lines 18-120)
- `NotificationDispatcher` – Moves queued notification dispatches into user-facing records and pushes updates to the hub. (see Services/Notifications/NotificationDispatcher.cs lines 21-140)
- `NotificationRetentionService` – Periodically trims old notifications according to retention options. (see Services/Notifications/NotificationRetentionService.cs lines 20-147)

All workers honour `CancellationToken`s and write structured logs so operators can monitor progress.

## Minimal APIs

The app exposes several API groups in addition to Razor Pages:

- **Calendar (`/calendar/events`)** – CRUD operations guarded by role-based authorization. Supports recurrence rules, Markdown descriptions (sanitised via Markdig + HtmlSanitizer), and range-limited queries to protect the database. (see Program.cs lines 240-420)
  - `/calendar/events/holidays` surfaces admin-managed `Holiday` rows, returning ISO dates plus UTC spans for the UI while clamping queries to 400 days and requiring authentication. (see Program.cs lines 492-534)
- **Notifications (`/api/notifications`)** – List, count unread, mark read/unread, and mute per project. Responses respect project visibility via `ProjectAccessGuard` and normalise routes for consistent client-side navigation. (see Program.cs lines 500-615) (see Services/Notifications/UserNotificationService.cs lines 23-220)
- **Process templates (`/api/processes/...`)** – Fetch and maintain stage templates and checklist items with row-version checks to prevent conflicting edits. Policies `Checklist.View` and `Checklist.Edit` gate read/write access. (see Program.cs lines 617-760)
- **Project analytics (`/api/analytics/projects/*`)** – Returns pre-aggregated stage, lifecycle, slip-bucket, and overdue datasets consumed by the analytics dashboard. Filters accept lifecycle, category, and technical category parameters and normalise Indian Standard Time ranges for monthly series. (see Features/Analytics/ProjectAnalyticsApi.cs lines 16-120) (see Services/Analytics/ProjectAnalyticsService.cs lines 20-220)
- **Project remarks (`/api/projects/{id}/remarks`)** – CRUD endpoints for structured project remarks, including audit history, mention expansion, scoped filters, and role-based authorisation for project governance roles. (see Features/Remarks/RemarkApi.cs lines 20-260) (see Services/Remarks/RemarkService.cs lines 21-320)
- **User mentions (`/api/users/mentions`)** – Lightweight lookup used by comment and remark editors to resolve display names and initials while guarding against disabled or pending-deletion accounts. (see Features/Users/MentionApi.cs lines 17-78)
- **Document viewer (`/Projects/Documents/View`)** – Streams published PDFs once project access (or a preview token) is validated, applies private caching with ETags, and rejects archived or draft records so moderators can safely share review links. (see Program.cs lines 1376-1456) (see Services/Documents/DocumentPreviewTokenService.cs lines 10-124)

## Storage and uploads

Uploads share a single root resolved by `UploadRootProvider`:

- Defaults to `wwwroot/uploads` (or `PM_UPLOAD_ROOT` env) and ensures project folders exist on demand. (see Services/Storage/UploadRootProvider.cs lines 17-100)
- `ProjectPhotoService` stores derivatives under `projects/{projectId}/{sizeKey}` with size/quality limits defined in `ProjectPhotoOptions`. It accepts optional virus scanning by plugging in an `IVirusScanner`. (see Services/Projects/ProjectPhotoService.cs lines 82-512)
- `DocumentService` and `ProjectCommentService` reuse the same provider for document previews and comment attachments, ensuring consistent storage hygiene. (see Services/Documents/DocumentService.cs lines 20-240) (see Services/ProjectCommentService.cs lines 22-238)

## Configuration binding

Options classes under `Configuration/` and `Services/Projects/` are bound during startup:

- `TodoOptions`, `UserLifecycleOptions`, `NotificationRetentionOptions`, `ProjectPhotoOptions`, and `ProjectDocumentOptions` all map to sections in `appsettings.json`. See [configuration-reference.md](configuration-reference.md) for defaults and overrides. (see Program.cs lines 108-236)
- `ProjectPhotoOptionsSetup` populates a default storage root when none is configured so local development works without extra setup. (see Services/Projects/ProjectPhotoOptionsSetup.cs lines 1-33)

Keeping these bindings up to date ensures new configuration keys are documented and discoverable.
