# Photo Tools

Photo Tools is a standalone workspace for changing a user's **own** photos. It is not part of
any PRISM record and is separate from the Projects photo module: PRISM only links to it. It
opens in a new browser tab from the navigation drawer (**Photo Tools**) at `/Tools/Photo`.

The build plan and owner decisions are in [ux/photo-tools.md](ux/photo-tools.md). This document
describes what is built.

## Privacy and data contract

- Every operation runs in the user's browser. Photos are read with the File API, drawn on a
  canvas and downloaded with a `blob:` link.
- Nothing is uploaded, stored, logged or retained by PRISM. The page model
  (`Pages/Tools/Photo/Index.cshtml.cs`) has only `OnGet`; there is no form post, API endpoint,
  database table or migration.
- The page requires a signed-in user (`[Authorize]`), like the rest of PRISM.
- Usage telemetry records only that the `photo-tools` module was opened
  (`ErpUsageModuleCatalog`), never file names or contents.
- A saved photo is re-drawn, so the hidden data in the original (GPS location, camera details)
  is never in the saved file. The page says so.
- The last Reduce size settings are remembered in that browser's `localStorage` only.

`ProjectManagement.Tests/Tools/PhotoToolsPageContractTests.cs` guards these points: sign-in
required, no handler other than `OnGet`, no form, no network calls in the scripts, and all
assets local.

## Offline deployment

No third-party library is used. The page loads Bootstrap, Bootstrap Icons and the Mulish font
that already ship in `wwwroot`, and its own scripts. No CDN or Internet access is needed.

The site's Content-Security-Policy already allows what the page needs: `script-src 'self'` for
its scripts and `img-src 'self' data: blob:` for the photo previews.

## Tools

| Tool | What it does |
| --- | --- |
| Reduce size | One or many JPG, PNG or WEBP photos. Target file size in KB or MB (or none), smaller dimensions, save as JPG, PNG, WEBP or the original type, optional black and white. A drag-to-compare check of the selected photo. One photo downloads as a file; several download as one ZIP. |
| Passport photo | One photo under a fixed crop frame with a face guide (oval, top-of-head and chin lines). Drag, zoom, straighten (±15°) and rotate. Sizes: 35 × 45 mm, 2 × 2 inch, 20 × 25 mm, custom mm or inches, or exact pixels. One-click lighting fix and brightness. Saved as a JPG at exactly the pixels shown, at 300 dpi, optionally under a KB limit. |
| Print sheet | On the Passport photo screen. The finished photo repeated on one sheet of 4 × 6 inch photo paper or A4, each copy inside a thin cut line. One JPG at 300 dpi. |
| Signature | The Passport photo screen switched to **Signature**: a wide frame (35 × 15 mm, 50 × 20 mm, 140 × 60 px, or custom), no face guide, and **Clean paper** in place of the lighting tools. Saved as a JPG at exactly the pixels shown, optionally under a KB limit. |
| Edit photo | One photo: crop (any shape or a fixed one), turn left or right, flip, straighten (±15°), resize in pixels. One-press clean-ups (auto enhance, fix colour cast, even out lighting, clean document, reduce noise, sharpen), sliders for brightness, contrast, colour and warmth, and six filters shown on the photo. Mark up (box, circle, arrow, pen, text, numbered markers; six colours, three sizes) and Blur or Pixelate to hide an area. Undo and redo, and hold a button to see the original. Saved as JPG, PNG or WEBP with a quality setting. |

Every step of the build plan is done, including the review of phones, the Dark theme and the
keyboard across all screens.

### How a passport photo is made

- The crop frame has the shape of the saved photo and stays still; the photo moves under it. The
  photo can never leave an empty corner in the frame (`coverScale`, `clampOffset`).
- Sizes are pixels at 300 dpi (35 × 45 mm is 413 × 531 px). The saved JPG carries 300 dpi in its
  header (`setJpegDpi`), so it prints at its true size.
- The screen works on a copy no larger than 1600 px; the saved photo is drawn again from the
  full-size photo, so zooming in does not cost sharpness that the original has.
- The dimensions are never changed to meet a KB limit, only the JPG quality (down to 0.2). If the
  limit still cannot be met, the page says what the smallest possible size is.
- **Fix lighting** measures the whole photo once (`measureLighting`): it stretches the range
  between the darkest and brightest half percent, and corrects most of a colour cast. The same
  correction is applied to the preview and the saved photo. It does not change the background to
  white; a plain, light background is still needed.
