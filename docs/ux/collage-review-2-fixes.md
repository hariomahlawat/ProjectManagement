# Collage Studio: second review fixes (plan, 16 Nov 2026)

**Status: built.** PRs 1 to 13 were merged on 16 Nov 2026 (#3307, #3308, #3311, #3314, #3315,
#3316 and #3318, #3320, #3321, #3322, #3323, #3324, #3328, #3325); PR 14 is the docs, and a fresh
cold review follows it. Where the build differs from a row below, the row says so.

All fourteen PRs of [collage-review-fixes.md](collage-review-fixes.md) were merged on 15 Nov 2026.
A fresh cold review followed: two reviewers who had not seen the work read the code (one the
editor, one the server and the tests), and a third used the real page in a browser as a first-time
user, at 1366 and 1920 wide, on a phone, in the Dark theme and with the keyboard only. It scored:

| Area | First review | Second review |
| --- | --- | --- |
| First-time use (UX) | 7.5 | **7.5** |
| Editor code | 7 | **7.5** |
| Server and tests | 8 | **8** |

There was no High finding for security or data loss in the code. The owner's aim is still **at
least 9** for both UX and code, built in small PRs in the best order, with a fresh review at the
end.

The guide is [../collage-studio.md](../collage-studio.md).

## What the review found

### Work that can be lost (editor, Medium)

- **E1. Deleting a draft while it is opening.** The start screen stays usable while a draft
  opens. Deleting that draft lets it open anyway, the purge then removes its photos, and the next
  autosave writes it back with none. Another tab purging old drafts has the same effect on an
  open draft.
- **E2. Photos never leave the tray.** Nothing removes a photo from a collage, so the 60-photo
  limit runs out after enough replacing, and the draft keeps every original ever added.
- **E3. The page canvas grows with zoom.** Zoomed in on a phone, the canvas can pass the
  browser's size limit (iPhone: 16.7 million pixels) and draw blank. This was worked out from the
  numbers, not seen on a phone.
- **E4. One unreadable file drops the whole batch** with no message.

### Code (editor, Low)

- Held typing or nudges are not finished before the next click, so one Undo can take back both.
- The "delete the oldest draft?" question comes from the autosave timer, so it can appear in the
  middle of a drag.
- Save does not release its canvases when it fails.
- Small maps keyed by item are not cleared when a collage closes.
- The whole page is re-rendered for each draft thumbnail.
- Paste adds photos behind an open dialog.
- The PRISM Photos dialog downloads a photo before checking its size.
- A missing editor script makes the page go dead without a message.
- **Structure.** `collage-app.js` is still about 2,400 lines with about 20 shared variables.
  Most edits go through `commit()`, `holdEdit()` or `recordQuietly()` by hand. The draft open,
  save, leave and delete code has no tests of its own.

### Server and tests (Medium)

- **S1. No test proves sign-in is required** on the three Collage APIs. The tests call the
  handlers directly.
- **S2. Library pictures always use `/Photos/Media`.** PRISM Photos itself shows project, visit
  and social-media photos through their own pages and access checks. It is safe today only
  because those checks happen to be the same.
- **S3. Incomplete audit.** Moving, retiring and restoring a crest record no "before" state.
  Moving also does not record the other crests whose order changed.
- **S4. The browser test page can drift from production.** It sends none of PRISM's security
  headers (Content Security Policy), and its API answers are hand-written.
- **Low.** A sticker file can be left behind if the process stops between writing the file and
  saving it. A missing picture is not logged. A virus-scanner refusal would show as a server
  error. Browser test retries in CI hide flaky tests. One unrelated test assumes the collage
  migration is the newest.

### First-time use (UX)

**High**

- **U1. Phone buttons with no name.** On phones the Save and Preview buttons have no accessible
  name.
- **U2. Deleting a photo also deletes its frame.** This leaves a hole in the layout. Undo is only
  announced to screen readers, never shown.

**Medium**

- **U3. "Make it casual" overwrites choices.** It replaces the frames and shapes the user picked
  by hand.
- **U4. Preview shows empty frames.** Preview shows the dashed "Add photo" frames, which Save
  leaves out.
- **U5. Frames hard to see.** White border, Polaroid and Torn edge barely show on a white page.
- **U6. Placeholder names.** A collage nobody named is called "Your heading" (the starter text),
  and so are its saved files.
- **U7. Long settings panel.** It runs to about 15 sections for text, and Layers is far down.
- **U8. Sticker grid.** The Shapes grid is cut off on the right, and the "Thank you" stamps are
  tiny.
- **U9. Cramped phone header.** The name and page size are cut off.
- **U10. Mouse words on phones.** Hints talk about clicking, scrolling and Ctrl+V on touch
  screens.
- **U11. New items stack up.** New text and stickers land on top of earlier ones, and the
  floating toolbar covers the item above.
- **U12. Save choices.** Save always starts on PNG at print quality, even on a phone. The "What
  is it for?" buttons never show which one is chosen.
- **U13. Extra photos.** Photos beyond the layout's frames float on top of the grid with no
  message.

**Low**

- The text presets look like input boxes.
- A damaged JPG is called "not a JPG".
- Focus goes to the page body after Delete.
- Ctrl+S in the name box opens the browser's own Save page.
- Number sliders show no unit.
- "+ Add photo" wraps onto two lines in the toolbar.
- Draft card details are cut off.
- The PRISM Photos dialog shows its search and filters when the library is off.
- On a phone, the bottom bar marks the wrong button while the text sheet is open.

## Owner decisions taken in this plan

- **Deleting a photo** that sits in a layout frame leaves the frame empty ("Add photo"). Deleting
  the empty frame removes it. Every delete shows a toast with **Undo**.
- **"Make it casual"** keeps any frame or shape the user chose by hand. It changes only photos
  still on the layout's own frame and shape. On a fresh layout it looks the same as before (owner,
  16 Nov: "Make it casual" was good; the planned change is fine).
