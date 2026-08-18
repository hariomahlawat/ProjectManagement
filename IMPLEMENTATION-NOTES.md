# PRISM Photos — Consolidation & Curation UX

## Scope

This phase is intentionally a consolidation and curation-throughput phase over the current organisation-wide Albums implementation. It does not introduce personal/private albums, a new media ownership model, automatic identity decisions, or another database migration.

## Implemented

### 1. Direct **Add media** from an Album

Album managers can now start curation from the album itself rather than navigating back to Photos and choosing the album again.

- Active, manageable albums below capacity expose **Add media** in the album header.
- Empty albums use **Add media** as their primary call to action.
- The action opens the normal Photos wall in a dedicated target-album selection mode.
- Selection starts automatically and the target album remains explicit throughout the workflow.
- Search, filters, sorting and pagination preserve the target album.
- **Clear filters** also preserves the target album.
- Cancel / Escape returns to the album rather than leaving the user in an ambiguous selection state.
- The selection bar becomes target-specific: **Select page · Add selected · Clear**.
- Media already in the album is dimmed, labelled **In album**, disabled and excluded from click, range, Select page and lasso selection.
- The server remains authoritative and re-validates album permission, archive state, membership, visibility and capacity.

### 2. Correct capacity semantics

Album capacity is now based on **total membership**, not only currently visible media. A hidden or temporarily unavailable album member still occupies a membership slot, matching the mutation service's invariant.

`MediaAlbumDetails` now exposes `TotalMembershipCount`; this is a contract/query change only and does not require a schema change.

### 3. Album action semantics tightened

- **Organise** is available only when the user can manage the album, it is active, and at least two visible media items exist.
- Direct `OrganizeAlbum=true` URLs are normalised back out when organisation is not meaningful.
- **Add media** is hidden when the album is archived, read-only, or at the membership limit.

### 4. Creator/audit presentation

Album list cards and album detail resolve the creator from the application user directory.

- Owner cards show **Created by you**.
- Other albums show the creator's resolved display name.
- Album detail shows creator, created date and last-updated timestamp.
- Rank/name formatting avoids duplicate rank prefixes.
- Directory lookup failure is non-fatal; album access remains available with a safe fallback label.

### 5. Photos UX refinement

- Desktop Photos header and tab spacing are more compact so media appears higher in the viewport.
- Source Collection/Album grid minimum width is reduced from 245px to 220px to make better use of wide displays.
- Pagination is hidden when there is only one page instead of displaying a redundant `Page 1` footer.

### 6. Maintainability consolidation

The current behaviour was retained while separating responsibilities:

- Album-specific `IndexModel` orchestration moved to `Pages/Photos/Index.Albums.cs` using a partial PageModel.
- Album/caption/reorder browser behaviour moved out of `photos-library.js` into `photos-curation.js`.
- Curation-specific styling moved out of `photos-library.css` into `photos-curation.css`.
- Small deterministic curation-state rules moved into `PhotosCurationPresentation` and are unit-testable without Razor infrastructure.

This reduces the tendency to append further album phases into the already-large core Photos PageModel, JS and CSS files.

### 7. Regression coverage

Added coverage for:

- creator label normalisation;
- Add-media authority/archive/capacity semantics;
- total-membership capacity despite fewer visible items;
- Organise minimum-item semantics;
- mixed existing/new album additions remaining idempotent;
- archived album rejecting targeted additions;
- target-album browser contract, Cancel destination and automatic selection;
- existing album members being excluded from selection;
- curation JS being isolated from the core gallery JS.

## Database impact

**No EF migration is required.**

The phase uses the existing organisational Albums schema. `TotalMembershipCount` is derived from existing `MediaAlbumItems` rows.

## Permissions

Permissions are unchanged:

- all authorised Photos users can view organisation-wide active albums;
- album creators manage their own albums;
- Admin / HoD / Comdt can manage any album;
- organisation-level editorial-caption authority remains elevated-role controlled.
