# Media Library (Photos)

The Media Library is a unified, read-optimised catalogue of every photo and video in PRISM plus
optional external folders (local disk, NAS, UNC shares). It powers `/Photos`, albums, bulk ZIP
download, media classification and the opt-in People (face recognition) feature. Source modules
keep owning their files; the catalogue only references them and stores private derivatives.

Code lives in `Features/MediaLibrary` (registered by `AddMediaLibrary` in
`MediaLibraryServiceCollectionExtensions`, called from `Program.cs`). Pages are in `Pages/Photos`
and `Pages/Admin/MediaSources`, `Pages/Admin/MediaIntelligence`.

## Database

- Separate EF context `Features/MediaLibrary/Data/MediaLibraryDbContext` on the **same PostgreSQL database and
  connection string** as the application, with its own history table
  `__EFMigrationsHistory_MediaLibrary` and migrations in `Features/MediaLibrary/Data/Migrations`
  (latest: `20260820103000_HardenMediaPersonUserLinkExperience`; lineage pinned in
  `immutable-migration-ids.txt`).
- Migrations for both contexts are applied **automatically and mandatorily at startup** by the
  startup gate in `Program.cs` (`DatabaseStartupMigrator`). `MediaLibrary:AutoMigrate` is a legacy
  bind point and has no effect. `MediaLibrarySchemaService` verifies required tables afterwards.
- Tables: `MediaLibrarySources`, `MediaAssets`, `MediaProcessingJobs`, `MediaClassificationRuns`,
  `MediaClassificationAudits`, `MediaFaces`, `MediaFaceEmbeddings`, `MediaPersons`,
  `MediaPersonFaces`, `MediaPersonUserLinks`, `MediaFaceReviewDecisions`, `MediaIdentityAudits`,
  `MediaAlbums`, `MediaAlbumItems`, `MediaCurationAudits`.
- `PrismMediaOutboxMessages` lives in `ApplicationDbContext` (written in the same transaction as
  the source change).

## Sources and ingestion

| Source | How assets enter the catalogue |
| --- | --- |
| PRISM (`MediaLibrarySourceType.Prism`, key `prism`) | `PrismMediaCatalogueSynchronizer` periodically reconciles project photos, project videos, visit photos, social-media event photos and activity photos (`MediaAssetOrigin`). Interval: `Catalogue:SynchronizeIntervalSeconds`, else `SynchronizeIntervalMinutes` (code default 10, appsettings 5). |
| Activity attachments (fast path) | `PrismMediaOutboxSaveChangesInterceptor` writes outbox rows when `Activity`/`ActivityAttachment` change; `PrismMediaOutboxWorker` (2 s poll, 5 min leases, row-lock claims) ingests them via `PrismActivityMediaIngestionService`. |
| External folders (`FileSystem`; `NetworkShare` is an obsolete alias) | `MediaSourceScannerWorker` scans enabled sources through `SafeFileEnumerator` (skips reparse points/symlinks) and `FileSystemMediaSourceScanner`. |

External sources require `ExternalSources:Enabled=true` and `ScannerWorkerEnabled=true` (both
false by default). Sources are created and maintained in **Admin > Media sources**
(`Pages/Admin/MediaSources`); `MediaSourceBootstrapper` can also import sources from
`ExternalSources:Sources` in configuration, but database-created sources remain authoritative.
A source root must be a fully qualified local or UNC path, is treated as read-only, and the
account running the app pool must have read access (see `README-MEDIA-LIBRARY-NAS.md` and
`README-MEDIA-LIBRARY-EXTERNAL-SOURCES.md` at the repo root for share setup). Default allowed
extensions: jpg, jpeg, png, webp, gif, bmp, mp4, webm, mov, m4v, ogg.

Path safety: `FileSystemPathResolver.ResolveAssetPath` rejects absolute relative-paths and any
resolved path outside the source root; the face-thumbnail and portrait endpoints apply the same
root check to the cache.

Availability: `MediaAssets` carry `AvailabilityStatus` (Available, TemporarilyUnavailable,
SourceMissing, AccessDenied, Unsupported, Corrupt). `MediaAvailabilityReconciliationWorker` turns
missing-content failures into availability state. `MediaAssetVisibilityPolicy` is the single
read filter (available, not deleted/archived, source enabled and visible, external sources only
when the external feature is on) used by listings, direct media requests and bulk downloads.

## Processing, derivatives and classification

- `MediaProcessingWorker` leases `MediaProcessingJobs` (`AnalyseAsset`, `BuildDerivatives`,
  `RebuildDerivatives`, `ExtractMetadata`, `ClassifyMedia`, `ReclassifyAsset`, `DetectFaces`,
  `GenerateFaceEmbeddings`, `AssignFaceCluster`, `RebuildIntelligence`) and runs
  `MediaAssetProcessor`. Failed jobs retry with jittered backoff and move to `DeadLetter` after
  `Processing:MaxAttempts` (default 5) or on a permanent error.
- `MediaDerivativeService` produces WebP thumbnails (`ThumbnailMaxPixels`, default 480) and
  previews (`PreviewMaxPixels`, default 1920) with SkiaSharp at `WebpQuality` 84, stored in
  `CacheRoot` (default `App_Data/media-cache`) as `<shard>/<assetId>/v<version>-thumb|preview.webp`
  (`MediaCachePathResolver`). Derivatives are also generated on demand by `Pages/Photos/Media`.
