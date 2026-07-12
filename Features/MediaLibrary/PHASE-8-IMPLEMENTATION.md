# Media Library Phase 8 — People Experience and Face-Intelligence Hardening

## Scope delivered

Phase 8 turns the existing face-intelligence foundation into a governed, user-facing People capability while preserving the Media Library's fail-safe behaviour.

Delivered capabilities:

- People index with search, sorting, review counts and representative-face cards.
- Person detail timeline with cover selection, assignment correction, rename, hide/restore and merge workflows.
- Human review queue for model suggestions, manual assignment, person creation, intentional unidentified decisions and false-detection suppression.
- Person filtering and person-name search in the main Photos timeline.
- Recognised and unidentified face metadata in the full-screen Photos viewer.
- Dedicated face-analysis status/version fields that do not overwrite classification state.
- Explicit YuNet detector and SFace embedding adapters on ONNX Runtime.
- YuNet output decoding, IoU non-maximum suppression and five-landmark SFace alignment.
- Explainable face-quality assessment using resolution, sharpness, exposure, contrast, pose and crop completeness.
- Model-key, model-version and embedding-dimension isolation.
- Bounded candidate lookup using high-quality, human-confirmed reference faces.
- Transactional identity governance, optimistic concurrency, database constraints and audit records.
- Idempotent automatic queueing and retry-safe background processing.
- Model checksum, licence, tensor-contract, schema and cache readiness checks.

## Open-source model profile

The approved default profile uses:

| Function | Model | Runtime | Licence |
|---|---|---|---|
| Face detection and five landmarks | OpenCV Zoo YuNet `2026may` | Microsoft ONNX Runtime | MIT |
| Face embedding | OpenCV Zoo SFace `2021dec` | Microsoft ONNX Runtime | Apache-2.0 |

The model weights are not committed to the application source. Install only the pinned files using the scripts under `Features/MediaLibrary/models`, then retain the model licences and source manifest with the deployment record.

InsightFace bundled pretrained weights are intentionally not used by this implementation because their project documentation restricts those supplied model packs to non-commercial research unless separately licensed.

## Security and privacy posture

- The feature is disabled by default.
- Only `Admin` and `HoD` roles can browse People, view face thumbnails or perform identity review.
- No identity is automatically confirmed.
- Embedding vectors are never returned by a web endpoint.
- Face thumbnails are stored outside `wwwroot` and served by an authorised endpoint with private caching.
- Model files are accepted only when their SHA-256 hashes match the approved configuration.
- Reprocessing never replaces an asset that already contains a human-reviewed active identity assignment.
- Every identity mutation is audit recorded.
- A person name is a display attribute, not a unique biometric identifier.

## Processing pipeline

1. `FaceAnalysisQueueWorker` discovers eligible photographs not processed by the current detector/embedder pair.
2. `FaceQueueService` creates or resets an idempotent `DetectFaces` processing job.
3. `MediaProcessingWorker` claims the job using PostgreSQL `FOR UPDATE SKIP LOCKED`.
4. `OnnxFaceAnalysisEngine` decodes the photograph, runs YuNet, applies NMS, assesses quality, aligns eligible faces and runs SFace.
5. `FaceIntelligenceService` replaces only unreviewed detections transactionally, stores private review thumbnails and compatible embeddings, and marks the asset with the current analysis version.
6. `FaceCandidateSearchService` compares the new embedding only with bounded, human-confirmed, model-compatible references.
7. Suggestions enter `MediaFaceReviewDecisions`; they never create a confirmed assignment.
8. An authorised reviewer confirms, rejects, manually assigns, creates, ignores or suppresses the face.

## Identity lifecycle

Supported governed actions:

- Create a person and assign a face.
- Assign a face to an existing person.
- Confirm or reject a suggested person.
- Leave a valid face intentionally unidentified.
- Suppress a false or unusable face detection.
- Rename a person.
- Hide or restore a person.
- Change the representative face.
- Remove an incorrect assignment.
- Merge a duplicate person into the canonical person.

The database enforces one active person assignment per face and one pending suggestion per face/person pair. Concurrency tokens translate stale writes into a user-facing refresh-and-retry conflict instead of silently overwriting another reviewer's decision.

## Scale profile

The supplied candidate service is intentionally bounded by:

- `MaximumCandidateReferenceEmbeddings`
- `ReferenceFacesPerPerson`
- `CandidateLimit`
- model key, model version and vector dimension
- human-confirmed reference assignments only

This is appropriate for a controlled initial enterprise rollout. The `IFaceCandidateSearchService` boundary allows a PostgreSQL `pgvector` implementation to replace the bounded service later without changing the detector, review or UI layers. Introduce vector indexing after representative production volumes and latency targets are measured; do not add a database extension before its backup, replication and security implications are approved.

## Principal files

- `Options/MediaLibraryOptions.cs`
- `Options/MediaLibraryOptionsValidator.cs`
- `Services/OnnxFaceAnalysisEngine.cs`
- `Services/FaceGeometry.cs`
- `Services/FaceQualityEvaluator.cs`
- `Services/FaceIntelligenceService.cs`
- `Services/FaceCandidateSearchService.cs`
- `Services/FaceReviewService.cs`
- `Services/MediaPeopleQueryService.cs`
- `Hosted/FaceAnalysisQueueWorker.cs`
- `Hosted/MediaProcessingWorker.cs`
- `Data/Migrations/20260628190000_HardenPeopleExperience.cs`
- `Pages/Photos/People/*`
- `Pages/Photos/Index.cshtml*`
- `wwwroot/css/pages/photos-people.css`
- `wwwroot/js/pages/photos-library.js`

## Operational invariant

The People capability is optional. Failure of models, workers, the face schema or an external media source must not prevent authorised users from viewing core PRISM photographs through the existing Photos fallback path.
