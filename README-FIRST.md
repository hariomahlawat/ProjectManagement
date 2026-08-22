# PRISM Compendium Phase 41 — production convergence

Ready-to-paste overlay for the supplied `ProjectManagement-master` source tree.

## Outcome

This package fixes both reported failure classes:

1. **Changing cover imagery could leave the wrong crop/image in the editor or PDF.**
   A new source photograph now receives a centred focal point, reselecting the same photograph
   preserves its crop, stale photo-picker responses cannot overwrite a newer choice, and the
   server-returned canonical cover state is rehydrated after save. Front and back automatic image
   allocation is now genuinely independent on both the browser and server; Portfolio Quartet still
   requires four distinct front-cover photographs.
2. **Compendium PDF preview/download was environment-dependent on the offline IIS server.**
   The original code permitted three deployment-dependent states: a framework-dependent publish,
   different DM Sans discovery rules for SkiaSharp and QuestPDF (including a silent renderer
   fallback to Lato), and only a 1-point planning reserve between the two shaping engines. With a
   large publication, an IIS/native shaping difference could therefore cross QuestPDF's physical
   page boundary. The generic catch converted the real font/layout/drawing exception into the
   unhelpful `unexpected error` alert seen in the photograph.

The LAN being offline is not itself a required PDF dependency. The fault was that the deployed
payload and font/layout contracts were not deterministic or validated. Phase 41 makes the publish
self-contained, uses one exact local six-face DM Sans contract, reserves one normal body line
(12 points), verifies the PDF after composition, and records the precise failing stage/page/project
if a data-specific error remains.

## Scope

- 23 project-relative source/test/deployment files
- no database migration
- no preset schema change
- no permission-model change
- no external CDN, web font, telemetry or runtime Internet dependency
- no redesign of the existing Compendium PDF

## Apply to source

1. Back up or commit the current source tree.
2. Copy every folder and file beside this README into the root containing
   `ProjectManagement.csproj`.
3. Allow replacement of files with the same path. New files are added automatically by the SDK
   project globs.
4. Do **not** copy `README-FIRST.md`, `VALIDATION.md`, `FILE-MANIFEST.md`, `SHA256SUMS.txt` or
   `CHANGESET.patch` into the application unless you want to retain the handoff material.

Alternatively, from the original supplied source baseline:

```bash
patch -p1 < CHANGESET.patch
```

## Validate on the build machine

Run from the project root:

```powershell
npm ci --ignore-scripts
npm test
dotnet restore .\ProjectManagement.sln
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release --no-restore
.\ops\publish\create-publish-folder.ps1
```

The publish script now creates a **self-contained win-x64** payload and refuses to complete unless
all of the following are present and usable:

- .NET host/runtime files
- the SkiaSharp win-x64 native library
- all six local DM Sans publication faces
- QuestPDF PDF generation
- PdfPig PDF reopening and page-count verification

## Deploy to the offline IIS server

1. Stage the newly generated `artifacts\publish\ProjectManagement` directory as a complete release;
   do not merge individual DLLs into the old live folder.
2. From that exact staged/deployed directory, run:

   ```powershell
   .\ProjectManagement.exe --compendium-offline-self-test
   ```

   Continue only when it returns a JSON line containing `"status":"ok"` and exit code `0`.
3. Create a durable diagnostics folder and grant Modify permission to the application-pool identity:

   ```powershell
   New-Item D:\PMData\Logs\Compendium -ItemType Directory -Force
   icacls D:\PMData\Logs\Compendium /grant "IIS AppPool\ProjectManagementPool:(OI)(CI)M"
   ```

4. Configure the IIS worker environment variable
   `PRISM_COMPENDIUM_DIAGNOSTICS_DIR=D:\PMData\Logs\Compendium`.
5. Swap the staged folder into service using the site's normal `app_offline.htm`/release rollback
   procedure, recycle the application pool, and confirm the response header
   `X-PRISM-Compendium-Build` contains
   `CompendiumPdf_2026-08-22_phase41-production-convergence`.
6. Test a one-project preview, the production 78-project selection, final download, and front/back
   cover-image replacement.

`PRISM_PUBLICATION_FONTS_DIR` is optional. Use it only if the six fonts are intentionally stored
outside the site; point it at an absolute publication-font directory containing `dm-sans`.

## Failure diagnosis after deployment

The publisher still receives a safe message and reference. The matching JSONL record is written to:

```text
D:\PMData\Logs\Compendium\compendium-generation-YYYYMMDD.jsonl
```

It identifies the build, operation, trace reference, generation stage, planned physical page,
project ID/name and inner exception chain. Failure codes now distinguish publication read, cover
resolution, font initialization, page planning, QuestPDF layout, drawing, composition and final PDF
verification.

## Rollback

No database rollback is required. Restore the previous complete publish directory and recycle the
application pool. Any Phase 41 JSONL diagnostic files may be retained safely.
