# PDF Tools

PDF Tools is a standalone, iLovePDF-style workspace for editing a user's **own** PDFs. It is
not part of any PRISM record: PRISM only links to it. It opens in a new browser tab from the
navigation drawer (**PDF Tools**) at `/Tools/Pdf`.

## Privacy and data contract

- Every operation runs in the user's browser. Files are read with the File API, changed with
  pdf-lib, rendered with pdf.js and downloaded with a `blob:` link.
- Nothing is uploaded, stored, logged or retained by PRISM. The page model
  (`Pages/Tools/Pdf/Index.cshtml.cs`) has only `OnGet`; there is no form post, API endpoint,
  database table or migration.
- The page requires a signed-in user (`[Authorize]`), like the rest of PRISM.
- Usage telemetry records only that the `pdf-tools` module was opened (`ErpUsageModuleCatalog`),
  never file names or contents.

`ProjectManagement.Tests/Tools/PdfToolsPageContractTests.cs` guards these points: sign-in
required, no handler other than `OnGet`, and all assets local. Every file of the tool (each
script in `wwwroot/js/pdf-tools`, so a new one is covered without editing the test, the shared
ZIP writer, `pdf-tools.css`, and the page and its partials) is checked for addresses on another
site, network channels (`fetch`, `XMLHttpRequest`, `sendBeacon`, `WebSocket`, `EventSource`,
CSS `@import` and remote `url(...)`), and code loaded from a literal address: `import()`, the
worker and `importScripts` take only the addresses the page gives, which are this application's.

## Offline deployment

All browser assets ship in `wwwroot` (recorded in `libman.json`); no CDN or Internet access is
needed at runtime:

| Library | Version | Licence | Path |
| --- | --- | --- | --- |
| pdf-lib | 1.17.1 | MIT | `wwwroot/lib/pdf-lib/1.17.1/dist/pdf-lib.min.js` |
| pdf.js (legacy build, broad browser support) | 4.10.38 | Apache-2.0 | `wwwroot/lib/pdfjs/4.10.38/legacy/build/` |
| pdf.js character maps (CMaps) | 4.10.38 | Apache-2.0 | `wwwroot/lib/pdfjs/4.10.38/cmaps/` |
| SortableJS (existing) | 1.15.2 | MIT | `wwwroot/lib/sortable/1.15.2/` |

The CMaps let pdf.js render and extract text in fonts that use predefined character maps
(common in Chinese, Japanese and Korean PDFs). pdf.js fetches them from PRISM itself, so
`Program.cs` maps the `.bcmap` extension for static files; without that mapping they would
return 404 and such text would be missing from thumbnails, the editor and exported images.

pdf.js is an ES module (`.mjs`), served by ASP.NET Core's default content types. It runs with
`isEvalSupported: false`, so it works under the site's strict Content-Security-Policy
(`script-src 'self'`) with no exception.

## Tools (Phase 1)

| Tool | What it does |
| --- | --- |
| Organise & merge | Combine PDFs; drag to reorder (or use the keyboard, below); rotate, delete, select pages; save all or only the selected pages (extract). Form fields on the kept pages stay fillable. A field whose name an earlier file already uses is renamed "name (2)", so two filled copies of the same form keep their own answers. A page used twice keeps one field with a box on each copy, so both show the same answer (a field built as its own box cannot be shared and is renamed instead). The form's settings (default font and text, resources, "viewer must redraw") come from the files whose fields were kept. |
| Split PDF | By page ranges (`1-3, 5, 8-`), every page, or every N pages. Several outputs download as one ZIP. The file is read once for all the parts. |
| Images to PDF | JPG/PNG embedded as-is (JPEG EXIF orientation respected); WEBP/GIF/BMP re-encoded to PNG. A JPG whose orientation also mirrors it (some front cameras) is drawn by the browser and saved as a JPG again, so it is not flipped. Page size A4/A3/Letter/Legal or image size, orientation and margin. |
| PDF to images | PNG or JPG at 72/150/300 dpi, optional page range; several pages download as a ZIP. A page over 24 million pixels at the chosen dpi (A2 and larger at 300 dpi) is made at the highest dpi that fits, and the result says which pages and at about what dpi. |
| Watermark | Text, position, angle, size, colour, transparency, optional page range. |
| Page numbers & header/footer | Number formats with `{n}`, `{total}` (the last number used: three pages starting at 5 read "Page 5 of 7"), `{date}`; position, first number, skip cover page, header/footer text. A number field left blank uses its default. |
| Reduce size | Makes a PDF smaller in the browser: repeated fonts and pictures stored once, pictures re-saved at the size they are shown (Screen 100 dpi, Standard 150 dpi, Print 220 dpi), plain parts compressed, page thumbnails left out. Text, links, form fields and bookmarks keep working. Optionally **Keep under** a size (500 KB, 1 MB, 2 MB, 5 MB or typed), remembered in this browser. The work runs in a background worker and can be stopped. The result shows a page before and after. Signed PDFs are refused. A file that would not get smaller is never offered as a saving. See "How Reduce size works" below. |
| Sign & annotate | Text, signature (draw, type or upload), image, highlight, box and white-out; drag to move, corner handle to resize, or use the keyboard. Items are flattened into the page on save. While White-out is chosen or used, the screen says that it does not remove the covered text, and saving asks first. On a phone, with Select a swipe on a page scrolls and two fingers zoom; a tool that places or draws keeps the touch. Pages refit when the window is resized or the phone turned, and text is kept on the page. Only the main button or first finger places an item. **Choose another file** asks first when items were added. A page that cannot be drawn says so. |

