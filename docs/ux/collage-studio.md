# Collage Studio (plan, 15 Nov 2026)

> **Complete.** All twelve PRs are built; what is built is described in
> [../collage-studio.md](../collage-studio.md).

On 15 Nov 2026 the owner asked for "the best collage making module with all the features", so that
nobody needs a website to make a collage. It is used for casual unit functions (farewells,
picnics, birthdays, festivals) as well as for reports and slides.

The approved mockup is [mockups/collage-studio.html](mockups/collage-studio.html). It opens in Edge
or Chrome from the file, and everything in it can be clicked.

## Owner decisions (15 Nov 2026)

- **Photos at any angle and stickers are required**, with all the styles a casual collage needs:
  frames, shapes, decorated text and backgrounds. It is not a grid-only tool.
- **Drafts are kept in the browser** first. Saving collages in PRISM, to share them or open them on
  another computer, is a later decision.
- **Hindi text: yes.** Bundled Devanagari fonts, handwritten and formal.
- **Themes are left out for now.** Themes are ready-made Farewell, Picnic and similar designs.
  A collage starts from a layout or a blank page. Themes can come later as their own plan.
- **Not yet decided:**
  - unit crests and logos uploaded by an administrator as stickers (needs server storage);
  - choosing photos from the PRISM Photos library.

  Neither is in this plan. Both were approved later; see
  [collage-studio-library-and-unit-stickers.md](collage-studio-library-and-unit-stickers.md).

## Where it lives

- Its own page, **`/Tools/Collage`**, with the same rule as Photo Tools (`[Authorize]`, any signed-in
  user). It has no handlers that accept files. Collage Studio is too large to be one more screen
  inside `/Tools/Photo`.
- A **Collage** card on the Photo Tools home opens it. It also gets an entry in the Tools menu
  beside PDF Tools and Photo Tools. Both come with Save (PR 4); until then the page is reached
  only by its address and says it is being built.
- Everything runs in the browser, like the other Photo Tools:
  - no photo or collage is sent to the server;
  - nothing needs the Internet;
  - fonts, stickers and textures are files shipped with PRISM.

## What the user sees

The mockup is the reference. In short:

**Start screen**
- **Your drafts**, newest first, each with a picture of the page, Open and Delete. Delete has Undo.
  The screen says: "Drafts are kept in this browser only. Save the collage as a picture or PDF to keep a copy."
- **Start with a layout**: 11 grids, from 2 side by side up to 12. A mosaic of 7 is among them.
- **Start blank** in one of 7 sizes:

  | Size | Use | Pixels at 300 dpi (print) |
  | --- | --- | --- |
  | A4 landscape | Print | 3508 × 2480 |
  | A4 portrait | Print | 2480 × 3508 |
  | A3 poster | Print | 3508 × 4961 |
  | Slide 16:9 | Briefing slides | 3840 × 2160 |
  | Square | WhatsApp, posts | 3000 × 3000 |
  | Phone status 9:16 | Full screen on a phone | 2160 × 3840 |
  | Banner 3:1 | Notice board strip | 6000 × 2000 |

**Editor**
- **Top bar:**
  - Back to the start screen.
  - The collage's name.
  - "Draft saved in this browser".
  - Page size.
  - Undo and Redo.
  - Zoom.
  - Preview.
  - **Save as…**
- **Left rail**, each item opening a panel:
  - **Layouts**: the 11 grids, space between photos, rounded corners, plus three buttons: Make it
    casual (tilts and frames the photos), Straighten all, Shuffle photos.
  - **Photos**: add from this computer, drop, or paste with Ctrl+V. A tray of the collage's photos
    marks each one Used or 2×. Click a photo to add it, or drag it onto the page.
  - **Stickers**: search, plus groups:
    - Shapes (drawn stickers);
    - Celebration, Picnic, Food, Sports, Festival, Nature and Faces (emoji).
  - **Text**:
    - add a heading, a subheading, a line, or Hindi text;
    - ready-made English and Hindi headings.
  - **Background**: colours with a picker, gradients, patterns, textures, or one of the photos
    softly blurred.
- **The page**:
  - Click to select.
  - Drag to move. Pink guides snap to the page's centre and edges and to other items; Alt turns
    snapping off.
  - Corner handles resize. A photo's side handles change the shape of its frame.
  - The round handle turns the item to any angle. It clicks to straight and 45° on its own;
    Shift gives 15° steps; the angle shows while turning.
  - Double-click a photo to move and zoom it inside its frame.
- **Floating toolbar** by the selection:
  - Move inside frame and Replace (photos);
  - Edit text (text);
  - Duplicate, Forward, Backward, Lock, Delete.
