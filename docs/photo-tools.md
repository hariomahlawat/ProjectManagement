# Photo Tools

Photo Tools is a standalone workspace for changing a user's **own** photos. It is not part of
any PRISM record and is separate from the Projects photo module: PRISM only links to it. It
opens in a new browser tab from the navigation drawer (**Photo Tools**) at `/Tools/Photo`.

The build plan and owner decisions are in [ux/photo-tools.md](ux/photo-tools.md). This document
describes what is built.

## Privacy and data contract

- Every operation runs in the user's browser, with two exceptions that use the PRISM server:
  **Remove background** sends a copy of the photo (at most 2048 px), whose model returns only a
  mask; and **Passport photo** sends a copy (at most 1600 px) to find where the face is, and, when
  its colour method cannot change the background or the user presses Better edges, the same copy
  as Remove background for the mask. The server holds the photo in memory for that request and
  never writes, stores or logs it (see
  [photo-tools-background-removal.md](photo-tools-background-removal.md)). Photos are read with
  the File API, drawn on a canvas and downloaded with a `blob:` link.
- Nothing is stored or retained by PRISM. The page model (`Pages/Tools/Photo/Index.cshtml.cs`)
  has only `OnGet`; there is no form post, database table or migration. The only API is three
  endpoints (`Features/PhotoTools/PhotoToolsApi.cs`): status, the mask of one photo, and where
  the face is in one photo, all for signed-in users, the last two also checked for the
  antiforgery token.
- The page requires a signed-in user (`[Authorize]`), like the rest of PRISM.
- Usage telemetry records only that the `photo-tools` module was opened
  (`ErpUsageModuleCatalog`), never file names or contents.
- A saved photo is re-drawn, so the hidden data in the original (GPS location, camera details)
  is never in the saved file. The page says so.
- Each screen's last choices are remembered in that browser's `localStorage` only, under
  `prism.photoTools.<screen>`: Reduce size (target, width and height with the exact size and its fit, file type, black and
  white, the watermark with its text),
  Passport photo (per Photo or Signature: the size or custom size and the KB limit; and the
  background colour), Print sheet (paper, PDF or JPG, cut lines), Edit photo (file type and
  quality, once picked) and Remove background (background and file type). Photos are never kept,
  and a choice that no longer fits is ignored.
- Leaving the page (a link, reload or closing the tab) after moving or painting a passport photo, editing a photo, or painting a cut-out, without downloading since, makes the browser ask first. Moving between the tools inside the page does not.
- A photo dropped anywhere on the page outside a drop zone is ignored, so the browser does not
  open it in place of the page and lose the work on every screen (as in PDF Tools).

`ProjectManagement.Tests/Tools/PhotoToolsPageContractTests.cs` guards these points: sign-in
required, no handler other than `OnGet`, no form, every asset the page names ships in `wwwroot`,
no outside address in the page or its stylesheets (no `@import`, no outside `url()`), and no
network call (`fetch`, `XMLHttpRequest`, `sendBeacon`, `WebSocket`, `EventSource`, `import()`,
`importScripts`, `new Worker`) in any script the page loads, read from the page itself, but
`photo-tools-background.js`, and that one only to the three PRISM endpoints. Passport photo asks
the server only through it (`window.PrismPhotoServer`).

## Offline deployment

The one third-party library is pdf-lib 1.17.1, for the Print sheet PDF; it is the copy PDF Tools
already ships in `wwwroot/lib/pdf-lib`. The page also loads Bootstrap, Bootstrap Icons and the
Mulish font that ship in `wwwroot`, and its own scripts. No CDN or Internet access is needed.

The site's Content-Security-Policy already allows what the page needs: `script-src 'self'` for
its scripts and `img-src 'self' data: blob:` for the photo previews.

## Tools

