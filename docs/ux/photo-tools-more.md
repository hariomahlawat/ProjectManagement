# Photo Tools: paste, hand-offs, ID card copy, exact pixels and watermark (plan, 14 Nov 2026)

On 14 Nov 2026 the owner asked what could easily be added to Photo Tools. Five gaps were
offered, and the recommended three were chosen:

1. **Paste a photo (Ctrl+V)** on every tool.
2. **Hand-offs between tools**: a finished photo goes on to another tool without being downloaded
   and chosen again. This was left as "a separate plan, later" in the second review
   ([photo-tools-review-2-fixes.md](photo-tools-review-2-fixes.md)).
3. **ID card copy**: the front and back of a card on one A4 page, at true size.

The other two offered were added on 14 Nov 2026, once the first three were in (sections 4 and 5):

4. **Exact width and height** in Reduce size, for one photo or many.
5. **Watermark**: a line of text on every photo, in Reduce size.

Camera capture is left out on purpose: browsers allow the camera only over HTTPS or on localhost,
and PRISM on the LAN may be served over plain HTTP.

Everything stays in the browser, as for the other tools: no photo is sent to the server, and
nothing needs the Internet.

## 1. Paste a photo

- Every tool screen takes a photo pasted with Ctrl+V (Cmd+V on a Mac): a screenshot, or an image
  copied from a browser, Word or a chat. Reduce size takes all the images pasted at once; the
  other tools take the first.
- A pasted photo is used exactly as a chosen or dropped one: the same file-type check, the same
  "only the photo chosen last counts", and it replaces the photo on the screen in the same way.
- A paste into a text box (a size field, the text of a mark) is left to the text box.
- A paste with no image on the clipboard says so on the screen ("There is no photo on the
  clipboard…") instead of doing nothing without a word. The home screen ignores a paste.
- A pasted image has no file name, so it is named `pasted-photo.png` (or `.jpg`, `.webp`, by its
  type); several are numbered.
- The drop areas say it: "or drop a photo here, or paste one (Ctrl+V)".
- One listener for the page, in `photo-tools-canvas.js`. Each tool registers the function that
  takes photos for its screen (`receivePhotos`), the same one its drop area uses.

## 2. Hand-offs between tools

- Under Download, a **Continue in** row of small buttons sends the finished photo to another tool,
  which opens with it as if it had been chosen there:

  | From | To |
  | --- | --- |
  | Edit photo | Passport photo, Remove background, Reduce size, Print sheet |
  | Remove background | Edit photo, Passport photo, Reduce size, Print sheet |
  | Passport photo | Print sheet (exists today, unchanged) |

  Reduce size is usually the last step and has no row. Passport photo keeps its Print sheet
  button; its own KB limit already does what Reduce size would.
- The photo sent is exactly the file Download would save, in the type and quality chosen, under the
  name Download would give it. In Edit photo a change still pending (a slider, a straighten) is
  applied first, as Download does.
- Sending is not saving: the source screen keeps its work and still counts as unsaved, so leaving
  the page still asks first.
- The target screen's photo is replaced, as when a photo is chosen there.
- The same `receivePhotos` functions as paste take the photo; `sendPhoto(view, file)` changes
  screen and hands it over once the screen is shown.

## 3. ID card copy

A new tool card on the home screen, **ID card copy** (`#idcard`): "Front and back of a card on
one A4 page, at its true size."

- Two places for photos, **Front** and **Back**; each takes a chosen, dropped or pasted photo. A
  paste or drop of two photos fills both. One side alone is enough to download.
- Each side is framed at the card's shape, **85.6 × 54 mm** (the ID-1 size of Aadhaar, PAN,
  driving licence and bank cards): drag to choose the part, zoom up to 3×, as in Print sheet, so the
  table around the card is left out. **Turn** turns a side a quarter, for a card photographed on
  its side.
- The page: A4 upright, the front above the back, centred across, 25 mm from the top with 12 mm
  between them, so there is room below to sign or write "self-attested". A thin outline around each
  card can be turned off. **Black and white** gives the look of a photocopy.
- Saved as a PDF whose page is A4's true size (recommended), or one JPG at 300 dpi, the same two
  ways as Print sheet, named `id-card-copy.pdf` / `.jpg`.
- The layout is a pure function in the core (`idCardLayout`), tested in Node; the PDF uses the
  same page plan as Print sheet.

## 4. Exact width and height (Reduce size)

Forms and portals often ask for a photo of an exact size, such as 600 × 400 px, and the same size
for every photo of a set.

- **Width and height** gets one more choice, **Exact width and height**. It shows two boxes, Width
  and Height (16 to 10,000 px), and a choice of how a photo of another shape is made to fit:
  - **Crop to fill** (the default): the photo fills the size and the edges that do not fit are cut
    off evenly, from the centre.
  - **Fit inside, white border**: the whole photo is kept and the spare space is white.
- Every photo comes out at exactly that size. A smaller photo is made larger, since the size was
  asked for; nothing else in Reduce size ever makes a photo larger.
- With a file size limit too, only the quality is lowered, never the pixels: the size asked for is
  kept. A photo that cannot get under the limit at that size is shown in amber, as today, with a
  note to choose fewer pixels or a larger limit.
- The choice, the two sizes and the fit are remembered with the other Reduce size settings.
- The sizes are worked out in the core (`exactFit`), tested in Node.

## 5. Watermark (Reduce size)

A line of text on every photo, such as "For official use only" or a name and date, so a copy
shared outside cannot be passed off as an original.

- A new panel under Options, **Watermark**, with **Add a watermark** (off by default). When on:
  - **Text** (up to 60 characters). An empty text adds nothing.
  - **Place**: Bottom right (the default), Bottom left, Centre, or **Across the photo** (repeated
    at a slant over the whole photo, hardest to crop out).
  - **Strength**: Light or Strong.
- White letters with a soft dark edge, so the text reads on light and dark photos alike. The size
  follows the photo (a share of its shorter side), so it looks the same on a small and a large
  photo, and long text is made smaller to fit across.
- Drawn after the size and black and white are applied, so the text stays sharp and keeps its
  size whatever the photo was reduced to.
- The home card for Reduce size says it: "…, set exact pixels or add a watermark".
- Remembered with the other Reduce size settings, the text too.
- The text's size, margin and spacing are worked out in the core (`watermarkPlan`), tested in Node.

## PRs

| PR | What |
| --- | --- |
| 1 | This plan |
| 2 | Paste a photo on every tool, the drop areas' wording, tests, `docs/photo-tools.md` |
| 3 | Hand-offs: Continue in rows on Edit photo and Remove background, tests, docs |
| 4 | ID card copy: the new tool, its layout in the core with tests, docs |
| 5 | Sections 4 and 5 added to this plan |
| 6 | Exact width and height in Reduce size, tests, docs |
| 7 | Watermark in Reduce size, tests, docs |

Each PR is checked in the app before it is merged, and merged only when Codex and CI are clean.