- The face guide is an aid for placing the head. It is not a check against any authority's rules.

### How a print sheet is made

- The sheet is the paper in pixels at 300 dpi (4 × 6 inch is 1200 × 1800, A4 is 2480 × 3508), so
  each copy prints at its true size when the sheet is printed at actual size (100%). The page
  says so, because "fit to page" would shrink the photos.
- `sheetLayout` places as many copies as fit, 2 mm apart, in a centred grid, and turns the paper
  on its side when that fits more: eight 35 × 45 mm photos on 4 × 6 inch paper, thirty on A4.
- An edge is left clear for the printer (3 mm on photo paper, 5 mm on A4).
- The copies are the same pixels as the downloaded photo, with the same clean-up.

### How a signature is cleaned

- **Clean paper** is on by default. `measurePaper` reads the whole photo once: the paper is the
  usual brightness, the ink the darkest half percent. `cleanPaper` then makes everything near the
  paper white and everything near the ink black, keeping the soft edge of each stroke.
- The result is black ink on white, whatever the colour of the pen.
- It is one rule for the whole photo. A strong shadow across the paper can be left grey; the page
  asks for plain white paper photographed from straight above.
- If no clear ink is found the photo is left as it is and the page says so.

### How editing works

- The opened photo is never altered. The edited photo is the original with a list of steps
  applied in order (`crop`, `rotate`, `flip`, `straighten`, `resize`, `cleanup`, `adjust`,
  `filter`, `shape`, `hide`). Undo drops the last step
  and replays the rest; Redo puts it back. Only the original and the current result are held in
  memory, however many steps there are.
- The same function applies a step to the screen copy (no larger than 1600 px) and to the
  full-size photo, so what is saved is what was shown.
- The crop box rules are pure functions in the core (`defaultCrop`, `moveCrop`, `resizeCrop`):
  the box cannot leave the photo, cannot be smaller than 16 px, and keeps its shape when one is
  chosen. Cropping copies pixels one for one; nothing is re-scaled.
- Straighten turns the photo and trims it to the largest upright piece of the same shape
  (`straightenedSize`), so no empty corner shows.
- Resize accepts 16 to 10000 px a side. Making a photo bigger, or changing its shape, is allowed
  and the page says what it costs.
- A crop or straighten that was not applied is dropped when the tool is changed.
- Ctrl+Z and Ctrl+Y work while the screen is open, except while typing in a box.

### How clean-ups, adjustments and filters work

All are pure pixel functions in the core, tested in Node on small pixel arrays. Each press is one
step, so Undo takes it back.

| Clean-up | What it does |
| --- | --- |
| Auto enhance | Stretches the range between the darkest and brightest half percent and corrects most of a colour cast (`measureLighting`, `applyLighting`; the same fix as on the passport screen). |
| Fix colour cast | The colour correction alone; the light is left as it is. |
| Even out lighting | Divides the photo into a coarse grid, pulls each area towards the overall brightness and blends the correction smoothly between areas (`evenLighting`). Lifts a shadow on one side or a dark corner. |
| Clean document | Evens the light fully, then takes the paper to white and the ink to dark. Each colour has its own white point, so yellowish paper under a lamp comes out white while a red stamp stays red (`cleanDocument`). |
| Reduce noise | Moves each pixel halfway towards the average of its neighbours (`softenPixels`). Softens fine detail a little, and the page says so. |
| Sharpen | Pushes each pixel away from the average of its neighbours (`sharpenPixels`). |

- **Adjust** has four sliders from -50 to 50 (brightness, contrast, colour, warmth) with a live
  preview; zero changes nothing (`adjustPixels`). Nothing is applied until Apply is pressed.
- **Filters** are six fixed recipes (black and white, sepia, warm, cool, vivid, faded), each
  shown on a small copy of the photo as it is now (`FILTERS`, `filterPixels`).
- Measurements (the lighting range, the paper level) are taken from the picture the step is
  applied to, so the screen copy and the full-size photo are each measured on their own. Sharpen
  and Reduce noise work on a pixel's nearest neighbours, so on a large photo their effect is
  finer than on the smaller screen copy.

### How mark up and blur work

- A mark is one step: a box, circle, arrow, pen line, text or numbered marker, in one of six
  colours and three sizes. Line and text sizes are a share of the photo's longer side
  (`markMeasures`), so a mark looks the same on a small photo and a large one. The measured
  size is stored in the step, so replaying it after an Undo draws exactly the same mark.