| Tool | What it does |
| --- | --- |
| Reduce size | One or many JPG, PNG or WEBP photos. Target file size in KB or MB (or none); the pixels are kept unless the size cannot be reached otherwise, or made fewer on request, or set to an exact width and height (crop to fill, or fit inside with a white border); save as JPG, PNG, WEBP or the original type, optional black and white, optional watermark. A drag-to-compare check of the selected photo. One photo downloads as a file; several download as one ZIP. |
| Passport photo | One photo under a fixed crop frame with a face guide (oval, top-of-head and chin lines). The face is placed for you when the PRISM server's face detector is set up: straightened, sized and centred on the guide. Drag, zoom, straighten (±15°) and rotate; Place the face again puts it back. Sizes: 35 × 45 mm, 2 × 2 inch, 20 × 25 mm, 630 × 810 px for online forms that ask for pixels (the shape of 35 × 45 mm; no one portal is named), custom mm or inches, or exact pixels. One-click lighting fix and brightness. A plain background can be made white, light blue or any other colour; any other background uses Remove background's model when it is set up (by itself, or with Better edges). What the tool gets wrong is touched up with Refine edge, Erase and Restore brushes. Saved as a JPG at exactly the pixels shown, at 300 dpi, optionally under a KB limit. |
| Print sheet | Its own tool. Copies of one photo on 4 × 6 inch, 5 × 7 inch, A5 or A4 paper, shown on a preview of the paper drawn to scale. A photo of another shape than the print size is dragged and zoomed to choose the part printed. The sheet is filled, or holds the number of copies chosen; cut lines can be turned off. The photo comes from the **Print sheet** button on Passport photo, or from a file. Saved as a PDF whose page is the paper's exact size (recommended), or as one JPG at 300 dpi. |
| ID card copy | Its own tool (`#idcard`). The front and back of a card, 85.6 × 54 mm (Aadhaar, PAN, a driving licence, a bank card), on one A4 page at true size, the front above the back. Each side is dragged and zoomed so only the card is printed, and turned a quarter when it was photographed on its side. Wide or upright card, a thin outline, black and white. One side alone can be saved. Saved as a PDF whose page is A4 (recommended), or one JPG at 300 dpi. |
| Signature | Its own card on the home screen (`#signature`), which opens the Passport photo screen switched to **Signature**: a wide frame (35 × 15 mm, 50 × 20 mm, 140 × 60 px, or custom), no face guide, and **Clean paper** in place of the lighting tools. Saved as a JPG at exactly the pixels shown, optionally under a KB limit. |
| Remove background | One photo, any background. The PRISM server's model (BRIA RMBG) finds the subject; the browser takes the old background's tint out of the soft edge, and the cut-out is saved see-through (PNG, WEBP), on a plain colour or on a blur of its own background (JPG, PNG, WEBP), at up to 4096 px. Refine edge, Erase and Restore brushes touch up the edge. Off until an administrator installs the model; see [photo-tools-background-removal.md](photo-tools-background-removal.md). |
| Edit photo | One photo: crop (any shape or a fixed one), turn left or right, flip, straighten (±15°), resize in pixels. One-press clean-ups (auto enhance, fix colour cast, even out lighting, clean document, reduce noise, sharpen), sliders for brightness, contrast, colour and warmth, and six filters shown on the photo. Mark up (box, circle, arrow, pen, text, numbered markers; six colours, three sizes) and Cover, Blur or Pixelate to hide an area. Undo and redo, and hold a button to see the original. Saved as JPG, PNG or WEBP with a quality setting. |

Every step of the first build plan ([ux/photo-tools.md](ux/photo-tools.md)) is done, including
the review of phones, the Dark theme and the keyboard across all screens. So is the follow-up plan
([ux/photo-tools-print-background.md](ux/photo-tools-print-background.md)): the Print sheet tool,
its PDF download and the background colour change for passport photos. The fixes from the review
of 13 Nov 2026 are planned in [ux/photo-tools-review-fixes.md](ux/photo-tools-review-fixes.md).

### How a passport photo is made

- The crop frame has the shape of the saved photo and stays still; the photo moves under it. The
  photo can never leave an empty corner in the frame (`coverScale`, `clampOffset`).
- Which size is which: a passport applied for in India takes a 4.5 × 3.5 cm photo (the
  Passport Seva FAQ and application instruction booklet), so 35 × 45 mm is labelled
  "Indian passport, most forms". 2 × 2 inch (51 × 51 mm) is what Indian embassies and consulates
  abroad ask for, so it is labelled "Passport from an embassy abroad, visa". A Passport Seva
  Kendra takes its own photo at the counter; a printed one is needed at other collection centres
  and for children under 4. Rules change, so the labels are a guide, not a promise.
- Sizes are pixels at 300 dpi (35 × 45 mm is 413 × 531 px). The saved JPG carries 300 dpi in its
  header (`setJpegDpi`), so it prints at its true size.
- The screen works on a copy no larger than 1600 px; the saved photo is drawn again from the
  full-size photo, so zooming in does not cost sharpness that the original has.
- A KB is 1,000 bytes and an MB 1,000,000 (owner decision, 13 Nov 2026): upload forms count a KB as
  1,000 or 1,024 bytes, and a file under 50,000 bytes passes both. Sizes on the screen are rounded
  up in KB (`formatBytes`), so a file just over a KB limit never reads as fitting it; sizes
  in MB are shown to the nearest 0.1 MB.
- The dimensions are never changed to meet a KB limit, only the JPG quality (down to 0.2). If the
  limit still cannot be met, the page says what the smallest possible size is. A limit that is
  not a number stops only the file: the photo, its background and the checks are still shown.
- **Fix lighting** measures the whole photo once (`measureLighting`): it stretches the range
  between the darkest and brightest half percent, and corrects most of a colour cast. The same
  correction is applied to the preview and the saved photo. It does not change the background;
  that is the Background choice below.
- The face guide asks for the head (top of the head to chin) at about 75% of the photo's height:
  its lines are at 8% and 83% (owner decision, 14 Nov 2026; Indian passport photos ask for 70 to
  80%). It is an aid for placing the head, not a check against any authority's rules; nor are the
  checks listed in step 3 (see below).

### How the face is placed

When a photo is opened in Photo mode, Passport photo sends its 1600 px screen copy to
`POST /api/photo-tools/face`. The server runs only the Photos library's face detector (no
recogniser, no record) and answers the largest face's box and five points, or "no face".
`placeFace` in the core file then:

- turns the photo so the eyes are level, within the ±15° the Straighten slider allows (in its
  half degrees);
