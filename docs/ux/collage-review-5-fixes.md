# Collage Studio: fifth review fixes (plan, 16 Nov 2026)

All fifteen PRs of [collage-review-4-fixes.md](collage-review-4-fixes.md) were merged on 16 Nov
2026. A fresh cold review followed. Three new reviewers took part, none of whom had seen the work
or the earlier plans:

- one read the editor code;
- one read the server and the tests;
- one used the real page as a first-time user, through the browser test harness, with
  phone-camera-sized photos. They worked at 1280, 1366 and 1920 wide, on a 390 px touch phone,
  in the Dark theme and with the keyboard only.

It scored:

| Area | 1st | 2nd | 3rd | 4th | 5th review |
| --- | --- | --- | --- | --- | --- |
| First-time use (UX) | 7.5 | 7.5 | 7 | 7.5 | **7.5** |
| Editor code | 7 | 7.5 | 7.5 | 7.5 | **7.5** |
| Server and tests | 8 | 8 | 8 | 8.5 | **8.5** |

There was no High finding in the code, and no authorization, upload or data-integrity defect. The
UX review found two High problems, and both were checked against their screenshots before this
plan was written. The owner's aim is still **at least 9**.

The guide is [../collage-studio.md](../collage-studio.md).

## What the review found

### First-time use (7.5)

The basics worked the first time:

- the start page and the rail;
- adding photos (8 photos in about 5 s);
- Hindi headings and Hindi file names;
- lock, hide and Layers;
- Undo and Redo;
- Preview;
- Save, in under a second;
- reopening a draft;
- Dark;
- the phone layout.

No shaking was seen at 1280, 1366 or 1920.

What holds it down is arranging photos in a layout, which is the core task.

- **U1 (High): photos cannot be swapped.** Dragging a photo in a layout onto another photo does
  not swap them. It leaves a gap, and the dragged photo is drawn under the other one. The same
  happens with touch.
- **U2 (High): layouts stretch on portrait pages.** On A4 portrait, "8 grid" and "6 grid" make
  frames about 1:2.3, which cuts off faces. "Mosaic of 7" on Phone status becomes thin strips. The
  layout pictures are always drawn landscape.
- **U3: new photos pile up.** Photos added to a page that is already full are stacked in the
  centre, over the photos there. "Make it casual" keeps the pile.
- **U4: a stale toast keeps a dead Undo.** "Photos tilted and scattered… Undo" stays after a
  layout change, and its Undo does nothing visible.
- **U5: casual frames stay on.** Polaroid frames and tape that "Make it casual" put on stay when
  the user goes back to a grid. The tape overlaps the next photo. The toast says these are frames
  "you chose", which the user did not.
- **U6: a tray photo dropped on a filled frame becomes a loose copy.** No frame lights up while
  dragging, and the copy floats over two photos.
- **U7: photo shape is not matched to frame shape.** A tall photo went into a wide strip.
- **U8: new words and stickers land across the seam** between two photos.
- **Low:**
  - a single click on an empty frame only selects it;
  - focus drops to the page body after Save;
  - the page itself has no keyboard stop;
  - "PDF for printing" can be saved at screen quality;
  - the Stickers panel opens on a nearly empty "Crests and logos";
  - navy stickers are hard to see in Dark;
  - toasts cover the font buttons on a phone;
  - a ready-made heading's text is all selected when it is focused;
  - "Done moving" wraps at 1366;
  - the lock question blurs the photo it is about;
  - the draft subtitle is cut off;
  - the page size appears twice.

### Editor code (7.5)

The reviewer praised several things:

- the collage is frozen and changes through `edit()`;
- one change log;
- draft writes made one at a time;
- soft IndexedDB failures;
- photos released on purpose;
- good unit tests for the pure modules.

It found:

- **E1: a photo import that finishes in the background takes over the selection and focus.** If
  the user is typing words, the Words box goes and focus falls to the page body. The next
  Backspace deletes the photo just placed, and the typing is merged into the import's Undo step.
