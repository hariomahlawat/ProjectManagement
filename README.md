# Project Management

A modular ASP.NET Core 8 platform for running task boards, celebrations, shared calendars, procurement-heavy projects, and admin workflows in one place. The solution combines Razor Pages, Entity Framework Core, SignalR, and background services to serve both day-to-day collaboration and long-running governance processes.

## Quick facts

| Area | Highlights |
| --- | --- |
| **Dashboard** | Landing hub that surfaces the personal task strip, upcoming organisation-wide events, and shortcuts to key modules. Widgets share the same todo service used elsewhere so pinning, snoozing, and undo behave consistently.【F:Pages/Dashboard/Index.cshtml.cs†L18-L113】 |
| **Tasks** | Personal todo list with quick-add tokens, IST-aware due dates, bulk clearing, pinning, snoozing presets, and drag-to-reorder. All writes go through `TodoService`, which guards concurrency and emits audit logs for lifecycle changes.【F:Pages/Tasks/Index.cshtml.cs†L17-L206】【F:Services/TodoService.cs†L12-L198】 |
| **Celebrations** | Birthday and anniversary registry with search, window filters, edit rights for Admin/TA roles, leap-day handling, and soft delete. UI relies on helpers that compute the next occurrence in Indian Standard Time.【F:Pages/Celebrations/Index.cshtml.cs†L17-L78】【F:Helpers/CelebrationHelpers.cs†L10-L37】 |
| **Calendar** | FullCalendar UI backed by minimal APIs. Admin/TA/HoD roles can create, edit, drag, resize, and delete events including recurrence rules; everyone else gets read-only views with Markdown descriptions and undo toasts on mutations.【F:Program.cs†L240-L420】【F:wwwroot/js/calendar.js†L1-L200】 |
| **Projects** | Workspace that aggregates procurement facts, timeline plans, remarks, documents, and role assignments with staged approvals and audit logging. Document uploads, replacements, and removals flow through a moderated request queue so HoDs/Admins can approve or reject changes before they reach the published library.【F:docs/projects-module.md†L1-L260】【F:Services/Documents/DocumentRequestService.cs†L12-L179】【F:Services/Documents/DocumentDecisionService.cs†L11-L188】 |
| **Notifications** | Background dispatcher and retention workers move queued notifications into the realtime hub and prune old rows. The API supports unread counts, per-project filters, and mute toggles scoped by project access checks.【F:Program.cs†L236-L356】【F:Services/Notifications/UserNotificationService.cs†L23-L220】 |
| **Admin centre** | Role-gated area for user lifecycle, process templates, analytics, and lookup management. User guardrails block disabling the last active admin and export CSV snapshots for audits.【F:Areas/Admin/Pages/Users/Index.cshtml.cs†L14-L87】【F:Program.cs†L125-L206】 |

## Analytics space

The portal includes a dedicated **Analytics** area focused on project insights. The landing page renders six responsive chart cards—category share, stage distribution, lifecycle counts, monthly stage completions (with KPI row), slip buckets, and the top overdue projects—each with in-card filters and drill-through links back into the Projects workspace. Data flows through minimal API endpoints (`/api/analytics/projects/*`) that rely on `ProjectAnalyticsService` for pre-filtered aggregations while excluding archived or trashed projects. Chart.js rendering and client-side filter orchestration live in `wwwroot/js/analytics-projects.js` alongside styles under `wwwroot/css/site.css`.

## Architecture overview

The solution follows a layered design:

1. **Contracts & ViewModels** define data exchanged between services, APIs, and Razor Pages (`Contracts/`, `ViewModels/`).
2. **Services** encapsulate business logic, audits, notifications, and background work. Most pages depend on interfaces registered in `Program.cs` so alternate implementations can be substituted during testing or hosting.【F:Program.cs†L82-L236】
3. **Razor Pages** under `Pages/` and `Areas/Admin` deliver the UI. Each page model composes services, materialises view models, and enforces per-request authorization.
4. **Infrastructure** houses cross-cutting helpers (timezone conversion, password-change filter, upload root resolution) that are registered once and reused across modules.
5. **Background services** (`UserPurgeWorker`, `TodoPurgeWorker`, `LoginAggregationWorker`, `NotificationDispatcher`, `NotificationRetentionService`) run via hosted services configured in `Program.cs`. They keep soft-deleted records tidy, pre-aggregate analytics, and fan out notifications without blocking UI threads.【F:Program.cs†L108-L206】

A deeper tour of the architecture lives in [docs/architecture.md](docs/architecture.md).

