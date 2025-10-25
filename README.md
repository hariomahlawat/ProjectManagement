# Project Management

A modular ASP.NET Core 8 platform for delivering task boards, celebrations, shared calendars, procurement-heavy projects, and administration workflows in a single solution. Razor Pages, Entity Framework Core, SignalR, and hosted background services work together so day-to-day collaboration and long-running governance all live in the same place.

## Table of contents

1. [Feature highlights](#feature-highlights)
2. [Analytics workspace](#analytics-workspace)
3. [Architecture overview](#architecture-overview)
4. [Configuration](#configuration)
5. [Local development](#local-development)
6. [Operational checklist](#operational-checklist)
7. [Documentation index](#documentation-index)
8. [Development guardrails](#development-guardrails)

## Feature highlights

- **Dashboard** – Landing hub with a personal task strip, organisation-wide event highlights, and shortcuts into other modules. Widgets reuse the same todo service used elsewhere so pinning, snoozing, and undo actions stay consistent (see `Pages/Dashboard/Index.cshtml.cs`).
- **Tasks** – Personal todo list with quick-add tokens, Indian Standard Time aware due dates, bulk clearing, pinning, snooze presets, and drag-to-reorder behaviour. All writes flow through `TodoService`, which protects concurrency and records lifecycle audits (`Pages/Tasks/Index.cshtml.cs`, `Services/TodoService.cs`).
- **Celebrations** – Birthday and anniversary registry with search, rolling window filters, edit rights for Admin/TA roles, leap-day handling, and soft delete. Helpers compute the next occurrence in IST (`Pages/Celebrations/Index.cshtml.cs`, `Helpers/CelebrationHelpers.cs`).
- **Calendar** – FullCalendar-based UI that exposes create, edit, drag, resize, delete, and recurrence management for Admin/TA/HoD roles while providing read-only drawers for everyone else. Minimal APIs sit in `Program.cs` and client logic lives in `wwwroot/js/calendar.js`.
- **Projects** – Workspace aggregating procurement facts, timeline plans, remarks, documents, and role assignments with staged approvals plus audit logging. Uploads, replacements, and removals run through moderated request queues handled by document services (`docs/projects-module.md`, `Services/Documents/*`).
- **Notifications** – Background dispatcher and retention workers move queued notifications into the SignalR hub, prune old rows, and surface unread counts with per-project filters (`Services/Notifications/*`).
- **Admin centre** – Role-gated area for user lifecycle, process templates, analytics, and lookup management. Guardrails block disabling the last active admin and offer CSV exports (`Areas/Admin/Pages/Users/Index.cshtml.cs`, `Program.cs`).

## Analytics workspace

The dedicated **Analytics** area focuses on project insights. Six responsive chart cards (category share, stage distribution, lifecycle counts, monthly completions with KPIs, slip buckets, and top overdue projects) include in-card filters and drill-through links into the Projects workspace. Minimal API endpoints under `/api/analytics/projects/*` rely on `ProjectAnalyticsService` to deliver pre-filtered aggregations while excluding archived or trashed projects. Chart.js orchestration runs from `wwwroot/js/analytics-projects.js`, with shared styles in `wwwroot/css/site.css`.

## Architecture overview

The solution follows a layered design:

1. **Contracts and view models** – Data contracts exchanged between services, APIs, and Razor Pages (`Contracts/`, `ViewModels/`).
2. **Services** – Business logic, audit pipelines, notification delivery, and background work registered through dependency injection so alternate implementations can be substituted for tests or specialised hosting (`Program.cs`).
3. **Razor Pages** – UI entry points under `Pages/` and `Areas/Admin`, each composing services, materialising view models, and enforcing per-request authorisation.
4. **Infrastructure** – Cross-cutting helpers (timezone conversion, password change guardrails, upload root resolution) that are registered once and reused across modules.
5. **Hosted background services** – Workers such as `UserPurgeWorker`, `TodoPurgeWorker`, `LoginAggregationWorker`, `NotificationDispatcher`, and `NotificationRetentionService` keep soft-deleted records tidy, pre-aggregate analytics, and push notifications without blocking UI threads.

A deeper tour of the architecture lives in [docs/architecture.md](docs/architecture.md).

## Configuration

Runtime configuration lives in `appsettings*.json` or environment variables:

- **Database** – `ConnectionStrings:DefaultConnection` (or the `DefaultConnection` environment variable) contains the PostgreSQL connection string.
- **Email** – `Email:Smtp:*` toggles between the production SMTP sender and the fallback no-op sender.
- **User lifecycle** – `UserLifecycle:HardDeleteWindowHours` and `UserLifecycle:UndoWindowMinutes` steer the deletion queue and undo window.
- **Todo retention** – `Todo:RetentionDays` controls how long completed items stay before purge.
- **Notifications** – `Notifications:Retention:*` powers the retention worker cadence and per-user cap.
- **Uploads** – `ProjectPhotos:*` and `ProjectDocuments:*` set storage limits, derivative presets, and virus-scan requirements. When the configured upload root cannot be created, the app falls back to `%LOCALAPPDATA%\ProjectManagement\uploads` on Windows or `<content-root>/uploads` elsewhere. Override with `PM_UPLOAD_ROOT` or `ProjectPhotos:StorageRoot` when needed.
- **Security headers** – `SecurityHeaders:ContentSecurityPolicy:ConnectSources` adds additional origins for websocket or API calls in reverse proxy scenarios.

See [docs/configuration-reference.md](docs/configuration-reference.md) for exhaustive key descriptions and environment overrides.

## Local development

1. **Prerequisites** – .NET 8 SDK, PostgreSQL 15 or higher, and Node.js 18 or higher for frontend tooling.
2. **Restore dependencies**
   ```bash
   dotnet restore
   npm install
   ```
3. **Prepare the database** – Update `appsettings.Development.json` or export `DefaultConnection`. The application runs migrations automatically on startup, but you can run `dotnet ef database update` (or `Update-Database`) to ensure tables such as `ProjectTotRequests` exist and the `__EFMigrationsHistory` table is current. Use PascalCase names for new migrations to avoid Roslyn warnings.
4. **Run the site**
   ```bash
   dotnet run --project ProjectManagement.csproj
   ```
   The development seeder provisions sample HoD and Project Officer accounts for quick testing.
5. **Lint Razor views** – `npm run lint:views` blocks inline scripts and styles.
6. **Execute tests**
   ```bash
   dotnet test
   npm test
   ```
   The Node suite covers modular frontend behaviour such as project scripts.

## Operational checklist

- Schedule regular backups of the PostgreSQL database.
- Ensure the uploads root (default `wwwroot/uploads` or `PM_UPLOAD_ROOT`) has sufficient storage and antivirus integration when `ProjectDocuments:EnableVirusScan` is true.
- Monitor background service logs—especially the notification dispatcher and purge workers—to detect stalled jobs early.
- Run `dotnet run -- --backfill-forecast` after deploying stage template changes so legacy projects receive recalculated forecast dates.

## Documentation index

Deep dives live under `docs/`:

- [Architecture and Configuration](docs/architecture.md)
- [Data and Domain](docs/data-domain.md)
- [Infrastructure and Services](docs/infrastructure-services.md)
- [Razor Pages Catalogue](docs/razor-pages.md)
- [Configuration Reference](docs/configuration-reference.md)
- [Projects Module Storyboard](docs/projects-module.md)
- [Project Office Reports Playbook](docs/ProjectOfficeReports_Directions.md)
- [Timeline Pipeline](docs/timeline.md)
- [Extending the Platform](docs/extending.md)
- [User Journeys](docs/user-guide/README.md)

## Development guardrails

- Inline styles and script handlers are forbidden in Razor views. Run `./tools/check-views.sh` before committing to ensure no inline `<script>` tags, `on*=` attributes, or inline `style` blocks land in `.cshtml` or `.html` files.
- Keep the documentation ASCII friendly. Earlier versions embedded non-standard citation brackets (`【】` and `†`) that rendered poorly on some platforms; all references now use plain Markdown so the files render consistently in browsers, IDEs, and PDF converters.