- **Captions on any photo** (owner, 16 Nov). Today a caption exists only on a Polaroid, and the
  review never found it. Every photo gets an optional **Caption** box near the top of its
  settings. On a photo without a Polaroid frame, a caption shows on a slim white strip under the
  photo, on screen and in Save. A Polaroid keeps its handwritten caption.
- **Draft tests** use the real browser (Playwright) plus unit tests with an in-memory drafts
  store. No new npm package.
- **Library pictures** use the same addresses the PRISM Photos page uses for each kind of photo,
  so each kind keeps its own access check.

## Build order (small PRs)

Each PR is merged only when Codex has no findings on its head and CI is green, then the next one
starts.

| PR | What | Done when |
| --- | --- | --- |
| 1 | This plan | Merged |
| 2 | **Draft safety (E1)**: the draft lifecycle (new, open, save, leave, delete and Undo, purge) moves into `collage-draft-session.js` with unit tests. The start screen is blocked while a draft opens. A draft deleted during opening is not opened. The open draft is never purged. A missing record gets its photos stored again. Tabs tell each other about purges (`BroadcastChannel`). "Delete the oldest draft?" is asked only when the user acts | Deleting a draft while it opens, or from another tab, never loses a photo |
| 3 | **Photo lifecycle (E2, E4)**: an unused tray photo can be removed. Saving a draft drops stored photos that neither the collage nor Undo still uses. Each file is read on its own, and a failure is reported. Paste waits while a dialog is open. The PRISM Photos dialog checks the size before downloading | Photos can be removed; a bad file is named, the rest are added |
| 4 | **One change path**: `collage-store.js` has `apply(change)`, which finishes held edits, records Undo, schedules the draft and redraws. Every edit uses it, with unit tests | No edit path records Undo or the draft by hand (built as `collage-changes.js`, `createChangeLog`: start, commit, record, hold and flush; the editor redraws after it) |
| 5 | **Phone canvas and memory (E3, Low)**: the stage stays the size of the view and the page moves inside it, with the pixel ratio capped under the browser's limit. Idle photos are cached. Save releases canvases when it fails. The maps are cleared when a collage closes. Cheaper thumbnails | Zoom 4 on a phone stays within limits; numbers checked in a test (built: the stage still grows with the page, in 128-pixel steps, and `core.canvasPixelRatio` lowers the pixel ratio so every canvas stays under 16 million pixels; a phone browser test zooms all the way in) |
| 6 | **Your choices kept (U2, U3, U6, U11, U13) and captions** (built in two PRs, the captions in 6b, #3318): delete behaves as decided above, with an Undo toast. An optional caption on any photo, as decided above. "Make it casual" keeps chosen frames and shapes. Unedited placeholder text never names a collage. Extra photos go to the tray with a message. New items are placed clear of earlier ones, and the toolbar moves to avoid covering them | Nothing the user did is undone without asking |
| 7 | **Preview and Save (U4, U5, U12)**: Preview shows exactly what Save makes. White, Polaroid and Torn frames get a soft shadow so they show on white. Save remembers the last purpose (WhatsApp first on phones) and shows the chosen button pressed | Preview equals the saved picture |
| 8 | **Settings panel (U7)**: rarely used groups (Effects, Order, Line up, Lock and hide) fold away and are remembered as opened or closed. Layers is a tab of its own | Text settings fit in about one screen |
| 9 | **Phones and keys (U1, U9, U10, Low)**: a compact phone header, a name on every icon button, touch wording on phones, focus to the next item after Delete, Ctrl+S opens Save everywhere, and the right bar button marked | A screen reader names every button; the phone header is not cut off |
| 10 | **Polish (U8, Low)**: the sticker grid fits its panel, text presets look like buttons, units on the sliders, draft card details shown in full, a clearer message for a damaged photo, the "Add photo" button on one line, the library dialog shows only its message when the library is off, a clear message when an editor script is missing | Every Low item from the review is fixed or written down as a decision |
| 11 | **Server (S1, S2, S3, Low)**: test-host tests prove each Collage API refuses an anonymous request. Library pictures use each kind of photo's own address and access check, with a test for each kind. A full before and after audit for move, retire and restore, including every crest whose order changed. Leftover sticker files and missing pictures are logged. A scanner refusal becomes a friendly message with an audit entry. The manifest test no longer assumes the newest migration | Each server finding has a test |
| 12 | **Test page like production (S4)**: the test page sends PRISM's Content Security Policy, taken from one shared place, and a policy violation fails the test. API samples come from the C# side. Browser test retries are reported | The browser tests fail where production would |
| 13 | **Editor split, part 3**: the panels, keys and pinch, and the drafts screen go into their own modules, each with a narrow interface | `collage-app.js` is about 1,200 lines or less (built: 2,611 → about 1,250, with the page, drafts screen, panels, tray and keys in their own modules) |
| 14 | **Docs**, then a **fresh cold review** | Scores reported; anything short of 9 gets its own plan |

## Deployment

The browser changes need no migration and no new setting. The server changes are additive: tests,
fuller audit entries, logging, a friendlier refusal, and library picture addresses that already
exist. A PRISM Photos item whose kind has no page of its own keeps `/Photos/Media`. Nothing needs
the Internet.
