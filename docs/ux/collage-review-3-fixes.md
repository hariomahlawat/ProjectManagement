# Collage Studio: third review fixes (plan, 16 Nov 2026)

**Status: built.** PRs 1 to 14 were merged on 16 Nov 2026: #3333, #3334, #3339, #3340, #3342,
#3336, #3343, #3337, #3335, #3344, #3345, #3347, #3338 and #3341, in plan order. PR 15 is the docs, and
a fresh cold review follows it. Several PRs were built at the same time, each in its own
worktree. Where the build differs from a row below, the row says so.

All fourteen PRs of [collage-review-2-fixes.md](collage-review-2-fixes.md) were merged on 16 Nov
2026. A fresh cold review followed. It used three reviewers who had not seen the work and were
not shown the earlier plans:

- one read the editor code;
- one read the server and the tests;
- one used the real page as a first-time user, through the browser test harness, at 1366 and
  1920 wide, on a 390 px touch phone, in the Dark theme and with the keyboard only.

It scored:

| Area | First review | Second review | Third review |
| --- | --- | --- | --- |
| First-time use (UX) | 7.5 | 7.5 | **7** |
| Editor code | 7 | 7.5 | **7.5** |
| Server and tests | 8 | 8 | **8** (server 8.5, tests 7.5) |

There was no High finding in the code, for security or for data loss. The UX review found three
High problems. Each was checked against its screenshot before this plan was written. The owner's
aim is still **at least 9** for both UX and code.

The guide is [../collage-studio.md](../collage-studio.md).

## What the review found

### First-time use

**High**

- **U1. Alerts stay and cover the page.** "3 photos did not fit the layout's frames" stays
  through every later action and sits over the bottom of the page and the selection toolbar. It
  is still there in Preview, and toasts stack over it.
- **U2. A blank page piles photos up.** Four photos added to a blank A4 page land as a small
  overlapping stack in the middle, with most of the page empty. The ones underneath cannot be
  seen or chosen.
- **U3. A layout change with a locked photo breaks the page.** The locked photo stays where it
  was and covers two of the new frames. One frame still says "Add photo", and nothing explains
  the overlap. A locked Polaroid can also run past the page edge.

**Medium**

- **U4. Lock is not the same everywhere.** The settings panel greys out Delete for a locked item,
  but the floating toolbar still offers Delete, Move and Replace.
- **U5. A frame looks both chosen and not chosen.** After Polaroid and then Heart, Polaroid is
  still marked, faded, with a small note that frames go with the Square shape.
- **U6. The caption example looks like a real caption.** The empty Caption box shows "Last day
  at office" in a strong grey.
- **U7. Phones are cramped.** The page is about 315 px wide. The handles are bigger than a small
  heading. The selection toolbar covers a third of the page and is cut off behind an open sheet.
- **U8. New text lands on earlier items.** Each heading goes near the middle, over photos and
  earlier text, and the toolbar then hides the heading added before.
- **U9. Save asks twice.** "What is it for?" picks the format, yet every format and quality
  choice stays on show. The chosen purpose is not clearly pressed, and on a phone the dialog is a
  long scroll.
- **U10. The space slider undoes hand work.** One nudge of Space between photos puts every photo
  back into the layout, losing "Make it casual" and hand placing, with nothing to say so.

**Low**

- Scrolling panels lose their title, and "Make it casual" is below the fold at 768 px high.
- In the Dark theme, inactive buttons are faint (Undo, zoom, Preview, Italic, the unchosen Save
  purposes).
- Photo backgrounds are called "Photo 1, blurred" rather than by file name, and choosing one gives
  no feedback.
- The collage takes its name from the first heading without saying so.
- The Undo bar for a deleted draft is black, not the glass toast used elsewhere. "A4 / landscape"
  wraps badly on draft cards, and their delete button always shows.
- Keyboard:
  - The collage name field shows no focus ring.
  - In the Save dialog, Tab passes once to the page behind it.
  - There are 25 Tab stops before the Settings tab.
- The phone start screen uses two thirds of the width for layouts, and the page sizes are far
  down.
- At 1366 wide, 100% shows the page small.

### Editor code

**Medium**

- **E1. Lock leaks** (with U4).
  - The layout's Rounded corners slider changes locked frames.
  - The item's own corner slider is not among the controls a lock turns off.
  - Replace swaps a locked photo and resets its crop.
