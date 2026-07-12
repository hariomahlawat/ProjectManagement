# PRISM Media Library Phase 2

## Implemented

- Optional external-folder capability with independent feature switches.
- One `FileSystem` provider for local absolute paths and UNC shares.
- Core Photos fallback when the external catalogue or folder is unavailable.
- Database-managed source administration: add, edit, test, pause, hide, scan and disconnect.
- Read-only source handling and protected original streaming.
- Incremental, batched scanning with distributed PostgreSQL source leases.
- Safe reconciliation only after a complete successful scan.
- Bounded source health checks.
- Local derivative cache with CPU-only SkiaSharp processing.
- Explainable screenshot classification without an unapproved AI model.
- Durable processing jobs with `FOR UPDATE SKIP LOCKED`, retries and dead-letter handling.
- Page-window external browsing without a 1,000-item ceiling.
- Core catalogue migration separated from future People/pgvector activation.
- Legacy `NetworkShare` configuration compatibility during migration.
- Focused dependency and future-model licence manifests.

## Deliberately not activated

- Face detection.
- Face embeddings.
- Person naming or recognition suggestions.
- pgvector installation or vector queries.
- External/cloud inference APIs.

These remain a later opt-in phase after an approved open model and weights licence review.

## Safety guarantees

- No external source is required.
- No original external file is moved, renamed or deleted.
- A failed source does not stop another source or core PRISM Photos.
- Partial scans never mark the remainder of the archive missing.
- UNC credentials are not stored by PRISM.
- Existing antiforgery compatibility middleware is preserved in `Program.cs`.