- **E2: `edit()` is not yet the only way the collage changes.** A `setCollage` change and several
  direct assignments in collage-app.js remain. Every `edit(…, 'none')` must remember its own
  `commit()`.
- **E3: the "read-only" state is written to, and the parts can reach too much.**
  - The drafts screen sets `state.thumbnail` fields.
  - The helper groups repeat entries and give each part about 100 helpers.
  - The selection changes both through `select()` and through `choose()`, which skips the redraw.
    That split is where E1 comes from.
- **E4: a font arriving makes its own Undo step**, which undoes nothing visible.
- **Low:**
  - a change that changes nothing still writes the draft;
  - Ctrl+Z during a drag drops the Redo steps;
  - `edit()` recognises a returned collage by its `items` alone;
  - caches keep deleted items;
  - deleting a draft can fail without a message;
  - an abandoned PRISM Photos choice keeps its ticks;
  - a pinch keeps the first finger's accidental drag;
  - comments carry plan history;
  - collage-app.js and collage-page.js have no unit tests;
  - collage-core.js is 1,688 lines.

### Server and tests (8.5)

The server is small and careful. Sign-in is required on every collage route, and a test checks
it. The other strengths:

- library photos go through the Photos library's visibility rule;
- sticker uploads are checked by content and re-encoded;
- every change and its audit row are written in one transaction under a lock;
- the browser fixtures are generated from the real server.

It found:

- **S1: the From PRISM Photos dialog's paging, search and out-of-order answers are not tested.**
  No test clicks Show more.
- **S2: some browser tests prove "nothing happened" by waiting a fixed time.** One waits 400 ms,
  less than the 500 ms draft delay, so it cannot fail. The autosave hold matches the number 500.
- **Low:**
  - each search also computes library statistics and filter lists, which are thrown away;
  - a sticker's `Version` never changes, but a comment says it does;
  - the collage JavaScript tests are listed by hand;
  - the test server never answers 304;
  - Show more can list a photo twice.

## Owner decisions (16 Nov 2026)

- **Dragging a photo onto another photo in a layout swaps them.** Hold Alt to move it freely
  instead. A photo from the tray dropped on a filled frame replaces that frame's photo, and Undo
  puts it back. The frame under the pointer lights up while dragging.
- **Layouts turn with the page.** On a portrait page, grids are turned (8 grid becomes 2 across
  and 4 down). The layout pictures are drawn in the page's shape, and the start page lets the user
  choose the page size with the layout.

## Build order (small PRs)

Each PR is merged only when Codex has no findings on its head and CI is green, and each fix comes
with a test that fails without it.

