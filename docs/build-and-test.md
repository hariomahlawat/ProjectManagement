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

`npm test` also runs the dead-declaration ratchet (`tools/css-dead-declarations.test.js`). A dead declaration is one that a later rule with the same selector, in the same or an unconditional `@media`, `@supports` or `@container` context, always overrides; they accumulate when a fix is layered on top instead of edited in place. A later value that an older browser may reject (`dvh`/`svh`/`lvh` and container units, `color-mix()`, `oklch()` and similar; the list is `RECENT_VALUE` in the tool) does not count as an override, because the earlier declaration is that browser's fallback, which matters on an offline LAN where browsers can be several versions old. The test fails when any stylesheet under `wwwroot/css` has more than `tools/css-dead-declarations-baseline.json` allows, or when the tool cannot read a stylesheet. The baseline is empty since November 2026, so every stylesheet must have none; edit the earlier rule instead of overriding it. The tool reads those three conditions (nested ones combine) and skips `@keyframes`, `@page` and `@font-face`; any other at-rule, or a stray brace, makes it stop with an error rather than report a file it cannot reason about as clean. `node tools/css-dead-declarations.js <file.css>` lists a file's dead declarations; `--fix` removes them and merges same-selector rules only where no rule in between sets a related property, so the cascade result cannot change. After a cleanup, `node tools/css-dead-declarations.js --all --write` lowers the baseline (it refuses to raise one without `--allow-increase`).

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

## Local demo data

`tools/dev-data/` holds SQL scripts that add clearly labelled demo records to a **local development database**, for checking pages that are empty without recent activity. Never run them against production or a shared database.

- `command-desk-demo-seed.sql` adds what the Command desk's "What moved forward" counts: two projects (stages completed, one project completed, ToT, proliferation), IPRs filed and granted, an FFC delivery and installation, visits, a training and activities, plus three closed tasks. Every name starts with `Demo · ` (IPR numbers with `DEMO-`). All dates count back from the day the script runs, so the records fall within the last 30 days. It needs the user names of an existing Project Officer and HoD, and does nothing if the demo projects already exist.
- `command-desk-demo-remove.sql` deletes only those records (and their child rows).

```bash
psql -v ON_ERROR_STOP=1 -v po_user=<po-user-name> -v hod_user=<hod-user-name> -d <dev-database> -f tools/dev-data/command-desk-demo-seed.sql
psql -v ON_ERROR_STOP=1 -d <dev-database> -f tools/dev-data/command-desk-demo-remove.sql
```

To bring the dates up to date, run the remove script and then the seed script. The demo projects get new ids each time.

- `molti-sre-demo-seed.sql` adds twelve MOLTI projects and SRE projects named `Demo · …` (docs/ux/molti-sre-projects.md): cases at different stages with dated stages and costs, most assigned to Project Officers by user name (`ankur`, `arpan`, `chinmay`, `nishant`, `claude_review_po`; a name the database does not have leaves the project unassigned), two with no officer, one dropped, two delivered. They are created as the first active HoD (else Admin). Safe to run twice: a title that already exists in its module is skipped.
- `molti-sre-demo-remove.sql` deletes every MOLTI or SRE project whose title starts with `Demo · `, with its stages, years, remarks and attachment records.

```bash
psql -v ON_ERROR_STOP=1 -d <dev-database> -f tools/dev-data/molti-sre-demo-seed.sql
psql -v ON_ERROR_STOP=1 -d <dev-database> -f tools/dev-data/molti-sre-demo-remove.sql
```

- `returned-plans-demo-seed.sql` adds eight projects named `Demo · Return N · …`, each with a timeline plan the HoD returned, shaped as `PlanApprovalService` leaves the rows: returned with nothing to fix, correction draft discarded, resubmitted then withdrawn, project handed to another PO, project completed, the older return shape, and two that were resubmitted (pending, and approved). It needs a Project Officer, a HoD and a second Project Officer (`-v other_po_user=…`). `returned-plans-demo-remove.sql` deletes only those projects. Because their names also start with `Demo · `, the Command desk remove script deletes them too, and the Command desk seed script does nothing while they exist.

## CI note

`.github/workflows/notebook.yml` provides the broadest normal build/test path currently committed. The repository does not yet have one single workflow that expresses every required validation for every change type; use the affected-area tests plus the checks above.

## Test parallelism

`ProjectManagement.Tests/xunit.runner.json` sets xUnit's `parallelAlgorithm` to `conservative`. Keep it that way.

xUnit runs tests on `maxParallelThreads` worker threads, which defaults to the processor count, so 2 on a GitHub runner. Before October 2026 the suite used xUnit 2.4, which has only the "aggressive" algorithm. Whenever a test awaited, that algorithm started another test class on the freed thread, so hundreds of tests were in flight at once. Every awaiting test's continuation then queued for one of those 2 threads, behind other tests' synchronous work: `WebApplicationFactory` booting a host inside `CreateClient()`, `Process.WaitForExit` in the publish-script tests, and EF model building. `WebApplicationFactory` tests waited in that queue for over 100 seconds, so `HttpClient` gave up with "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing". The stack ended in `ResponseBodyReaderStream.ReadAsync`, because the TestServer response pipe resumes on xUnit's synchronization context. The .NET thread pool sat idle the whole time. Async tests that do not use a host were slowed too, typically 40–110 seconds each, but nothing timed them out. About 13% of `notebook` runs failed this way.

The conservative algorithm starts only as many tests as there are worker threads, so a test that is awaiting no longer competes with an unbounded queue. Do not raise `HttpClient.Timeout`, add retries or disable parallelization to work around a slow in-process host test. Look for synchronous blocking on the xUnit thread instead.

`longRunningTestSeconds` (60) with `diagnosticMessages` makes xUnit name any test still running after a minute in the test output, which is the first thing to check if such timeouts come back. `dotnet test --blame-hang` does not help here: vstest restarts its hang timer whenever any test starts or finishes, so it never fires while thousands of other tests keep completing.

## Before merging a change

At minimum, record:

- commands executed,
- whether the application built,
- tests run and their result,
- migration validation when schema is affected,
- frontend asset validation when Notebook inputs are affected,
- any checks that could not be executed and why.

Do not substitute an old `VALIDATION*.txt`, phase validation file or patch-bundle report for validation of the current commit.
