# PRISM repository housekeeping inventory

This inventory records the state of the repository root after the first conservative cleanup pass.

## Current root shape

After the initial deletions, the root still contains approximately:

- 314 entries before the final two obsolete package-instruction deletions in this branch
- 280 files and 34 directories at that point
- 69 `.patch` files
- 109 README-like files
- 114 phase-named files
- 18 validation/verification-style files
- 14 manifest/changed-file/package-list style files
- 15 apply/replace/ready-to-paste style files

The exact counts will change as cleanup proceeds; the important point is that historical delivery material still dominates the root.

## Deleted in the first cleanup pass

These files were removed because they were self-contained delivery artifacts or an accidental probe, had no live code/build/CI references, and contained no unique executable source:

- `_tmp_probe_should_not_exist.txt`
- `CHANGE-SUMMARY.txt`
- `CHANGED-FILES.md`
- `FILE-MANIFEST.md`
- `FILE-MANIFEST.txt`
- `PACKAGE-FILES.txt`
- `REPLACE_THESE_FILES.txt`
- `SOURCE-FILES.txt`
- `STATIC-VALIDATION.txt`
- `TEST-RESULTS.txt`
- `VALIDATION-node-tests.txt`
- `VALIDATION.txt`
- `APPLY-README.txt`
- `COPY INTO PROJECT ROOT.txt`

These were historical delivery metadata/transcripts. Their associated source changes remain in Git history and current source where applicable.

## Explicitly retained because they are still wired or referenced

### `ReadyToReplace/`

Retain for now.

Current evidence:

- `ProjectManagement.csproj` explicitly excludes `ReadyToReplace/**` from application source/content discovery.
- `.github/workflows/build-project-tot-v3-zip.yml` actively reads from `ReadyToReplace/Project-Tot-Precision-and-UX-v3/**`, runs its package builder and commits a generated ZIP.

This is distribution material, not application source, but it is still operationally wired.

### `SHA256SUMS.txt`

Retain for now because multiple historical package manifests/readmes reference it.

### `STATIC-VERIFICATION.txt` and `VERIFICATION.txt`

Retain for now because current repository documents under the Search V2 historical plan/package material reference them.

### `VALIDATION-static.txt`

Retain for now because the FFC ITO role-governance package documentation references it.

### `MANIFEST.txt` and `REPLACEMENT-MANIFEST.txt`

Retain for now because multiple phase-package README files reference them.

## Next cleanup candidates

The next pass should examine these classes as groups rather than deleting isolated files:

1. **Phase validation transcripts**
   - `PHASE*-VALIDATION-RESULTS.txt`
   - `PHASE32_VALIDATION_RESULTS.txt`
   - generic `VALIDATION-RESULTS.txt`

2. **Historical patch bundles**
   - root `*.patch`
   - `_PATCH/`

3. **Historical replacement/package bundles**
   - `_PACKAGE/`
   - remaining `README-APPLY*`, `README-REPLACE*`, `READY-TO-PASTE.md`
   - remaining changed-file and package manifests

4. **Visual proof directories**
   - `Phase34-Visual-Proof/`
   - `Phase35-Visual-Proof/`

5. **Historical phase READMEs**
   - `README_PHASE*`
   - `README-PHASE*`
   - subsystem-specific phase completion notes

The preferred outcome is either deletion when Git history is sufficient, or relocation under `docs/archive/` when the material still has enduring diagnostic or historical value.

## Rules for subsequent cleanup

Before deleting or moving a historical artifact:

1. search the current repository for references to its exact path/name;
2. check `.github/workflows`, `ProjectManagement.csproj`, build scripts, `ops/` and `tools/`;
3. verify the artifact is not the only copy of a migration, script, template, configuration example or deployment instruction still in use;
4. prefer Git history over retaining reproducible test transcripts and patch payloads;
5. keep behavioral code changes out of housekeeping PRs.

## Desired end state

The repository root should eventually contain only:

- core solution/project/build files;
- current configuration templates;
- current deployment entry points;
- top-level source/test/tooling directories;
- `README.md` and `AGENTS.md`;
- a very small number of intentionally retained operational files.

Historical implementation material should not compete with current instructions for developer or Codex attention.