- **Right panel** for the selection:
  - **Photo**:
    - zoom inside the frame, and fit the frame to the whole photo;
    - frame: none, white border, polaroid with a caption, taped, torn edge;
    - shadow;
    - shape: square with rounded corners, oval, heart, star, hexagon, arch;
    - look: original, black and white, sepia, vintage, vivid, soft, cool, warm;
    - brightness, contrast, warmth, mirror.
  - **Text**:
    - the text;
    - 9 fonts, two of them Hindi;
    - size, bold, italic, alignment, letter spacing, colour;
    - curve, outline with colour, shadow, glow, and a label behind the text.
  - **Sticker**: size, colour (drawn stickers), mirror, shadow.
  - **Every item**:
    - angle, with −15°, Straight and +15°;
    - line up on the page: left, centre, right, top, middle, bottom;
    - see-through, front and back.
  - With nothing selected, the panel shows the page size and the background.
  - **Layers**, always shown: front at the top, with hide and lock.
- **Keyboard**:

  | Key | Does |
  | --- | --- |
  | Ctrl+Z | Undo |
  | Ctrl+Y or Ctrl+Shift+Z | Redo |
  | Ctrl+D | Duplicate |
  | Delete | Delete |
  | Arrows | Nudge (Shift for 10) |
  | `]` and `[` | Forward and backward (Shift to the front and back) |
  | Esc | Done, or deselect |

- **Save as…**:
  - PNG (sharpest);
  - JPEG, optionally under a size in KB, through the same `encodeToTarget` as Reduce size;
  - PDF whose page is the paper's true size, through `pdf-lib`, which Photo Tools already ships.
  - Print quality (300 dpi) or Screen (1920 px on the long side).
- **Phones**: the rail moves to the bottom. Panels and the selection's settings open as a sheet,
  and the page moves up above it. The toolbar's Settings button (sliders) opens the settings;
  each sheet has a close button. Two fingers on the page zoom it.

## How it is built

- **Canvas library: Konva** (MIT), version pinned, added through `libman.json` into
  `wwwroot/lib/konva/<version>/` like the other libraries, with its licence file. Konva gives the
  things that are costly to write and get right:
  - a stage with layers;
  - hit-testing on turned items;
  - a transformer with resize and rotate handles that works with mouse and touch;
  - clipping for shapes;
  - text on a path;
  - export at any pixel ratio.

  PR 2 confirms it on the target browsers before anything else depends on it. If it falls short,
  the fallback is the mockup's own approach: the page drawn as HTML, plus a separate canvas
  drawing for Save.
- **One collage model, plain JSON.** Konva only draws it. The model is:

  ```js
  { version: 1, size, background, gap, radius, layout, items: [...] }
  ```

  Item fields as in the mockup: `photo`, `text` or `sticker`, with centre, width, angle, opacity,
  and so on.
  - Pure functions in `collage-core.js` are tested in Node in `npm test`:
    - layouts to cells;
    - frame padding and photo aspect;
    - snapping;
    - bounding boxes of turned items;
    - curve maths for text;
    - sticker search;
    - draft reading and upgrade by `version`.
  - Undo is a list of model snapshots.
- **Photos: working copies for the screen, originals for Save.**
  - On adding, each photo is decoded once. A working copy at most 2000 px on the long side is kept
    for the editor; the original file is kept as a `Blob`.
  - Save decodes each original only at the size it needs (`createImageBitmap` with a resize), one
    at a time. Thirty phone photos then do not exhaust the browser's memory.
  - File checks as in Photo Tools: JPEG, PNG, WebP, GIF or BMP; size and count limits shown on the
    screen.
- **Save size limits.** Some browsers limit the size of one canvas: Safari stops near 16.7 million
  pixels, so an A3 poster at 300 dpi is too big. When the chosen quality is too big for the
  browser, Save says so and offers the largest that works (for example 240 dpi). The picture is
  never cut short and nothing fails without a message.
- **Drafts: IndexedDB**, not `localStorage`, because drafts hold photos.
  - Each draft stores the model, the working copies and the originals.
  - It is saved half a second after each change. The top bar says "Draft saved in this browser",
    or plainly why it was not (storage full, private window, storage blocked).
  - The page asks the browser to keep the storage (`navigator.storage.persist()`). The start screen
    shows how much space drafts use.
  - At most 30 drafts, with the oldest named when the limit is reached; nothing is deleted
    without asking.
  - A draft from an older `version` of the model is upgraded when opened.
- **Fonts**: open-licence (OFL) `woff2` files in `wwwroot/fonts/collage/`, each with its licence:
  - Latin:
    - Mulish (already shipped);
    - Playfair Display (elegant);
    - Cinzel (engraved, capitals only);
    - Pinyon Script (formal script);
    - Dancing Script (script);
    - Caveat (handwritten);
    - Bebas Neue (poster);
    - Courier Prime (typewriter).
  - Rounded: Baloo 2, which has both Latin and Devanagari.
  - Hindi:
    - Kalam (handwritten);
    - Tiro Devanagari Hindi (formal);
    - Noto Sans Devanagari (clean).

  Fonts load only when Collage Studio opens. Hindi is typed with the Windows Hindi keyboard or pasted.
