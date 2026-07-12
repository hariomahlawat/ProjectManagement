# Media Library Phase 8 — Deployment Checklist

## 1. Pre-deployment

- Back up the application database and the Media Library database schema.
- Back up the configured media cache.
- Confirm the application targets .NET 8 and PostgreSQL uses the supported Npgsql/EF Core version.
- Confirm `Admin` and `HoD` role membership is current.
- Review local biometric/privacy policy, retention, access control and incident-response requirements.
- Keep `MediaLibrary:People:Enabled` and `WorkerEnabled` set to `false` during the initial deployment.

## 2. Replace application files

Replace the files from the Phase 8 replacement bundle while preserving environment-specific secrets and connection strings. The provided `appsettings*.json` files retain People as disabled by default; merge carefully if the installation has local configuration changes.

## 3. Build and test

From the solution root:

```powershell
npm ci
npm run build

dotnet restore
dotnet build ProjectManagement.sln --configuration Release --no-restore
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj --configuration Release --no-build
```

Do not deploy if compilation, Razor compilation, JavaScript build or automated tests fail.

## 4. Apply Media Library migration

Apply the migration against `MediaLibraryDbContext`:

```powershell
dotnet ef database update --context MediaLibraryDbContext
```

Verify that migration `20260628190000_HardenPeopleExperience` is recorded in `__EFMigrationsHistory_MediaLibrary`.

Important schema controls introduced by this migration include:

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

Expected files under `App_Data/media-models`:

- `face_detection_yunet_2026may.onnx`
- `face_recognition_sface_2021dec.onnx`

## 6. Validate readiness with processing disabled

Set:

```json
"People": {
  "Enabled": true,
  "WorkerEnabled": false
}
```

Restart the application and open **Admin → Media Intelligence**. Confirm:

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

Enable worker only after readiness and pilot approval:

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
- Person filter is visible only to `Admin` and `HoD`.
- Face-thumbnail endpoint rejects unauthorised users.
- Review queue includes faces with and without model candidates.
- Manual assignment and person creation work.
- Candidate rejection is not recreated for the same face/model version.
- “Leave unidentified” removes the face from the active queue.
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

- Set `MediaLibrary:People:WorkerEnabled` to `false` immediately.
- Set `MediaLibrary:People:Enabled` to `false` to hide People UI and stop queueing.
- Redeploy the previous application version if necessary.

Database rollback should normally be avoided because Phase 8 preserves existing data and adds governance history. If a schema rollback is formally approved, take a fresh backup first and use the migration `Down` path only after confirming that identity audits with null face references and new governance data may be removed.
