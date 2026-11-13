# PDF Tools: Reduce size

A new tool for PDF Tools (`/Tools/Pdf`) that makes a PDF smaller, asked for by the owner on
13 Nov 2026. The durable description of PDF Tools stays in [../pdf-tools.md](../pdf-tools.md);
this document is the plan.

PR 2 is built (see "Build plan"). What was built is described in [../pdf-tools.md](../pdf-tools.md)
("How Reduce size works"); where it differs from this plan, that document is right. The
differences are listed under "Differences found while building PR 2".

## What exists today

- No PDF Tools tool makes a file smaller. Saving through pdf-lib already writes object streams,
  and Split, Extract and Delete leave out objects nothing refers to, but nothing re-saves images,
  which is where most of the size of a large PDF is.
- [../pdf-tools.md](../pdf-tools.md) ("Possible later phases") says real compression would need
  server-side processing (qpdf). This plan does it in the browser instead; see "Why not on the
  server" below.
- Photo Tools already has what a size target needs: a search that finds the best quality under a
  number of KB, and results that never show a bigger file as a saving
  ([../photo-tools.md](../photo-tools.md), "How the target size is reached").

## Owner direction (13 Nov 2026)

| Topic | Decision |
| --- | --- |
| Where it runs | In the browser only. Nothing is uploaded, as in every other PDF and Photo tool |
| Settings | Three presets (Screen, Standard, Print), and an optional size to stay under ("under 2 MB") |
| Scan mode | Offered, as a separate choice with a plain warning, built last |
| Signed PDFs | Refused, with a clear message. No "continue anyway" |
| Next step | This plan, reviewed before any code |

The owner asked for the recommendations below to be used for the open questions (13 Nov 2026):

| Question | Answer |
| --- | --- |
| Preset names | Screen, Standard and Print, each with a short note under it |
| Remember "Keep under" | Yes, in this browser only, as Photo Tools does (PR 3) |
| Leave out attachments | Not now |
| White-out "remove for good" | Decided after Scan mode is built |

## Privacy, offline and deployment

Unchanged from the rest of PDF Tools:

- Everything runs in the browser. No upload, no handler, no endpoint, no table, no migration, no
  configuration key. The page model keeps only `OnGet`.
- No new library. The tool uses pdf-lib 1.17.1 and pdf.js 4.10.38, already shipped in
  `wwwroot/lib`, and browser features (`createImageBitmap`, canvas JPG encoding,
  `CompressionStream`). No CDN and no Internet.
- The site's Content-Security-Policy has no `worker-src`, so a background worker falls back to
  `script-src 'self'`: it must be a script file served by PRISM (as the pdf.js worker already
  is), never a `blob:` worker.
- Usage telemetry records only that the `pdf-tools` module was opened, as now.
- Rollback of each PR is reverting it. No data exists to clean up.
- `PdfToolsPageContractTests` keeps guarding: sign-in required, `OnGet` only, no network calls in
  the scripts, all assets local. A new script file is added to its list.

## Where the size of a PDF goes

| Kind of PDF | What makes it big | What makes it smaller |
| --- | --- | --- |
| Scans and phone photos of papers | Page images at 300 to 600 dpi | Images re-saved at a lower resolution: often 5 to 20 times smaller |
| Word or Excel exports with photos | Photos kept at camera resolution | The same, for the photos only |
| Merged files | The same fonts and images stored once per file | Identical items stored once, with no loss |
| Files saved many times over | Old copies of objects left behind | Objects nothing refers to left out, with no loss |
| Text only | Fonts, which are already small | Very little. The screen says so instead of promising a saving |

## Screen

A new card on the PDF Tools home screen, **Reduce size**, and its own view (`#compress`), built
like the other single-file tools.