When pages are dropped (Split, extract, delete), they are removed from the saved file, not just
hidden:

- Form fields whose widgets are only on dropped pages are pruned from the form.
- Every page reference inside annotations (links, form-button GoTo actions, additional actions,
  replies) is re-pointed at the output page, or removed with its action (a whole link) when its
  page was dropped. Named destinations are resolved to explicit ones first, so links keep working.
  The same is done for a page's own actions (`/AA`, on opening or closing it) and for the actions
  of a field's parent fields (`/A`, `/AA`), so such files can be split.
- Article-thread beads are dropped, and any object no longer referenced is deleted before saving.

What Organise, extract and Split keep (owner decision, 13 Nov 2026: keep both):

- **Layers** (`carryLayers`). Each kept file's layer settings (`/OCProperties`) are carried, naming
  the same layer objects its pages use, so a layer that was off (a draft or print-only layer)
  stays off. Merged files' settings are joined: all layers listed, the default view's on, off,
  locked and panel order lists joined. A page used twice gets its own layer objects, listed with
  their on/off state but not shown twice in the layer panel. Alternate views (`/Configs`) are not
  carried.
- **Bookmarks** (`carryBookmarks`). Each kept file's bookmarks are carried, in the order of the
  files' pages, pointing at the page in the new file. A bookmark to a dropped page is dropped,
  unless bookmarks under it still lead somewhere: then it stays as a heading without a link.
  Named destinations are resolved, web links kept, and open or closed, colour and style kept.
- **Page labels** are not carried: after reordering or dropping pages they would number the wrong
  pages.
- As a last check, if any page object other than the kept pages is still in the file, the tool
  refuses to save and says so, rather than leaking content the user removed.

Tests check that a dropped page's text and a dropped field's value are absent from the saved bytes.

All placements honour each page's `/Rotate`, so text and stamps appear upright on rotated
(for example scanned landscape) pages. The annotate editor works in PDF user units, dividing
out the page's `/UserUnit` that pdf.js includes in its viewport, so saved items match the
preview on large-format pages too. Watermark and page numbers divide their size and margin by
`/UserUnit`, so they keep the size in points that was chosen. Coordinates are mapped against the
visible page box (the CropBox clipped to the MediaBox, as pdf.js renders it), not the raw
CropBox. Both boxes may name their corners in any order (`[612 792 0 0]` is valid PDF), so they
are put lower-left first. A `/Rotate` that is not a multiple of 90 is not valid PDF; it is read
as 0, as pdf.js shows it, and Organise saves the page that way. Page ranges accept an en dash,
em dash or minus sign as a hyphen ("1–3", as Word writes it).

### Keyboard

- **Focus follows the screen.** Opening a tool puts the focus on its heading; a file that opens
  puts it on the first control of the workspace; a finished file puts it on "Your file is ready"
  (the next Tab is Download); Keep editing returns to the Run button; Start over to the choose
  button. A keyboard or screen reader user is never sent back to the top of the page.
- **One run at a time.** While a file is being made, the page behind "Working…" is `inert` (out
  of reach of the keyboard as well as the mouse), and a second run is ignored, so pressing Enter
  again on Run, or a double-click, cannot make the same file twice. Focus returns when the work
  ends, and an error is shown only then, so it is read out. The small preview of a finished PDF
  is drawn after the work, not as part of it.
