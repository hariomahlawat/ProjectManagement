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
  - If the user's PRISM session has ended (an answer of 401, or the sign-in page instead of the
    answer), the dialog, the page's message for photos being read, and the Crests and logos group
    say "Your PRISM session has ended. Sign in again in another tab, then try again. Your collage
    is kept in this browser." No more ticked photos are asked for, and the collage and its draft
    are untouched. A PRISM failure (for example 500) gets the plain "cannot be read right now"
    message. `tests/browser/collage.session.spec.js` covers both.
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
  and Banner 3:1. Drafts are listed below, newest first, with Delete (shown on hover or focus, always on
  touch screens) and a 10-second Undo in PRISM's glass toast.
- **Invitation cards** ([ux/invitations-posters.md](ux/invitations-posters.md), PR 5).
  - **Start screen:** "Make an invitation card" shows each ready-made design drawn at the chosen
    **Card size** (A5 at first; any size from the Cards group). Each picture is drawn the way Save
    draws. Clicking a design opens the card in the editor, with the SDD logo and realistic
    wording, every part editable.
  - **Card panel:** a card has a **Card** button at the top of the rail, which opens **Card
    details**. It has:
    - the designs, with the current one pressed;
    - a field for each part of the wording: occasion (with quick picks such as Raising Day Dinner,
      Barakhana and Change of Command), host, invitation line, guest's name (empty leaves a line
      to write it on), the words before the occasion, date and time, venue, dress (with quick
      picks), R.S.V.P. and the note at the bottom.
  - **Typing:** typing in a field changes the words on the card at once. They're kept in the
    draft, and recorded for Undo when typing pauses, as on the page. Words typed on the card show
    in the field the next time the panel is drawn. A field emptied takes its words off the card;
    filled again, they come back in the design's place. Words set by their left or right edge
    keep that edge as they grow.
  - **Changing the design** keeps every word, replaces only the design's own parts, keeps
    anything the user added, and offers Undo.
  - **Title:** a card's automatic title is its occasion ("Raising Day Dinner card").
  - A plain collage has no Card button.
- **Card sizes** ([ux/invitations-posters.md](ux/invitations-posters.md), PR 4). The page size
  menu (top bar and Page settings) groups every size: **Pages and screens** (the seven above),
  **Cards** and **Pocket cards**. Every card size is a true paper size, both ways round:
  - cards: A5, A6, 5 × 7 in, 4 × 6 in, DL, the 4 × 5 in service correspondence card, and 5 in
    and 6 in squares;
  - pocket cards: A7, the 3.5 × 5 in RSVP card, and the visiting-card entry card or car pass.

  Each saves at 300 dpi, and its PDF page is the paper size. The start screen's blank pages are
  still the seven pages and screens.
- **Custom size** (PR 5b): **Custom size…** in the start screen's Card size menu, or at the end of
  the page size menu, takes a size typed in mm, 50 to 300 a side, to a tenth of a mm.
  - In the editor, it opens Page settings, which always shows the width and height fields.
  - A size out of range is refused with the reason.
  - The size is kept as `mm:<width>x<height>` (for example `mm:120x180`). It's read back from
    drafts, listed under **Custom** in the menus, saved at 300 dpi, and its PDF page is that paper
    size.
- **Words that fit a width** (PR 4): a text item may carry a `maxWidth`. Its words then wrap onto
  new lines within it, up to `maxLines` (3 unless set), and if they still don't fit, the letters
  get smaller, down to 40% of the size. The words typed never change, only how they're laid out.
  Curved words aren't fitted. Dragging a corner of fitted words makes the width grow with them.
  The card designs (PR 4b) set this, so a long occasion never runs off a card.
- **Photos**: add up to 60 photos (JPEG, PNG, WEBP, GIF or BMP, at most 50 MB each) by the
  button, by dropping them, by pasting, or with **From PRISM Photos** (search by words, project
  and year, tick the photos, Add). Empty frames fill first. A photo the page does not show can be
  taken out of the collage with the × on its tray picture (Undo of its frame brings it back);
  one file that cannot be read is named and the rest go in. A photo can be turned to any
  angle, resized, mirrored, given an optional caption (on a white strip under the photo, or
  handwritten in a polaroid's edge), a frame (white border, polaroid, tape, torn
  edge), rounded corners, a shape (oval, heart, star, hexagon, arch), a shadow and a look (black and
  white, sepia, vintage, vivid, soft, cool, warm), and moved inside its frame (**Adjust**, or double-click); while it is moved, the rest of the
  photo shows faintly round the frame, so the user sees what can be brought into view.