- `MediaMetadataReader` (MetadataExtractor) reads EXIF; `MediaClassifier` +
  `MediaClassificationDecisionPolicy` classify photos as Photograph, Screenshot, ScannedDocument,
  Diagram, PresentationSlide, Graphic or Unknown using the `Classification` thresholds; uncertain
  results go to review (`Pages/Admin/MediaIntelligence/Classifications`).

## People (face detection and recognition), opt-in

Disabled by default (`People:Enabled=false`, `People:WorkerEnabled=false`). Nothing is ever
auto-confirmed: `AutoConfirmEnabled=true` fails options validation.

- Engine: `OnnxFaceAnalysisEngine` (Microsoft.ML.OnnxRuntime, CPU) with two pinned OpenCV Zoo models:
  - Detector **YuNet** `face_detection_yunet_2026may.onnx` (adapter `YuNet`, 320x320, BGR, MIT)
  - Embedder **SFace** `face_recognition_sface_2021dec.onnx` (adapter `SFace`, 112x112, 128-d, Apache-2.0)
- **Model placement**: files must be at `<People:ModelRoot>/<FileName>`; `ModelRoot` defaults to
  `App_Data/media-models`, resolved against the application content root (the deployed app folder
  on IIS). Weights are not in source control. Install with
  `Features/MediaLibrary/models/install-approved-models.sh [destination]` or
  `install-approved-models.ps1`; both verify SHA-256. The scripts default to `App_Data/media-models`
  relative to the repository, so on a server pass the deployed `App_Data/media-models` path.
  `FaceModelReadinessService` verifies presence, checksum (`Sha256` in configuration), licence
  metadata and ONNX tensor contracts; it is shown on **Admin > Media Intelligence**.
  Provenance: `Features/MediaLibrary/MODEL-MANIFEST.json`, `DEPENDENCY-LICENSES.json`,
  `THIRD-PARTY-NOTICES.md`.
- The YuNet detector is also used, when installed, by classification face-presence assistance
  (`Classification:FacePresenceAssistanceEnabled`, default true) even with People disabled; if it
  is missing the classifier proceeds without it.
- Workers (only when `People:Enabled` and `WorkerEnabled`): `FaceAnalysisQueueWorker` queues
  face jobs for eligible photographs; `FaceCandidateRefreshWorker` matches new embeddings to
  confirmed people (`CandidateSearchEnabled`); `FaceIdentityGroupingRefreshWorker` groups unnamed
  faces for review (`GroupingEnabled`). Suggestions are review-only.
- Review and identity management: `Pages/Photos/People/Review`, `Details` (Admin, HoD).
  A user whose account is linked to a person (`MediaPersonUserLinks`) can confirm or reject
  suggested photos of themselves from their profile in `/Photos`.

## Workers summary

| Worker | Registered when |
| --- | --- |
| `PrismMediaOutboxWorker` | catalogue enabled and `Catalogue:SynchronizePrismMedia` |
| `MediaSourceScannerWorker` | catalogue enabled and (`SynchronizePrismMedia` or external scanner enabled) |
| `MediaProcessingWorker`, `MediaAvailabilityReconciliationWorker` | `Processing:WorkerEnabled` or People worker enabled |
| `FaceAnalysisQueueWorker` | People enabled and worker enabled |
| `FaceCandidateRefreshWorker` / `FaceIdentityGroupingRefreshWorker` | as above plus `CandidateSearchEnabled` / `GroupingEnabled` |

Worker registration is decided from configuration at startup; changing these flags requires a restart.

## Pages and permissions

| Page | Access |
| --- | --- |
| `/Photos` (timeline, albums, people filter, person profiles) | any authenticated user; "manage identity" and People review links for Admin, HoD |
| `/Photos/Media` (thumb/preview/original/download) | any authenticated user, subject to `MediaAssetVisibilityPolicy` |
| `/Photos/Download` (ZIP, max `BulkDownload:MaxItems` 120 and `MaxSourceBytes` 2 GiB) | any authenticated user |
| `/Photos/Albums/Actions` | any authenticated user can create albums; an album can be changed by its creator or by Admin, HoD, Comdt (`MediaAlbumService.CanManage`) |
| `/Photos/People` (directory of confirmed people), `/Photos/People/Portrait` | any authenticated user (People enabled) |
| `/Photos/People/Review`, `/Photos/People/Details`, `/Photos/FaceThumbnail` | roles Admin, HoD |
| `/Admin/MediaIntelligence` (readiness, queueing), `/Admin/MediaIntelligence/Classifications` | roles Admin, HoD |
| `/Admin/MediaSources` | policy `Admin.Media.View`; actions additionally check `Admin.Media.Configure`, `Admin.Media.OperateQueue` or `Admin.Media.Recover` in the admin services |

Project photo upload/edit remains in `Pages/Projects/Photos` (governed by `ProjectAccessGuard`).

## Key options (`MediaLibrary`)

`Enabled`, `CacheRoot`, `Catalogue.*`, `ExternalSources.*`, `Processing.*`, `BulkDownload.*`,
`Classification.*`, `People.*` (thresholds, `ModelRoot`, `Detector`, `Embedder`). All are
validated at startup by `MediaLibraryOptionsValidator` (only for enabled capabilities).
See `Features/MediaLibrary/DEPLOYMENT-CHECKLIST.md` for the People rollout procedure.