## Configuration

All runtime knobs are stored in `appsettings*.json` or environment variables. Highlights:

- **Database** – `ConnectionStrings:DefaultConnection` (or `DefaultConnection` env var) for PostgreSQL connection strings.【F:Program.cs†L47-L78】
- **Email** – `Email:Smtp:*` toggles between the production SMTP sender and the no-op fallback.【F:Program.cs†L215-L228】
- **User lifecycle** – `UserLifecycle:HardDeleteWindowHours` and `UserLifecycle:UndoWindowMinutes` steer the deletion queue and undo window.【F:appsettings.json†L23-L28】
- **Todo retention** – `Todo:RetentionDays` defines how long completed items stay before purge.【F:appsettings.json†L29-L31】
- **Notifications** – `Notifications:Retention:*` drives the retention worker sweep cadence and per-user cap.【F:appsettings.json†L32-L37】
- **Uploads** – `ProjectPhotos:*` and `ProjectDocuments:*` control storage limits, derivative presets, and virus-scan requirements.【F:appsettings.json†L38-L65】  When the configured upload root cannot be created (for example, due to file-system permissions), the app falls back to a user-writable directory: `%LOCALAPPDATA%\ProjectManagement\uploads` on Windows or `<content-root>/uploads` on other operating systems. Set the `PM_UPLOAD_ROOT` environment variable or `ProjectPhotos:StorageRoot` configuration value to force a specific location. If the fallback cannot be created either, startup will fail with guidance to set `PM_UPLOAD_ROOT` explicitly.
- **Security headers** – `SecurityHeaders:ContentSecurityPolicy:ConnectSources` adds additional origins for websocket/API calls when reverse proxies are involved.【F:Program.cs†L229-L310】

A complete reference, including environment variable overrides, is available in [docs/configuration-reference.md](docs/configuration-reference.md).

## Local development

1. **Prerequisites**: .NET 8 SDK, PostgreSQL 15+, Node.js 18+ (for frontend tooling).
2. **Install dependencies**:
   ```bash
   dotnet restore
   npm install
   ```
3. **Database**: update `appsettings.Development.json` or export `DefaultConnection`. The app automatically applies migrations on startup, but if you are pointing at an existing database run `dotnet ef database update` (or `Update-Database` in Package Manager Console) to ensure tables such as `ProjectTotRequests` are created and the `__EFMigrationsHistory` table lists the latest entries.【F:Program.cs†L229-L244】
4. **Run**:
   ```bash
   dotnet run --project ProjectManagement.csproj
   ```
   The development seeder provisions sample HoD/Project Officer accounts for quick testing.【F:Data/IdentitySeeder.cs†L30-L78】
5. **Lint Razor views** (guards against inline scripts/styles):
   ```bash
   npm run lint:views
   ```
6. **Tests**:
   ```bash
   dotnet test
   npm test
   ```
   The Node test suite exercises modular frontend behaviour such as project scripts.

## Operational checklist

- Schedule regular backups of the PostgreSQL database.
- Ensure the uploads root (default `wwwroot/uploads` or `PM_UPLOAD_ROOT`) has sufficient storage and antivirus integration when `ProjectDocuments:EnableVirusScan` is true.【F:Services/Projects/ProjectPhotoOptionsSetup.cs†L1-L38】【F:Services/ProjectCommentService.cs†L174-L238】
- Monitor background service logs—especially the notification dispatcher and purge workers—to spot stalled jobs early.【F:Services/Notifications/NotificationDispatcher.cs†L21-L140】
- Run `dotnet run -- --backfill-forecast` after deploying stage template changes so legacy projects receive recalculated forecast dates.【F:Program.cs†L229-L252】

## Documentation index

The `docs/` folder hosts deep dives for every subsystem:

- [Architecture and Configuration](docs/architecture.md)
- [Data and Domain](docs/data-domain.md)
- [Infrastructure and Services](docs/infrastructure-services.md)
- [Razor Pages Catalogue](docs/razor-pages.md)
- [Configuration Reference](docs/configuration-reference.md)
- [Projects Module Storyboard](docs/projects-module.md)
- [Timeline Pipeline](docs/timeline.md)
- [Extending the Platform](docs/extending.md)
- [User Journeys](docs/user-guide/README.md)

## Development guardrails

- Inline styles and script handlers are forbidden in Razor views. Run `./tools/check-views.sh` before committing to ensure no `<script>` tags without `src`, `on*=` attributes, or `style="..."` slips into `.cshtml`/`.html` files.