1. **Choose a PDF.** The file is read once and a short summary is shown: pages, size, how many
   images and how much of the file they are ("Images are 18.4 MB of this 19.1 MB file"). A file
   that is mostly text says so before any work ("This PDF is mostly text, so it cannot be made
   much smaller").
2. **Choose how small.**
   - Three presets as large buttons: **Screen** (for viewing and email), **Standard** (default;
     good for most forms and portals), **Print** (keeps detail for printing).
   - **Keep under** an optional size, with KB and MB and quick choices (500 KB, 1 MB, 2 MB,
     5 MB), as in Photo Tools' Reduce size.
   - **Scan mode** (PR 4), a separate switch below the presets with its warning (see below).
3. **Result.** Before and after sizes, how much smaller, and Download. A page of the PDF before
   and after, at the same zoom, with the same drag-to-compare control as Photo Tools (PR 3).

The result is honest:

- A file that would come out no smaller is not offered as a saving. The screen says it was
  already as small as it can be made here, and the download is the original, unchanged.
- A size target that cannot be reached says how small it could be made, in amber, as Photo Tools
  does.
- What was changed is listed in one line ("12 images re-saved at 150 dpi, 3 repeated fonts stored
  once").

Phones, the Dark theme and the keyboard follow the rest of PDF Tools, including the rules from
#3177: focus moves with the screen, and one run at a time.

## How it works

### Lossless clean-up (always)

- Identical streams (fonts, images, colour profiles) are stored once: streams are compared by a
  hash of their bytes and dictionary, and references are pointed at one copy.
- Objects nothing refers to are left out (`removeUnreachableObjects`, already used by Split).
- Streams stored without compression are compressed with the browser's `CompressionStream`
  ("deflate", which is PDF's FlateDecode).
- Page thumbnails stored in the file (`/Thumb`) are left out; viewers draw their own.
- Saved with object streams, as every PDF Tools file already is.

### Re-saving images (the main saving)

- **How large an image is shown.** pdf.js lists the drawing operations of each page; with the
  transformation in force at each image, the tool knows the size the image is drawn at, and so its
  resolution there. An image drawn in several places keeps the resolution of its largest use. An
  image inside a form XObject, a pattern or an annotation's appearance is followed there too; one
  whose use cannot be measured is not made smaller than the preset's cap.
- **What happens to it.** An image above the preset's resolution is drawn smaller and re-saved as
  a JPG at the preset's quality. One at or below it is re-saved only when that alone makes it
  clearly smaller. The new stream replaces the old one in place, so every page that uses the image
  gets the new one. Each image is handled once.
- **Starting values,** to be tuned on real files before PR 2 is merged, as the Photo Tools
  background change was:

  | Preset | Resolution cap | JPG quality |
  | --- | --- | --- |
  | Screen | 100 dpi | 0.60 |
  | Standard | 150 dpi | 0.75 |
  | Print | 220 dpi | 0.85 |

- **Left as they are:**
  - Black-and-white scan formats (JBIG2, CCITT): already very small, and a JPG would be bigger
    and blurrier.
  - Image masks and soft masks (transparency): the image itself may be re-saved, but its mask
    stays a separate, lossless mask, so transparency is not flattened onto white.
  - CMYK and ICC-based images: the browser re-saves in screen colour only (sRGB), which shifts the
    colours of print files. Left alone, and counted in the summary.
  - Images written into the page content itself (inline images): there is no object to replace.
  - Small images (under about 10 KB): the saving is not worth the risk.
- **Grey images.** The browser's JPG encoder always writes colour, so a grey scan re-saved as a
  JPG can come out bigger. A grey image is kept grey: re-saved as a grey JPG where the browser
  writes one, otherwise as compressed grey pixels (FlateDecode), whichever is smaller.

### Keep under a size (PR 3)

The Photo Tools search, adapted: first the quality is searched at the preset's resolution, and
only when the lowest quality is still too big is the resolution stepped down, never below 72 dpi.
The search reads the sizes of the re-saved images rather than saving the whole PDF each time, and
the final file is checked against the target before it is offered.

### Scan mode (PR 4)

Each page becomes one picture: pdf.js draws it at the chosen resolution, and a new PDF holds the
pictures at the page's true size. This gives the most predictable result for scanned papers.

It loses selectable and searchable text, links, form fields, bookmarks and tags. The switch says
so before it is turned on, and saving asks once more, as White-out does. It is never a preset and
never on by default.

The same mechanism could later make White-out remove the covered text for good, by turning only
the pages with White-out into pictures. That is a separate decision, not part of this plan.

## What is refused, warned about or kept

| Case | What happens |
| --- | --- |
| Encrypted (password or edit restrictions) | Refused, as the other tools do |
| Digitally signed (a signature field with a value, or `/Perms`) | Refused: "This PDF is digitally signed. Making it smaller would break the signature, so it was not changed." |
| PDF/A or tagged PDF | Made smaller, with a note that it may no longer meet PDF/A |
| Form fields and their answers | Kept |
| Text, links, bookmarks, page labels, tags, metadata (title, author) | Kept (not in Scan mode) |
| Attachments (embedded files) | Kept. A later choice could leave them out, with a warning |
| JavaScript in the PDF | Kept. This tool does not decide what a document does |

The file is changed in place (loaded, its images replaced, saved), never rebuilt page by page:
`assemblePages`, used by Organise and Split, does not carry bookmarks.

## Large files

- Images are handled one at a time, with progress ("Image 14 of 230"), so memory holds one
  decoded image at a time, not all of them.
- From PR 3, the work runs in a background worker (a local script file, see "Privacy, offline and
  deployment"), so the page stays responsive and a Cancel button can stop it.
- The existing confirmation for files over 200 MB applies.
- A page or image too large for the browser's canvas is left as it is and counted in the result,
  never stopped with an error halfway.

## Why not on the server

A server tool (qpdf, Apache-2.0; Ghostscript is AGPL and ruled out) would:

- receive users' files, breaking the promise every PDF and Photo tool makes and the contract tests
  enforce;
- need a native program installed and kept up to date on every LAN server, plus size limits,
  immediate deletion of temporary files, sandboxed processes and antiforgery protection: a
  deployment change that needs approval under the production continuity rules in `AGENTS.md`.

The browser does most of what a server would on the files people need to shrink (scans and
photos). The server would add font subsetting and speed on very large files. That can be
reconsidered if the browser tool proves too slow in use.

## Testing

- **Core functions** are pure and tested in Node with pdf-lib: the resolution of an image from its
  transformation, which images to change, the target-size search, deduplication, the signed-PDF
  check.
- **Each output** is checked: same page count, text still extractable, links and form fields and
  bookmarks kept, every replaced image still drawn, and the file smaller (or the original offered
  unchanged).
- **A set of test files**, built for the tests or from public sources, never real user files: a
  scan, a phone-photo PDF, a Word export with photos, a merged file, a form, a signed file, a
  CMYK print file, a PDF/A file and a text-only file.
- **Browser checks** in headless Chromium, as for #3176 and #3177, including the keyboard.
- The page contract tests are extended for the new script file.

## Known limits (expected)

- Fonts are not subset; a PDF with large embedded fonts stays large.
- CMYK and ICC-based images, JBIG2 and CCITT images and inline images are left as they are.
- Vector drawings (maps, CAD exports) are not simplified.
- Timing depends on the computer; a 300-page scan may take a minute or more.

## Code map (proposed)

| File | Change |
| --- | --- |
| `Pages/Tools/Pdf/Index.cshtml` | Home card and the Reduce size view |
| `wwwroot/js/pdf-tools/pdf-tools-core.js` | Deduplication, signed-PDF check, image selection, resolution from a transformation, target search |
| `wwwroot/js/pdf-tools/pdf-tools-core.test.js` | Tests for each of the above |
| `wwwroot/js/pdf-tools/pdf-tools-compress.js` | New. The screen, reading images with pdf.js, re-saving them, the result |
| `wwwroot/js/pdf-tools/pdf-tools-compress-worker.js` | New in PR 3. The work, off the page |
| `wwwroot/css/pdf-tools.css` | The new view; theme tokens only |
| `ProjectManagement.Tests/Tools/PdfToolsPageContractTests.cs` | The new scripts are local and make no network calls |
| `docs/pdf-tools.md` | Updated by each PR |

## Build plan

Each PR is usable on its own and checked on phones, in the Dark theme and with the keyboard.

| PR | Scope | What the user gets |
| --- | --- | --- |
| 1 | This plan (done, #3178) | Nothing visible |
| 2 | Built. Reduce size tool: lossless clean-up, re-saving images with the three presets, signed and encrypted PDFs refused, honest before and after sizes | Scans and photo-heavy PDFs made much smaller, with text and forms kept |
| 3 | Built. Keep under a size, before and after preview of a page, background worker with Cancel | Files that fit a portal's limit, and large files without a frozen page |
| 4 | Scan mode | The smallest scans, for files where selectable text does not matter |

## Differences found while building PR 2

- **Measuring.** How large a picture is drawn is read from the page content by a small reader in
  the core (`contentOperators`, `measureImageUses`), not from pdf.js, so it is tested in Node. A
  picture whose use cannot be measured keeps its pixel size rather than being capped.
- **Compression.** Plain streams are compressed with pdf-lib's own Flate (`context.flateStream`,
  the bundled pako), not the browser's `CompressionStream`, so the core needs no browser feature.
- **JPG orientation.** Current Chromium turns a JPG by its EXIF orientation even when
  `createImageBitmap` is asked not to. PDF viewers do not, so a photo tagged upside down or
  mirrored would have been saved turned. The page now decodes a copy whose orientation says "as
  stored" (`jpegClearOrientation`); checked against PDFium and Poppler.
- **No file when nothing shrinks.** Instead of offering the original under a new name, no file is
  made and the screen says the PDF is already as small as this tool can make it.

## Differences found while building PR 3

- **Cancel is called Stop**, on the "Working…" box. In the worker it ends the worker at once; on
  the page it stops between pictures. No file is made either way.
- **The worker loads the page's own scripts.** The page passes the addresses it loaded them from,
  versioned, so a worker started after a deployment cannot run older code than the page. Its own
  address is versioned with `IFileVersionProvider`, as the Document Repository viewer's is.
- **Without a worker the page does the work.** A browser without workers or `OffscreenCanvas`, or a
  worker that cannot start, runs the same `reducePdf` on the page; the file is the same.
- **The size search** saves the file once after the clean-up to know the part that does not
  change, and then works from the sizes of the re-saved pictures; the final file is checked
  against the target, and the screen never claims a size it did not reach.
- **The preview** shows the page of the largest re-saved picture, except a JPG with its own EXIF
  orientation: pdf.js in the browser turns such a photo, so before and after would not match.

## Open questions for the owner (answered)

The answers are in the table under "Owner direction".

1. Are the preset names Screen, Standard and Print clear, or would "Small / Medium / High
   quality" read better?
2. Should "Keep under" remember the last size chosen, as Photo Tools' Reduce size does?
3. Should a later PR offer to leave out attachments, with a warning?
4. After Scan mode, should White-out get a "remove for good" choice built on it?