- estimates the top of the head, because the detector's box ends at the brow: as far above the
  eyes as the chin is below them, plus 5% for hair;
- zooms so the top of the head and the chin sit on the guide's two lines, and moves the photo
  so the eyes are in the middle across.

Zoom goes to 4 times on any photo, and further on a photo with pixels to spare: up to where the
saved photo would be enlarged 1.15 times (just short of where the Detail check warns), and never past 12 times.
The Zoom slider reaches the same limit. The mouse wheel scrolls the page; Ctrl with the wheel,
or a pinch on a touchpad, zooms.

If the user moves, zooms or straightens before the answer arrives, the photo is left where they
put it. **Place the face again** (shown when a face was found) puts it back. Opening another
photo, turning it a quarter or switching from Signature back to Photo asks again. A head that stays
smaller than the guide at the limit (a photo taken from too far away) has its top on the top line
rather than its middle in the middle, and the checks say the person is too far away instead of
"zoom in". Without the
face detector set up nothing is asked and the photo is placed by hand.

While the server looks, the bar under the photo says "Placing the face…". The request is cancelled
when the photo, its quarter turn or the mode changes, so an old request never holds one of the
server's places. A busy answer is tried once more after 2 seconds; after that, or after 20 seconds,
the bar says the face could not be placed, with **Try again**. A photo whose eye line is more than
30° off level (on its side) is not turned a few degrees and zoomed: the bar asks for Rotate.

### How the background colour is changed

Background (Keep, White, Light blue, or any colour) is a choice in step 2 of Passport photo. It
first finds the background by colour, starting from the edges of the photo, which works on a plain
wall or curtain and refuses anything else. Where that cannot cope, Remove background's model
is used (see "When the model is used" below).

It runs on the cropped photo at its saved size, after the lighting fix and brightness, so the new
colour is exactly the colour chosen (`changeBackground` in the core file):

1. **Read the background.** A thin band along the top edge and down the upper 60% of both sides
   is sampled; the bottom is left out because the shoulders reach it. The median is the
   background colour (`measureBackground`).
2. **Refuse a busy background.** If the edge pixels differ too much from that colour (a patterned
   curtain, shelves, outdoors), nothing is changed and the screen says so. A little hair or a
   shoulder at the edge does not count: the eighth decile of the differences is used.
