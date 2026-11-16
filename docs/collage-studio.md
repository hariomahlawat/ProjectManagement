# Collage Studio

Collage Studio makes a collage from a user's **own** photos: a farewell or picnic poster, a
birthday wish, a briefing slide or a report page. It is not part of any PRISM record. It opens in
a new browser tab from the Tools menu (**Collage Studio**) and from the Collage card on the Photo
Tools home, at `/Tools/Collage`.

The build plan, owner decisions and the approved mockup are in
[ux/collage-studio.md](ux/collage-studio.md). This document describes what is built.

## Privacy and data contract

- Everything runs in the user's browser. Photos are read with the File API, drawn with Konva on
  a canvas and saved with a `blob:` download. Nothing the user makes, and no photo from the
  computer, is sent to the PRISM server.
- PRISM only sends pictures **to** the page, all read by `wwwroot/js/collage/collage-library.js`
  with GET requests (see
  [ux/collage-studio-library-and-unit-stickers.md](ux/collage-studio-library-and-unit-stickers.md)):
  - the **Crests and logos** stickers an administrator uploaded (Administration › Crests and
    logos), listed by `/api/collage/stickers` and drawn from `/api/collage/stickers/{id}/image`;
    when PRISM cannot be reached the group says so and the other stickers still work;
  - photos picked with **From PRISM Photos**, listed by `/api/collage/library-photos` through the
    library's own search and visibility rule, and read from the same address PRISM Photos uses
    for that kind of photo, so that page checks access again: a project photo from
    `/Projects/{id}/Photos/{photoId}/View/original`, a visit or social media photo from its
    `/ProjectOfficeReports/…/ViewPhoto/…` page, and an activity photo or a photo from another
    folder from `/Photos/Media/{id}/original`. One resolver,
    `Features/MediaLibrary/Services/MediaLibraryItemAddresses.cs`, gives these addresses to both
    pages, and `collage-library.js` fetches only those path shapes. A picked photo becomes a file in the browser and goes through the same
    checks as a photo from the computer; from then on it is kept only in the browser like any
    other. The button is shown only when the Photos library is switched on.
- Nothing the user makes is stored or retained by PRISM. The page model
  (`Pages/Tools/Collage/Index.cshtml.cs`) has only `OnGet`; there is no form post.
- The page requires a signed-in user (`[Authorize]`), like the rest of PRISM.
- Usage telemetry records only that the `collage` module was opened (`ErpUsageModuleCatalog`),
  never file names or contents.
- **Drafts are kept in the browser, on that computer only**, in IndexedDB (database
  `prism-collage-studio`, stores `drafts` and `photos`). A draft holds the collage and copies of
  its photos, so it reopens without asking for the files again. Another browser, another
  computer or clearing the browser's site data does not have them. The start screen says so.
- A saved picture is drawn afresh, so the hidden data of the original photos (GPS location,
  camera details) is never in it.
- Leaving the page while a change is still being written to the draft makes the browser ask
  first.

`ProjectManagement.Tests/Tools/CollageStudioPageContractTests.cs` guards these points: sign-in
required, no handler other than `OnGet`, no form, every script the page loads, no network call
(`fetch`, `XMLHttpRequest`, `sendBeacon`, `WebSocket`, `EventSource`, `import()`) in them except
`collage-library.js`, whose three `fetch` calls read the fixed crests-and-logos and PRISM Photos
addresses and a listed photo's original (checked against one pattern), no
outside address in the page, its scripts or its stylesheets, the bundled fonts with their licence,
and the vendored Konva.

## Offline deployment

Everything the page needs ships with PRISM; no CDN or Internet access is needed.

