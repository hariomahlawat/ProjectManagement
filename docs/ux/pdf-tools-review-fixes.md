# PDF Tools: review (13 Nov 2026) and fix plan

After Reduce size PR 3 (#3181), three reviewers read PDF Tools cold:

- **Core:** the pdf-lib operations in `pdf-tools-core.js` (merge, organise, split, images, watermark,
  page numbers, annotations), checked with scripts in Node.
- **Reduce size:** its rules in the core, the screen, the encoder and the worker.
- **Screens:** the page, `pdf-tools-app.js`, `pdf-tools-annotate.js`, the CSS and the contract test.

**Verdict:** no privacy or leak problems. Nothing goes over the network, removed pages and fields
do not reach the saved file, and the fixes from #3176 and #3177 hold. All 61 Node tests pass.
The findings are about phones, unusual but valid PDFs, and the Reduce size measurements.

## Fix first

| # | Finding | Where |
| --- | --- | --- |
| 1 | On a phone, Sign & annotate pages cannot be scrolled or pinch-zoomed: the overlay has `touch-action: none` in every mode, and the pages fill the screen | `pdf-tools.css` `.pdft-overlay` |
| 2 | A page box with its corners in the other order (`[612 792 0 0]`, valid PDF) puts page numbers and watermarks off the page, and annotations mirrored | `visibleBox` / `effectiveBox` in the core |
| 3 | Reduce size: an XObject name with `#xx` (a space, an accented letter) is never matched, so a picture's large use is missed and it can be cut to about 12 dpi | `contentOperators`, `measureImageUses` |

## Medium

| # | Finding |
| --- | --- |
| 4 | Merged forms take `/DA`, `/DR`, `/NeedAppearances` and `/Q` from the first file with a form, even when none of its pages were kept |
| 5 | Split and extract refuse a file with a page `/AA` or an action on a parent field (`unsupported-structure`); nothing leaks, but the file cannot be split |
| 6 | Sign & annotate has no Choose another file or Start over until a file is saved |
| 7 | Organise thumbnails are never released, so a 1,000-page merge holds about 185 MB of canvases |
| 8 | Reduce size re-saves a JPG with `/DecodeParms /ColorTransform`, which can change its colours (plausible) |
| 9 | On a phone, a swipe on the Organise and Images grids may start a drag instead of scrolling (check on a phone) |
| 10 | Found while checking PR 2: the page does not load the PRISM confirm dialog, so every confirmation (white-out, large files, opening another PDF) falls back to the browser's `confirm()` box; Photo Tools is the same |

## Low

- **Sign & annotate:** the toolbar Delete sends the focus to the top of the page; text near the
  right edge can be saved partly off the page; pages are not refitted when the window or phone
  turns; right-click and a second finger place items; a page that fails to draw is blank with no
  message; object URLs of signatures and images are not revoked.
- **Screens:** page-card buttons (`btn-light`) stay white in the Dark theme; cards and grids have a
  name but no role; "Working…" may not be read out because its live region appears already filled;
  pressing Back while a file is made hides the tool under the overlay.
- **Geometry:** a `/Rotate` that is not a multiple of 90 is rounded (pdf.js shows it as 0); watermark
  and page numbers ignore `/UserUnit`; page ranges reject an en dash ("1–3").
- **Forms:** a page used twice gives two fields of the same name that do not share one answer.
- **Reduce size:** inline-image data containing `EI` can end the skip early; past nesting depth 12
  pictures are neither measured nor marked; a stream with no filter but a stray `/DecodeParms` is
  corrupted when compressed; a soft mask with `/Matte` keeps its size while its picture shrinks;
  the Keep under search can stop near 94 dpi while saying 72, keeps every try in memory and still
  runs when the budget is already 0; `/SigFlags` and signature widgets outside `/Fields` are not
  checked; a worker that dies with no error leaves "Working…" up until Stop.
- **Contract test:** looks only for `fetch(`, `XMLHttpRequest` and `sendBeacon` in scripts; it does
  not check the CSS or partials for outside addresses, or a worker or `import()` with an absolute one.

## For the owner

1. **Hidden layers.** Organise and Split do not carry `/OCProperties`, so layers that were off
   (draft, print-only) show after saving. Carry them over (recommended), or say so on the screen?
2. **Bookmarks and page labels** are dropped by Organise and Split. Keep bookmarks that point at
   kept pages (a larger change), or say on the screen that they are not kept?

## Build plan

Each PR is checked on a phone, in the Dark theme and with the keyboard, and adds tests for what it fixes.

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | Merged, #3184. Sign & annotate on phones and in the editor: scroll and zoom, refit on turn, primary pointer only, text kept on the page, Choose another file, toolbar Delete focus, revoke object URLs, show a page that failed to draw | 1, 6, Low (Sign & annotate) |
| 3 | Merged, #3185. Page geometry: corners in any order, `/Rotate` not a multiple of 90 read as 0, `/UserUnit` in watermark and page numbers, en dash in ranges | 2, Low (Geometry) |
| 4 | Reduce size measuring: `#xx` names, inline images by `/L`, depth limit marks unmeasured, JPG `ColorTransform` left as is, `/Matte` masks, stray `/DecodeParms` | 3, 8, Low (Reduce size, measuring) |
| 5 | Forms and split: AcroForm settings from kept files merged, page and parent-field actions tagged, a page used twice | 4, 5, Low (Forms) |
| 6 | Organise and Images: release thumbnails, touch drag delay, card roles, Dark buttons, busy announcement, Back while working | 7, 9, Low (Screens) |
| 7 | Reduce size search and signing: search reaches 72 dpi and says so truthfully, fewer tries kept, no search when the budget is 0, `/SigFlags`, a worker that dies | Low (Reduce size, search) |
| 8 | Contract test covers the CSS, partials, workers and `import()` | Low (Contract test) |
| 9 | The PRISM confirm dialog on PDF Tools and Photo Tools | 10 |
| 10 | Owner decisions on layers and bookmarks | For the owner |

Reduce size PR 4 (Scan mode) in [pdf-tools-reduce-size.md](pdf-tools-reduce-size.md) is separate
and waits for these fixes.
