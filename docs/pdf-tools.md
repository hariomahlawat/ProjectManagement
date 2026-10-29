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
required, no handler other than `OnGet`, no network calls in the tool scripts, and all
assets local.

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
| Organise & merge | Combine PDFs; drag to reorder; rotate, delete, select pages; save all or only the selected pages (extract). |
| Split PDF | By page ranges (`1-3, 5, 8-`), every page, or every N pages. Several outputs download as one ZIP. |
| Images to PDF | JPG/PNG embedded as-is (JPEG EXIF orientation respected); WEBP/GIF/BMP re-encoded to PNG. Page size A4/A3/Letter/Legal or image size, orientation and margin. |
| PDF to images | PNG or JPG at 72/150/300 dpi, optional page range; several pages download as a ZIP. |
| Watermark | Text, position, angle, size, colour, transparency, optional page range. |
| Page numbers & header/footer | Number formats with `{n}`, `{total}`, `{date}`; position, first number, skip cover page, header/footer text. |
| Sign & annotate | Text, signature (draw, type or upload), image, highlight, box and white-out; drag to move, corner handle to resize. Items are flattened into the page on save. |

All placements honour each page's `/Rotate`, so text and stamps appear upright on rotated
(for example scanned landscape) pages. The annotate editor works in PDF user units, dividing
out the page's `/UserUnit` that pdf.js includes in its viewport, so saved items match the
preview on large-format pages too. Coordinates are mapped against the visible page box (the
CropBox clipped to the MediaBox, as pdf.js renders it), not the raw CropBox.

## Code map

| File | Role |
| --- | --- |
| `Pages/Tools/Pdf/Index.cshtml` (+ `_PdfTool*.cshtml` partials) | Standalone page with its own minimal shell (`Layout = null`). |
| `wwwroot/js/pdf-tools/pdf-tools-core.js` | Pure operations and geometry (page ranges, rotation mapping, ZIP writer, EXIF, pdf-lib operations). Runs in Node for tests. |
| `wwwroot/js/pdf-tools/pdf-tools-core.test.js` | Node tests, part of `npm test`. |
| `wwwroot/js/pdf-tools/pdf-tools-app.js` | Shell, routing (`#tool`), and the six form-based tools. |
| `wwwroot/js/pdf-tools/pdf-tools-annotate.js` | Sign & annotate editor. |
| `wwwroot/css/pdf-tools.css` | Styles; Bootstrap theme variables only, so Light and Dark both work. |

## Known limits

- Password-protected or edit-restricted PDFs are refused with a clear message (pdf-lib cannot
  decrypt them).
- Added text uses the standard PDF Helvetica font, so it supports Latin characters only;
  other scripts (for example Devanagari) are rejected with a clear message.
- Existing text in a PDF cannot be edited; white-out plus new text is the supported pattern.
  White-out covers content visually; it does not remove the underlying text from the file,
  so it is not a redaction tool.
- Everything is held in browser memory, so very large files depend on the user's computer.
  Files over 200 MB ask for confirmation first.

## Possible later phases

Server-side processing would be needed for real compression and password protect/unlock
(qpdf, Apache-2.0), Office-to-PDF (LibreOffice) and OCR. These would receive users' files on
the server, so they need size limits, immediate temporary-file deletion, no retention,
antiforgery protection and sandboxed converter processes. Avoid AGPL tools (iText,
Ghostscript) unless the licence has been reviewed.