| What | Where | Licence |
| --- | --- | --- |
| Konva 10.9.1 (the editor's canvas) | `wwwroot/lib/konva/10.9.1/`, listed in `libman.json` | MIT, `LICENSE` beside it |
| pdf-lib 1.17.1 (Save as PDF) | `wwwroot/lib/pdf-lib/`, the copy PDF Tools ships | MIT |
| Text fonts, English and Hindi | `wwwroot/fonts/collage/<family>/` (woff2, latin and devanagari only), faces in `wwwroot/css/fonts/collage-fonts.css` | SIL Open Font License, `OFL.txt` in each folder |
| Emoji stickers (143 chosen Twemoji drawings) | `wwwroot/img/collage/stickers/twemoji/` | CC BY 4.0, `LICENSE-GRAPHICS.txt` |

The twelve drawn stickers, every gradient, pattern and texture are drawn by code, so there are no
other image files. The page's **Credits** dialog (start screen and Stickers panel) names the
fonts, Twemoji and Konva with their licences, as CC BY 4.0 requires.

`libman install` fails on an unrelated broken `cytoscape-panzoom` entry, so Konva was copied from
its npm package by hand; the libman entry records the version.

## What the user can do

- **Start**: pick a layout (2 side by side up to a 12 grid and a mosaic of 7) or a blank page in
  one of seven sizes: A4 landscape or portrait, A3 poster, Slide 16:9, Square, Phone status 9:16
  and Banner 3:1. Drafts are listed below, newest first, with Delete and a 10-second Undo.
- **Photos**: add up to 60 photos (JPEG, PNG, WEBP, GIF or BMP, at most 50 MB each) by the
  button, by dropping them, by pasting, or with **From PRISM Photos** (search by words, project
  and year, tick the photos, Add). Empty frames fill first. A photo the page does not show can be
  taken out of the collage with the × on its tray picture (Undo of its frame brings it back);
  one file that cannot be read is named and the rest go in. A photo can be turned to any
  angle, resized, mirrored, given an optional caption (on a white strip under the photo, or
  handwritten in a polaroid's edge), a frame (white border, polaroid, tape, torn
  edge), rounded corners, a shape (oval, heart, star, hexagon, arch), a shadow and a look (black and
  white, sepia, vintage, vivid, soft, cool, warm), and moved inside its frame (**Adjust**, or double-click).
- **Casual or neat**: one button scatters the photos at slight angles as polaroids and taped
  prints (a frame or shape the user chose is kept, and the photo is only tilted), with Undo on
  the screen; another straightens everything back. Shuffle swaps the photos between frames.
- **Text** in English and Hindi with bundled fonts: ready-made headings (Farewell, Happy
  Birthday, Thank you!, Congratulations and others), curved text, outline, shadow and a label
  behind the words. Hindi is typed with the Windows Hindi keyboard or pasted.
- **Stickers**: first the **Crests and logos** an administrator uploaded (shown first when there
  are any; a retired one still draws in drafts that use it), then twelve drawn shapes and stamps
  that take any colour, and emoji drawings in eight
  groups with a search box.
- **Background**: colours, gradients, patterns and textures, or one of the photos softly
  blurred.
- **Arrange**: snap guides to the page and other items, Line up on the page, a **Layers** tab
  (beside **Settings**, at the top of the settings panel) to select, hide or lock any item, bring forward and send backward, Duplicate, and Preview (the
  page alone, exactly as Save makes it: empty frames are left out; Esc comes back). A white
  frame put on a photo turns its shadow on, so it shows on a white page.
- **Lock**: a locked item cannot be moved, resized, turned, given another frame, shape or
  corners (the layout's Rounded corners pass it by), moved inside its frame, replaced or deleted,
  from the toolbar, the settings or the keys. Choosing a layout while photos are locked asks
  first: put them in the new layout too (they stay locked), or keep them where they are, and the
  layout leaves out the frames they cover. A kept item well off the page is moved just inside it.
- **Undo and Redo** for every change.
- **Save as** PNG, JPEG (optionally under a size in KB) or PDF whose page is the paper's real
  size; at print quality (300 dpi) or for the screen (1920 px on the long side). Save reads each
  original photo again at the size its frame needs, so the saved picture is sharper than the
  screen; empty frames and hidden items are left out.

## How it is built

| File | What it holds |
| --- | --- |
| `wwwroot/js/collage/collage-core.js` (`window.PrismCollage`) | The collage as plain JSON and every pure rule: sizes, layouts, crop and pan, frames, shapes, looks, text layout, snapping, lining up, Save sizes, reading a draft back safely, undo history |
| `wwwroot/js/collage/collage-draw.js` (`window.PrismCollageDraw`) | Drawing a photo, text, sticker and background on a 2D canvas. The editor uses it inside Konva shapes and Save uses it on its own canvas, so **the saved picture always matches the screen** |
| `wwwroot/js/collage/collage-stickers.js` | The sticker catalogue, the drawn stickers as SVG and the search |
| `wwwroot/js/collage/collage-drafts.js` | The IndexedDB store; every call fails softly, and the editor still works when the browser refuses storage |
| `wwwroot/js/collage/collage-render.js` | Save's drawing from the original photos (same drawing as the page), the largest canvas a browser can draw, and what was left out |
| `wwwroot/js/collage/collage-edits.js` | Edits still going on (typing, sliders, nudges): kept in the draft at once, recorded for Undo once they pause |
| `wwwroot/js/collage/collage-focus.js` | Keeping keyboard focus on the control used when a panel is drawn again |
| `wwwroot/js/collage/collage-library-dialog.js` | The From PRISM Photos dialog |
| `wwwroot/js/collage/collage-settings.js` | The settings panel's sections as HTML (arranging, Layers, text, frames, shapes and looks), every value escaped |
| `wwwroot/js/collage/collage-photos.js` | Reading photos at their working size, tray pictures, and grey stand-ins for draft photos that cannot be opened |
| `wwwroot/js/collage/collage-save.js` | The Save as… dialog and making the PNG, JPG or PDF |
| `wwwroot/js/collage/collage-draft-session.js` | The draft being edited and the drafts on the start screen: when a draft is written, room for a new one, delete and Undo, purges, other tabs |
| `wwwroot/js/collage/collage-changes.js` | The change log: the one way a change reaches Undo and the draft, and where a held edit ends |
| `wwwroot/js/collage/collage-page.js` | The page: the Konva stage and layers, drawing each item, the handles, zoom and Preview |
| `wwwroot/js/collage/collage-drafts-screen.js` | Drafts in the editor: the draft status, writing the draft and its small picture, the start screen's drafts |
| `wwwroot/js/collage/collage-panels.js` | The left drawer and the settings panel with its Settings and Layers tabs |
| `wwwroot/js/collage/collage-tray.js` | Photos: choosing, dropping, pasting, reading in, the tray, From PRISM Photos |
| `wwwroot/js/collage/collage-keys.js` | The keyboard and the mouse wheel |
| `wwwroot/js/collage/collage-app.js` | The editor itself (about 1,250 lines): its state, selection and the toolbar, moving a photo in its frame, the change actions, and creating the parts above. Each part is made once with the helpers it uses and `app`, the editor's view of its own state (read when used, never before it exists) |
| `wwwroot/css/collage.css` | All styles, Bootstrap variables only (Light and Dark) |

Photo handling keeps memory low: the editor draws a copy at most 2000 px on its long side, the
tray shows a 240 px thumbnail, and the original file is kept only for Save. Thirty phone photos
then do not exhaust the browser's memory.

Each layer of the page is a canvas of at most 16 million pixels at any zoom
(`core.canvasPixelRatio`); iPhones draw nothing on a canvas over 16.7 million, so zoomed far in on
a phone the page turns a little softer instead of blank. The stage grows in steps of 128 pixels, so
a pinch makes new canvases now and then rather than at every move. The draft's small picture for
the start screen is made at most every five seconds while editing, and always on leaving. Save
lets go of its picture, and of every smaller copy made to fit a JPG size, even when it fails.

Drafts are saved half a second after a change. Opening another draft, going back to the start
screen or leaving waits for the last write, and a draft that cannot be written says why (for
example, the browser is out of room). The browser keeps at most 30 drafts; the 31st asks to
delete the oldest.

### Keys

| Key | Does |
| --- | --- |
| Arrow keys | Move the selected item 1 unit; with Shift, 10. While adjusting, move the photo in its frame |
| Delete or Backspace | Delete the selected item (not when locked). A photo in a layout leaves its frame empty; Undo is offered on the screen |
| Ctrl+D | Duplicate |
| `]` / `[` | Bring forward / send backward; with Shift, to the front / back |
| Enter | When the page has focus: adjust the selected photo, or edit the selected text. On a focused button, Enter presses that button |
| Ctrl+Z, Ctrl+Y (or Ctrl+Shift+Z) | Undo, redo |
| Ctrl+S | Save as… (also while typing in a box) |
| Shift while turning | 15° steps |
| Esc | Leave adjusting, deselect, or leave Preview |

A keyboard user picks an item in the **Layers** list; the page then says how to move it. Photos
and empty frames have a **Size** slider, so everything can be resized without a mouse. When a
panel is drawn again after a change, focus goes back to the control that was used, inside that
panel (`collage-focus.js`). Arrow keys do not move a hidden item.

### The settings panel

The settings panel has two tabs: **Settings** for the selected item (or the page), and **Layers**
for every item. Choosing an item on the page shows its settings; choosing it in Layers keeps
Layers open, so a keyboard user can pick and move items one after another. Groups used less often
(angle, corners and see-through, order, line up, text effects, brightness and contrast) are
folded; one opened stays open, remembered in this browser.

### Changes, Undo and drafts

Every change reaches the page, Undo and the draft the same way, through the change log
(`collage-changes.js`, unit tests in `collage-changes.test.js`); nothing else touches the Undo
history. Clicks and drags are recorded at once (`commit()`). Edits that go on (typing, sliders,
colour pickers, nudges, the wheel) are kept in the draft at once and recorded for Undo once they
pause, or as soon as anything else happens: another item, Undo, leaving (`collage-edits.js`).
Every user action begins with a boundary (a pointer pressed, focus moved, or a key that is
neither typing nor a nudge), where a held edit becomes its own step, so typing and then at once
clicking Bold are two Undo steps. Closing the tab right after typing
loses nothing, and the draft is also written when the page is hidden or put away
(`visibilitychange`, `pagehide`), which phones need.

The draft being edited, and the drafts on the start screen, are kept by
`collage-draft-session.js` (unit tests in `collage-draft-session.test.js`). The draft open in this
tab is never purged from it. The start screen waits while a draft opens, so it cannot be deleted
half way through, and a draft deleted meanwhile is not opened. When another tab deletes or purges
the draft open here, the tabs say so to each other (`BroadcastChannel`) and this tab writes it
back at once; every save also stores any photo the database no longer holds for it. "Delete the
oldest draft?" is asked only when the user acts: on starting a collage when 30 drafts are kept
already, or on **Keep as a draft**; never from the autosave.

A draft photo this browser cannot open now (memory pressure, a damaged file) keeps its place as a
grey stand-in with its stored file, crop and caption; it comes back when the draft is opened
where it can be read, and Save leaves it out and says so. Photos are decoded straight at their
working size, and a picture over 250 million pixels is refused before it is read.

### First use

The start screen shows one row of drafts (with "Show all") so the layouts for a new collage are
in view. A collage nobody named is called by the first line of its first text (not the words
that only show where to type, such as "Your heading"), or by when it was started. A photo goes
into the empty frame closest to its shape; photos past a layout's frames wait in Photos, with a
message, rather than sit loose on the grid. New words and stickers go where they cover nothing
yet, and the item's toolbar moves below it when above would cover another item. Save asks what the picture is
for: WhatsApp or email (JPG for the screen), printing (PDF at print quality), or a slide or a
screen (PNG). The purpose chosen is shown pressed and remembered in this browser; the first time
it is WhatsApp or email.

### Phones and the Dark theme

At 820 px wide and below, the panels move to a bar at the bottom. A panel, or the selected item's
settings (the **Settings** button on the toolbar over the item), opens as a sheet over the lower
half with a close button, and the page fits in the space above it. The bottom bar's **Layers**
button opens the Layers tab even when nothing is selected, so hidden items can always be
shown again. Preview fills the whole screen. Two fingers on the page zoom
it, around the point between the fingers; one finger moves items. The zoom buttons are hidden on
phones, and the draft status shows only when something is wrong, with its reason in the alert.
On a phone the header keeps to one line: "by PRISM" and the page size go (the size is in the
Page settings), so the collage name has the room; every icon button keeps its name for screen
readers. Hints speak of tapping on a touch screen and of clicking and keys elsewhere.

Colours come from Bootstrap variables, so the Dark theme needs no rules of its own; the page
itself stays the colour the user chose, as it will print.

## Tests

- `wwwroot/js/collage/collage-core.test.js`, `collage-draw.test.js` and
  `collage-stickers.test.js`, run by `npm test` (registered in `tools/run-js-tests.js`).
- `ProjectManagement.Tests/Tools/CollageStudioPageContractTests.cs` (above), and the menu and
  usage-catalogue tests that list Collage Studio.
- `wwwroot/js/collage/collage-editor-parts.test.js`: held edits, focus kept in the redrawn panel
  (jsdom), Save's drawing (photos read at frame size, what was left out counted), and the
  settings HTML (escaped names, Size only for photos and frames, Layers order).
- Browser tests (`npm run test:browser`, Playwright, see [build-and-test.md](build-and-test.md)):
  make a collage and Save; reopen a draft after a reload; a damaged draft photo kept as a
  stand-in; keyboard only; Save by purpose; frames by shape; the Dark theme; From PRISM Photos with
  a stubbed library; and a phone (sheets, Layers, `pagehide`, the canvas limit, the header).
  Since the second review also: drafts deleted while opening or from another tab, removing a
  photo, typing then Bold, keeping the user's choices, captions, Preview and Save, the settings
  tabs, Ctrl+S, rounded corners, and the polish (stickers fit, units, PRISM Photos off, a missing
  script). They run on every pull request, with the page served under PRISM's own Content Security
  Policy (`tests/browser/fixtures/production-headers.json`, written from the C# side by
  `CollageBrowserFixtureTests`); a policy violation fails the test.
- `ProjectManagement.Tests/Tools/CollageApiHostTests.cs` runs the real PRISM host: each of the
  three Collage APIs answers an anonymous request with 401, every `/api/collage` route requires
  authorization, and the library lists each kind of photo at its own page's address.
- `ProjectManagement.Tests/Tools/CollageStickerTests.cs` (including the audit before and after,
  a scanner refusal, and the missing-file and left-over-file log lines),
  `CollageLibraryPhotosTests.cs`, and
  `CollageStickerPostgresIntegrationTests.cs` on a real PostgreSQL (two uploads at once never pass
  60; a failed audit write rolls the change back and removes the picture), run in the database
  migrations workflow.

## Known limits

- iPhone HEIC photos are not supported; the page names the types it takes when one is chosen.
- Drafts live in one browser on one computer. There is no sharing of drafts and no copy in
  PRISM; Save as is the way to keep or send a collage.
- Everything is held in browser memory, so a print-quality A3 poster from many large photos
  depends on the user's computer. When a browser cannot draw a canvas that large, Save makes the
  largest picture it can and says so, with its dots per inch.
- **Crests and logos**: only an Admin can add them (permission "Manage crests and logos", easy to
  widen to HoD in the capability catalog). At most 60 are in use; a retired one is never deleted,
  so drafts keep it. SVG files are refused. A user's own logo as a sticker is not built (owner,
  15 Nov 2026); it can still be added as a photo.
- **From PRISM Photos** lists what the Photos library's catalogue holds. A photo uploaded moments
  ago appears once the catalogue has taken it in, as on `/Photos`. Videos are not offered.
- Themes (ready-made Farewell, Picnic and similar designs) are not built.

## Administration › Crests and logos

`Areas/Admin/Pages/CollageStickers`, under Reference lists, for the "Manage crests and logos"
permission (`Admin.CollageStickers.Manage`, Admin only). An administrator adds a PNG, JPEG or WebP
of up to 5 MB with a name and optional search words, moves stickers earlier or later, renames,
retires and puts them back. `Features/Collage/CollageStickerService.cs` checks the file with
`PrismImageCodec`, re-encodes it as a PNG of at most 1024 pixels (keeping a clear background),
writes it under the upload root in `collage-stickers/`, and saves each change with its admin
audit entry in one transaction, under one PostgreSQL advisory lock so the limit of 60 holds when
two administrators work at once. The table is `CollageStickers` (migration
`20270104080022_AddCollageStickers`); the pictures are covered by the upload-root backup.

What the audit log records (Administration › Logs, under Reference lists):

| Action | Before | After |
| --- | --- | --- |
| `CollageStickerAdded` (added) | none | name, search words, place, retired |
| `CollageStickerUpdated` (renamed) | name, search words, place, retired | the same, changed |
| `CollageStickerReordered` (moved) | `Order`: every sticker whose place changed, with its old place | `Order`: the same stickers with their new places |
| `CollageStickerRetired` / `CollageStickerRestored` | name, search words, place, retired | the same, changed (put back goes to the end) |
| `CollageStickerRefused` (upload refused by the scanner) | none | name, file name and size; outcome Refused, level Warning |

A move renumbers the picker from 1, so its one entry lists the moved sticker, its neighbour and
any other sticker whose number changed. Moves recorded before 16 Nov 2026 are
`CollageStickerUpdated` entries without a before state.

If an upload scanner (`IVirusScanner`) is registered and refuses the picture or fails, nothing is
stored: the administrator sees "The picture did not pass the upload check" and the refusal is in
the audit log and the server log. PRISM registers no scanner by default.

Server log lines worth knowing:

- a warning when a sticker's picture file is missing (the sticker then draws nothing);
- an information line with the storage key each time a picture is written, before its row is
  saved; and after each successful upload, a warning naming any picture file in
  `collage-stickers/` over an hour old that no sticker uses. Such a file is left when the process
  stops between writing the file and saving the row. Nothing deletes it; an administrator can.
