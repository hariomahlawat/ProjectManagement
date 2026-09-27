# Media Library — People (Phase 8) Deployment Checklist

Architecture overview: `docs/media-library.md`.

## 1. Pre-deployment

- Back up the PostgreSQL database (application and Media Library tables share one database).
- Back up the configured media cache.
- Confirm the application targets .NET 8 and PostgreSQL uses the supported Npgsql/EF Core version.
- Confirm `Admin` and `HoD` role membership is current.
- Review local biometric/privacy policy, retention, access control and incident-response requirements.
- Keep `MediaLibrary:People:Enabled` and `WorkerEnabled` set to `false` during the initial deployment.

## 2. Replace application files

Deploy the current application build while preserving environment-specific secrets and connection strings. The shipped `appsettings.json` keeps People disabled (`Enabled=false`, `WorkerEnabled=false`); merge carefully if the installation has local configuration changes.

## 3. Build and test

From the solution root:

```powershell
npm ci
npm run build

dotnet restore
dotnet build ProjectManagement.sln --configuration Release --no-restore
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj --configuration Release --no-build
```

`npm ci` must run before `dotnet build`: the project's `ValidateNotebookDependencies` target fails the build when `node_modules/esbuild` is missing. Do not deploy if compilation, Razor compilation, JavaScript build or automated tests fail.

## 4. Media Library migrations

Migrations for `MediaLibraryDbContext` (history table `__EFMigrationsHistory_MediaLibrary`, same
database and connection string as the application) are applied **automatically and mandatorily
at application startup** together with the application migrations (`DatabaseStartupMigrator` in
`Program.cs`). `MediaLibrary:AutoMigrate` is a legacy setting and has no effect. No manual
`dotnet ef database update` step is required; the database role used by the application must be
allowed to apply the migrations.

After the first start, verify that `__EFMigrationsHistory_MediaLibrary` contains the latest
migration listed in `Features/MediaLibrary/Data/Migrations/immutable-migration-ids.txt`
(currently `20260820103000_HardenMediaPersonUserLinkExperience`). The People schema controls
were introduced by `20260628190000_HardenPeopleExperience`, including:

- Dedicated face-analysis status and version fields.
- One active person assignment per face.
- Unique pending face/person suggestion.
- Unique intentional-unidentified acknowledgement per face.
- Model/version candidate indexes.
- Identity audit person and metadata fields.
- Optimistic-concurrency tokens.

## 5. Install approved models

Windows PowerShell:

```powershell
./Features/MediaLibrary/models/install-approved-models.ps1
```

Linux:

```bash
chmod +x ./Features/MediaLibrary/models/install-approved-models.sh
./Features/MediaLibrary/models/install-approved-models.sh
```

The scripts download the pinned OpenCV Zoo model files, verify exact SHA-256 hashes and stop on mismatch. Do not rename or substitute model files without creating a separately reviewed model profile and model-version migration plan.

The files must end up in `MediaLibrary:People:ModelRoot` (default `App_Data/media-models`,
resolved against the application content root, i.e. the deployed site folder). The scripts
default to `App_Data/media-models` relative to the repository checkout; on a server pass the
deployed folder explicitly, for example
`./install-approved-models.sh /var/www/prism/App_Data/media-models` or
`./install-approved-models.ps1 -Destination <site>\App_Data\media-models`. Expected files:

- `face_detection_yunet_2026may.onnx`
- `face_recognition_sface_2021dec.onnx`

The configured `Detector:Sha256` / `Embedder:Sha256` values must match these files. Note that the
YuNet detector is also used, if present, by classification face-presence assistance
(`MediaLibrary:Classification:FacePresenceAssistanceEnabled`, default `true`) even while People is
disabled.

## 6. Validate readiness with processing disabled

Set:

```json
"People": {
  "Enabled": true,
  "WorkerEnabled": false
}
```

Restart the application and open **Admin → Media Intelligence** (`/Admin/MediaIntelligence`,
roles Admin and HoD). Confirm:

- Model configuration recognised.
- Model files installed.
- Detector and embedder checksums verified.
- Licence metadata recorded.
- ONNX Runtime available.
- ONNX input/output contracts validated.
- Media Library migration ready.
- Private derivative cache writable.

People pages may now be inspected, but automatic processing remains off.

## 7. Pilot processing

Before organisation-wide enablement:

- Use a representative, approved pilot set.
- Validate face boxes, duplicate suppression, landmark alignment and thumbnail quality.
- Check false positives and false negatives across lighting, pose, age, headgear and image quality.
- Calibrate `MinimumDetectionConfidence`, `MinimumQualityScore` and `CandidateSimilarityThreshold` using local validation data.
- Never lower similarity thresholds merely to increase suggestion volume.
- Confirm reviewers understand that every suggestion is unverified until explicitly confirmed.

Enable worker only after readiness and pilot approval (worker registration is read at startup, so
restart after changing it):

```json
"People": {
  "Enabled": true,
  "WorkerEnabled": true,
  "MaximumConcurrentAssets": 1,
  "BatchSize": 1
}
```

Start conservatively. Increase concurrency only after observing CPU, memory, database load and job latency on production-class infrastructure.

## 8. Post-deployment checks

- Core Photos opens with People enabled and disabled.
- Photos still falls back when the catalogue is intentionally unavailable.
- The People directory (`/Photos/People`), person filter and portraits are available to all
  authenticated users but show only confirmed people; hidden people are excluded for users other
  than `Admin`/`HoD`.
- People review (`/Photos/People/Review`), identity management (`/Photos/People/Details`) and the
  face-thumbnail endpoint (`/Photos/FaceThumbnail`) reject users outside `Admin`/`HoD`.
- A user linked to a person can confirm/reject suggested photos only for their own linked person.
- Review queue includes faces with and without model candidates.
- Manual assignment and person creation work.
- Candidate rejection is not recreated for the same face/model version.
- “Close unidentified” removes the face from active review and the Closed unidentified queue can reopen it later.
- “Not a face” suppresses the detection and invalidates its embedding.
- Rename, hide/restore, representative-face change, correction and merge create audit records.
- Concurrent reviewers receive a conflict rather than overwriting each other.
- Reprocessing does not overwrite human-reviewed identity assignments.
- Classification status remains unchanged by face processing.

## 9. Monitoring

Monitor:

- Pending/running/dead-letter `DetectFaces` jobs.
- Face-analysis failure reasons.
- Worker heartbeat and expired-lock recovery.
- Cache disk usage and write failures.
- Average processing time per photograph.
- Number of reviewable unidentified faces.
- Suggestion acceptance/rejection rate by model version.
- Database size of embeddings, thumbnails and audits.

Do not log embedding vectors, raw image bytes or sensitive identity data.

## 10. Rollback

Application rollback:

- Set `MediaLibrary:People:WorkerEnabled` to `false` and restart (hosted workers are registered at startup).
- Set `MediaLibrary:People:Enabled` to `false` to hide People UI and stop queueing.
- Redeploy the previous application version if necessary.

Database rollback should normally be avoided because Phase 8 preserves existing data and adds governance history. If a schema rollback is formally approved, take a fresh backup first and use the migration `Down` path only after confirming that identity audits with null face references and new governance data may be removed.
