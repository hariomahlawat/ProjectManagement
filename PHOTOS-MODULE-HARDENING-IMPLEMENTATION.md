# PRISM Photos and Media Intelligence Hardening

## Scope

This package hardens the existing Photos catalogue, automatic image classification, human review workflow, background processing and optional face-intelligence foundation. It retains the current source-owned media architecture and does not introduce a database migration.

## Implemented changes

### 1. Classification admission gate

- Upgraded the deterministic classifier to `hybrid-media-v6`.
- Converts filename, metadata, pixel-structure and optional detector evidence into a normalized category distribution.
- Enforces category thresholds, winner-to-runner-up score margins and stricter Photograph acceptance.
- Blocks automatic Photograph acceptance when material non-photograph evidence remains.
- Honors the configured screenshot, document and diagram detector switches.
- Adds exact-flatness and micro-variation measurements to better separate natural photographs from digitally filled graphics.
- Uses keyword-boundary matching so words such as `uniform` are not misread as `form`.

### 2. Detector-only portrait assistance

- Added an isolated detector-only readiness path.
- The classifier can use the approved YuNet detector while `MediaLibrary:People:Enabled` and the People worker remain disabled.
- Detector-only analysis does not create face rows, thumbnails, embeddings, candidates or identities.
- The full People pipeline still requires the stricter feature, worker, detector, embedder, schema and protected-cache checks.

### 3. Content-version integrity

- Added a central content-change invalidation service.
- Binary source changes reset automatic and manual classification state.
- Existing faces, embeddings, review decisions and active identity assignments are retired and audited.
- Manual reclassification to a non-photograph, or reset to automatic classification, removes active face intelligence.
- Exact SHA-256 verification before face processing prevents a replaced source file from inheriting an earlier identity.
- Face-analysis results are revalidated against current content and current classification immediately before commit.

### 4. Human review workflow

- Unknown images now start with `Choose classification…`; Photograph is never preselected implicitly.
- A reason is required when correcting the automatic prediction and for every bulk decision.
- Individual and bulk actions validate asset availability, type and concurrency tokens.
- Bulk changes are transactional.
- Reviewer concurrency conflicts are returned as actionable warnings rather than generic failures.
- Review cards show prediction, runner-up, margin, final decision, decision reason and face-eligibility status.
- Review previews use contained images rather than destructive cropping.
- The bulk toolbar is sticky and selection tokens are submitted only for selected images.
- Stale reclassification uses classification-only jobs when derivatives are already available.

### 5. Worker and retry reliability

- Processing leases are renewed while long-running jobs execute.
- Completion and failure updates verify that the current worker still owns the lease.
- Face-only worker mode processes all face job types.
- Normal media processing excludes face jobs while the People worker is disabled.
- Catalogue or reviewer changes during inference raise a recoverable superseded-result condition and safely retry.
- Graceful application cancellation returns unfinished work to Pending instead of recording a false classifier failure.
- Derivative and classification status updates no longer incorrectly fail a completed derivative when only classification fails.

### 6. Catalogue refresh and stale classifier handling

- PRISM and external-source scans identify automatic results produced by an older classifier.
- Stale classification state is fully reset before requeueing.
- Classification-only and derivative-plus-classification work are queued separately.
- Active valid job locks are not stolen; the concurrency token causes obsolete in-flight results to yield safely.
- Photo-to-video and video-to-photo source changes invalidate earlier derived intelligence.

### 7. Model provenance

- Readiness accepts either an authoritative HTTP/HTTPS source URL or complete approved offline provenance.
- Offline provenance requires publisher, approved artifact ID and acquisition date in `yyyy-MM-dd` format.
- This aligns production configuration with controlled offline deployment.

### 8. Main Photos experience

- User-facing counts and controls use `images` where the catalogue contains diagrams, documents and graphics as well as photographs.
- Classification filters are not fabricated when the optional catalogue is unavailable.
- Single-item collections have bounded portrait, square and landscape presentation sizes.
- Viewer deep links use stable asset-based keys rather than positional indexes.
- Viewer navigation updates stable hashes.
- The viewer traps keyboard focus, makes the background inert, restores prior focus and supports Escape/arrow/zoom shortcuts.

## Database impact

No schema change or new migration is included. The implementation uses the existing classification, processing, face, identity-audit and concurrency columns introduced by the current Media Library migrations.

## Deployment

1. Back up the application database, configuration and source-owned media.
2. Stop the IIS application pool and all PRISM worker instances.
3. Copy the ready-to-replace package into the project root, preserving directory structure.
4. Restore dependencies:
   - `npm ci`
   - `dotnet restore`
5. Validate and publish:
   - `dotnet test ProjectManagement.sln`
   - `dotnet publish ProjectManagement.csproj -c Release -r win-x64 --self-contained false`
6. Keep the following disabled during classifier validation:
   - `MediaLibrary:People:Enabled = false`
   - `MediaLibrary:People:WorkerEnabled = false`
7. Verify the approved detector and embedder files, SHA-256 values, model provenance and cache permissions.
8. Start the application and open **Photos > Intelligence**.
9. Refresh readiness.
10. Run **Reclassify stale** from Classification review.
11. Review ambiguous images before considering People activation.

## People activation gate

Do not enable People solely because readiness is green. First validate the classifier against an approved representative corpus and confirm at minimum:

- Photograph precision of at least 98%.
- Non-photograph-to-Photograph false-positive rate below 1%.
- Separate results for images with and without EXIF metadata.
- All ambiguous results remain in human review.
- Privacy, retention, licensing and access-control approval is recorded.

## Validation completed in this review environment

- JSON configuration files parsed successfully.
- `wwwroot/js/pages/photos-library.js` passed Node syntax validation.
- JavaScript test suite: 125 of 126 tests passed. The one failure is an unrelated existing Action Tasks searchable-select placeholder test; no Action Tasks file is changed by this package.
- Changed C# files passed delimiter and lexical static checks.
- `git diff --check` passed.

The review environment does not contain a .NET SDK or compiler, so a C# build and xUnit execution could not be performed here. `dotnet test ProjectManagement.sln` remains a mandatory deployment gate.

## Rollback

1. Stop the application pool and workers.
2. Restore the previous source/package and configuration.
3. Restart the application.
4. No database rollback is required because this package adds no migration.
