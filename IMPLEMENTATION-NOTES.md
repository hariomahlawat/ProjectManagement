# PRISM Photos — Identity Matching Bootstrap & Reference Readiness

## Purpose

This phase closes the bootstrap gap exposed by the Person Profile / **Find more photos** workflow:

1. a confirmed person can exist without a usable trusted face reference;
2. the identity page previously showed a generic `Matching reference` action even when the appearance had no current embedding;
3. the server then rejected the operation with a broad, non-actionable message; and
4. a fresh installation with no trusted-reference corpus could leave unresolved faces appearing to remain in background matching rather than moving cleanly to individual review.

The implementation makes matching readiness explicit, repairable and governed end-to-end.

## 1. One matching-reference eligibility authority

New service:

- `IFaceReferenceReadinessService`
- `FaceReferenceReadinessService`

It evaluates a confirmed appearance against the same requirements used for the mutation path and returns a concrete reason code/state, including:

- ready to trust;
- already-trusted and usable;
- source unavailable;
- suppressed detection;
- quality below the trusted-reference threshold;
- non-embedding-eligible quality state;
- current embedding missing;
- embedding outdated/model-incompatible;
- embedding preparation pending;
- embedding preparation failed/retryable;
- source not eligible for face processing; and
- assignment no longer active.

`FaceReviewService.SetReferenceStatusAsync` now consumes this service before trusting an appearance, so the UI and server no longer implement competing eligibility rules.

Excluding a trusted reference also now requires another **usable** trusted reference, not merely another database row whose status says `TrustedReference` while its embedding is stale or missing.

## 2. Repairable confirmed appearances

If an otherwise valid confirmed appearance lacks a current matching embedding, the identity page now offers:

**Prepare for matching**

This queues the existing durable processing job type:

`GenerateFaceEmbeddings`

The job is idempotently created/reset using the current media job infrastructure and is audited as `ReferencePreparationQueued`.

No identity assignment is removed or recreated.

## 3. Safe embedding refresh without face re-detection replacement

`IFaceIntelligenceService` now exposes `RefreshEmbeddingsAsync` and `MediaAssetProcessor` routes only `GenerateFaceEmbeddings` to that path.

The refresh path:

- validates source/media eligibility;
- resolves the original bytes;
- verifies the source content hash has not changed since the human-reviewed face was established;
- runs the current face-analysis engine;
- spatially matches current detections back to the existing reviewed face records using IoU;
- refreshes quality/model metadata;
- replaces only the active face embedding when a current embedding-eligible detection is found;
- preserves the existing `MediaFace`, confirmed assignment, person identity, audit history and face box;
- never creates a new automatic identity;
- does not invalidate an existing embedding merely because a repair attempt failed to produce a replacement; and
- marks face-analysis failure explicitly when background embedding preparation fails.

If already-trusted biometric evidence is successfully refreshed, the existing evidence-invalidation coordinator requeues unresolved candidate matching correctly.

## 4. Exact readiness UX on People / Details

The identity-governance page now shows per-appearance **Matching readiness**, for example:

- `Ready to trust`
- `Trusted reference ready`
- `Embedding not available`
- `Embedding needs refresh`
- `Preparing embedding`
- `Embedding preparation failed`
- `Quality below reference threshold`

The previous generic error is removed.

Actions are state-driven:

- **Use as matching reference** when immediately eligible;
- **Prepare for matching** when the missing/stale embedding can be repaired;
- **Preparing embedding** while the durable job is active;
- retry preparation after a failed/dead-letter job;
- a disabled ineligible state with the exact reason when the appearance cannot be used.

The page also reports the number of **usable trusted matching references**, not merely database reference flags.

## 5. Person Profile prerequisite-aware action

For a single-person profile:

- zero usable trusted references → **Set up matching**;
- usable trusted reference exists → **Find more photos**.

The setup action deep-links directly to `#matching-reference-setup` on the identity page. The discovery panel's prerequisite action uses the same targeted link.

This prevents the user from entering a discovery workflow that cannot yet produce suggestions.

## 6. Zero-reference candidate-search fast path

`FaceCandidateSuggestionService` now explicitly checks whether any usable trusted-reference corpus exists for the active embedding model.

When the corpus is empty, PRISM does **not** call the similarity engine. It:

- removes stale pending candidate suggestions for the processed faces;
- marks candidate search `Ready`;
- records the current model/version and completion time;
- leaves zero known-person candidates; and
- releases the faces to the normal individual-review/grouping workflow.

This makes first-run/bootstrap behaviour deterministic. Once the first trusted reference is later created, the existing reference-evidence invalidation path queues unresolved faces again for normal known-person matching.

This also works with the matching-recovery phase: old stale `Processing` faces are recovered to `Pending`, then immediately complete with zero candidates when no reference corpus exists.

## 7. Canonical Person Profile URLs

Single-person URLs now use:

`PersonIds=<guid>`

instead of the unnecessary indexed representation:

`PersonIds[0]=<guid>`

Indexed route keys remain only for true multi-person state.

## 8. Audit readability

`ReferencePreparationQueued` now renders in the identity history as:

**Matching evidence preparation queued**

rather than exposing the internal action code.

## 9. Tests added

- `FaceReferenceReadinessServiceTests`
  - missing current embedding is repairable;
  - current embedding becomes trustable;
  - preparation creates a durable `GenerateFaceEmbeddings` job and audit record.

- `FaceCandidateZeroReferenceBootstrapTests`
  - an empty trusted-reference corpus completes candidate search without invoking the similarity engine.

- DI regression coverage now asserts `IFaceReferenceReadinessService` registration while preserving the existing Album, Person Discovery, face-review and candidate-queue registrations.

## Database impact

No migration is required.
