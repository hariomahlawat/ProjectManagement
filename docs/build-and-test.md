# Build and test PRISM ERP

This document records the repository's current build and validation path.

## Prerequisites

- .NET 8 SDK
- Node.js 22 (matches current GitHub Actions)
- npm
- PostgreSQL 16 for migration integration tests

## Offline production versus connected build

Production PRISM runs on an offline LAN. Development/CI restore commands below may require access to NuGet/npm registries, but deployed application behavior must not require those registries or other public Internet services.

For release readiness, distinguish two questions:

1. **Can the source be restored/built in the approved build environment?**
2. **Can the published application start and execute its required features with public Internet access unavailable?**

The second question is a production requirement. Where practical, release validation should include an offline-runtime smoke test using only the LAN/local dependencies that will exist at the deployment site.

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

`tools/check-views.sh` requires `bash` and ripgrep (`rg`) built with PCRE2. Because the Content-Security-Policy `script-src` has no `'unsafe-inline'`, it rejects inline `<script>` blocks, inline `on*` event handler attributes and `javascript:` URLs, matching across lines. It also rejects `<style>` elements, which `style-src-elem` blocks outside Development and a few routes. Script tags with `src` or `asp-src-include`, and data blocks (`application/json`, `application/ld+json`, `text/template`, `text/x-template`, `text/html`), are allowed. It exits 1 on a violation and 2 when it cannot run (missing path, ripgrep missing, without PCRE2, or failing), so a broken check never passes silently. Not checked: `style` attributes (allowed by `style-src-attr 'unsafe-inline'`), unquoted handler values, and markup inside comments. Its fixture tests run in the Lint Views workflow and as part of `npm test`; they are skipped, not failed, where bash or a PCRE2 ripgrep is unavailable (for example plain Windows).

`npm test` also runs the theme-coverage ratchet (`tools/theme-coverage.test.js`): it fails when a stylesheet gains literal colours, or literals that no dark-theme rule repaints, beyond `tools/theme-coverage-baseline.json`. Use the site tokens instead; after converting a page, run `node tools/theme-coverage.js --write` to lower the baseline (it refuses to raise one without `--allow-increase`). See [the dark theme doc](ux/dark-theme-palette.md).

## Notebook assets

Notebook source inputs include JavaScript under `wwwroot/js`, the Notebook build tooling and the npm manifests. Committed outputs include the Notebook bundle, source map and manifest under `wwwroot/dist`.

Useful commands:

```bash
npm run build:notebook
npm run build:notebook:prod
npm run check:notebook-assets
```

Do not hand-edit generated Notebook outputs to make a check pass. Change the source/build inputs and regenerate.

The build reads sources with LF line endings, so a Windows checkout (CRLF working files) produces the same source map as CI. `check:notebook-assets` runs the build with Node directly and prints an error if a step cannot start, rather than exiting silently.

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