| PR | What | Done when |
| --- | --- | --- |
| 1 | This plan | Merged |
| 2 | **Swap and replace (U1, U6)**: a framed photo dropped on another swaps the two, with mouse and touch. Alt moves it freely. The dragged photo comes to the front while it moves. A tray photo dropped on a filled frame replaces its photo. The frame under the pointer lights up. One Undo step each | A drag from frame 1 to frame 3 swaps them; Undo puts both back |
| 3 | **Layouts and the page (U2, U7)**: grids and mosaics are turned on portrait and tall pages. The layout pictures follow the page's shape. The start page offers the page size next to the layouts. Frames are filled by matching photo shape | On A4 portrait no frame of any layout is thinner than 1:1.6; a tall photo goes to a tall frame |
| 4 | **No piles (U3)**: photos added to a full page start a neat grid in free space, or the layout grows, never a stack. "Make it casual" spreads photos that overlap | 2 photos added to a full 6 grid overlap no existing photo |
| 5 | **Toasts and casual (U4, U5, Low)**: an offer's toast goes when a later change makes its Undo meaningless. Going back to a grid takes off the frames and tape that "Make it casual" put on, and keeps those the user chose. Toasts stay off the phone's font buttons | Undo on any visible toast changes the page; a grid after casual shows no tape |
| 6 | **Words and stickers (U8)**: new words and stickers go to clear space, and never across a seam. On a full page they go over the middle of one photo, readable | Three headings and two stickers added to a 4 grid cross no seam |
| 7 | **Polish (UX Low)**: <ul><li>a single click on an empty frame opens the photo chooser;</li><li>focus stays in the Save dialog and starts on the first purpose;</li><li>the page is a keyboard stop with a hint;</li><li>PDF for printing defaults to print quality;</li><li>Stickers opens on a filled set;</li><li>sticker tiles are readable in Dark;</li><li>a ready-made heading's text is not all selected;</li><li>"Done moving" fits;</li><li>the lock question shows the photo it is about;</li><li>the draft subtitle and the page size are shown once, whole.</li></ul> | Every UX Low is fixed or written down as a decision |
| 8 | **Import and selection (E1, part of E3)**: `select()` is the only way the selection changes. A photo import that finishes while the user is typing, or while another item is selected, does not take the selection or focus | A unit test and a browser test type words while an import lands; the words stay, and Backspace deletes no photo |
| 9 | **One way to change (E2, E3)**: `load()` and `restore()` are the only other ways in. `setCollage` and the direct assignments go. A small transaction helper replaces `edit(…, 'none')` plus a later `commit()`. The drafts screen changes `thumbnail` through named changes. `edit()` recognises a collage it is given back reliably | A source scan finds no assignment to the collage outside the model editor, `load` and `restore` |
| 10 | **Small editor bugs (E4, Low)**: a font arriving changes the size without an Undo step. A change that changes nothing writes no draft. Ctrl+Z during a drag keeps Redo. Caches let go of deleted items. Deleting a draft that fails says so. Ticks from an abandoned PRISM Photos choice go. A pinch keeps nothing of the first finger's drag | Each has a test that fails without the fix |
| 11 | **Each part gets only what it calls (E3)**: the helper groups go. Each part is handed exactly the functions it uses, so the page and app wiring can be tested with small fakes. Unit tests for that wiring are added | No part is handed a helper it does not call (unit test); the app and page each have wiring tests |
| 12 | **Smaller files (Low)**: moving a photo in its frame, the gestures and the selection move out of collage-app.js. collage-core.js is split by concern. Plan and PR history leave the code comments | collage-app.js under 700 lines, and no collage module over 900 |
| 13 | **PRISM Photos dialog tests (S1, Low)**: browser tests for Show more, a page past the end, search, project and year, and two answers in the wrong order, with a page-2 fixture. Show more de-duplicates. The collage JavaScript tests are found by folder | Breaking Show more, or appending page 1 twice, fails a test |
| 14 | **No fixed waits (S2)**: tests wait for a draft write that happened after the action, not for a time. The autosave delay is set through the module. The browser specs are named by feature, not by review round | No spec proves "nothing happened" with a wait shorter than the thing it waits for |
| 15 | **Server loose ends (Low)**: the collage search asks for no statistics, and for the filter lists only on the first unfiltered page. The meaning of `Version` is settled and tested. The test server answers 304 like production | Each has a test |
| 16 | **Docs**, then a **fresh cold review** | Scores reported; anything short of 9 gets its own plan |

**What can run at the same time:**

- **UX first:** PRs 2 to 7 (UX), PR 13 (dialog tests) and PR 15 (server) touch mostly different
  code. They are built together, each in its own worktree. Among the UX PRs:
  - PRs 2 and 3 both touch layout code, so 3 follows 2.
  - PRs 4 and 6 both touch placing new items, so 6 follows 4.
- **Then the editor:** PRs 8, 9, 10 and 11 rework how the parts reach the editor. They follow one
  another after the UX PRs, because those change the same parts.
- **Then the rest:** PR 12 (file split) comes after PR 11, and PR 14 (fixed waits) comes after
  PR 12, so the renamed specs land on the final file layout.

## Deployment

- **Browser changes:** no migration and no new setting. Drafts saved before the change still open:
  - a turned layout applies only when a layout is chosen again;
  - the frames that "Make it casual" put on are marked only from now on, so older casual drafts
    keep their frames when a grid is chosen.
- **Server changes:** the collage search runs fewer queries, which is additive and returns the
  same answer. Nothing about stickers changes for users.
- **Internet:** nothing needs it.
