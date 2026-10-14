# Repository housekeeping policy

See [housekeeping-inventory.md](housekeeping-inventory.md) for the current root inventory, deletions already made, retained live dependencies and next cleanup candidates.

PRISM has accumulated a large amount of historical delivery material in the repository root. The root currently contains hundreds of entries, including phase READMEs, patch files, changed-file manifests, replacement instructions, validation transcripts and temporary packaging material.

This material can be useful as historical evidence, but its current placement makes navigation harder for developers and code agents and creates a risk that obsolete instructions are mistaken for current guidance.

## Objectives

1. Keep the repository root limited to active project entry points, build/configuration files and primary source directories.
2. Keep durable engineering documentation under `docs/`.
3. Preserve genuinely useful historical material without allowing it to masquerade as current instructions.
4. Remove accidental/generated artifacts that Git can already reproduce.
5. Avoid large destructive cleanup commits until each class of artifact has been classified.

## Current artifact classes

### Durable — keep active

Examples:

- `README.md`
- `AGENTS.md`
- `ProjectManagement.sln`
- `ProjectManagement.csproj`
- `Directory.Build.props`
- appsettings templates that are intentionally versioned
- `MIGRATIONS-POLICY.md` until incorporated into durable docs
- source directories, tests, `docs/`, `ops/`, `tools/`, `.github/`

### Historical documentation — archive after review

Examples:

- `README_PHASE*.md/txt`
- `README-PHASE*.txt`
- module phase completion notes
- production-hardening notes tied to a specific completed phase
- one-off build/hotfix READMEs

Target location: `docs/archive/` grouped by subsystem or time period if retention is still valuable.

### Patch/replacement delivery material — remove or archive after verification

Examples:

- `*.patch`
- `*_PATCH/`, `_PACKAGE/`
- `READY-TO-PASTE*`
- `REPLACE_THESE_FILES.txt`
- `COPY INTO PROJECT ROOT.txt`
- changed-file and replacement manifests

Before deleting these, verify that the corresponding changes are already present in Git history/current code and that no current build/deployment workflow consumes the artifact.

### Validation transcripts — normally remove after historical review

Examples:

- `VALIDATION*.txt`
- `STATIC-VERIFICATION.txt`
- `TEST-RESULTS.txt`
- phase validation-result files

Validation should be reproducible through scripts, tests and CI. Static transcripts tied to old commits should not be treated as proof for the current tree.

### Temporary/probe material — remove

Files explicitly named as temporary probes (for example `_tmp_probe_should_not_exist.txt`) should not remain on the default branch unless a test intentionally requires the committed file.

## Cleanup sequence

Do not perform a mass deletion first.

1. Establish the durable README, agent guidance and documentation index.
2. Inventory references to historical artifacts from code, scripts, CI and current docs.
3. Delete obvious temporary/generated artifacts.
4. Move retained historical documentation into `docs/archive/`.
5. Remove patch/replacement bundles only after confirming their changes exist in Git/current source.
6. Update `.gitignore` and packaging scripts so the clutter does not return.
7. Keep cleanup in dedicated PRs separate from behavioral application changes.

## Agent rule

Code agents must not follow a root-level historical patch/readme instruction merely because its filename says `APPLY FIRST`, `README FIRST`, `READY TO PASTE`, or similar. Validate the instruction against the current branch and current durable documentation first.
