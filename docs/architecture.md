# Architecture and configuration

This guide explains how the application is wired together: which layers exist, how dependencies are registered, and which configuration switches control behaviour. Use it alongside [docs/data-domain.md](data-domain.md) and [docs/infrastructure-services.md](infrastructure-services.md) for a complete picture of the platform.

## Solution structure

```
Contracts/                  Shared DTOs used by APIs, services, and SignalR hubs
Configuration/              Options classes bound from appsettings
Data/                       EF Core DbContext, migrations, and seeders
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

Most Razor Pages depend on interfaces defined in `Services/`. These interfaces are registered in `Program.cs` so they can be swapped with mocks during tests or alternative implementations in production.【F:Program.cs†L82-L236】 Dependency injection keeps page models light and focussed on HTTP concerns.

## Startup pipeline

`Program.cs` bootstraps the application and configures the middleware pipeline.【F:Program.cs†L1-L760】 Key steps:

1. **Logging & diagnostics** – Adds console logging, suppresses noisy EF logs, and registers metrics.【F:Program.cs†L21-L74】
2. **Data protection** – Persists key rings to `DP_KEYS_DIR` (env override) so authentication cookies survive restarts.【F:Program.cs†L29-L45】
3. **Database** – Configures `ApplicationDbContext` with PostgreSQL and applies migrations automatically at startup.【F:Program.cs†L47-L244】
4. **Identity** – Registers ASP.NET Core Identity with relaxed password rules, username-based accounts, and custom policies (`Project.Create`, `Checklist.View`, `Checklist.Edit`). Cookie settings enforce secure, HttpOnly cookies scoped to the host.【F:Program.cs†L82-L148】
5. **Security headers** – Enables HSTS, rate limiting for the login endpoint, forwarded headers for proxy scenarios, and a global middleware that sets CSP/COOP/CORP headers. Document preview routes relax these headers to allow embedded PDF viewers.【F:Program.cs†L125-L310】
6. **Services** – Wires every service, options binding, and hosted worker. Notable registrations include plan/timeline services, document pipeline, remark notifications, SignalR hub, and upload providers.【F:Program.cs†L108-L236】
7. **Razor Pages** – Applies global authorization conventions, enables antiforgery, and attaches `EnforcePasswordChangeFilter` so users flagged with `MustChangePassword` are redirected to the change-password page.【F:Program.cs†L215-L236】【F:Infrastructure/EnforcePasswordChangeFilter.cs†L6-L42】
8. **Middleware** – After building the app, the pipeline enforces HTTPS, security headers, static files, routing, rate limiting, authentication, and authorization. Minimal APIs handle calendar events, notifications, and process checklists alongside the Razor Pages endpoints.【F:Program.cs†L229-L760】

### Environment awareness

- Development builds call `IdentitySeeder.SeedAsync` which creates an `admin` user plus sample HoD/Project Officer accounts.【F:Data/IdentitySeeder.cs†L12-L78】
- Additional Content Security Policy connect sources can be supplied per environment via `SecurityHeaders:ContentSecurityPolicy:ConnectSources` (see [configuration reference](configuration-reference.md)).【F:Program.cs†L229-L310】

## Authentication and authorization

- **Identity store** – `ApplicationUser` extends `IdentityUser` with `FullName`, `Rank`, and a `MustChangePassword` flag.【F:Models/ApplicationUser.cs†L6-L34】
- **Roles** – Seeded roles include Admin, HoD, Project Officer, TA, Comdt, MCO, Project Office, and Main Office.【F:Data/IdentitySeeder.cs†L14-L32】
- **Policies** – `Project.Create` restricts project creation to Admin/HoD; `Checklist.Edit` grants stage checklist edits to MCO/HoD; `Checklist.View` requires authentication.【F:Program.cs†L108-L148】
- **Filters** – `EnforcePasswordChangeFilter` redirects any authenticated user with `MustChangePassword = true` to the change-password screen, excluding login/logout routes.【F:Infrastructure/EnforcePasswordChangeFilter.cs†L6-L42】
- **SignalR** – The notifications hub (`/hubs/notifications`) requires authentication and streams notification updates to connected clients.【F:Program.cs†L229-L356】【F:Hubs/NotificationsHub.cs†L1-L62】

## Background services

Hosted services run alongside the web host:

- `LoginAggregationWorker` – Aggregates daily login counts for analytics.【F:Services/LoginAggregationWorker.cs†L13-L110】
- `TodoPurgeWorker` – Deletes completed tasks after `Todo:RetentionDays` and logs warnings when operations fail.【F:Services/TodoPurgeWorker.cs†L14-L108】
- `UserPurgeWorker` – Enforces the user lifecycle (undo window and hard delete).【F:Services/UserPurgeWorker.cs†L18-L120】
- `NotificationDispatcher` – Moves queued notification dispatches into user-facing records and pushes updates to the hub.【F:Services/Notifications/NotificationDispatcher.cs†L21-L140】
- `NotificationRetentionService` – Periodically trims old notifications according to retention options.【F:Services/Notifications/NotificationRetentionService.cs†L20-L147】

All workers honour `CancellationToken`s and write structured logs so operators can monitor progress.

## Minimal APIs

The app exposes several API groups in addition to Razor Pages:

- **Calendar (`/calendar/events`)** – CRUD operations guarded by role-based authorization. Supports recurrence rules, Markdown descriptions (sanitised via Markdig + HtmlSanitizer), and range-limited queries to protect the database.【F:Program.cs†L240-L420】
- **Notifications (`/api/notifications`)** – List, count unread, mark read/unread, and mute per project. Responses respect project visibility via `ProjectAccessGuard` and normalise routes for consistent client-side navigation.【F:Program.cs†L500-L615】【F:Services/Notifications/UserNotificationService.cs†L23-L220】
- **Process templates (`/api/processes/...`)** – Fetch and maintain stage templates and checklist items with row-version checks to prevent conflicting edits. Policies `Checklist.View` and `Checklist.Edit` gate read/write access.【F:Program.cs†L617-L760】

## Storage and uploads

Uploads share a single root resolved by `UploadRootProvider`:

- Defaults to `wwwroot/uploads` (or `PM_UPLOAD_ROOT` env) and ensures project folders exist on demand.【F:Services/Storage/UploadRootProvider.cs†L17-L100】
- `ProjectPhotoService` stores derivatives under `projects/{projectId}/{sizeKey}` with size/quality limits defined in `ProjectPhotoOptions`. It accepts optional virus scanning by plugging in an `IVirusScanner`.【F:Services/Projects/ProjectPhotoService.cs†L82-L512】
- `DocumentService` and `ProjectCommentService` reuse the same provider for document previews and comment attachments, ensuring consistent storage hygiene.【F:Services/Documents/DocumentService.cs†L20-L240】【F:Services/ProjectCommentService.cs†L22-L238】

## Configuration binding

Options classes under `Configuration/` and `Services/Projects/` are bound during startup:

- `TodoOptions`, `UserLifecycleOptions`, `NotificationRetentionOptions`, `ProjectPhotoOptions`, and `ProjectDocumentOptions` all map to sections in `appsettings.json`. See [configuration-reference.md](configuration-reference.md) for defaults and overrides.【F:Program.cs†L108-L236】
- `ProjectPhotoOptionsSetup` populates a default storage root when none is configured so local development works without extra setup.【F:Services/Projects/ProjectPhotoOptionsSetup.cs†L1-L33】

Keeping these bindings up to date ensures new configuration keys are documented and discoverable.
