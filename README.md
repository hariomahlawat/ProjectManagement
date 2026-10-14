# PRISM ERP

PRISM ERP is a .NET 8 ASP.NET Core project and operational information system built around project lifecycle management, planning and approvals, reporting, collaboration, documents/media and associated institutional workflows.

This README is the repository entry point. Historical phase notes and patch instructions elsewhere in the repository are not authoritative for the current code.

## Technology

- ASP.NET Core / .NET 8
- Razor Pages, HTTP APIs and SignalR
- Entity Framework Core with PostgreSQL
- ASP.NET Core Identity and policy-based authorization
- Node/esbuild for Notebook frontend assets
- ClosedXML, OpenXML, QuestPDF, SkiaSharp and related reporting/media tooling

## Get started

Prerequisites:

- .NET 8 SDK
- Node.js 22 and npm
- PostgreSQL when running database-backed integration/migration tests

Restore and validate:

```bash
npm ci
dotnet restore
npm test
npm run check:notebook-assets
dotnet build
dotnet test
```

See [docs/build-and-test.md](docs/build-and-test.md) for the current validation matrix and PostgreSQL migration-test setup.

## Repository orientation

Start here before making non-trivial changes:

- [AGENTS.md](AGENTS.md) — engineering rules for Codex and other code agents
- [Documentation index](docs/README.md)
- [Current system state](docs/current-state.md)
- [Engineering invariants](docs/engineering-invariants.md)
- [Build and test](docs/build-and-test.md)
- [Housekeeping policy](docs/housekeeping.md)

The primary composition root is `Program.cs`. The primary EF Core context is `Data/ApplicationDbContext.cs`.

## Source of truth

When documentation conflicts with reachable code, EF mappings/migrations, tests or current runtime configuration, treat the implementation as authoritative and update the documentation.

The repository contains historical patch bundles, phase notes, replacement instructions and validation transcripts. Those are historical evidence unless current code or durable docs explicitly depend on them.

## Change discipline

PRISM is treated as a modular monolith. Prefer small, independently testable changes. Preserve server-side resource authorization, workflow transactional integrity, migration lineage, audit behavior and canonical business-fact resolution.

Do not introduce broad architectural rewrites merely to modernize style. Improvements should reduce a demonstrated correctness, security, operability or maintenance risk.