- **Organise & merge, Images to PDF.** Each card is one Tab stop; its own buttons and checkbox
  are for the mouse. Space or Enter picks a card up, the arrow keys move it, Space or Enter puts
  it down, Escape puts it back. In Organise, R turns a page right and Shift with R left, X
  selects it, and Delete (or Backspace) removes it; in Images, Delete removes an image. The
  focus moves to the next card, and each step is announced to screen readers.
- **Sign & annotate.** With a tool chosen, a page can be reached with Tab; Enter or Space places
  the item in the middle of the part of the page on the screen. When a signature or image is
  ready to place, the focus goes to the first page on the screen. An item has the focus once it
  is placed (or with Tab): the arrow keys move it (Shift for bigger steps), Alt and the arrow
  keys change its size, Enter edits text and Delete removes it. Delete, Backspace and Escape act
  only on the item that has the focus, never on a selected item while the focus is elsewhere.
  Escape ends typing and returns the focus to the text item.
- **Signature dialog.** Draw, Type and Upload follow the tabs pattern: one Tab stop, the arrow
  keys (and Home, End) move between them, and each is linked to its panel. Draw needs a pointer;
  Type and Upload work from the keyboard.

### Memory in Sign & annotate and Organise

Pages are drawn when they come near the window and their pictures are released when they are
far from it (`KEEP_MARGIN`), so a long PDF holds only a few full-size pages at a time, not every
page it has shown. A released page is drawn again when it comes back. Organise does the same for
its page thumbnails (`createThumbnailQueue`, about 1200px beyond the window), so merging hundreds
of pages holds only the thumbnails near the screen.

### Phones and screen readers

- On a touch screen a swipe on the Organise or Images grid scrolls; press and hold a card to drag
  it (`delayOnTouchOnly`).
- The grids are lists and each card a list item with its name.
- "Working…" and each step of it are read out from a live region that is always in the page, not
  from inside the overlay that appears.
- Pressing Back while a file is being made keeps the tool on the screen (the address is put back),
  so the work is not hidden under "Working…".
- The small buttons on page and image cards use the card's colours, in Light and Dark.

### How Reduce size works

The plan, with the owner's decisions, is [ux/pdf-tools-reduce-size.md](ux/pdf-tools-reduce-size.md).
The rules are pure functions in the core (`reducePdf` and the functions it calls), tested in Node
in `pdf-tools-reduce.test.js`; the page only re-encodes pixels with a canvas (`encodeImage` in
`pdf-tools-compress.js`).

- **Before any work** the file is read once (`describePdf`): pages, size, and how much of it is
  pictures. A file whose pictures are under a quarter of it is told it may not get much smaller.
  A signed PDF (a signature field with a value, found through the form's field list or through a
  widget on a page and its parents; `/Perms`; or the AppendOnly bit of `/SigFlags`) is refused on
  opening (`isSignedPdf`), as an encrypted one is. An empty signature field alone is not refused.
- **The file is changed in place,** never rebuilt page by page, so bookmarks, page labels, tags,
  metadata, links and form fields stay as they were.
- **Lossless clean-up.** Identical streams, fonts, font descriptors and graphics states are stored
  once (`deduplicateObjects`; pages are never merged). Streams stored without compression are
  compressed with pdf-lib's own Flate (`compressPlainStreams`; XMP metadata stays readable, and a
  stream with `/DecodeParms` but no filter is left alone, since Flate would make them apply). Page
  thumbnails (`/Thumb`) and objects nothing refers to are left out.
