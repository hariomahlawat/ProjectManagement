# PRISM ERP — Agent Engineering Guide

This repository contains PRISM ERP, an ASP.NET Core application. Treat the executable implementation as the primary source of truth.

## Deployment constraint: offline local LAN

PRISM is deployed as an **offline application on a local LAN**. Production runtime must not depend on public Internet connectivity.

Engineering consequences:

- Do not introduce runtime dependencies on public CDNs, cloud APIs, SaaS services, external identity providers, remote fonts, public telemetry endpoints, or Internet-hosted assets unless an explicit deployment change has been approved.
- Browser assets required in production should be shipped with the application or served from approved LAN infrastructure.
- Features must fail safely when Internet access is absent; "works only when online" is a production defect unless the feature is explicitly optional.
- Package restore/download activity is a build/development concern and must not be confused with production runtime connectivity.
- Prefer deterministic, locally deployable dependencies and document any native/runtime prerequisites that must be included in the deployment package.
- LAN-only deployment does not relax authentication, authorization, audit, upload/download validation, CSRF protection, secrets handling, or resource-level access control.
- Do not assume a cloud secret store, cloud queue, cloud object store, cloud monitoring platform, or externally reachable callback URL exists.

## Active production and change continuity

PRISM ERP is already deployed and running in production. Production continuity is a first-class constraint for every change.

- Treat existing deployed behavior and configuration as compatibility constraints until verified otherwise. Repository defaults and documentation are not proof of actual deployed configuration; never claim production state has been verified unless it has actually been inspected.
- Prefer additive, backward-compatible, reversible changes. Preserve current routes, configuration keys, persisted-data contracts and operator-script interfaces unless a deliberate transition plan exists.
- Do not introduce startup-fail or fail-closed behavior merely because it is theoretically safer unless the deployed prerequisite has been verified. When immediate enforcement could interrupt production, separate preparation and observability from enforcement, and plan the transition.
- High-risk changes must include deployment impact, a rollback path and regression coverage. Database changes must preserve migration lineage and compatibility with live data.
- Assume rolling or overlapping process and worker execution unless the deployment procedure explicitly guarantees a full outage. Design changes to remain compatible during that overlap; follow the deployment SOP's outage procedure when applicable.
- Security and integrity fixes remain necessary. Design production continuity into their remediation rather than trading it away; these guardrails do not relax authentication, authorization or data-integrity requirements.

## Source-of-truth order

When sources disagree, use this order:

1. Reachable application code and dependency registration
2. EF Core model/configuration and migration lineage
3. Automated tests
4. Runtime configuration and deployment scripts
5. Current architecture/engineering docs
6. Historical phase notes, patch bundles, replacement-package instructions, and old validation reports

Do not change working implementation merely to make it agree with an older document. Update the document instead.

## Technology baseline

- .NET 8 / ASP.NET Core
- Razor Pages plus HTTP APIs and SignalR
- Entity Framework Core with PostgreSQL
- ASP.NET Core Identity and policy-based authorization
- Node/esbuild for committed Notebook frontend assets
- ClosedXML/OpenXML/QuestPDF/SkiaSharp and related libraries for document/media/report workflows

The main application project is `ProjectManagement.csproj`; tests are in `ProjectManagement.Tests`.

## Required local validation

A normal code change should start from a clean dependency restore:

```bash
npm ci
dotnet restore
```

Then run, as applicable:

```bash
npm test
npm run check:notebook-assets
dotnet build
dotnet test
```

The .NET build can rebuild Notebook assets and intentionally fails when required Node dependencies are absent.

Database/migration work must also pass the PostgreSQL migration tests used by `.github/workflows/database-migrations.yml`.

## Architectural guardrails

- Keep PRISM a modular monolith unless a separate deployment boundary is explicitly required.
- Do not add a generic repository abstraction around EF Core.
- Keep HTTP/Razor PageModels thin; business rules belong in application/domain services.
- Treat project-specific authorization as a server-side concern. Hiding UI controls is never sufficient authorization.
- Preserve resource-level access checks on reads, mutations, downloads, and API endpoints.
- Do not duplicate business calculations in Razor Pages, exports, PDF/Excel/PPT generators, or JavaScript. Resolve business facts once and project them into output-specific models.
- Workflow/state transitions that update authoritative state, history/audit records, and outbox work should be transactionally coherent.
- Background workers and outbox consumers must tolerate retry and duplicate execution.
- Preserve migration lineage. Do not edit an already-shipped migration unless the migration policy explicitly permits it.
- Avoid introducing a second implementation named `V2`, `New`, `Legacy`, or similar without a documented convergence/removal plan.
- Never commit credentials, production secrets, private keys, connection passwords, or real user data.

## High-risk areas

Changes in these areas require focused tests and careful review:

- Project stage/workflow transitions and planning/approval
- Authorization and Access Governance
- Project facts and derived reporting values
- Upload/download, media, document and attachment storage
- Background workers, outbox processing and retention
- Database migrations and startup database behavior
- Search/SearchV2 convergence
- Report/export generation
- Any new network dependency or externally hosted production asset

## Documentation rules

Durable engineering documentation belongs under `docs/`. The root README is the project entry point, not a patch note.

Historical implementation notes and replacement bundles may be useful evidence, but they must not be treated as current operating instructions unless referenced by current code or current docs.

If a change invalidates a durable document, update that document in the same change.

Start with:

- `README.md`
- `docs/README.md`
- `docs/current-state.md`
- `docs/engineering-invariants.md`
- `docs/build-and-test.md`
- `docs/housekeeping.md`