3. **Spread from the edges.** The background grows from the edge band to every neighbour that
   passes two tests. Its colour must be close to the background colour *expected at that spot*,
   and the brightness there must not change quickly from pixel to pixel. The slider ("How much
   to change", Less to More) loosens both tests together.
   - *Expected colour.* A wall is not one colour: light falls off across it and a backdrop is
     mottled. The colour is kept per square of a coarse grid (about 24 squares across the
     shorter side), starting as the one colour read from the edges. After each spread, every
     square is corrected from the background found in it, and the spread is repeated (up to 12
     rounds, stopping when nothing more is found). A square with little background in it, or
     whose colour has moved far from the edge colour, takes the colour of its sure neighbours,
     so the colour cannot creep onto the person.
   - *Edges.* A wall is smooth; the outline of a person and the weave of cloth are not. Where
     the brightness (averaged over three by three pixels, so grain is not taken for an edge)
     changes quickly, the background does not spread. This is what keeps a blue jacket against
     a blue backdrop.
4. **Tidy.** Islands left standing inside the background (a mark or a darker patch on the wall,
   up to half a percent of the photo) go with it, unless they reach the bottom edge, where the
   person is. Wall that is fully enclosed by the person is not reached and keeps its colour.
5. **Stop if it went wrong.** If under 3% of the photo was found, the screen says no plain
   background was found. If over 85% was taken, or the middle of the face, it says too much was
   taken and to move the slider towards Less. In each case the photo is left as it is.
6. **Soft edge.** The three rows of pixels on the person's side of the boundary are a mix of
   person and wall. The wall's share of each gets the change of colour, measured from the
   colour expected there, which also takes the old wall colour out of the edge. Hair does not
   end in a hard line, though a thin rim of the old colour can remain.

The method was tuned on six real photographs (public-domain NASA portraits kept with the local
demo data): a mottled studio backdrop, three people against a blue sky, a black and white photo
with a white sky, and one against a picture of the moon. At the default setting four came out
clean, the moon was refused as busy, and one lost part of a blue cardigan against the blue sky
(it is kept at Less). Brightness counts for less than colour only when deciding whether a
background is plain; when spreading it counts nearly in full, which is what tells a white shirt
from a white wall.

The stage shows the saved result: the finished photo is laid over the crop frame each time it is
made, and "Hold to see the original" takes it off while held. Print sheet receives the photo with
its new background. Signature has no Background choice.

Limits, stated on the screen where they matter:

- It needs a plain background. A busy one is refused, not guessed at.
- Clothes or hair close to the wall colour (a white shirt on a white wall, a blue jacket against
  a blue backdrop) can be taken with the wall, most of all towards More. The slider and the
  comparison are there for this.
- It takes about a fifth of a second at 413 × 531 px and two to three seconds at 4000 × 4000 px,
  the largest size the screen allows.
- Fine loose hair is softened, not cut out strand by strand.
- Some authorities do not accept a photo with a changed background. The screen says to check.

### Touching up the background

When the background has been changed, **Touch up edges** opens three brushes, named as in common
photo editors (Photoshop's Select and Mask, Canva, remove.bg):

| Brush | What it does | For |
| --- | --- | --- |
| **Refine edge** (the default) | Only what still looks like the old background takes the new colour. Hair, skin and clothes under the brush stay. | The rim of old wall left around hair and shoulders. |
| **Erase** | Everything under the brush takes the new background colour. | Something left standing in the background that does not look like the wall. |
| **Restore** | The photo as it was before the change comes back. | Part of the person that was given the new colour. |

None of them makes anything see-through: the photo is a JPG, and "erased" means the chosen
background colour shows there. There are three brush sizes (Small, Medium, Large; **[** and **]**
step through them, as in Photoshop), Undo, Reset (removes every stroke) and Done.

- It is a separate mode. While it is open, dragging on the photo paints; Done goes back to
  dragging the photo into position. With the keyboard, the arrow keys move a ring over the photo
  and Space or Enter paints one dab where it is; Escape leaves the mode.
- Each stroke is stored in the photo's own pixels, with its width and brush. The strokes are
  drawn through the same move, turn and zoom as the photo, so a stroke stays on the part of the
  photo it was painted on when the photo is moved, zoomed or straightened afterwards. A brush is
  a share of the crop frame's width (a radius of 2%, 4% or 8%), so it is the same size on any
  screen.
- **The brush is soft.** Each stroke fades out over about a third of its own radius on each
  side of its edge: it is drawn into the stroke picture as six nested, partly see-through strokes
  of falling width around a solid core. The softness belongs to the stroke, so a stroke painted
  later with another brush size does not change the edge of an earlier one, and Undo of one
  stroke changes nothing else. A later stroke lies over an earlier one.
- **How Refine edge decides.** At the edge of a person a pixel is a mix of the person and the
  wall behind them, as thin hair over a wall is. The background change keeps the wall's colour
  at every spot (the grid of colours in step 3 above) and hands it to the brush
  (`changeBackground` returns it as `wall`, read with `wallColourAt`). Around the stroke, the
  pixels that are clearly not wall are collected in small squares, each split by brightness into
  a dark and a light group (hair and skin, say), and each group's average colour is kept
  (`personColours`; the squares are about 3% of the photo's width, fixed to the photo, so another
  stroke elsewhere or its Undo never changes them). Each pixel under the brush is then explained by the best-fitting mix
  (`personShare`):
  - wall and one of the person's colours near it: a strand of hair over the wall. How far the
    pixel lies from the wall colour towards that colour is the person's share, and only the
    wall's share is moved to the new colour;
  - two colours of the person: the soft edge where skin meets hair. It is all person and is not
    changed;
  - nothing fits well: a colour of its own, and person.

  When two explanations fit equally well (grey hair against a grey wall, where every colour lies
  on one line), the one that keeps more of the person is taken: the brush errs on keeping the
  person.

  Old wall becomes the new colour, a strand of hair keeps its colour on the new background, and
  solid hair, skin and the edge between them are not changed. The first tenth of the way from
  the wall (grain on the wall) counts as wall. Where no person is near, only a pixel well away
  from the wall colour counts as person.
- Refine edge cannot tell a shirt of the wall's colour from the wall; Restore is there for that.
- The saved photo is made in three steps: the cropped photo with its clean-up, the background
  change, then the strokes (`applyBackgroundFixes`). The first two steps are kept, so a stroke
  shows on the stage at once without finding the background again; the file follows a moment
  later, made from the same kept steps. Only a change to the photo itself (moving, zooming, size,
  lighting, background or slider) finds the background again; a stroke, Undo or Reset does not,
  so painting stays quick on a large photo. The strokes are worked out only inside the box they
  cover: a large stroke on a 4000 × 4000 px photo takes about a fifth of a second.
- Strokes are cleared by a new photo and by turning the photo a quarter. They are kept, but not
  applied, while the background is set to Keep or cannot be changed. A change of background
  colour applies them in the new colour. Print sheet receives the photo with its strokes.

### When the model is used

Owner decision (14 Nov 2026): the colour method stays first, because a plain, evenly lit wall needs
no wait and no place in the server's one-photo-at-a-time queue.

- **By itself**, once per photo, when the colour method gives up on the placed photo (not plain
  enough, no plain background found, or too much taken). It waits until the face has been placed,
  so the whole photo shown for a moment on opening does not count.
- **Better edges** asks for it on purpose (a shadowed wall, a white shirt on a white wall, loose
  hair). It is shown only when Remove background is set up on the server.
- It sends the same copy as Remove background (at most 2048 px) and makes the cut-out the same way
  (the old background's tint taken out of hair and edges). The cut-out is kept with the photo and
  placed with it, so moving the photo or changing the colour afterwards is instant and sends
  nothing. Touch up edges (Refine edge, Erase, Restore) works on the model's mask.
- While it works, a note in the Background panel says "Finding the person…", then "Still working"
  with **Cancel** after 8 seconds. If the server is busy, too slow or out of reach, the photo
  keeps the colour method's result and the note offers **Try again**. Download and Print sheet wait until it is done (the line under them says why), so the photo is never saved with the old background half way; when it stops, they come back with the colour method's result. The photo can be moved
  while it waits. A keyboard user who pressed Better edges is taken to the note, then to the line
  that says the model was used.
- The model keeps whatever it sees as the subject: a second person, or a machine the person stands
  in front of, stays in the photo. The Erase brush takes it out.
- Without Remove background set up, the colour method is used as before and Better edges is not
  shown.

### Checks before saving a passport photo

Under the file's size, step 3 lists what a form is likely to reject, or says "No problems
found". The checks are a guide and the screen says so: they use only what can be measured
without a model that knows what a face looks like (`passportChecks` in the core file).

| Check | How it is measured | Warns when |
| --- | --- | --- |
| Detail | How many times the photo is enlarged to fill the frame | Over 1.2 times |
| Face | The face detector, when it is set up | No face was found |
| Head position | The face found by the detector (top of the head estimated as for placing it), or, without it, the person's outline from the background search (`findBackground`, `headOutline`) | The head touches the top edge, or starts well above or below the guide's top line (8% of the height); with the face found, also when the face is off the middle across |
| Head size | The face found by the detector: top of the head to chin; or the width of the outline across the rows where the face is | With the face: well over or under the guide's 75% (at the zoom limit the advice is a closer photo, not "zoom in"). With the outline: it fills more than 85% of the width, or under a third |
| Background | The same reading of the edges that the background change uses | The edges are not plain, or the colour is not light. Once the background has been replaced, the edges of the finished photo are read instead: when they are still not plain (the model took part of the scene for the person), it says parts of the old background are left |
| Light on the face | Average brightness inside the guide's oval | Very dark or washed out |

- With the face detector, head position and size are judged from the real face on any
  background. Without it they need a background the tool can tell the person from; with a busy
  background they are not judged, and only the background warning shows.
- The outline is the hair as well as the face, so "top of the head" means the top of the hair.
  A line along the edge of a scan, or a mark on the wall, is not taken for the head: the head
  starts where the middle stays person for 1% of the height.
- Above 2 million pixels the outline is not looked for, because the search would hold the
  screen up; the other checks still run.
- When neither the face nor an outline could be found, the title says "No problems found, but the head position could not be checked" instead of claiming more than was looked at.
- Signature has no checks.
- They do not check expression, glasses, head tilt, eyes open, or any one authority's
  measurements.

### How a print sheet is made

- The sheet is the paper in pixels at 300 dpi (4 × 6 inch is 1200 × 1800, 5 × 7 inch is
  1500 × 2100, A5 is 1748 × 2480, A4 is 2480 × 3508), so each copy prints at its true size when
  the sheet is printed at actual size (100%). The page says so, because "fit to page" would
  shrink the photos.
- `sheetLayout` places the copies 2 mm apart in a centred grid, and turns the paper on its side
  when that fits more. Of 35 × 45 mm photos, eight fit on 4 × 6 inch paper, nine on 5 × 7 inch,
  twelve on A5 and thirty on A4.
- With fewer copies than fit, the places are the first ones of the full sheet, row by row from
  the top left. The unused paper stays in one piece and can be cut off and used again.
- An edge is left clear for the printer (3 mm on photo paper, 5 mm on A4 and A5).
- **From Passport photo.** The Print sheet button there hands over the finished photo (an event
  on the page, `phot:print-sheet`), with the same pixels and clean-up as the downloaded photo.
  Its size is already the print size; a custom size is offered as "As made in Passport photo".
- **From a file.** The photo is printed at the size chosen on the screen. A photo of another
  shape cannot fit whole, so the user chooses the part: the small picture of the copy in step 1
  grows and is dragged (or moved with the arrow keys), with a zoom slider up to three times and
  a Centre button. It starts at the middle. `coverCrop` gives the piece for a place (zoom, and
  where in the room that is left), and `dragCover` moves the place by a drag and stops at the
  edge of the photo. A photo that already has the shape, such as one from Passport photo, shows
  no controls. Print sheet only moves and zooms; the face guide, straighten and clean-up stay in
  Passport photo.
- The preview and the saved JPG are drawn by the same function from the same layout, and the PDF
  is placed from that layout too, so the preview cannot disagree with the file.
- **PDF.** `sheetPdfPlan` turns the layout into a page and boxes in points (72 to the inch, from
  the bottom left corner). The page is the paper's size, the photo is stored once as a JPG and
  placed at each position at its true size, and the cut lines are drawn as lines outside the
  photo. The file is small and the photos keep their 300 dpi. It is written by pdf-lib 1.17.1,
  the library already shipped for PDF Tools (`wwwroot/lib/pdf-lib/`); if it is missing, the tool
  offers the JPG only. Paper and photo sizes are whole pixels at 300 dpi, so they are within
  0.05 mm of the nominal size.
- A PDF page of the right size still depends on the print dialog: "fit to page" on other paper
  scales it. The screen says to print at actual size.

### How an ID card copy is made

Plan: [ux/photo-tools-more.md](ux/photo-tools-more.md).

- The page is `core.idCardLayout(tall)`: A4 upright (2480 × 3508 px at 300 dpi), the card
  1011 × 638 px (85.6 × 54 mm) or turned upright, centred across, 25 mm from the top and 12 mm
  between the front and the back. Both cards sit in the top half, which leaves room below to sign
  or write "self-attested".
- Each side is taken from its photo as Print sheet takes a copy of another shape (`coverCrop`,
  `dragCover`): the largest piece at the card's shape, zoomed up to 3× and dragged on the side's
  picture, or moved with the arrow keys (plus and minus zoom). **Turn** turns the photo a quarter to
  the right and starts the placing again.
- Photos come from the side's own **Choose** button or drop area, or are dropped or pasted on the
  page: two photos fill the front and the back; one goes to the first side without a photo, else to
  the side last worked on.
- On the screen a side without a photo is a dashed box with its name; the file leaves it out, so
  one side alone can be downloaded.
- **Black and white** makes the cards grey (`toGreyscale`), like a photocopy. The outline is a
  thin grey line outside each card, drawn as lines in the PDF.
- The PDF is one A4 page from the same page plan as Print sheet (`sheetPdfPlan`), each card stored
  once as a JPG at its true size. The JPG is the whole page at 300 dpi. Both are named
  `id-card-copy`.
- The card's shape, PDF or JPG, the outline and black and white are remembered in this browser
  (`rememberedChoices('idcard')`).

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
- Each step is applied to the full-size photo. The screen shows a copy of that result no larger
  than 1600 px, so what is saved is what was shown. Only the live previews (the Adjust sliders
  and Straighten before it is applied) are worked out on the screen copy.
- If an Undo or Redo cannot be replayed (a very large photo can run out of browser memory),
  nothing changes and the page says so: the list of steps always matches the photo shown and
  the file that is saved.
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
  preview; zero changes nothing (`adjustPixels`). An adjustment or a straighten shown on the screen but not yet applied is applied by itself on Download (the file is saved once it is made) and on picking another tool, so what is saved is always what the screen showed (owner decision, 14 Nov 2026); the line under Download says so meanwhile. A crop box that was not applied is still dropped on changing tool.
- **Filters** are six fixed recipes (black and white, sepia, warm, cool, vivid, faded), each
  shown on a small copy of the photo as it is now (`FILTERS`, `filterPixels`).
- Measurements (the lighting range, the paper level) are taken from the full-size photo the step
  is applied to.
- Sharpen and Reduce noise hold only three rows of sums at a time (`withNeighbourAverage`), not
  a second copy of the photo, so they need little memory beyond the photo itself.

### How mark up and hiding an area work

- A mark is one step: a box, circle, arrow, pen line, text or numbered marker, in one of six
  colours and three sizes. Line and text sizes are a share of the photo's longer side
  (`markMeasures`), so a mark looks the same on a small photo and a large one. The measured
  size is stored in the step, so replaying it after an Undo draws exactly the same mark.
- Box, circle, arrow and pen are dragged; a drag too short to mean anything is ignored (for the
  pen, one that never moves as far as the line is wide, `penStroke`). Text and
  numbers are placed with one press. Numbers count up by themselves from the markers already on
  the photo. Text has an outline in a contrasting colour so it reads on any photo.
- The geometry is in the core and tested (`dragRect`, `arrowHead`, `thinPoints`); the drawing is
  the browser's canvas.
- **Cover** (the default) fills the dragged area with solid black (`hidePixels` with `cover`).
  The fill does not depend on what was covered (an average colour would still tell how much ink
  was there), so nothing of it is left in the saved file. It is the mode for writing, names and
  numbers.
- **Blur** and **Pixelate** cut the dragged area into squares about a tenth of its longer side
  and replace each square with its average colour; Blur then blends between the squares. The
  detail is gone from the pixels, but one average per square is kept. A short word or number can
  sometimes be guessed back from those averages by trying every possible text, so the page
  recommends them for faces and backgrounds and Cover for writing.
- In every mode, how see-through each pixel is gets averaged the same way as its colour, so
  writing on a see-through PNG or WEBP loses its outline as well as its colour.
- Marks and hidden areas are part of the photo from then on: a later crop, resize or filter
  treats them like any other pixels. They cannot be moved after they are placed; Undo removes
  the last one.
- With the keyboard, the photo takes focus while Mark up or Hide is open. The arrow keys move a
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
- **Keyboard.** The passport frame, the crop box and the photo under Mark up and Hide all work
  with the arrow keys and Enter; Ctrl+Z and Ctrl+Y undo and redo in the editor.
- **Passport photo.** Place the face again sits in the bar under the photo with Rotate, at 44 px
  on a phone. Where the layout is one column (900 px and narrower), Hold to see the original is also in that bar. The note while the model works is in the Background panel, next to the colour or
  Better edges that asked for it, so on a phone it is on the screen where the user tapped; it
  uses the theme's tertiary background, in Light and Dark. Focus moves from Better edges to the
  note, to Try again when the server is busy, and to the model's line when it is done.

### Laptops and wide screens

- **The photo stays in view.** Wider than 900 px, the photo column of Passport photo, Print
  sheet, Edit photo and Remove background stays on the screen while the settings beside it are
  scrolled (Reduce size is left out: its main column is a list that can be longer than the
  screen). The photo grows with the height of the screen, from 640 px up to 960 px.
- **Large screens.** From 1600 px wide the tools use up to 1680 px, with a settings column of
  up to 420 px; the home cards keep their width.
- **The Save bar.** On those four tools, while the panel's own Download is out of view and there
  is something to download, a bar at the foot of the settings column holds a copy of Download
  with the "Ready" line (Passport photo: the checks' summary, which goes to the list). It hides
  while the real Download can be seen, so there is never a second Download in sight, and it
  never covers the photo. Phones get the same bar across the screen. It is
  `PrismPhotoCanvas.wireSaveBar` in `photo-tools-canvas.js`.

### While Remove background works

- Looking at another tool on the page does not stop the cut-out: the server keeps working and the
  result is there on coming back. **Cancel**, another photo, the time limit
  (`PhotoTools:BackgroundRemoval:TimeoutSeconds`) or leaving the page stop it.
- Until there is a cut-out (still working, or stopped with **Try again**), the brushes and brush
  sizes are off and "Check the edges…" under the photo is not shown.
- A photo given while the server is still getting ready (checking its model after a restart,
  status `checking`) is held, not dropped: the note says it opens as soon as the tool is ready,
  the server is asked again every 2 seconds, and the photo opens when it answers ready. After
  90 seconds it stops waiting and the server's note gets the focus, as for a tool that cannot be
  used. A later photo takes the place of a held one.

### Exact width and height (Reduce size)

**Width and height** has an **Exact width and height** choice (plan:
[ux/photo-tools-more.md](ux/photo-tools-more.md)): Width and Height, 16 to 10,000 px each, and
how a photo of another shape is made to fit.

- **Crop to fill** (the default) fills the size and cuts the edges that do not fit evenly, from
  the centre. **Fit inside, white border** keeps the whole photo and fills the spare space white.
- Every photo comes out at exactly that size; a smaller photo is made larger. This is the only
  way Reduce size makes a photo larger.
- With a file size limit too, only the quality is lowered (`encodeToTarget` with `fixedSize`),
  never the pixels. A photo still over the limit is shown in amber, and the note says to choose
  fewer pixels or a larger size.
- Until both boxes hold whole numbers in range the size is not used, and the hint under
  the boxes says so in amber.
- `core.exactFit` works out the part of the photo to read and where it goes; `drawExact` in
  `photo-tools-canvas.js` draws it. Both are tested in Node (`exactFit`, `parseExactSide`).

### Watermark (Reduce size)

A **Watermark** panel in Reduce size puts a line of text on every photo, such as "For official
use only" or a name and date (plan: [ux/photo-tools-more.md](ux/photo-tools-more.md)).

- **Add a watermark** is off by default. When on: **Text** (up to 60 characters; empty adds
  nothing), **Place** (Bottom right, Bottom left, Centre, or Across the photo, repeated at a
  slant) and **Strength** (Light, Strong).
- White letters with a soft dark edge, so the text reads on light and dark photos alike.
- The size is a share of the photo's shorter side (4.5%, or 6% across), so it looks the same on a
  small and a large photo; text too long for the photo is made smaller to fit, with no lower limit, so it is never cut off.
- It is drawn after the size and black and white are applied, so it stays sharp and keeps its
  size whatever the photo was reduced to. It counts toward the file size like the rest of the
  photo.
- A watermarked photo is not compared with the original's size: it is not tried again at a lower
  quality to get under it, and is never marked "not smaller" (the original has no watermark, so
  "use the original" would be the wrong advice).
- `core.watermarkPlan` works out the size, place and repeat steps (tested in Node);
  `drawWatermark` in `photo-tools-canvas.js` draws it.

### Pasting a photo

Every tool screen takes a photo pasted with Ctrl+V: a screenshot, or an image copied from a
page, Word, a chat or a folder (plan: [ux/photo-tools-more.md](ux/photo-tools-more.md)).

- One `paste` listener for the page, in `photo-tools-canvas.js`. Each screen registers the
  function its drop area uses (`receivePhotos`), so a pasted photo has the same file-type check
  and the same "only the photo chosen last counts" as a chosen one, and replaces the photo on the
  screen in the same way. Signature pastes into Passport photo's screen.
- Reduce size takes all the files pasted; the other screens take the first.
- A paste into a text field (a size, a mark's text) is the field's own. The home screen ignores a
  paste. A paste with no file on the clipboard (only text) says "There is no photo on the
  clipboard to paste…" instead of doing nothing.
- A copied file keeps its name. A screenshot or an image copied from a page comes as `image.png`
  or with no name and is named `pasted-photo.png` (`.jpg`, `.webp` by its type), numbered when
  there are several (`pastedName`).
- Nothing pasted leaves the browser, as for a chosen photo.

### Continuing in another tool

A finished photo goes on to another tool without being downloaded and chosen again. Under
Download, **Continue in another tool** has a button for each tool it can go to:

| From | To |
| --- | --- |
| Edit photo | Passport photo, Remove background, Reduce size, Print sheet |
| Remove background | Edit photo, Passport photo, Reduce size, Print sheet |
| Passport photo | Print sheet (its own button, with the photo at its print size) |

- The photo sent is the file Download would save: the same type, quality and name. In Edit photo
  a change still pending (a slider, a straighten) is applied first, as Download does
  (`withSavedFile`).
- The buttons are on while Download is (`wireSendButtons`). `sendPhoto(screen, file)` shows the
  other screen first and then gives it the file through the same function a paste uses
  (`receivePhotos`), so it is opened exactly as a chosen photo, and replaces the photo there.
- Sending is not saving: the screen it came from keeps its work, which still counts as unsaved.
- Remove background is not offered while it is not set up on the server.
- Reduce size has no row: it is usually the last step. Passport photo keeps only Print sheet: its
  own KB limit does what Reduce size would.

### When a tool cannot start

The page and its scripts must come from the same version of PRISM. When they do not (an update
part-way through, a browser holding an old copy, or a development server left running on an
old build while the files change under it), a screen's script can stop while the page loads and
leave an empty screen. `photo-tools-canvas.js` watches for an error from a Photo Tools script
while the page is loading and puts a note under that screen's heading: "This tool could not
start. Reload the page (Ctrl+F5). If it happens again, PRISM may be part-way through an update…".
The screen is found from the error's file name and stack, so a screen's script that fails inside
the shared helpers is still named. Errors after the page has loaded are left to the tools.

### How the target size is reached

The pixels are kept as they are by default (owner decision, 13 Nov 2026): a photo is only made
smaller in pixels when its file size cannot be reached any other way, or when the user picks a
smaller size. Settings saved before then held the old default of 1600 px, which is not carried
over; a size the user picks is remembered as before.

For JPG and WEBP the tool first tries top quality (0.92). If that is over the target it searches
the quality (down to 0.4) for the best one that fits. Only when the lowest quality is still too
big are the dimensions stepped down, and the search repeats. PNG has no quality setting, so only
its dimensions are stepped down; choosing JPG is the better way to make a PNG small.

If a target cannot be reached at all, the photo is shown in amber with the size that was
reached. The tool never reports a size it did not reach.

A photo that was already small can come out bigger than the file it was made from. When it is
saved in the type it already had (JPG or WEBP), the quality is searched again at the same
dimensions for a file under the original (`retryUnderOriginal`). When that cannot help (a PNG,
or a PNG saved as JPG), the result is shown in amber as "(not smaller)" and the summary says so; it
is never shown as a saving. The original is not put in its place, because saving through the
canvas is what removes a photo's hidden data.

A change of settings starts every photo again once the settings have been left alone for a
third of a second, also while photos are being reduced: the photo in hand is finished and
dropped, and no further photo is started with a half-typed size.

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
| `wwwroot/js/photo-tools/photo-tools-passport.js` | Passport photo and signature: the face placed, the colour method and the model's cut-out, the checks. Hands the finished photo to Print sheet. |
| `wwwroot/js/photo-tools/photo-tools-print.js` | Print sheet: preview, paper, copies, the saved PDF or JPG. |
| `wwwroot/js/photo-tools/photo-tools-idcard.js` | ID card copy: the two sides, the A4 preview, the saved PDF or JPG. |
| `wwwroot/lib/pdf-lib/1.17.1/` | PDF library shared with PDF Tools; used here to write the print sheet and ID card copy PDFs. |
| `wwwroot/js/photo-tools/photo-tools-editor.js` | Edit photo. |
| `wwwroot/js/photo-tools/photo-tools-background.js` | Remove background: asks the server for the mask, then the cut-out, brushes and saving. The only script that talks to the server: it also lends Passport photo the status, the face and the mask (`window.PrismPhotoServer`). |
| `Features/PhotoTools/` | The server side: Remove background (settings, model check, ONNX Runtime, the queue), the face for Passport photo (`PassportFaceService`, the Photos library's detector only), and the three endpoints. |
| `wwwroot/js/tools-shared/zip-writer.js` | ZIP writer shared with PDF Tools. |
| `wwwroot/css/photo-tools.css` | Photo Tools styles. The shell (top bar, home cards, drop zone) is shared with PDF Tools through `pdf-tools.css`. Bootstrap theme variables only, so Light and Dark both work. |

## Known limits

- iPhone HEIC photos are not supported; the page says so when one is chosen.
- A browser that cannot write WEBP (some older Safari versions) says so and asks for JPG or PNG.
- Everything is held in browser memory, so very large photos depend on the user's computer.
  Edit photo works on the full-size photo, so on a very large photo each step, and each Undo,
  can take a few seconds. The page shows "Working…" over the photo meanwhile (above about 4
  million pixels of work) and ignores further presses until the step is done; the work itself
  still runs on the page, not in a background worker.
- Saved photos are in standard sRGB colour. A wide-colour photo (such as an iPhone photo in
  Display P3) can look very slightly less vivid after saving.
- Reduce size shows a small thumbnail of each photo once it has been opened; a photo still
  waiting shows an empty box.
- Remove background needs the PRISM server and its installed model. When either is missing the
  screen says so and the other tools are not affected. Passport photo then uses its colour method
  only, and places the face only when the Photos library's face detector is installed. See
  [photo-tools-background-removal.md](photo-tools-background-removal.md) for its own limits.