- **E2. A drop that places nothing says "Photo added".** Dropping a tray photo beside the page
  when every frame is full commits nothing. A screen reader still hears "Photo added". A click
  already gives the right message.
- **E3. No memory budget.** Up to 60 photos each keep a working copy of up to 2,000 px, about
  700 MB in all. A removed photo stays in memory while any of the 100 Undo steps names it. This
  was worked out from the code, not measured on a phone.
- **E4. The split is mechanical.**
  - Each module gets a mutable `app` object with about 30 properties and many loose helpers, and
    modules write each other's state.
  - `syncPage` copies `stopAdjust`.
  - The Text, Background and Sticker panels and the 125-line `act` switch are still in
    `collage-app.js` (1,264 lines).
  - Some helpers are passed in but never used.
- **E5. Test gaps.**
  - The tray, page, panels, keys, drafts screen and drafts store have no unit tests.
  - No browser test covers:
    - an unreadable draft photo;
    - the leave prompt;
    - a full browser;
    - drop and paste;
    - lock against bulk changes;
    - Undo after a drag or resize.
  - Two browser test runs at once share `test-results/` and collide.

**Low**

- Stale comments in `collage-photos.js` and `collage-tray.js`.
- `PDFLib` and `PrismPhotoTools` are missing from the startup parts check, so a missing script
  gives a wrong message in Save.
- The settings tabs have no `tabpanel` or `aria-controls`.
- The busy indicator is not announced.
- A damaged draft leaves its decoded photos in memory.
- A lost touch pointer could leave pinch mode on.
- A test canvas is made on every settings render.
- Style attributes are built in HTML strings. They work because PRISM's CSP allows
  `style-src-attr 'unsafe-inline'`.

### Server and tests

**Medium**

- **S1. Admin upload authorization is checked by reflection only.** No test sends a non-admin
  POST through the real host.
- **S2. A sticker picture can be deleted after its row was saved.** Any exception from the change,
  including from COMMIT itself, deletes the new file. If COMMIT succeeded on the server but the
  connection dropped, the row and its audit entry remain with no picture. This is rare, but the
  loss is permanent.
- **S3. A missing sticker file logs a warning on every request**, before the 304 check.
- **S4. The test page is not quite production.**
  - The CSP fixture is taken from the sign-in redirect under test settings, not from the real
    page.
  - The API samples hold no sticker, so no browser test draws a crest.
  - Violations are found by console text.
  - One spec allows a `blob:` violation without saying why.

**Low**

- Refused uploads (type, size, unreadable) are not audited; only a scanner refusal is.
- An upload over 6 MB gets the framework's raw 413, not a friendly message.
- The Projects filter in From PRISM Photos can list projects that have only videos.
- The pixel-limit refusal and the second count in Restore have no tests.
- Two admins renaming one sticker: the last one wins. This is accepted.
- The EF model snapshot lacks `CollageStickers`. This is known and project-wide, since migrations
  are written by hand.

## Owner decisions taken in this plan (16 Nov 2026)

- **Blank page:** several photos added to a blank page are laid out in a **neat grid**, each fully
  visible. The user can then move them freely.
- **Layout change with a locked item:** **ask first**. The question is "1 photo is locked. Put it
  in the new layout too, or keep it where it is?" If the user keeps it, the layout leaves out any
  frame the locked item covers, so nothing overlaps without the user choosing it. A locked item is
  never left past the page edge.
- **Space between photos:** it still puts the photos back into the layout. When that undoes hand
  placing or "Make it casual", a toast says so and offers **Undo**.
- **Save:** the **purpose comes first**, with its result line. Format and quality move under a
  **More options** fold, which stays open if the user opened it before.

## Build order (small PRs)

Each PR is merged only when Codex has no findings on its head and CI is green; then the next one
starts. Each fix comes with a browser test or unit test that fails without it.

