# Extending the platform

Follow these guardrails when introducing new capabilities. Keeping to these patterns makes it easier to reason about deployments, testing, and documentation.

## Roles and identity

1. Add the role name to `IdentitySeeder` so fresh environments pick it up automatically.【F:Data/IdentitySeeder.cs†L14-L71】
2. Update authorization policies in `Program.cs` if the new role should grant access to existing features, and register additional policies for new modules as needed.【F:Program.cs†L108-L206】
3. Reflect the new role in the admin UI (filter dropdowns, export columns) and update [docs/razor-pages.md](razor-pages.md) with its capabilities.

## Configuration

1. Bind new configuration sections through `builder.Services.AddOptions<TOptions>().Bind(...)` in `Program.cs` so values can be overridden via environment variables.【F:Program.cs†L108-L236】
2. Document defaults and environment overrides in [docs/configuration-reference.md](configuration-reference.md). Mention any CLI switches or environment variables you introduce.
3. When configuration affects uploads or storage paths, route values through `IUploadRootProvider` rather than hard-coding directories.【F:Services/Storage/UploadRootProvider.cs†L17-L100】

## Services and background work

* Register new services/interfaces in `Program.cs` with scoped lifetimes unless state truly needs to be singleton.
* Prefer constructor-injected collaborators; avoid service location inside page handlers.
* Background workers should derive from `BackgroundService`, honour cancellation tokens, and log failures explicitly (see `NotificationDispatcher` for an example).【F:Services/Notifications/NotificationDispatcher.cs†L20-L200】 Document new workers in [docs/infrastructure-services.md](infrastructure-services.md).
* Emit audit entries for every mutating operation using `IAuditService` so admin exports remain trustworthy.【F:Services/AuditService.cs†L16-L120】

## Razor Pages and APIs

* Keep Razor Page models thin—delegate business logic to services. Enforce authorization at both the page attribute level and service layer.
* Avoid inline `<script>` or `style` attributes; rely on static assets initialised via `wwwroot/js/*.js`. Run `npm run lint:views` before committing.
* When exposing new minimal APIs, place them in `Program.cs` near related routes, apply appropriate `[Authorize]` policies, and document them in [docs/architecture.md](architecture.md).

## Database changes

* Use EF Core migrations (`dotnet ef migrations add`) and check them into `Migrations/`. Keep `ApplicationDbContext` tidy by grouping DbSet declarations.
* Update [docs/data-domain.md](data-domain.md) with any new entities, including edge cases or concurrency tokens.

## Documentation checklist

After implementing a feature:

- Update the relevant guide in `docs/` (architecture, data-domain, infrastructure, razor-pages, user-guide) with behaviour, edge cases, and configuration touchpoints.
- Add manual test steps under `docs/manual-tests/` if QA needs new coverage.
- Mention new user-facing flows in [docs/user-guide/README.md](user-guide/README.md).

Staying disciplined about these steps keeps the codebase maintainable and prevents regressions when onboarding new contributors.
