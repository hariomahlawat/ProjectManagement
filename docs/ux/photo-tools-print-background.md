# Photo Tools: print sheet and background colour

Two additions to Photo Tools (`/Tools/Photo`), asked for by the owner on 12 Nov 2026 after the
first build plan ([photo-tools.md](photo-tools.md)) was finished. The durable description of what
is built stays in [../photo-tools.md](../photo-tools.md); this document is the plan.

All four PRs are built. What was built is described in [../photo-tools.md](../photo-tools.md); where it
differs from this plan, that document is right. One difference: when a background cannot be
changed, the choice stays selected with a message and the photo is left as it is; it does not go
back to Keep. "What exists today" below describes the tool before
this plan.

## What exists today

- **Print sheet.** A select and a button at the end of step 3 on the Passport photo screen. It
  repeats the finished photo on 4 × 6 inch photo paper or A4, each copy inside a cut line, and
  saves one JPG at 300 dpi. The sheet is always filled; there is no preview, no PDF and no
  choice of copies. It is easy to miss, and it only works on a photo just made on that screen.
- **Background.** Not changed at all. "Fix lighting" brightens a background that is already
  light. The first plan left a background change out because it assumed an AI model was needed.

## Owner direction (12 Nov 2026)

| Topic | Direction |
| --- | --- |
| Print sheet | The user should be able to make and download an A4 or other size print sheet |
| Background | Change the background colour of a passport size photo |
| Next step | Plan and mockup of both before building |

Proposed until the owner says otherwise:

| Topic | Proposal |
| --- | --- |
| Where the print sheet lives | Its own tool with a card on the home screen. The Passport photo screen keeps one "Print sheet" button that opens the tool with the finished photo |
| Paper | 4 × 6 inch, 5 × 7 inch, A5 and A4 |
| Copies | "Fill the sheet" or a number the user picks, up to what fits |
| File | PDF (recommended, prints at true size) or JPG |
| Mixed sizes on one sheet | Left out. One photo size per sheet |
| How the background is found | By colour, starting from the edges of the photo. Works on a plain wall or curtain; says so plainly when the background is too busy. No AI model |
| Background colours | White, light blue, or any colour the user picks |
| Where the background step lives | Passport photo only, in step 2 (Clean up). Not in Edit photo |

## Privacy, offline and deployment

Unchanged from the rest of Photo Tools:

- Everything runs in the browser. No upload, no handler, no endpoint, no table, no migration,
  no configuration key.
- No CDN and no AI model. The one addition is that the Photo Tools page loads **pdf-lib 1.17.1**,
  which is already shipped in `wwwroot/lib/pdf-lib/` and used by PDF Tools. No new library is
  added to the repository.
- Rollback of each PR is reverting it. No data exists to clean up.
- The page contract test (`PhotoToolsPageContractTests`) keeps guarding: sign-in required,
  `OnGet` only, no network calls in the scripts, all assets local.

## Print sheet

### Screen

A fourth card on the home screen, **Print sheet**: "Put copies of one photo on 4 × 6 inch, 5 × 7
inch, A5 or A4 paper with cut lines. Download a PDF or JPG that prints at true size."

The screen has the paper on the left and three numbered steps on the right, the same layout as
Passport photo.

| Part | What it shows |
| --- | --- |
| Paper preview | The sheet drawn to scale with the copies and cut lines exactly where they will print. It changes as the options change. Under it: paper name, number of copies, and "Print at actual size (100%), not fit to page" |
| 1 Photo | The photo in use, its print size (35 × 45 mm, 2 × 2 inch, 20 × 25 mm or custom), and Change photo |
| 2 Paper and copies | Paper as four choices. Copies as "Fill the sheet (30)" or a number with minus and plus. Cut lines on or off |
| 3 Save | PDF or JPG, and Download sheet |

### How a photo gets here

- **From Passport photo.** The "Print sheet" button there opens this tool with the finished
  photo and its size already set. This is the main route.
- **Opened directly.** The user chooses any photo and a print size. A photo whose shape does not
  match the size is cropped around its centre, and the screen says so with a link to Passport
  photo for placing the face properly. The tool does not grow a second crop stage.

### How it works

- `sheetLayout` in `photo-tools-core.js` already places a centred grid and turns the paper when
  that fits more. It gains a `copies` argument: with fewer copies than fit, the rows fill from
  the top left so the unused paper is one clean piece that can be cut off and reused.
- New paper in `SHEET_SIZES` at 300 dpi: 5 × 7 inch is 1500 × 2100 px, A5 is 1748 × 2480 px.
- **PDF.** The page is the paper's true size in points (A4 is 595.28 × 841.89). The photo is
  embedded once as a JPG and drawn at each position at its true size; the cut lines are thin
  vector lines. The file is small and the photos keep their full 300 dpi. A pure function in the
  core file turns a layout into page size, image boxes and line segments in points, so the
  geometry has Node tests; `photo-tools-print.js` passes that to pdf-lib.
- **JPG.** As today: one canvas at 300 dpi with the dpi written into the header.
- **Preview.** Drawn from the same layout result the files use, so the preview cannot disagree
  with the download.

### Known limits

- One photo and one size per sheet.
- A home printer cannot print to the very edge, so each paper keeps a clear margin (3 mm on photo
  paper, 5 mm on A4 and A5).
