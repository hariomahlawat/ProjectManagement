# Implementation notes — Organisation-wide Albums & Curation

## Scope

This phase deliberately keeps PRISM source-derived Collections and user-curated Albums as separate concepts.

### Source collections
Project, Visit, Activity and Event collections remain automatic and source-owned.

### Organisation-wide albums
Albums are curated sets that all authorised Photos users can view. They do not move, duplicate or delete source media. A media asset can belong to several albums.

## Implemented

- Organisation-wide album domain, membership ordering, cover, archive/restore, concurrency token and durable curation audit.
- Creator-owned routine management; Admin/HoD/Comdt management of any album.
- Collections workspace split into **Source collections** and **Albums** without adding another primary Photos tab.
- `Select → Add to album`, including creation of a new album without leaving the Photos wall.
- Album detail inside Photos with selection, download, people review, remove from album and photo-only cover selection.
- Manual drag ordering in explicit **Organise** mode with server-side membership/permission validation and automatic save.
- Album edit, archive and restore. No destructive media delete is introduced.
- Active album-name uniqueness, case-insensitive at PostgreSQL level.
- Canonical media-visibility policy applied to album lists, album membership operations and covers.
- Editorial Photos caption, stored separately from the source Project/Visit/Activity/Event caption, with optimistic concurrency and audit.
- Media Info panel enriched with albums, people, unidentified-face count, filename, file size, dimensions/duration, caption and source actions.
- Unified Photos search extended to editorial captions and active album names.
- Central display-metadata formatter prevents repeated title/context presentation such as `Visit of X / VISIT OF X`.
- Source Collections summary now counts only the source collections actually represented by the current singleton-suppression policy.
- Source Collection and Album cards use whole-card primary navigation while `Open source` remains an independent secondary action.
- Slightly tighter Photos presentation for the curation workspace.

## Persistence

Migration: `20260818170000_AddOrganisationalMediaAlbums`

New tables:
- `MediaAlbums`
- `MediaAlbumItems`
- `MediaCurationAudits`

New `MediaAssets` fields:
- `EditorialCaption`
- `EditorialCaptionUpdatedByUserId`
- `EditorialCaptionUpdatedAtUtc`
- `EditorialConcurrencyToken`

The migration backfills the concurrency token for existing assets and then removes the temporary database default so the physical schema remains aligned with the EF model.

## Governance

- No personal/private albums.
- No generic bulk delete.
- No manual mutation of source Collection membership.
- Removing media from an Album affects only that Album.
- Album covers must be currently visible photographs.
- Unavailable/hidden media is automatically excluded from album presentation and cover fallback.
- Media editorial captions are organisation-level metadata and therefore restricted to Admin/HoD/Comdt.

## Capacity

An album is capped at 250 media assets in this phase. Membership is idempotent: adding the same media twice does not duplicate it.
