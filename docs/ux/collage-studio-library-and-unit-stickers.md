# Collage Studio: unit stickers and photos from PRISM Photos (plan, 15 Nov 2026)

Collage Studio ([../collage-studio.md](../collage-studio.md)) was built in the browser only. Its
first plan ([collage-studio.md](collage-studio.md)) left two choices to the owner. On 15 Nov 2026
the owner decided to build both, as nice to have:

- **Unit stickers**: crests, badges and logos that an administrator uploads once, for everyone
  to put on a collage.
- **Photos from PRISM Photos**: pick photos from the `/Photos` library instead of finding the
  files on the computer.

Both read from the PRISM server, which changes the page's promise of "nothing leaves the
browser" in one way only: PRISM sends pictures **to** the browser. A collage, and any photo the
user adds from the computer, is still never sent to PRISM.

## Decisions taken for the owner (change any before its PR)

| Question | Choice | Why |
| --- | --- | --- |
| Who uploads unit stickers | **Admin only**, through a new capability `Admin.CollageStickers` | Easy to widen to HoD later in the capability catalog; a crest is an official mark |
| What can be uploaded | PNG, JPEG or WebP, at most 5 MB; **not SVG** | An SVG can carry script. A PNG with a clear background keeps a crest's shape |
| How it is stored | Re-encoded as PNG (keeps the clear background), at most 1024 px on the long side, under the upload root in `collage-stickers/` | Nothing the uploader sent is kept or served as sent |
| How many | At most 60 active stickers | One picker row stays quick to scan |
| Removing one | **Retire** (hidden from the picker) and Restore; no hard delete in the first version | Old drafts that use it still open |
| Which library photos | Only what the user can already see in `/Photos`, through `IMediaAssetVisibilityPolicy`; photos only, not videos | No new access rule; the library's own rule decides |
| Which size is fetched | The **original**, read once into the browser like a chosen file | Save is at print quality from the original, as for photos from the computer |
| When `/Photos` is switched off | The button is not shown | No dead ends |
| Name of the sticker group | **Crests and logos** (owner, 15 Nov) | Named by what is in it, like the other groups; "Custom" would suggest the user's own uploads |
| A user's own logo as a sticker | **Not now** (owner, 15 Nov) | A user can still add a logo as a photo; a browser-only "Your own" group can be a later plan |

## What the user sees

- **Stickers panel**: a new first group, **Crests and logos**, showing the uploaded stickers by
  name, with the same search box. When there are none it says "An administrator can add your
  unit's crests and logos."
- **Photos panel**: beside **Add photos**, a second button **From PRISM Photos**. It opens a
  dialog: a search box (words, project, person, year as in `/Photos`), a grid of thumbnails,
  tick up to the photos still allowed (60 in all), **Add**. The photos then behave exactly like
  photos from the computer: tray, frames, drafts and Save.
- The start screen's privacy line changes to: "Photos from your computer stay in this browser.
  Photos from PRISM Photos and unit stickers are read from PRISM. Nothing you make is sent to
  PRISM."
- **Administration → Collage stickers**: a list with each sticker's picture, name and search
  words; Add (file, name, words), Rename, Move up/down, Retire and Restore. Every change is in
  the admin audit log in plain words.

## How it is built

- **Entity** `CollageSticker` (`Id`, `Name` ≤ 40, `SearchWords` ≤ 200, `StorageKey`, `Width`,
  `Height`, `Version`, `SortOrder`, `IsRetired`, `CreatedByUserId`, `CreatedAtUtc`,
  `UpdatedAtUtc`), one hand-written migration.
- **Service** `CollageStickerService`: validation as in `ProjectPhotoService` (size,
  `PrismImageCodec.Identify`, `DecodeOriented`, `ResizeToFit`, `EncodePng`), files through
  `IUploadRootProvider`/`IUploadPathResolver`, written to a temporary name and moved, so a failed
  upload leaves nothing behind.
- **Read API** (signed-in users, `GET` only):
  - `/api/collage/stickers`: the active stickers (id, name, words, size, version, image URL);
  - `/api/collage/stickers/{id}/image?v={version}`: the PNG, `private`, 7-day cache, ETag;
    retired ones are still served, so old drafts keep their crest;
  - `/api/collage/library-photos?q=&projectId=&year=&page=`: photos from
    `IMediaLibraryQueryService.SearchAsync`, kind photo, paged, with thumbnail URL and size; the
    images themselves come from the existing `/Photos/Media/{id}/{variant}`, which already applies
    the visibility rule.
- **Admin page** `Areas/Admin/Pages/CollageStickers`, policy `Admin.CollageStickers`, entry in
  `AdminNavigationCatalog` so it is in "Every admin page" and Home search; changes through
  `IAdminAuditService`.
- **Collage page**: a new `wwwroot/js/collage/collage-library.js` is the only collage script that
  talks to PRISM, to those three fixed addresses and `/Photos/Media/`. The page contract test
  keeps every other collage script free of network calls and checks this one's addresses, as
  Photo Tools does for its background script. Unit stickers are items with
  `sticker: "unit:<id>"`; a draft that names one PRISM no longer has shows an empty box.
- No CSP change: everything is same-origin.

## Build order (small PRs)

| PR | What | Done when |
| --- | --- | --- |
| 1 | This plan | Merged |
| 2 | Sticker entity, migration, storage service, read API, tests | API returns uploaded stickers; migration tests pass |
| 3 | Admin page: add, rename, order, retire, restore; capability, navigation, audit | An admin can manage stickers; guard tests pass |
| 4 | Crests and logos in the collage: the group, drawing, Save, drafts | A crest goes on, saves and reopens |
| 5 | Library photos API | Returns only photos the user can see |
| 6 | From PRISM Photos dialog in the collage, privacy wording, contract test | Library photos behave like computer photos |
| 7 | Docs and a phone and Dark theme pass | Guide updated, plan complete |

## Deployment

- One additive migration (a new table); nothing existing changes. Rolling back the code leaves an
  unused table.
- Sticker files live under the existing upload root, so the existing backup covers them.
- No new Internet dependency; nothing changes when `/Photos` is switched off except that its
  button is hidden.