- Box, circle, arrow and pen are dragged; a drag too short to mean anything is ignored. Text and
  numbers are placed with one press. Numbers count up by themselves from the markers already on
  the photo. Text has an outline in a contrasting colour so it reads on any photo.
- The geometry is in the core and tested (`dragRect`, `arrowHead`, `thinPoints`); the drawing is
  the browser's canvas.
- **Blur** and **Pixelate** cut the dragged area into squares about a tenth of its longer side
  and replace each square with its average colour (`hidePixels`); Blur then blends between the
  squares. The detail is gone from the pixels: unlike a box drawn over something, nothing under
  it is in the saved file. The page says this. How see-through each pixel is gets averaged the
  same way, so writing on a see-through PNG or WEBP loses its outline as well as its colour.
- Marks and hidden areas are part of the photo from then on: a later crop, resize or filter
  treats them like any other pixels. They cannot be moved after they are placed; Undo removes
  the last one.
- With the keyboard, the photo takes focus while Mark up or Blur is open. The arrow keys move a
  cross over it (Shift for small steps). Enter places a text or a number at the cross; for the
  other marks and for a hidden area, Enter starts it, the arrow keys stretch it and Enter ends
  it. Escape cancels. The cross uses the same code path as the pointer.

### Phones, the Dark theme and the keyboard

- **Phones.** Below 576 px, and on any touch screen, every button, chip, select and text box is
  at least 44 px high and the colour swatches are 44 px. The editor stage is shorter on a phone
  so the photo and the tools under it are on the screen together, and Undo and Redo are icon
  buttons so the bar above the photo is one row. No screen scrolls sideways at 375 px.
- **Dark theme.** Colours come from theme tokens only. The grey outline buttons take the body
  text colour, because the stock grey is too faint on a dark surface.
- **Keyboard.** The passport frame, the crop box and the photo under Mark up and Blur all work
  with the arrow keys and Enter; Ctrl+Z and Ctrl+Y undo and redo in the editor.

### How the target size is reached

For JPG and WEBP the tool first tries top quality (0.92). If that is over the target it searches
the quality (down to 0.4) for the best one that fits. Only when the lowest quality is still too
big are the dimensions stepped down, and the search repeats. PNG has no quality setting, so only
its dimensions are stepped down; choosing JPG is the better way to make a PNG small.

If a target cannot be reached at all, the photo is shown in amber with the size that was
reached. The tool never reports a size it did not reach.

Photos are never enlarged. Phone photos are opened the right way up (their rotation is hidden
data). Big reductions are drawn in halving steps, which keeps fine detail. Saving as JPG fills
see-through areas with white.

## Code map

| File | Role |
| --- | --- |
| `Pages/Tools/Photo/Index.cshtml` | Standalone page with its own minimal shell (`Layout = null`). |
| `wwwroot/js/photo-tools/photo-tools-core.js` | Pure rules: target-size search, resize maths, output type and names, greyscale, lighting, passport and signature sizes, crop limits, print sheet layout, signature clean-up, JPG print resolution, editor steps and crop box, clean-ups, adjustments and filters, mark-up geometry and hiding an area. Runs in Node for tests. |
| `wwwroot/js/photo-tools/photo-tools-core.test.js` | Node tests, part of `npm test`. |
| `wwwroot/js/photo-tools/photo-tools-canvas.js` | Browser helpers shared by the tools: open a photo, draw it at a new size, canvas to file bytes, save, show an error. |
| `wwwroot/js/photo-tools/photo-tools-app.js` | Shell, routing (`#tool`) and Reduce size. |
| `wwwroot/js/photo-tools/photo-tools-passport.js` | Passport photo, signature and print sheet. |
| `wwwroot/js/photo-tools/photo-tools-editor.js` | Edit photo. |
| `wwwroot/js/tools-shared/zip-writer.js` | ZIP writer shared with PDF Tools. |
| `wwwroot/css/photo-tools.css` | Photo Tools styles. The shell (top bar, home cards, drop zone) is shared with PDF Tools through `pdf-tools.css`. Bootstrap theme variables only, so Light and Dark both work. |

## Known limits

- iPhone HEIC photos are not supported; the page says so when one is chosen.
- A browser that cannot write WEBP (some older Safari versions) says so and asks for JPG or PNG.
- Everything is held in browser memory, so very large photos depend on the user's computer.
