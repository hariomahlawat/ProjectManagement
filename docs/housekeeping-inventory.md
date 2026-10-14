# PRISM repository housekeeping inventory

This inventory records the repository-root cleanup performed before broader Codex-assisted maintenance.

## Current root shape

After three cleanup passes, the repository root contains:

- 47 entries
- 17 files
- 30 directories
- only two root Markdown/text documents: `README.md` and `AGENTS.md`
- no root `.patch`, `.sql` or `.ps1` delivery/verification files
- no root APPLY/REPLACE/READY-TO-PASTE instruction files

The root is now reserved for current project entry points, build/configuration files and primary source/tooling directories.

## What was removed

The cleanup removed historical delivery material whose implementation is already preserved by Git history/current source, including:

- phase READMEs and completion notes;
- changed-file/package/source manifests;
- validation and static-verification transcripts;
- root patch bundles;
- obsolete APPLY/REPLACE/READY-TO-PASTE instructions;
- `_PATCH/` and `_PACKAGE/` delivery directories;
- old visual-proof directories;
- one-off module/hotfix notes for Admin, IPR, Notebook, Search V2, Media Library, Publications, workflow and reporting;
- obsolete root verification scripts and phase preflight SQL;
- an obsolete Phase 7 hotfix helper under `tools/` that referenced already-removed delivery material.

Git history remains the archive for these materials.

## Durable material preserved and relocated

Material that still has current engineering or operational value was moved into stable locations:

- migration policy → `docs/engineering/migrations.md`
- production readiness → `docs/deployment/production-readiness.md`
- production deployment checklist → `docs/deployment/production-checklist.md`
- publication-font offline guidance → `docs/deployment/publication-fonts.md`
- production migration inventory query → `ops/database/production-migration-inventory.sql`
- production startup diagnostic query → `ops/database/production-startup-diagnostic.sql`
- Media Library LAN/NAS guidance → `docs/media-library/`
- Search V2 verification helper → `tools/Verify-SearchV2.ps1`
- Compendium cover-slot acceptance checks → `docs/manual-tests/compendium-cover-slots.md`

## Explicitly retained because it is still operationally wired

### `ReadyToReplace/`

Retain for now.

Current evidence:

- `ProjectManagement.csproj` explicitly excludes `ReadyToReplace/**` from application source/content discovery.
- `.github/workflows/build-project-tot-v3-zip.yml` actively reads from `ReadyToReplace/Project-Tot-Precision-and-UX-v3/**`, runs its package builder and commits a generated ZIP.

This is distribution material rather than application source, but it is still consumed by current automation. Removing it requires first retiring or redesigning that workflow.

## Ongoing rule

New phase/hotfix delivery documents must not be added to the repository root. Durable guidance belongs under `docs/`, operator assets under `ops/`, reusable engineering scripts under `tools/`, and ephemeral delivery evidence belongs in Git/CI rather than committed transcripts.

Housekeeping changes should remain separate from behavioral application changes.
