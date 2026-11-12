# Photo Tools (`/Tools/Photo`)

A standalone photo tool for a user's **own** photos, built on the same model as PDF Tools
([../pdf-tools.md](../pdf-tools.md)). It is not part of the Projects photo module and never reads
from or saves to a PRISM record. The user opens a photo from their computer, changes it and
downloads the result.

This document records the owner direction of 11 Nov 2026 and the proposed build plan. Nothing
here is built yet.

## Owner direction (11 Nov 2026)

| Topic | Decision |
| --- | --- |
| Separation | A separate tool with its own page and drawer entry, like PDF Tools. Not tied to the Projects photo module |
| Main jobs | Make passport size photographs, and reduce a photo's file size |
| Also wanted | Basic one-click cleaning tools and filters |
| Look | Mockup of four screens (home, passport photo, reduce size, edit photo) accepted as the direction |

Assumed until the owner says otherwise (each is cheap to change later):

| Topic | Assumption |
| --- | --- |
| White background for passport photos | Left out. It needs an AI model shipped with the app (tens of MB, slow on older PCs). The tool only brightens an already light background |
| HEIC (iPhone) photos | Not supported; the tool says so clearly. JPG, PNG and WEBP are supported |
| Named form presets | None. The size presets plus custom pixels and KB cover every form; the last custom values are remembered in that browser only |
| Auto straighten | Left out; a manual tilt slider is used instead |

## Privacy and data contract

The same contract as PDF Tools, guarded by the same kind of test:

- Every operation runs in the user's browser (File API, canvas, `blob:` download).
- Nothing is uploaded, stored, logged or retained by PRISM. The page model has only `OnGet`:
  no form post, API endpoint, database table or migration.
- The page requires a signed-in user (`[Authorize]`).
- Usage telemetry records only that the `photo-tools` module was opened, never file names or
  contents.
- Saving through canvas drops hidden data (GPS position, camera details). The tool says this
  plainly because it is a benefit users should know about.

## Offline deployment

No new library is needed for the first build. Everything uses the browser's canvas plus code in
this repository. Cropper.js 2.0.1 is already shipped (`wwwroot/lib/cropperjs/`) and may be used
for the crop stage if it fits the "fixed frame, move the photo" behaviour; otherwise the crop
stage is a small custom canvas component. No CDN, no remote font, no AI model.

## Screens

| Screen | What it does |
| --- | --- |
| Home | Three large choices (Passport photo, Reduce size, Edit photo) and a drop area. Same top bar as PDF Tools with "Photos stay on this computer" |
| Passport photo | Photo on a dark stage with a locked crop frame, face oval, top-of-head and chin lines; zoom, straighten, rotate. Three numbered steps: photo size, clean up, save. A Photo / Signature switch |
| Reduce size | Photo list with size now and after, a drag-to-compare quality check, target size box with quick sizes, dimension and format options, one ZIP download |
| Edit photo | Tool rail (Clean up, Crop, Rotate, Adjust, Mark up, Blur, Resize), photo on a dark stage, panel on the right, undo/redo, "hold to see the original" |

## How the hard parts work

- **Target file size.** JPG and WEBP quality is searched (binary search on canvas quality) for
  the best quality that fits. If the lowest acceptable quality still does not fit, the
  dimensions are stepped down and the search repeats. PNG has no quality setting, so a PNG that
  cannot fit is offered as JPG. The result always shows the real size reached; the tool never
  claims a size it did not reach.
- **Exact print size.** A 35 x 45 mm photo at 300 dpi is 413 x 531 px. The saved JPG carries
  300 dpi in its header so it prints at true size. The print sheet is one image (4 x 6 inch or
  A4 at 300 dpi) with copies and thin cut lines.
- **Phone photos.** EXIF orientation is honoured when the photo is opened, so portrait photos
  are not sideways.
- **Large photos.** The stage shows a reduced preview so sliders stay smooth; the full-size
  photo is processed only on download. Very large photos ask for confirmation first, as PDF
  Tools does for large files.
- **Cleaning and filters.** Pure pixel functions (levels stretch, grey-world white balance,
  unsharp mask, light blur for noise, shadow lift, document clean) in one core file that also
  runs in Node, so each has a unit test on small pixel arrays.
