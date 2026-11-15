# Collage Studio: cold review fixes (plan, 15 Nov 2026)

A cold review of Collage Studio on 15 Nov 2026 (two reviewers who had not seen the work read the
code; a first-time-user pass in the app) scored it **UX 7.5/10, code 7.5/10** (editor 7, server 8).
No security problem was found. The owner asked to take both scores to **at least 9**, approved
browser tests with Playwright, and asked for all the work in small PRs.

The guide is [../collage-studio.md](../collage-studio.md); earlier plans are
[collage-studio.md](collage-studio.md) and
[collage-studio-library-and-unit-stickers.md](collage-studio-library-and-unit-stickers.md).

## What the review found

**High**

- H1. Reopening a draft whose photo cannot be read back leaves that photo out, and the next
  autosave writes over the draft without it, so the photo, its frame and caption are lost for good.

**Medium**

- M1. Words typed in a text (and caption, text colours) are not autosaved or recorded for Undo
  until the field fires `change`; closing the tab right after typing loses them without asking,
  and clicking another item merges the typing into the next Undo step.
- M2. Enter on a focused button is taken by the editor: with text selected it jumps to the
  Words box; with a photo selected it starts Move photo.
- M3. The settings panel is rebuilt after almost every change, so keyboard focus jumps to the top.
- M4. A photo cannot be resized from the keyboard (no Size control).
- M5. Every photo is decoded at full size; sixty large photos can exhaust a phone, which is what
  triggers H1. PRISM Photos originals are downloaded in full before any size check.
- M6. On phones a failed draft save is never shown, and the last half second of work can be lost
  when the user switches app (no `pagehide` save).
- M7. With the Background panel open, every change redraws up to nine blurred previews.
- M8. A crest whose picture fails once is never tried again, and Save leaves it out silently.
- M9. No tests on PostgreSQL for the sticker limit under two uploads at once, or for a change
  rolling back with its audit entry.
- UX (first use): with many drafts, starting a new collage is below the fold, and every draft is
  "Untitled collage"; photos fill frames in the order added, so a wide photo lands in a tall frame
  and loses its edges; "PNG: best for WhatsApp" at print quality makes large files.

**Low**: a locked text can still change size; pinch zoom drifts from the fingers; arrow keys move a
hidden item; declining "delete the oldest draft" turns drafts off for that collage; the sticker
colour picker flickers; the draft reader does not check item ids, `aspect` or crop numbers; the
drafts database does not close for a newer tab; an interrupted crest upload can leave a `.tmp`
file; the editor is one 2,870-line file with no tests of its own.

**Sound**: permissions, upload checks and re-encoding, one transaction with the audit entry under
a lock, the additive migration, undo/redo snapshots, dropping stale async work, Save matching the
screen, offline use.

## What a 9 needs beyond the fixes

1. **One path for every change.** Most editor bugs come from some edits going through `commit()`
   and others recording Undo and the draft by hand. Every change goes through one function that
   updates the page, Undo, the draft and the settings panel, and keeps focus where it was.
2. **The editor split into modules with tests**, not one closure.
3. **Browser tests** (Playwright, development and CI only; nothing in production) for the main
   flows: make, save, reopen a draft, keyboard only, phone size.
4. **A UX polish pass** on first use, then a **fresh cold review** to score it.

## Build order (small PRs)

Each PR is merged only when Codex has no findings and CI is green, then the next starts.

| PR | What | Done when |
| --- | --- | --- |
| 1 | This plan | Merged |
| 2 | **Drafts never lose photos** (H1): a photo that cannot be read keeps its place as an empty frame with its crop and caption, and the draft keeps the stored file; **photos read at a capped size** (M5) with `createImageBitmap` resizing, and PRISM Photos refuses oversized photos before downloading | A draft with an unreadable photo reopens without losing it; 60 large photos stay within memory |
| 3 | **One change path** (M1, M3): every edit, typing included, goes through one function (typing recorded once per pause); the settings panel is patched or re-rendered with focus kept | Typing is in Undo and the draft; focus stays on the control used |
| 4 | **Phones and leaving** (M6, L): save on `pagehide`/`visibilitychange`; the draft status shows on phones when a save fails; declining "delete the oldest draft" can be asked again | No work lost when switching app; a failed save is visible |
| 5 | **Keyboard** (M2, M4, L): Enter only acts when the page has focus; a Size control for photos and frames; arrows do not move a hidden item; a locked text keeps its size; pinch zoom stays between the fingers | Every action works from the keyboard |
| 6 | **Crests and Save** (M8, M7, L): failed pictures are tried again; Save says what it left out; the colour picker does not flicker; the Background panel redraws only what changed | Save never drops something without saying so |
| 7 | **First use**: start a new collage first, drafts in one row with "All drafts"; drafts named from their first heading or the date; photos fill frames by shape; Save suggests the type by purpose (share, print, slide) | A returning user starts in one click; a wide photo goes in a wide frame |
| 8 | **Hardening** (L): the draft reader checks ids, `aspect` and crop numbers; the drafts database closes when a newer tab needs it; an interrupted crest upload leaves no file | Crafted or old drafts are tidied on open |
| 9 | **Sticker tests on PostgreSQL** (M9): two uploads at once never pass 60; a failed audit write rolls the change back and removes the file | Tests run in the migration workflow |
| 10 | **Editor split, part 1**: the change path and editor state in their own module with unit tests | `collage-app.js` loses the state and change code |
| 11 | **Editor split, part 2**: photos and drafts, and Save, in their own modules with unit tests | No editor file over about 1,200 lines |
| 12 | **Browser tests, harness**: Playwright (dev dependency), a test page built from the real Razor page, a CI workflow | One smoke test passes in CI |
| 13 | **Browser tests, flows**: make a collage, Save, reopen the draft, keyboard only, phone size, Dark theme | The main flows are tested in a real browser |
| 14 | **Docs**, then a **fresh cold review** by reviewers who have not seen the work | Scores reported; anything short of 9 gets its own plan |

## Deployment

All browser changes are additive; the one server change (PR 8) only tidies a temporary file.
Playwright is a development dependency: CI downloads a browser, production does not change. No
migration.
