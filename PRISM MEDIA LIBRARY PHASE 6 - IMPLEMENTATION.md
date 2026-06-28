# PRISM Media Library Phase 6

## Implemented

- Source-neutral `IMediaContentProvider` abstraction.
- Providers for Project photos, Project videos, Visit photos, Social Media Event photos, and external file-system assets.
- Universal media-content resolver.
- Central thumbnail/preview generation for PRISM-owned and external photos.
- Source-neutral metadata extraction and screenshot classification.
- SHA-256 content hashes for exact duplicate detection preparation.
- Internal PRISM photo processing jobs automatically queued during catalogue reconciliation.
- Changed internal photos invalidate cache and are reprocessed safely.
- Completed/failed/dead-letter analysis jobs can be reset idempotently when source content changes.
- Protected `/Photos/Media` delivery now works through the content-provider abstraction for every catalogue origin.
- Existing source-specific pages and files remain authoritative and unchanged.

## Deliberate boundaries

This phase establishes universal processing and duplicate-hash foundations. It does not yet add a destructive duplicate-removal workflow, face models, pgvector, or paid/proprietary components.

## Deployment

1. Copy the package into the ProjectManagement root.
2. Run `dotnet clean`, `dotnet restore`, `dotnet build`, and `dotnet test`.
3. Restart PRISM.
4. Open `/Admin/MediaSources` and run **Synchronize PRISM** once.
5. Confirm that processing jobs are queued and drain successfully.

No database migration is required because the required processing and hash columns already exist.