- **Casual or neat**: one button scatters the photos at slight angles as polaroids and taped
  prints (a frame or shape the user chose is kept, and the photo is only tilted), with Undo on
  the screen; another straightens everything back. Each print stays inside its own frame's box and
  on the page. Shuffle swaps the photos between frames.
- **Text** in English and Hindi with bundled fonts: ready-made headings (Farewell, Happy
  Birthday, Thank you!, Congratulations and others), curved text, outline, shadow and a label
  behind the words. Hindi is typed with the Windows Hindi keyboard or pasted.
- **Stickers**: first **Crests and logos**. It starts with PRISM's own **SDD logo** (`logo:sdd`,
  drawn from `wwwroot/img/logos/sdd-hd.webp`, shipped with PRISM, PR 4), then the crests an
  administrator uploaded (a retired one still draws in drafts that use it); a line under them says
  when there are none, or why they could not be read. Then **Lines and borders**, then
  twelve drawn shapes and stamps that take any colour, and emoji drawings in eight groups with a
  search box.
- **Lines and borders** ([ux/invitations-posters.md](ux/invitations-posters.md), PR 3): a line,
  a thin gold rule, a page border, a double border (two borders in one step), a band, a
  rectangle, an oval and a circle. Each is a `shape` item: unlike a sticker it **stretches
  freely** from every handle, corners included. Its settings choose Rectangle, Oval or Line, the
  line colour and thickness (0 to 40), and a fill (rectangles and ovals; "None" for an outline).
  A rectangle takes rounded corners. The stroke is drawn inside the item's box, so the handles
  and the saved picture agree. A border or band takes no room when a new item looks for free
  space, so words and stickers still land in the middle of the page. A line's box is 36 page
  units tall, so it's easy to grab, with the line drawn across its middle.
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
  layout leaves out the frames they cover more than half of (the question says how many). A kept item
  well off the page is moved just inside it. A stopped action on a locked item says so on the screen.
- **Undo and Redo** for every change. Words are measured for drawing without changing the collage; their size
  changes only inside a change (typing, a setting, resizing), and a font that arrives later gives
  them their size as one recorded change, kept in the draft. Moving **Space between photos** puts every photo back in
  the layout; when that undoes a photo moved by hand, a message says so, with Undo. One frame's
  own corners (its settings) stay its own: the layout's Rounded corners and the next layout are
  not changed by them, and a change of space keeps them.
  Undo steps are not kept with a draft: a reopened draft says that Undo starts again from there.
