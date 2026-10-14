# PRISM repository housekeeping inventory

This inventory records the repository-root cleanup performed before broader Codex-assisted maintenance.

## Current root shape

After the first two cleanup passes, the root contains:

- 146 entries
- 116 files
- 30 directories
- 0 root `.patch` files
- 48 README-like files
- 20 phase-named files
- 8 validation/verification-style files
- 0 APPLY/REPLACE/READY-TO-PASTE style root files

The root is substantially cleaner, but a third pass is still warranted for remaining one-off hotfix/module READMEs and historical validation material.

## First cleanup pass

The first pass removed clearly isolated delivery metadata/transcripts and an accidental probe, including:

- `_tmp_probe_should_not_exist.txt`
- generic changed-file/file/package/source manifests
- generic static validation and test-result transcripts
- obsolete copy/apply instructions

No application runtime source, migrations, configuration or deployment logic was changed.

## Second cleanup pass

The second pass removed historical delivery clusters whose source changes are already preserved by Git history/current code:

- Phase 6–14 changed-file and validation-result pairs
- Publications Phase 29–35 delivery READMEs/manifests
- Phase 16–24 and 39–40 delivery READMEs
- early `README_PHASE*` delivery notes
- all root `*.patch` bundles
- FFC phase `README_FIRST` delivery files
- obsolete root APPLY/REPLACE/READY-TO-PASTE instructions
- the historical `_PATCH/` patch bundle directory
- the historical `_PACKAGE/` delivery bundle directory
- Phase 34/35 visual-proof directories

Git history is the archive for these materials. They are no longer allowed to compete with current engineering instructions in the repository root.

## Explicitly retained because it is still operationally wired

### `ReadyToReplace/`

Retain for now.

Current evidence:

- `ProjectManagement.csproj` explicitly excludes `ReadyToReplace/**` from application source/content discovery.
- `.github/workflows/build-project-tot-v3-zip.yml` actively reads from `ReadyToReplace/Project-Tot-Precision-and-UX-v3/**`, runs its package builder and commits a generated ZIP.

This is distribution material rather than application source, but it is still consumed by current automation. Removing it requires first retiring or redesigning that workflow.

## Remaining historical/reference material requiring a third pass

The root still contains several classes that should be reviewed individually or by subsystem:

1. **One-off module/hotfix READMEs**
   - build/compile/migration fix notes
   - IPR refinement notes
   - Search V2 implementation/convergence notes
   - Admin phase notes
   - media/publications/workspace hardening notes

   Where the guidance is still durable, merge it into `docs/`; otherwise rely on Git history and remove it.

2. **Remaining validation/verification artifacts**
   - `SEARCH-V2-2-STATIC-VERIFICATION.txt`
   - `SEARCH-V2-QUALITY-HARDENING-STATIC-VERIFICATION.txt`
   - `STATIC-VERIFICATION.txt`
   - `VALIDATION-RESULTS.txt`
   - `VALIDATION-static.txt`
   - `VALIDATION.md`
   - `VERIFICATION.txt`
   - `PRODUCTION-VERIFICATION.sql`

   SQL that is genuinely part of production operations should move under `ops/` or `docs/deployment/`; reproducible validation transcripts should be removed.

3. **Remaining generic/historical manifests**
   - `MANIFEST.txt`
   - `SHA256SUMS.txt`
   - search-specific manifests and static-verification files

4. **Operational root scripts/SQL**
   - verify whether each root `.sql` and `.ps1` file is an active operator entry point;
   - move active operational assets under `ops/`;
   - remove historical delivery-only scripts.

## Rules for subsequent cleanup

Before deleting or moving a historical artifact:

1. search the current repository for references to its exact path/name;
2. check `.github/workflows`, `ProjectManagement.csproj`, build scripts, `ops/` and `tools/`;
3. verify the artifact is not the only copy of a migration, script, template, configuration example or deployment instruction still in use;
4. prefer Git history over retaining reproducible test transcripts and patch payloads;
5. move durable operational knowledge into `docs/` or `ops/` rather than leaving one-off instructions in the root;
6. keep behavioral application changes out of housekeeping PRs.

## Desired end state

The repository root should contain only:

- core solution/project/build files;
- current configuration templates;
- current deployment entry points;
- top-level source/test/tooling directories;
- `README.md` and `AGENTS.md`;
- a very small number of intentionally retained operational files.

Historical implementation material should not compete with current instructions for developer or Codex attention.