| PR | What | Done when |
| --- | --- | --- |
| 1 | This plan | Merged |
| 2 | **Lock everywhere (U3, U4, E1)**: one rule decides what a lock blocks, and the toolbar, keys, panel, both corner sliders, Replace and bulk changes all use it. A layout change with a locked item asks first, as decided above. A locked item is kept inside the page | A locked item cannot be changed by any path; layout change never leaves an overlap the user did not choose |
| 3 | **Messages (U1, E2)**: alerts close on the next action or after a few seconds. Alerts and toasts share one corner and stack neatly. Nothing shows in Preview. A drop that places nothing says so | No message covers the page; Preview shows only the page (built: messages use PRISM's toast stack with the Undo offers; a message's text goes when its toast goes, so one being read is never cut short) |
| 4 | **Placing things (U2, U8, U10)**: photos added to a blank page form a neat grid. New text goes to free space, a little lower each time, and the toolbar stays off it. The space slider offers Undo when it puts photos back | Four photos on a blank page are all visible; three headings do not overlap (built: "Make it casual" already lets go of the layout, so the space slider's Undo offer is for photos moved or reshaped by hand) |
| 5 | **Settings that say what they do (U5, U6, Low)**: with a non-square shape, the frame shows None as chosen and a note says the frame returns with Square. The caption example becomes "e.g. Last day at office" in a light grey. Photo backgrounds are named by file and marked when chosen. A short note says the collage is named after its first heading until the user renames it | No setting looks both on and off |
| 6 | **Save (U9)**: purpose first, with format and quality under More options as decided. The chosen purpose is clearly pressed, and Tab stays inside the dialog | Save fits one phone screen with the fold closed |
| 7 | **Phones (U7, Low)**: smaller handles with the same touch area. The toolbar is placed clear of the page edge and of open sheets. The page grows when sheets close. The start screen uses the full width, with page sizes higher up | At 390 px the page is at least 340 px wide and the toolbar never covers the chosen item (built: about 374 px; the page fits the space above a sheet as the sheet really is) |
| 8 | **Dark, keyboard and panels (Low)**: inactive buttons readable in Dark, a focus ring on the name field, fewer Tab stops before the settings (a skip link), panel titles that stay in view, a glass Undo bar for a deleted draft, tidy draft cards, and a larger default view at 1366 | Contrast at least 4.5:1 for button text in Dark; every focusable control shows a ring |
| 9 | **Memory (E3, Low)**: a budget for working copies (fewer pixels each as the count grows). Removed photos are released once Undo can no longer reach them, and a damaged draft releases its photos | 60 photos stay under a set number of working pixels, checked in a unit test (built: 60 million pixels in all, about 240 MB; up to 15 photos keep 2,000 px) |
| 10 | **Editor state, part 1 (E4)**: a small state store (`collage-state.js`) with named reads and changes replaces the shared `app` object. Each module is given only what it uses. `syncPage` calls `stopAdjust` | No module writes another module's state directly (built: `state` is read only, assigning to it throws, and each part is handed only its own named changes; a unit test scans the parts for writes) |
| 11 | **Editor state, part 2 (E4, Low)**: the Text, Background and Sticker panels and the actions move out of `collage-app.js`. Unused helpers and stale comments go. The startup check lists every script. The tabs get `tabpanel` and `aria-controls`, and the busy indicator is announced | `collage-app.js` is under 800 lines and only creates and joins the parts (built: 752 lines; the start screen, the toolbar and the messages moved out too) |
| 12 | **Editor tests (E5)**: jsdom unit tests for the tray, keys, panels and drafts screen, and an in-memory IndexedDB test for `collage-drafts.js`. Browser tests for an unreadable draft photo, the leave prompt, a full browser, drop and paste, and Undo after a drag or resize. Each browser run uses its own results folder | Every editor module has unit tests |
| 13 | **Server (S1, S2, S3, Low)**: host tests that a non-admin POST to each admin handler gets 403. The picture file is kept once COMMIT has been sent: the row is checked first, with a test that fakes a failed COMMIT. One warning per missing sticker per hour. Refused uploads are audited. Over 6 MB gives a friendly message. The Projects filter lists only projects with photos. Tests for the pixel limit and the Restore count | Each server finding has a test |
| 14 | **Test page from the real page (S4)**: the CSP fixture comes from an authenticated 200 response of `/Tools/Collage` under Production settings. The samples include a real crest sticker, served with production headers, and a spec draws and saves it. Violations are caught with `securitypolicyviolation` events, and the `blob:` exception is removed or explained | A crest is drawn in a browser test under production headers |
| 15 | **Docs**, then a **fresh cold review** | Scores reported; anything short of 9 gets its own plan |

PRs 13 and 14 touch only the server and the test harness, so they can be built alongside the
editor PRs.

## Deployment

- **Browser changes:** no migration and no new setting.
- **Server changes:**
  - additive: tests, audit entries for refused uploads, a friendlier over-size message and a
    rate-limited log line;
  - the Projects filter in From PRISM Photos lists only projects that have photos;
  - after a failed COMMIT, a sticker picture is now kept until the row is known to be missing.
- **Internet:** nothing needs it.