- **Save as** PNG, JPEG (optionally under a size in KB) or PDF whose page is the paper's real
  size; at print quality (300 dpi) or for the screen (1920 px on the long side). The dialog asks
  first **what it is for** (WhatsApp or email: a JPG for the screen; Printing: a PDF at print
  quality; A slide or a screen: a PNG for the screen). The chosen purpose shows pressed, with a
  tick, and a line under it says what Save will make. The type, the quality and "Keep it under"
  are under **More options**, closed at first; once opened it stays open next time (both choices
  are remembered in this browser only). A type or quality changed there is what Save makes, and
  then no purpose shows pressed. Tab stays inside the dialog, and on a phone it fits one screen
  with More options closed. Save reads each
  original photo again at the size its frame needs, so the saved picture is sharper than the
  screen; empty frames and hidden items are left out, and Save says how many ("2 hidden items
  left out"). In the editor a hidden item shows as a faint dashed outline, never in Preview or
  Save. The purposes not chosen keep full-strength words, so they do not look disabled.

## How it is built

| File | What it holds |
| --- | --- |
| `wwwroot/js/collage/collage-core.js` (`window.PrismCollage`) | The collage as plain JSON and every pure rule: sizes, layouts, crop and pan, frames, shapes, looks, text layout, snapping, lining up, Save sizes, reading a draft back safely, undo history |
| `wwwroot/js/collage/collage-draw.js` (`window.PrismCollageDraw`) | Drawing a photo, text, sticker, shape (line, rectangle, oval) and background on a 2D canvas. The editor uses it inside Konva shapes and Save uses it on its own canvas, so **the saved picture always matches the screen** |
| `wwwroot/js/collage/collage-designs.js` (`window.PrismCollageDesigns`) | Ready-made card designs as data ([ux/invitations-posters.md](ux/invitations-posters.md), PR 4b and PR 6). There are six:
  - **Classic ivory** and **Navy and gold**;
  - **Regimental**: three bands in the unit's colours with the logo in a roundel. The host's words, the dress and the rules follow the same colours, set under **Unit colours** in Card details and kept in the card's `colours` detail. Each part that follows a colour records which in `tint`;
  - **Ceremonial photo**: a frame for the user's own photo, `role: "photo"`. A photo put in it moves to the next design's frame with its crop, or stays as the user's own when that design has none. An emptied frame stays the design's;
  - **Bilingual**: Hindi and English side by side, with `hostHi`, `inviteHi` and `occasionHi`. Card details shows those fields only for this design; it shows only the fields the current design uses;
  - **Modern**.

   Each has a portrait and a landscape arrangement, with parts placed by fractions of the page and sized by the card's scale (`unitOf`: 1 on A5, following the page's area, at most 1.3× the shorter side). `applyDesign(collage, key, details, { measure, newId })` makes ordinary items: borders and rules (`shape`), the SDD logo (`logo:sdd`), and words that fit a width, each linked to its `field` and marked with a `role`. It replaces the parts of a design applied before and keeps the user's own items in front. `detailsOf` reads the wording back, with words typed on the card winning. The card's design and wording are kept in the collage's `card` field, which drafts read back. The start screen and the Card panel offer them (PR 5, `collage-card.js`) |
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
| `wwwroot/js/collage/collage-model.js` | The one way the collage changes: the collage is frozen, and `edit`/`editItem` copy it, make the change on the copy, freeze the result and tell the change log how to keep it (a finished change, one still going on, a quiet one, or none yet while something is dragged) |
| `wwwroot/js/collage/collage-state.js` | The editor's values as the parts see them: `state` reads them and cannot set them (an assignment throws), each part is handed only the named changes it owns (the tray chooses a photo it placed with `choose(id)`; the page sets its own zoom and scale), and `parts` reaches the parts made later |
| `wwwroot/js/collage/collage-messages.js` | The live line for screen readers, messages in the toast stack, and the busy line |
| `wwwroot/js/collage/collage-start.js` | The start screen: blank pages, layouts and their small drawings |
| `wwwroot/js/collage/collage-toolbar.js` | The selected item's toolbar (placed where it can be seen, clear of the item) and the angle shown while it turns |
| `wwwroot/js/collage/collage-add-panels.js` | The Text, Stickers and Background panels, with their own buttons |
| `wwwroot/js/collage/collage-actions.js` | The actions: toolbar and menu buttons, Undo and Redo, delete with its Undo offer, page size, and the clicks the panels do not answer |
| `wwwroot/js/collage/collage-app.js` | The editor itself: its values, selection, moving a photo in its frame, the change log, and creating the parts above. Each part is handed the libraries it uses, `state`, its own named changes and its helpers in a few groups (`ui`, `doc`, `history`, `messages`, `surface`, `selection`, `screens`), each read when used, so a part made before another can still reach it; no part is handed more than twelve names (a unit test checks) |
| `wwwroot/css/collage.css` | All styles, Bootstrap variables only (Light and Dark) |

Photo handling keeps memory low: the editor draws a working copy at most 2000 px on its long side,
the tray shows a 240 px thumbnail, and the original file is kept only for Save, which reads every
original again at full quality. All working copies together stay within
`core.PHOTO_LIMITS.workingPixels` (60 million pixels, about 240 MB): up to 15 photos keep the full
2000 px, and beyond that each copy gets an equal share (`core.budgetedSize`), about a million
pixels each at 60 photos (1,154 by 866 for a 4:3 photo). A new batch is decoded at the share for
the count after it; copies made while there were fewer photos are first drawn again smaller from
themselves, with no new decoding (`fitBudget` in `collage-photos.js`). Copies are not made larger
again when photos are removed. A photo removed from the tray is let go (its canvas emptied) as
soon as no Undo or Redo step can bring it back: checked after every recorded step, since a new
step drops the Redo steps and, past 100 steps, the oldest one. A draft that turns out damaged
lets go of the photos already read for it. These sizes were worked out, not measured on a phone.

