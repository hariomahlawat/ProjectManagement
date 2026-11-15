# Collage Studio

Collage Studio makes a collage from a user's **own** photos: a farewell or picnic poster, a
birthday wish, a briefing slide or a report page. It is not part of any PRISM record. It opens in
a new browser tab from the Tools menu (**Collage Studio**) and from the Collage card on the Photo
Tools home, at `/Tools/Collage`.

The build plan, owner decisions and the approved mockup are in
[ux/collage-studio.md](ux/collage-studio.md). This document describes what is built.

## Privacy and data contract

- Everything runs in the user's browser. Photos are read with the File API, drawn with Konva on
  a canvas and saved with a `blob:` download. Nothing is sent to the PRISM server.
- Nothing is stored or retained by PRISM. The page model (`Pages/Tools/Collage/Index.cshtml.cs`)
  has only `OnGet`; there is no form post, API, database table or migration.
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
(`fetch`, `XMLHttpRequest`, `sendBeacon`, `WebSocket`, `EventSource`, `import()`) in them, no
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
  button, by dropping them or by pasting. Empty frames fill first. A photo can be turned to any
  angle, resized, mirrored, given a frame (white border, polaroid with a caption, tape, torn
  edge), rounded corners, a shape (oval, heart, star, hexagon, arch), a shadow and a look (black and
  white, sepia, vintage, vivid, soft, cool, warm), and moved inside its frame (**Adjust**, or double-click).
- **Casual or neat**: one button scatters the photos at slight angles as polaroids and taped
  prints; another straightens everything back. Shuffle swaps the photos between frames.
- **Text** in English and Hindi with bundled fonts: ready-made headings (Farewell, Happy
  Birthday, Thank you!, Congratulations and others), curved text, outline, shadow and a label
  behind the words. Hindi is typed with the Windows Hindi keyboard or pasted.
- **Stickers**: twelve drawn shapes and stamps that take any colour, and emoji drawings in eight
  groups with a search box.
- **Background**: colours, gradients, patterns and textures, or one of the photos softly
  blurred.
- **Arrange**: snap guides to the page and other items, Line up on the page, a Layers list to
  select, hide or lock any item, bring forward and send backward, Duplicate, and Preview (the
  page alone; Esc comes back).
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
| `wwwroot/js/collage/collage-app.js` | The editor: panels, Konva stage, toolbar, keys, drafts and Save |
| `wwwroot/css/collage.css` | All styles, Bootstrap variables only (Light and Dark) |

Photo handling keeps memory low: the editor draws a copy at most 2000 px on its long side, the
tray shows a 240 px thumbnail, and the original file is kept only for Save. Thirty phone photos
then do not exhaust the browser's memory.

Drafts are saved half a second after a change. Opening another draft, going back to the start
screen or leaving waits for the last write, and a draft that cannot be written says why (for
example, the browser is out of room). The browser keeps at most 30 drafts; the 31st asks to
delete the oldest.

### Keys

| Key | Does |
| --- | --- |
| Arrow keys | Move the selected item 1 unit; with Shift, 10. While adjusting, move the photo in its frame |
| Delete or Backspace | Delete the selected item (not when locked) |
| Ctrl+D | Duplicate |
| `]` / `[` | Bring forward / send backward; with Shift, to the front / back |
| Enter | Adjust the selected photo, or edit the selected text |
| Ctrl+Z, Ctrl+Y (or Ctrl+Shift+Z) | Undo, redo |
| Ctrl+S | Save as… |
| Shift while turning | 15° steps |
| Esc | Leave adjusting, deselect, or leave Preview |

A keyboard user picks an item in the **Layers** list; the page then says how to move it.

### Phones and the Dark theme

At 820 px wide and below, the panels move to a bar at the bottom. A panel, or the selected item's
settings (the **Settings** button on the toolbar over the item), opens as a sheet over the lower
half with a close button, and the page fits in the space above it. Two fingers on the page zoom
it; one finger moves items. The zoom buttons and the draft status are hidden on phones.

Colours come from Bootstrap variables, so the Dark theme needs no rules of its own; the page
itself stays the colour the user chose, as it will print.

## Tests

- `wwwroot/js/collage/collage-core.test.js`, `collage-draw.test.js` and
  `collage-stickers.test.js`, run by `npm test` (registered in `tools/run-js-tests.js`).
- `ProjectManagement.Tests/Tools/CollageStudioPageContractTests.cs` (above), and the menu and
  usage-catalogue tests that list Collage Studio.
- The editor itself (dragging, sheets, Save) has no browser test harness; each PR was checked by
  hand in the app at desktop and phone sizes.

## Known limits

- iPhone HEIC photos are not supported; the page names the types it takes when one is chosen.
- Drafts live in one browser on one computer. There is no sharing of drafts and no copy in
  PRISM; Save as is the way to keep or send a collage.
- Everything is held in browser memory, so a print-quality A3 poster from many large photos
  depends on the user's computer. When a browser cannot draw a canvas that large, Save makes the
  largest picture it can and says so, with its dots per inch.
- Admin-uploaded unit stickers, themes, and the PRISM Photos library as a source of photos are
  not built; the owner will decide later (see [ux/collage-studio.md](ux/collage-studio.md)).
