# Extending the platform

These guardrails keep new work consistent with how PRISM is actually wired. For the composition root, policies, pipeline and workers, see [architecture.md](architecture.md).

## Roles and identity

1. **Define the role.** Add a constant to `Configuration/RoleNames.cs`. If it should be assignable, add it to `AssignableRoleArray`. `IdentitySeeder` and Administration both read `RoleNames.AssignableRoles`, so do not keep a separate list.
2. **Provision it in existing databases.** Production normally runs with `Database:RunSeedersOnStartup=false`, so the seeder will not create the role there. Provision it through a migration or an administrative step.
3. **Grant access through role arrays.** Add the role to the relevant array in one of:
   - `Configuration/Policies.cs`
   - `Areas/ProjectOfficeReports/Application/ProjectOfficeReportsPolicies.cs`
   - `Services/Admin/AdminCapabilityCatalog.cs`

   Avoid literal role strings in `[Authorize(Roles = ...)]`. Some older code still uses them, for example the `/api/lookups` group and `NotebookSystemItemsController`.
4. **Check legacy aliases.** If a policy must also accept a legacy alias (`ProjectOffice`, `Main Office`), include it explicitly. Most Project Office policies already do.
5. **Update documentation.** Document the role and its capabilities in [architecture.md](architecture.md#roles) and [razor-pages.md](razor-pages.md).

## Authorization

- **Register policies in one place.** Add them in the `AddAuthorization` block in `Program.cs`. Administrative capabilities belong in `AdminCapabilityCatalog`, which registers the policy and also feeds Access Governance and navigation.
- **Protect every new page explicitly.** There is no fallback policy, so a page is anonymous unless one of these applies:
  - It carries `[Authorize]` or `[Authorize(Policy = ...)]`.
  - It sits under a folder that has an authorization convention in `AddRazorPages`.

  Pages in the Admin area are only authentication-gated by the folder convention, so they also need an `AdminPolicies.*` policy.
- **Protect every new minimal API** with `.RequireAuthorization(...)` on the endpoint or its group.
- **Add resource-level checks where needed.** For example, `ProjectAccessGuard` handles project visibility. Role policies alone do not enforce this.
- **Enforce authorization twice.** Apply it at the endpoint/page and again in the service layer.

## Antiforgery

- **Razor Pages** validate antiforgery tokens on POST by default.
- **Controllers** should carry `[AutoValidateAntiforgeryToken]`, like the notebook and proliferation controllers.
- **Minimal APIs** that change state must validate the token themselves, because the global `UseAntiforgery` middleware does not check JSON endpoints. Call `IAntiforgery.ValidateRequestAsync`, catch `AntiforgeryValidationException` and return 400. `NotificationApi.ValidateAntiforgeryAsync` and `/api/usage/heartbeat` show the pattern.
- **JavaScript** sends the token in the `X-CSRF-TOKEN` header. The legacy `RequestVerificationToken` header is still accepted through the header shim in `Program.cs`, but should not be used in new code.

## Configuration

1. **Bind options** with `builder.Services.AddOptions<TOptions>().Bind(builder.Configuration.GetSection(...))`. Add `.ValidateDataAnnotations()` / an `IValidateOptions<T>` validator and `.ValidateOnStart()` so bad configuration fails at startup rather than at first use.
2. **Update the upload limit** if you add an upload limit key. Add it to `Infrastructure/UploadRequestLimitResolver`, so that the server-wide request-body limit (Kestrel, IIS and `FormOptions`) covers it.
3. **Document the configuration.** Record defaults and environment overrides in [configuration-reference.md](configuration-reference.md).
4. **Route file-system paths through the existing resolvers** rather than hard-coding directories. Examples are `IUploadRootProvider`, `IProjectDocumentStorageResolver` and the media path resolvers.
5. **Add CSP sources through configuration.** If a new page needs an external source, use `Security:Csp:*Extra` rather than editing the header middleware.

## Services and background work

- **Registration.** Register services in `Program.cs`. For a self-contained module, use a `ServiceCollection` extension method, following `AddSearchV2`, `AddProjectPublications` and `AddMediaLibrary`. Default to scoped lifetimes.
- **Injection.** Prefer constructor injection and avoid service location inside page handlers.
- **Background workers** derive from `BackgroundService`:
  - Create a DI scope per iteration.
  - Honour `stoppingToken`.
  - Wrap each iteration in `try/catch` that logs and continues. .NET 8 stops the whole host when an exception escapes `ExecuteAsync`, and PRISM does not override `BackgroundServiceExceptionBehavior`. `NotificationRetentionService`, `AuditRetentionWorker` and `ProjectRetentionWorker` are good models.
  - Report status through the optional worker-status service (`MarkStarted`/`MarkSucceeded`/`MarkFailed`) so Admin diagnostics can see failures.
  - Gate optional workers with a configuration flag at registration time.
- **Startup timing.** Hosted services start only after the database startup gate, so a worker can assume the application schema is current. Media workers must still check media schema readiness, because the media gate is non-fatal.
- **Auditing.** Emit audit entries for mutating operations through `IAuditService`. Use `IProjectContentAuditQueue` for fire-and-forget project-content audits.
- **Documentation.** Document new workers in the background-services table in [architecture.md](architecture.md#background-services).

## Razor Pages and APIs

- **Page models.** Keep page models thin and delegate to services.
- **No inline script.** CSP forbids inline scripts (`script-src 'self'`). Put scripts in `wwwroot/js/*.js`. Inline `style` attributes are allowed (`style-src-attr 'unsafe-inline'`), but inline `<style>` blocks are not, outside the routes listed in `architecture.md`. Run `npm run lint:views` before committing.
- **Minimal API placement.** Put new minimal APIs in a `Features/<Feature>/<Feature>Api.cs` `Map...` extension rather than growing `Program.cs` further, and call it from `Program.cs`. Add the route and its authorization to [architecture.md](architecture.md#http-apis).
- **API authentication responses.** Keep APIs under `/api/...` so that unauthenticated calls receive 401/403 rather than a login redirect (`IsApiOrRealtimeRequest`).

## Database changes

Follow [MIGRATIONS-POLICY.md](../MIGRATIONS-POLICY.md):

- **Creating migrations.** Create them with `./tools/Add-PrismMigration.ps1 -Name <Name> -Context ApplicationDbContext|MediaLibraryDbContext`, not with a bare `dotnet ef migrations add`. The helper keeps timestamps after the lineage tail and appends the immutable manifest.
- **Applied migrations.** Never rename, edit or delete an applied migration.
- **No runtime migrations.** Never call `Database.Migrate()` from runtime code.
- **Startup schema checks.** If a change needs a startup physical-schema check, extend `ApplicationDatabaseSchemaValidator`.
- **Documentation.** Update [data-domain.md](data-domain.md) with new entities and concurrency tokens.

## Documentation checklist

After implementing a feature:

- Update the relevant guide in `docs/` (architecture, data-domain, infrastructure-services, razor-pages, configuration-reference, user-guide).
- Add manual test steps under `docs/manual-tests/` if QA needs new coverage.
- Mention new user-facing flows in [user-guide/README.md](user-guide/README.md).