Each layer of the page is a canvas of at most 16 million pixels at any zoom
(`core.canvasPixelRatio`); iPhones draw nothing on a canvas over 16.7 million, so zoomed far in on
a phone the page turns a little softer instead of blank. The stage grows in steps of 128 pixels, so
a pinch makes new canvases now and then rather than at every move. The draft's small picture for
the start screen is made at most every five seconds while editing, and always on leaving. Save
lets go of its picture, and of every smaller copy made to fit a JPG size, even when it fails.

Drafts are saved half a second after a change. Opening another draft, going back to the start
screen or leaving waits for the last write, and a draft that cannot be written says why (for
example, the browser is out of room). A change still being written when leaving is never dropped
silently: the editor asks first. If the browser closes the drafts database, the next save opens
it again. The browser keeps at most 30 drafts; the 31st asks to
delete the oldest.

A draft keeps the parts it does not understand
([ux/invitations-posters.md](ux/invitations-posters.md), PR 2). An item of a kind this page
does not draw (a newer PRISM's shape, for example), a sticker it does not know, and any field of
the collage it does not use (a card's back page) are held aside by `core.readDraftModel` in
`kept`. They're never drawn, and `core.draftModel` writes them back into the draft unchanged
whenever the draft is saved here. Each goes back just under the first item that was above it and
is still there (on top when none is), so deleting items here never moves it in front of what it
sat behind. A text item's font that this page doesn't
ship keeps its name and draws in the clean font. On opening, the toast says how many parts were
"made by a newer PRISM" and are kept but not shown. A photo named only inside a kept part stays in
the draft. The model version stays 1 for such additions; a draft whose version is higher is still
not opened. Only names a PRISM could write are kept (short, lower case, no markup), up to 500
items and about 2 million characters.

### Keys

| Key | Does |
| --- | --- |
| Arrow keys | Move the selected item 1 unit; with Shift, 10. While adjusting, move the photo in its frame |
| Delete or Backspace | Delete the selected item (not when locked), when the page or the item's own Layers row has focus; on any other button or control the key does nothing. A photo in a layout leaves its frame empty; Undo is offered on the screen |
| Ctrl+D | Duplicate |
| `]` / `[` | Bring forward / send backward; with Shift, to the front / back |
| Enter | When the page has focus: adjust the selected photo, or edit the selected text. On a focused button, Enter presses that button |
| Ctrl+Z, Ctrl+Y (or Ctrl+Shift+Z) | Undo, redo |
| Ctrl+S | Save as… (also while typing in a box) |
| Shift while turning | 15° steps |
| Esc | Leave adjusting, deselect, or leave Preview |

A keyboard user picks an item in the **Layers** list (empty "Add photo" frames too: they are
reached there, not by Tab on the page, which is one canvas; Add photos fills them first); the page then says how to move it. Photos
and empty frames have a **Size** slider, so everything can be resized without a mouse. When a
panel is drawn again after a change, focus goes back to the control that was used, inside that
panel (`collage-focus.js`). Arrow keys do not move a hidden item. The editor's first Tab stop is
**Skip to settings**, which takes the keyboard past the top bar, the panels and the page straight
to the settings panel.

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
clicking Bold are two Undo steps. A step that changes nothing is not recorded and does not count
as a change (an Undo message stays good). Closing the tab right after typing
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
where it can be read, and Save leaves it out and says so. Leaving a collage lets go of every
photo's working copy at once, and so does a photo read for a collage that was left meanwhile.
Photos are decoded straight at their working size, and a picture over 250 million pixels is refused before it is read.

### First use

The start screen shows one row of drafts (with "Show all") so the layouts for a new collage are
in view. A collage nobody named is called by the first line of its first text (not the words
that only show where to type, such as "Your heading"), or by when it was started; the first time
its words name it, a short message says so. A frame goes with the Square shape only: with another
shape the Frame row shows None, and the frame chosen before comes back with Square. Photo
backgrounds are named by their file. A photo goes
into the empty frame closest to its shape (a batch is matched tall to tall and wide to wide); photos past a layout's frames wait in Photos, with a
message, rather than sit loose on the grid. Several photos added to a page with no layout are laid
out in a neat grid, each fully in view. New words and stickers go where they cover nothing
yet (covering other words or stickers counts far more than covering a photo; words that land on a
photo get a white outline so they can be read), and the item's toolbar moves below it when above would cover another item. Save asks what the picture is
for: WhatsApp or email (JPG for the screen), printing (PDF at print quality), or a slide or a
screen (PNG). The purpose chosen is shown pressed and remembered in this browser; the first time
it is WhatsApp or email.

Messages (a photo that did not fit, a file that could not be read, a full layout) show in PRISM's
toast stack at the bottom, with the Undo offers, never over the page in Preview. Each goes after a
few seconds or at the user's next change. In the editor the stack moves up out of the way of the
selected item's toolbar and the phone's bottom bar (`collage-messages.js` sets
`--clg-toast-lift`); other PRISM pages keep it where it is. An Undo offer goes once its step is
undone, by the toast, Ctrl+Z or the Undo button. The editor's toasts close when the user goes back
to the start screen, and wait dimmed behind an open dialog.

### Phones and the Dark theme

At 820 px wide and below, the panels move to a bar at the bottom. A panel, or the selected item's
settings (the **Settings** button on the toolbar over the item), opens as a sheet over the lower
half with a close button, and the page fits in the space above it (measured from the sheet, so a
short sheet leaves a bigger page; at 390 px the page is about 374 px wide). The selected item's
toolbar is docked in a strip kept free at the top of the work surface, so it never covers a photo,
and the handles are small with a wider area to touch. Adding words shows the Words box without
opening the phone's keyboard over the page, and the Hindi hint speaks of the phone's own keyboard. The bottom bar's **Layers**
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
  script). Since the third review: a crest from Crests and logos drawn and saved
  (`collage.crest.spec.js`), and the policy check checked itself (`collage.csp.spec.js`). Since
  the fourth review (`collage.r4tests.spec.js`, `collage.r4tests.phone.spec.js`): two fingers
  zoom the page on a phone while one finger moves an item; a drag moves a photo in its frame, one
  Undo takes it back, Done ends it; Back to the start, a hidden page and `pagehide` each write a
  change the half-second autosave has not; closing asks only while a change is unwritten; and a
  draft deleted in another tab is written back at once, whole, only when it is open here. The
  unit test `collage-every-change.test.js` checks that a real collage cannot be written in place
  (crop, layout, background, items) and that `edit()` leaves every earlier Undo step as it was. They
  run on every pull request, with the page served under PRISM's own Content Security Policy
  (`tests/browser/fixtures/production-headers.json`, taken by `CollageBrowserFixtureTests` from
  the page itself, signed in, under Production settings); a policy violation, seen as a
  `securitypolicyviolation` event or a console message, fails the test. One is allowed, in the
  damaged-photo test: Chromium reports `connect-src blob` when a `blob:` picture fails to decode
  in an `<img>` (how `collage-photos.js` reads a photo's size), with no script behind it.
  Production sends the same policy and shows the same line; nothing the page needs is blocked.
- `ProjectManagement.Tests/Tools/CollageApiHostTests.cs` runs the real PRISM host: each of the
  three Collage APIs answers an anonymous request with 401, every `/api/collage` route requires
  authorization, and the library lists each kind of photo at its own page's address.
- `ProjectManagement.Tests/Admin/CollageStickersAdminHostTests.cs` runs the real host too: a
  signed-in user who is not an administrator gets 403 from each change on Administration › Crests
  and logos (Add, Rename, Move, Retire, Restore), even with a valid antiforgery token; an
  administrator gets past authorization; and an upload over the request limit gets the page's
  plain message and an audit entry.
- `ProjectManagement.Tests/Tools/CollageStickerTests.cs` (including the audit before and after,
  every kind of refused upload, the pixel limit, the second count under the lock in Add and the
  count in Restore, the missing-file warning once an hour, and the left-over-file log line),
  `CollageStickerCommitTests.cs` (on SQLite: a COMMIT that fails after the row was saved keeps
  the picture; one that fails before removes it), `CollageLibraryPhotosTests.cs`,
  `ProjectManagement.Tests/MediaLibrary/MediaLibraryProjectFilterTests.cs`, and
  `CollageStickerPostgresIntegrationTests.cs` on a real PostgreSQL (two uploads at once never pass
  60; a failed audit write rolls the change back and removes the picture; Move, Retire and Restore
  each change the rows with their audit entry, and a failed audit rolls each back), run in the
  database migrations workflow.
- `CollageModelSnapshotTests.cs`: the Collage tables in the EF model match the model snapshot
  (only Collage tables, since the snapshot lags the model elsewhere in the repository).

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
  ago appears once the catalogue has taken it in, as on `/Photos`. Videos are not offered, and
  its Projects filter lists only projects that have photos (`MediaLibraryQuery.ProjectsOfKindOnly`;
  the PRISM Photos page itself still lists every project with media).
- Themes (ready-made Farewell, Picnic and similar designs) are not built. Ready-made invitation
  cards and posters are planned in [ux/invitations-posters.md](ux/invitations-posters.md).

## Administration › Crests and logos

`Areas/Admin/Pages/CollageStickers`, under Reference lists, for the "Manage crests and logos"
permission (`Admin.CollageStickers.Manage`, Admin only). An administrator adds a PNG, JPEG or WebP
of up to 5 MB with a name and optional search words, moves stickers earlier or later, renames,
retires and puts them back. `Features/Collage/CollageStickerService.cs` checks the file with
`PrismImageCodec`, re-encodes it as a PNG of at most 1024 pixels (keeping a clear background),
writes it under the upload root in `collage-stickers/`, and saves each change with its admin
audit entry in one transaction, under one PostgreSQL advisory lock so the limit of 60 holds when
two administrators work at once. The table is `CollageStickers` (migration
`20270104080022_AddCollageStickers`, and in the model snapshot since 16 Nov 2026, so a generated
migration does not create it again; `CollageModelSnapshotTests` checks that every Collage table
in the model matches the snapshot); the pictures are covered by the upload-root backup.

**Path base.** Sticker picture addresses are built with the request's path base: in the
`/api/collage/stickers` list, on this page's thumbnails, and in the editor, which reads the base
from the page's `data-app-base` (`Url.Content("~/")`) in `collage-library.js` and
`collage-stickers.js`. The Collage APIs and PRISM Photos addresses are read under the same base.
At the site root every address is unchanged. Whether the deployed PRISM uses a path base has not
been verified; confirm it on the server before relying on a sub-path.

**PRISM Photos paging.** The picker asks for at most page 200 (`CollageApi.LibraryMaxPage`, 9,600
photos); a higher page number is read as 200, and the library turns a page past its end into its
real last page.

What the audit log records (Administration › Logs, under Reference lists):

| Action | Before | After |
| --- | --- | --- |
| `CollageStickerAdded` (added) | none | name, search words, place, retired |
| `CollageStickerUpdated` (renamed) | name, search words, place, retired | the same, changed |
| `CollageStickerReordered` (moved) | `Order`: every sticker whose place changed, with its old place | `Order`: the same stickers with their new places |
| `CollageStickerRetired` / `CollageStickerRestored` | name, search words, place, retired | the same, changed (put back goes to the end) |
| `CollageStickerRefused` (upload refused: not a PNG, JPEG or WebP, over 5 MB or over the request limit, too many pixels, unreadable, or refused by the scanner; or an upload or a put back refused because 60 are already in use) | none | name, file name and size, and the reason; outcome Refused, level Warning |

A refusal changes nothing, so if its audit entry cannot be written the administrator still sees
the plain message (not an error page) and the failure is in the server log as an error. A
refusal for the limit of 60 is audited after its transaction, so it cannot break it.

A move renumbers the picker from 1, so its one entry lists the moved sticker, its neighbour and
any other sticker whose number changed. Moves recorded before 16 Nov 2026 are
`CollageStickerUpdated` entries without a before state.

If an upload scanner (`IVirusScanner`) is registered and refuses the picture or fails, nothing is
stored: the administrator sees "The picture did not pass the upload check" and the refusal is in
the audit log and the server log. PRISM registers no scanner by default.

The page's request limit is 6 MB. An upload whose declared size is over it is refused before the
body is read (`IndexModel.RefuseOversizeUploadAttribute`, after the authorization check), with
the same "larger than 5 MB" message and audit entry, instead of the framework's bare 413.

If COMMIT fails after it was sent, the row may still have been saved (a connection dropped on the
way back). The new picture is then deleted only when a fresh database context shows no sticker
uses it; otherwise it is kept and logged.

Server log lines worth knowing:

- a warning when a sticker's picture file is missing (the sticker then draws nothing), at most
  once an hour for each sticker;
- an information line with the storage key each time a picture is written, before its row is
  saved; and after each successful upload, a warning naming any picture file in
  `collage-stickers/` over an hour old that no sticker uses. Such a file is left when the process
  stops between writing the file and saving the row. Nothing deletes it; an administrator can.