- **Blur.** Cover, blur and pixelate are burned into the saved pixels. Unlike PDF white-out, the
  original content under them is not in the saved file. Blur and pixelate keep one average per
  square, from which short text can sometimes be guessed, so Cover is the default and is
  recommended for writing and numbers (see [../photo-tools.md](../photo-tools.md)).

## Code map (proposed)

| File | Role |
| --- | --- |
| `Pages/Tools/Photo/Index.cshtml` (+ `.cs`, partials) | Standalone page with its own minimal shell (`Layout = null`), `OnGet` only |
| `wwwroot/js/photo-tools/photo-tools-core.js` | Pure operations: size search, resize maths, print-sheet layout, dpi header, pixel filters. Runs in Node for tests |
| `wwwroot/js/photo-tools/photo-tools-core.test.js` | Node tests, added to `tools/run-js-tests.js` |
| `wwwroot/js/photo-tools/photo-tools-app.js` | Shell, routing (`#tool`), Reduce size |
| `wwwroot/js/photo-tools/photo-tools-passport.js` | Passport photo and signature flow |
| `wwwroot/js/photo-tools/photo-tools-editor.js` | Edit photo |
| `wwwroot/css/photo-tools.css` | Styles; theme tokens only, so Light and Dark both work |
| `Services/Navigation/RoleBasedNavigationProvider.cs` | "Photo Tools" beside "PDF Tools" in `BuildToolItems()`, opens in a new tab |
| `Services/Usage/ErpUsageModuleCatalog.cs` | `photo-tools` module row |
| `ProjectManagement.Tests/Tools/PhotoToolsPageContractTests.cs` | Sign-in required, `OnGet` only, no network calls in the scripts, all assets local |
| `docs/photo-tools.md` | Durable document, created in PR 2 and updated by each later PR |

The ZIP writer already exists inside `wwwroot/js/pdf-tools/pdf-tools-core.js`. PR 2 moves it
into a small shared file (`wwwroot/js/tools-shared/zip-writer.js`) used by both tools, with the
existing PDF tests kept green, so the logic is not copied. The EXIF orientation reader stays in
PDF Tools: browsers apply a photo's rotation themselves when it is drawn on a canvas.

## Build plan

Each PR is usable on its own and leaves the tool in a finished state.

| PR | Scope | What the user gets |
| --- | --- | --- |
| 1 | This plan | Nothing visible |
| 2 | Shell and Reduce size: page, top bar, home, drawer entry, usage row, contract tests, shared ZIP file, target-size search, dimensions, format, batch and ZIP, quality compare | Reduce one or many photos to a size limit |
| 3 | Passport photo: crop stage with face guide, size presets and custom, zoom, straighten, rotate, lighting fix, photo file at exact pixels and KB | A passport photo file for online forms |
| 4 | Print sheet (4 x 6 inch, A4, cut lines, 300 dpi) and Signature mode | Printable sheets, and a signature file for forms |
| 5 | Editor shell: crop, rotate and straighten, resize, undo/redo, hold to see the original, download with format and quality | A basic editor |
| 6 | Clean up, Adjust and Filters: auto enhance, colour cast, even lighting, clean document, noise, sharpen, brightness/contrast/saturation/warmth, six filters | One-click fixes and filters |
| 7 | Mark up and Blur: text, arrow, box, circle, pen, numbered markers; blur and pixelate areas | Annotated and redacted photos |
| 8 | Phones, dark theme check and keyboard review across all screens | Works on a phone and in Dark |

The two main jobs come first (PRs 2 to 4). PRs 5 to 7 can be reordered or dropped without
affecting them.

## Deployment impact and rollback

- No database change, no migration, no new endpoint, no configuration key, no new runtime
  dependency. Existing pages are untouched except one drawer entry and the ZIP writer moved
  out of the PDF Tools core file.
- Rollback is reverting the PR; no data exists to clean up.
- The site's Content-Security-Policy (`script-src 'self'`) must allow what the page uses
  (`blob:` images for previews). PR 2 checks this against the running app and records the result
  in `docs/photo-tools.md`.

## Known limits (to state in the tool and the durable document)

- No automatic background change, object removal or skin retouching.
- A badly blurred or out-of-focus photo cannot be rescued by Sharpen.
- Everything is held in browser memory, so very large photos depend on the user's computer.
- Added text uses the fonts PRISM ships; scripts without a shipped font fall back to the
  computer's own fonts.
