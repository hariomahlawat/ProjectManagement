# PRISM Admin Phase B — Core Architecture and Consistency

## Application order

Apply this package **after** `PRISM-Admin-PhaseA-Safety-Hardening-Ready-to-Replace.zip`.

This archive contains only the source files added or changed by Admin Phase B. Copy its contents over the project root while preserving the directory structure.

## Scope implemented

### Shared administrative foundations

- Central administrative capability policies for access, users, security, logs, recovery, master data, ingestion and media administration.
- One authoritative Admin navigation catalog used by the Admin module navigation, role-based navigation and dashboard shortcuts.
- Canonical administrative account-state resolver for Active, Password Change Required, Temporarily Locked, Disabled and Pending Deletion states.
- Central IST-aware administrative time service with exact local-day-to-UTC boundaries.
- Central structured administrative audit service with actor, roles, entity, before/after state, reason, trace ID, origin and outcome metadata.
- Reusable safe CSV writer with quoting, UTF-8 BOM and spreadsheet-formula neutralisation.
- Standard administrative operation-result contracts.

### Users and account lifecycle

- Batched user/role query service, replacing per-user role lookups.
- Server-side paging, role filtering, canonical account-state filtering and safe CSV export.
- Correct single-boundary IST formatting without double conversion.
- User create, edit, disable, enable, reset and deletion pages now use the shared Admin users policy.
- User management and lifecycle actions emit structured central Admin audit events while preserving the existing audit fallback.

### Dashboard

- Dedicated dashboard service consolidating account, authentication, audit, recovery and calendar metrics.
- Successful-login metrics use the canonical authentication event stream.
- Account figures use the shared state resolver.
- Dashboard attention links and quick links are generated from the navigation catalog rather than hard-coded routes.
- Recent administrative actions use consistent action families and IST presentation.

### Logs and login analytics

- Dedicated Admin log query service with exact User ID filtering, bounded paging, bounded pager rendering and safe export.
- Date filters use exact IST calendar-day boundaries converted to UTC.
- Login scatter drill-down now passes the exact User ID expected by Logs.
- Login scatter lookback is clamped server-side to 365 days.
- Login overview uses canonical authentication events and IST calendar-day grouping.
- Shared safe CSV handling is used for login analytics export.

### Database health

- Dedicated database-health service reporting:
  - connectivity and query latency;
  - provider, host and database;
  - PostgreSQL server version and database size;
  - known, applied and pending migrations;
  - applied migrations missing from the deployed assembly;
  - structured Healthy, Warning, Critical and Unavailable checks.

### Recovery adoption

- Project Trash and Document Recycle pages use the shared recovery policy and Admin time service.
- Document Recycle date filters use exact IST boundaries.
- Unexpected recycle failures return safe trace references rather than raw infrastructure details.
- Existing Phase A quarantine and rollback-safe permanent-deletion behaviour remains unchanged.

## Database migration

No database migration is required for Admin Phase B.

The Phase A migration `20261201180000_AdminPhaseASafetyHardening` must already be present and applied.

## Replacement procedure

1. Back up the application source and production database.
2. Stop the IIS application pool or application service.
3. Copy all files from this archive to the project root, preserving paths and replacing existing files when prompted.
4. Confirm that Admin Phase A files and migration are already present.
5. Restore dependencies and validate:

```powershell
npm ci
dotnet restore
dotnet build -c Release
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release --no-build
```

6. Publish using the normal production procedure.
7. Start the application and verify:
   - Admin Dashboard links and metrics;
   - Users paging, role/state filters and CSV export;
   - Login Analytics point-to-Logs drill-down;
   - Logs IST date filtering and bounded pagination;
   - DB Health migration-lineage checks;
   - Project Trash and Document Recycle access and timestamps.

## Important operational notes

- The Admin navigation catalog is now the authoritative source for Admin destinations. Future Admin links should be added there rather than independently in multiple providers or views.
- Administrative pages and service handlers must continue to enforce policies server-side; navigation visibility is not an authorisation control.
- Store and query timestamps in UTC. Use `IAdminTimeService` only at local-date query boundaries and presentation boundaries.
- Use `IAdminAuditService` for new administrative mutations.
- Use `ISafeCsvWriter` for new Admin CSV exports.

## Validation performed in the preparation environment

- All changed/new C# files were parsed using the C# tree-sitter grammar: no syntax errors detected.
- Modified JavaScript syntax check passed.
- Modified Razor files passed targeted guardrails for inline scripts, inline event handlers and inline styles.
- Admin navigation destinations were checked against existing Razor Page files.
- Admin policy constants were checked against policy registrations.
- Git whitespace/error check passed.
- Patch clean-application check passed against the Admin Phase A baseline.
- ZIP integrity and SHA-256 manifest verification passed.

The .NET SDK and installed frontend dependencies were unavailable in the preparation environment, so the Release build, xUnit suite and complete npm suite were not executed there.