- True size depends on the print dialog. The PDF sets its page to the paper size, but a user who
  picks "fit to page" on different paper still gets a scaled print. The screen says this.

## Background colour

### Screen

A **Background** row in step 2 (Clean up) of Passport photo, shown for a photo, not a signature.

| Part | What it shows |
| --- | --- |
| Choice | Keep, White, Light blue, and a colour picker for any other colour |
| Edge | One slider, "How much to change", from Less to More. It appears once a colour is chosen |
| Check | The photo on the stage shows the result at once. A "Hold to see the original" button compares |
| Notes | "Works on a plain wall or curtain. Check the hair and shoulders before you use the photo." and "Some offices do not accept a photo with a changed background. Check the rules of your form." |
| When it cannot work | "This background is not plain enough to change. Take the photo against a plain wall." The choice goes back to Keep |

### How it works

It works on the cropped photo at its output size (a 35 × 45 mm photo is 413 × 531 px), so it is
fast and the preview is the saved result.

1. **Read the background.** Pixels along the top edge and the upper part of both side edges are
   sampled. The bottom edge is left out because the shoulders reach it. Their median colour is
   the background colour; their spread tells whether the background is plain.
2. **Refuse a busy background.** When the spread is above a limit (patterned curtain, shelves,
   outdoors), the step stops with the message above and changes nothing.
3. **Grow from the edges.** Starting from the sampled edge pixels, the background spreads to each
   neighbour that is close in colour both to the background colour and to the pixel it came from.
   Brightness counts for less than hue, so a shadow or a lighting gradient on the wall is
   followed, and a face or shirt of another colour stops it. Because it spreads from the edges,
   a white shirt collar in the middle of the photo is not reached through a face or a dark coat.
4. **Tidy.** Small specks of leftover background inside the area are filled, and small islands
   are dropped.
5. **Soft edge.** Pixels on the boundary get a part share of the new colour by how close they are
   to the background colour, so hair does not end in a hard cut line. The old wall colour is
   taken out of those edge pixels so no grey fringe is left.
6. **Safety check.** If the changed area covers nearly the whole photo, or reaches the middle of
   the face oval, the tool says "Too much of the photo was taken as background" and suggests
   moving the slider towards Less.

All six steps are pure functions on RGBA pixels in `photo-tools-core.js`, with Node tests on
small made-up pictures: a plain wall, a wall with a lighting gradient, a busy wall that must be
refused, a light shirt against a light wall, and see-through pixels.

The order in the saved photo is crop, Fix lighting and Brightness, then Background, so the new
colour is exactly the colour chosen and is not shifted by the lighting fix.

### Known limits

To be stated on the screen and in the durable document:

- It needs a plain background. A busy background is refused, not guessed at.
- Clothes or hair close to the wall colour (a white shirt on a white wall, grey hair on a grey
  wall) can lose their edge. The slider and the comparison are there for this.
- Fine loose hair is softened, not cut out strand by strand.
- A strong shadow of the head on the wall may stay as a faint darker patch at Less, or take some
  hair with it at More.
- A model that finds the person on any background is deliberately left out. It can be added
  later behind the same Background row if plain backgrounds turn out not to be enough; it would
  mean shipping a model file and a runtime with the application.

## Code map (proposed)

| File | Change |
| --- | --- |
| `Pages/Tools/Photo/Index.cshtml` | Print sheet card and view; Background row in Passport photo; pdf-lib script tag |
| `wwwroot/js/photo-tools/photo-tools-core.js` | `sheetLayout` copies, two new papers, PDF geometry, background functions |
| `wwwroot/js/photo-tools/photo-tools-core.test.js` | Tests for each of the above |
| `wwwroot/js/photo-tools/photo-tools-print.js` | New. The Print sheet screen, preview, PDF and JPG |
| `wwwroot/js/photo-tools/photo-tools-passport.js` | Hands the finished photo to Print sheet; Background row; the old inline sheet code is removed, not kept beside the new tool |
| `wwwroot/css/photo-tools.css` | Paper preview, background choices; theme tokens only |
| `ProjectManagement.Tests/Tools/PhotoToolsPageContractTests.cs` | pdf-lib is loaded from `wwwroot/lib` only |
| `docs/photo-tools.md` | Updated by each PR |

## Build plan

Each PR is usable on its own. Phones, the Dark theme and the keyboard are checked in each PR, not
left to a review at the end.

| PR | Scope | What the user gets |
| --- | --- | --- |
| 1 | This plan | Nothing visible |
| 2 | Print sheet as its own tool: home card, screen, live paper preview, four papers, copies, cut lines on or off, JPG. Passport photo hands over to it | A print sheet that can be found, previewed and adjusted |
| 3 | PDF download for the print sheet with pdf-lib | A sheet that prints at true size |
| 4 | Background colour in Passport photo | White, light blue or any colour behind a photo taken against a plain wall |

PRs 2 and 3 do not depend on PR 4 and the other way round.

## Open questions for the owner

1. Is one photo size per sheet enough, or are mixed sheets (for example six passport and four
   stamp size) needed?
2. Is Letter paper needed besides 4 × 6, 5 × 7, A5 and A4?
3. Should Background also be offered in Edit photo, or only in Passport photo?