- **Stickers**:
  - Our own drawn stickers: ribbon, rosette, washi tape, speech bubble, arrow, burst, sparkle,
    heart, wheel, laurel, and the "Thank you" and "Best wishes" stamps. Drawn ones can be recoloured.
  - A chosen set of emoji drawings from **Twemoji** (graphics CC BY 4.0), bundled as SVG files in
    `wwwroot/img/collage/stickers/`. They then look the same on every computer and print sharply.
  - The size of the set is measured and stated in PR 8.
  - Each sticker has search words. The panel loads a group's files only when the group opens.
- **Credits**: a "Credits" link on the start screen lists every bundled font, sticker set and
  library with its licence. CC BY needs this attribution; OFL and MIT ask for the licence to go
  with the files. [../collage-studio.md](../collage-studio.md) lists the same.
- **Textures** are drawn in code (collage-draw.js), not shipped as pictures: nothing to license, and
  as sharp in a print-quality save as on the screen (changed from pictures in PR 9):
  - paper;
  - kraft;
  - linen;
  - wood;
  - chalkboard;
  - cork.
- **No server change.** No database, no migration, no upload handler. The only .NET code is the
  page and its tests: signed-in only, the page renders, no form posts.
- **Production**: additive only. A new page, new static files and a menu entry; no setting and no
  change to any existing route. Rolling back is removing the page and the files. The bundle loads
  only on `/Tools/Collage`.

## Build order (small PRs)

Each PR leaves a working tool. Each is checked in the browser at desktop and phone sizes before it
is opened.

| PR | What | Done when |
| --- | --- | --- |
| 1 | This plan and the approved mockup | Merged |
| 2 | **Page and editor core**: `/Tools/Collage`, Konva vendored, start screen (blank sizes), editor shell (top bar, rail, stage, right panel), page sizes, add photos (choose, drop, paste) with working copies, select, move with snapping, resize, turn, duplicate, order, delete, undo and redo. Not linked from anywhere yet: a collage cannot be saved until PR 4, so the page says it is being built | A user can make a free collage of turned photos on any page size |
| 3 | **Layouts**: the 11 grids (also on the start screen), space and rounded corners, Make it casual, Straighten all, Shuffle, side handles for the frame's shape, move and zoom inside the frame, Replace photo | A neat report grid and a tilted casual one are both easy |
| 4 | **Save as…**: PNG, JPEG under a size, PDF at the paper's true size; print or screen quality, drawn from the originals; the canvas-size limit handled. **Collage card on the Photo Tools home and in the Tools menu**, now that a collage can be kept | The saved file matches the screen at full quality |
| 5 | **Drafts** in IndexedDB: autosave, the start-screen list, open, delete with Undo, space used, plain messages when storage is blocked or full | Closing the browser loses nothing |
| 6 | **Photo finish**: frames (white border, polaroid with caption, taped, torn edge), shadow, shapes, looks, brightness, contrast, warmth, mirror (on screen and in Save) | Every frame, shape and look saves exactly as shown |
| 7 | **Text**: the bundled fonts with Hindi, size, colour, bold, italic, alignment, spacing, outline, shadow, glow, label, curve; ready-made English and Hindi headings | Hindi headings render correctly on screen and in Save |
| 8 | **Stickers**: the drawn stickers and the Twemoji set, groups, search, recolour, Credits | Stickers look the same on every computer |
| 9 | **Backgrounds**: colours, gradients, patterns, textures, a blurred photo | Every background saves as shown |
| 10 | **Arrange**: snap guides, line up on the page, layers (hide, lock, order), floating toolbar, keyboard shortcuts, Preview | Everything in the mockup's editor works |
| 11 | **Phones, Dark theme, keyboard and screen readers**: bottom rail and sheets, pinch to zoom on the page, focus order, labels | Usable on a phone; Dark theme tokens only |
| 12 | **Docs and close-out**: `docs/collage-studio.md` (what it does, privacy, limits, credits), the README index, `docs/current-state.md`, a first-time-user pass | Plan complete |

Later, each its own decision:
- themes;
- server-saved and shared collages;
- unit crests and logos as stickers;
- the Photos library as a source;
- selecting several items to move or group them.

## Tests

- **Node** (`npm test`): the pure functions in `collage-core.js` listed above.
  - Each later PR adds tests for its own functions:
    - Save size and dpi limits;
    - draft upgrade;
    - frame padding per frame;
    - sticker search words.
  - The existing guards apply. There is no browser `confirm()`; confirmations use PrismConfirm,
    and Undo uses PrismToast.
- **.NET**: the page needs a signed-in user, renders, and has no POST handler.
- **In the browser**, on each PR:
  - make, save and reopen a collage at desktop and phone sizes;
  - check that the saved file matches the screen.