- **How large each picture is shown** is read from the page content itself
  (`contentOperators`, `measureImageUses`): `q`, `Q`, `cm` and `Do`, through form XObjects and
  their `/Matrix`, and the page's `/UserUnit`. A picture drawn in several places keeps the
  resolution of its largest use. A picture that is also drawn where its size is not followed (an
  annotation's appearance, a tiling pattern, a Type 3 font's glyphs, a soft-mask group) or on a
  page that cannot be read is not measured, whatever its other uses, and keeps its pixel size (it
  may still be re-saved at the preset's quality). Patterns, glyphs and groups are read for the
  pictures they actually draw, by name in their own resources or, when they have none, in the
  enclosing ones, as viewers do. Each is read once per set of resources it is used with (one glyph
  shared by two Type 3 fonts can draw a different picture in each), so resources that lead back
  to themselves end. Names are matched decoded (`/Big#20Pic` is the picture named "Big Pic").
  An inline picture's data is skipped by its `/L` length when its dictionary has one and `EI`
  follows the data. Otherwise its end is guessed (an `EI` followed by text, not more binary data),
  and because a wrong guess could misread what follows, every picture that content can draw is
  then not measured: a wrong guess keeps pictures at their size and never shrinks them. Forms and
  patterns nested more than 12 deep are not followed; the pictures they can draw are not measured.
- **Which pictures change** (`reduceImagePlan`). Re-saved: 8-bit RGB or grey pictures, stored as
  JPG or with Flate (and LZW, ASCII85, ASCIIHex, RunLength; PNG predictors are undone by
  `undoPngPredictor`). Left as they are, and counted on the result: image masks and soft masks
  (the picture they belong to may be re-saved; the mask stays lossless), JBIG2 and CCITT
  black-and-white scans, CMYK, ICC-based and other print colour, a `/Decode` array or colour-key
  mask, other bit depths, a JPG with `/ColorTransform` (a browser could read its colours
  differently from a PDF viewer), and pictures under 10 KB. A picture whose soft mask has
  `/Matte` keeps its pixel size, because such a mask must stay the picture's size.
- **Re-saving.** A picture larger than the preset needs (`reduceTarget`, kept when within 10%) is
  drawn smaller in halving steps and saved as a JPG at the preset's quality. A grey picture is
  also tried as grey pixels with Flate, and the smaller of the two is kept. A re-saved picture is
  kept only when it is at least 10% smaller than the one it replaces; it replaces it in place, so
  every page that uses it gets the new one.
- **A JPG is read as stored.** Browsers turn a JPG by its EXIF orientation when decoding it, and
  current Chromium does so whatever `createImageBitmap` is asked; PDF viewers (PDFium, Poppler,
  Acrobat) never do. The page decodes a copy whose orientation says "as stored"
  (`jpegClearOrientation`), so an upside-down or mirrored tag cannot turn or flip a photo. A
  picture whose decoded size differs from the PDF's is left as it is.
- **Order of work.** The lossless clean-up runs first (`deduplicateObjects`, thumbnails left out,
  `compressPlainStreams`, unreferenced objects left out); only then are pictures chosen and
  re-saved, so each picture is compared with what is really stored.
- **Keep under a size.** `parseSizeLimit` reads the size (KB or MB; "1,5" is 1.5 and "1,000" is a
  thousand). The file is saved once after the clean-up to find what everything except the
  pictures being re-saved takes; the rest is the pictures' budget. `searchReduceSettings` then
  tries the preset, then searches the quality (down to 0.35) at the preset's resolution, and only
  when that is still too big steps the resolution down (by the square root of the shortfall,
  never below 72 dpi) and searches again. It always reaches 72 dpi before giving up, and goes
  straight there when a step down saved less than 2% (most pictures keep their size). When the
  rest of the file is already over the size, there is no budget to search: the lowest settings
  are tried once. Each try re-saves every picture without putting it in the file; only the
  best fit and the smallest try so far keep their pictures, the others are let go at once. The
  best-looking try that fits is used, or the smallest one when nothing fits. The
  budget is an estimate (a new picture's dictionary and the file's tables add a little), so the
  saved file is checked against the target; when it is over, the overshoot comes off the budget
  and the search runs again, reusing the settings already tried (up to four rounds). The result
  says it is under the size, with the resolution and quality used, or says in amber that it could
  not be made smaller, naming 72 dpi as the lowest only when that is where the search ended, and
  saying how many pictures keep their own size because where they are shown could not be read.
  The size and its unit are remembered in this browser only (`localStorage`,
  `prism.pdfTools.reduce`).
- **In the background.** The work runs in a worker (`pdf-tools-compress-worker.js`), so a large
  PDF does not freeze the page. The page gives the worker the addresses of the very scripts it
  loaded, versioned as loaded (`asp-append-version`), and the worker's own address is versioned
  with `IFileVersionProvider`, so after a deployment the worker never runs older code than the
  page. Pictures are re-encoded with `OffscreenCanvas` (`pdf-tools-encode.js`, shared with the
  page). A browser without workers or `OffscreenCanvas`, or a worker that cannot start, does the
  same work on the page instead; the result is the same file. The worker is a script file on this
  site, as the Content-Security-Policy (`script-src 'self'`, no `worker-src`) requires. The
  worker says it is alive every 5 seconds; a worker the browser ends without a word (out of
  memory) is noticed after 90 seconds of silence, and the screen says no file was made.
- **Stop.** While the work runs, the "Working…" box has a Stop button (outside the inert page, and
  given the focus). It ends the worker at once, or, on the page, stops between pictures
  (`signal`). No file is made, and the screen says so.
- **The result** gives the size before and after, what was done and what was left. When the file
  would not be smaller, no file is made and the screen says so. A file that says it is PDF/A is
  made smaller with a warning that it may no longer meet PDF/A.
- **A page before and after.** The page where the largest re-saved picture is drawn is shown from
  both files at the same size, with a line to drag between them and a 2× zoom. A page whose
  picture is a JPG with its own EXIF orientation is not chosen, because pdf.js in the browser
  turns such a photo and the two would not match (see "Known limits"). The preview is drawn after
  the work, so the page is not kept busy for it.

## Code map

| File | Role |
| --- | --- |
| `Pages/Tools/Pdf/Index.cshtml` (+ `_PdfTool*.cshtml` partials) | Standalone page with its own minimal shell (`Layout = null`). |
| `wwwroot/js/pdf-tools/pdf-tools-core.js` | Pure operations and geometry (page ranges, rotation mapping, EXIF, pdf-lib operations). Runs in Node for tests. |
| `wwwroot/js/tools-shared/zip-writer.js` | ZIP writer, shared with Photo Tools ([photo-tools.md](photo-tools.md)). Loaded before the core script. |
| `wwwroot/js/pdf-tools/pdf-tools-core.test.js` | Node tests, part of `npm test`. |
| `wwwroot/js/pdf-tools/pdf-tools-app.js` | Shell, routing (`#tool`), and the six form-based tools. |
| `wwwroot/js/pdf-tools/pdf-tools-annotate.js` | Sign & annotate editor. |
| `wwwroot/js/pdf-tools/pdf-tools-compress.js` | Reduce size: the screen, Keep under, running the work in the worker or on the page, Stop, the result and the page before and after. The rules are in the core. |
| `wwwroot/js/pdf-tools/pdf-tools-encode.js` | Reduce size: re-encoding one picture smaller with a canvas (`OffscreenCanvas` where there is one), on the page and in the worker. |
| `wwwroot/js/pdf-tools/pdf-tools-compress-worker.js` | Reduce size in a background worker: loads the page's own scripts and runs `reducePdf`. |
| `wwwroot/js/pdf-tools/pdf-tools-reduce.test.js` | Node tests for Reduce size, part of `npm test`. |
| `wwwroot/css/pdf-tools.css` | Styles; Bootstrap theme variables only, so Light and Dark both work. |

## Known limits

- Password-protected or edit-restricted PDFs are refused with a clear message (pdf-lib cannot
  decrypt them).
- Added text uses the standard PDF Helvetica font, so it supports Latin characters only;
  other scripts (for example Devanagari) and symbols such as the rupee sign are rejected. The
  message quotes the text, names the character and the page, and suggests "Rs." for the rupee.
- Existing text in a PDF cannot be edited; white-out plus new text is the supported pattern.
  White-out covers content visually; it does not remove the underlying text from the file,
  so it is not a redaction tool. The screen says so while White-out is chosen or on the page,
  and saving a file with White-out asks for confirmation first.
- Reduce size does not subset fonts or simplify vector drawings, and leaves print-colour (CMYK,
  ICC-based), JBIG2, CCITT and inline pictures as they are; a file made of those stays large.
- Thumbnails and previews are drawn with pdf.js in the browser, which turns a JPG photo by its
  EXIF orientation. A PDF holding such a photo can look turned in PDF Tools while the file itself,
  and other viewers, show it as stored.
- Everything is held in browser memory, so very large files depend on the user's computer.
  Files over 200 MB ask for confirmation first.

## Possible later phases

A **Reduce size** tool is planned in the browser, with no upload
([ux/pdf-tools-reduce-size.md](ux/pdf-tools-reduce-size.md)); it re-saves images and removes
duplicated content, and does not need the server.

Server-side processing would be needed for password protect/unlock (qpdf, Apache-2.0),
Office-to-PDF (LibreOffice) and OCR, and for font subsetting if the browser tool proves
insufficient. These would receive users' files on
the server, so they need size limits, immediate temporary-file deletion, no retention,
antiforgery protection and sandboxed converter processes. Avoid AGPL tools (iText,
Ghostscript) unless the licence has been reviewed.
