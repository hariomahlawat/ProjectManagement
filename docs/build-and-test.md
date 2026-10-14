# Build and test PRISM ERP

This document records the repository's current build and validation path.

## Prerequisites

- .NET 8 SDK
- Node.js 22 (matches current GitHub Actions)
- npm
- PostgreSQL 16 for migration integration tests

## Restore

From the repository root:

```bash
npm ci
dotnet restore
```

The main .NET project intentionally depends on the Notebook frontend toolchain. `ProjectManagement.csproj` validates that required Node dependencies are present and may rebuild committed Notebook assets during `dotnet build` or publish.

## Standard validation

Run:

```bash
npm test
npm run check:notebook-assets
dotnet build
dotnet test
```

For view guardrails on a Unix-compatible shell:

```bash
npm run lint:views
```

## Notebook assets

Notebook source inputs include JavaScript under `wwwroot/js`, the Notebook build tooling and the npm manifests. Committed outputs include the Notebook bundle, source map and manifest under `wwwroot/dist`.

Useful commands:

```bash
npm run build:notebook
npm run build:notebook:prod
npm run check:notebook-assets
```

Do not hand-edit generated Notebook outputs to make a check pass. Change the source/build inputs and regenerate.

## Database migration validation

The repository contains a dedicated PostgreSQL migration workflow at `.github/workflows/database-migrations.yml`.

It currently:

1. starts PostgreSQL 16,
2. restores Node and .NET dependencies,
3. builds Release,
4. validates immutable migration metadata,
5. runs PostgreSQL migration integration tests.

The workflow uses:

```text
PRISM_RUN_POSTGRES_MIGRATION_TESTS=true
PRISM_TEST_POSTGRES_CONNECTION=...
```

For migration changes, reproduce the workflow locally where possible and ensure both the application migration lineage and Media Library migration lineage remain valid.

## CI note

`.github/workflows/notebook.yml` provides the broadest normal build/test path currently committed. The repository does not yet have one single workflow that expresses every required validation for every change type; use the affected-area tests plus the checks above.

## Before merging a change

At minimum, record:

- commands executed,
- whether the application built,
- tests run and their result,
- migration validation when schema is affected,
- frontend asset validation when Notebook inputs are affected,
- any checks that could not be executed and why.

Do not substitute an old `VALIDATION*.txt`, phase validation file or patch-bundle report for validation of the current commit.
