# Collage Studio: invitation cards and posters (plan, 18 Nov 2026)

Collage Studio ([../collage-studio.md](../collage-studio.md)) makes collages in the browser. Its
first plan ([collage-studio.md](collage-studio.md)) left **themes**, meaning ready-made designs, for a
later plan of their own. This is that plan. It adds two kinds of ready-made designs:

- **Invitation cards**: smart, official, military-style cards for unit functions (Raising Day,
  Regimental Dinner, Change of Command, Farewell, Barakhana, At Home and others).
- **Posters**: event announcements, notices, farewell or felicitation posters, and talks.

The approved mockup is [mockups/invitations-posters.html](mockups/invitations-posters.html). Open
it through a local web server, not as a file, so that the logo and fonts load.

## Owner decisions (18 Nov 2026)

- **No State Emblem and no national flag**, in any design, sticker or default.
- **Cards open with the SDD logo.** The logo is `wwwroot/img/logos/sdd-hd.webp` (1732 × 2940,
  transparent, added in #3428). The menu bar and the PDF reports keep `sdd.png`.
- **Posters have no default logo.** Choosing a poster asks which logo to use: the user's own
  file, Crests and logos, or no logo. Until the user chooses, the poster shows an "+ Add logo"
  box that Preview and Save leave out.
- **The user can put in their own logo**, on cards and on posters. It stays in the browser like
  any photo. This reverses the 15 Nov "own logo: not now" decision in
  [collage-studio-library-and-unit-stickers.md](collage-studio-library-and-unit-stickers.md).
- **Everything on a card or poster can be changed**: every word, the logo, colours, fonts,
  borders and the position, size and angle of every part. Any part can be deleted, and the user
  can add their own. A design is a starting point and has no fixed parts.

## Decisions taken for the owner (change any before its PR)

| Question | Choice | Why |
| --- | --- | --- |
| Where it lives | **In Collage Studio** (`/Tools/Collage`), with two new rows on the start screen. Not a new tool, and not in Photo Tools | The editor, fonts, Crests and logos, backgrounds, drafts and Save as PDF are already there. Photo Tools works on one photo at a time |
| How wording and look relate | **Kept apart.** Each design places the same named fields. Changing the design keeps every word | The user types once and tries looks freely |
| One field, one text item | Each field (occasion, host, date and so on) is its own text item, linked by a `field` name. Labels such as "Dress" or "R.S.V.P." are plain text items | Typing on the card and typing in the form change the same item. There's no merged text to keep in step |
| Changing the design after moving parts | The new design places the fields in its own way. Parts the user added (photos, stickers, own text) are kept where they are. A toast offers Undo | Predictable, and one Undo puts everything back |
| Own logo file types | **PNG, JPG and WEBP**, the types Collage Studio already reads. **Not SVG** | Same rule as Crests and logos. A PNG keeps a clear background |
| Card sizes | **All the sizes in common use**, each portrait or landscape, plus a custom size. See [Card sizes](#card-sizes) | The owner asked for every size, pocket cards included (18 Nov). Each is a real paper size in the PDF |
| Folded cards | **A5 folding to A6** and **A4 folding to A5**: one sheet, with outside and inside pages. The design places the front cover, the inside wording and the back | Folded cards are common for formal functions |
| Pocket cards for the same function | **Entry card or car pass**, **RSVP card** and **programme card** designs. Each fills from the same Card details, so the function's wording is typed once | "Pocket size" covers both a small invitation and the small cards that go with it |
| Poster sizes | A3 portrait (exists) and A4 portrait (exists). **Not A2 or larger** | Save draws a picture, and an A3 at 300 dpi is already near the browser's canvas limit |
| Front and back | **Both, optional.** A card starts with the front. "Add a back" makes the back (programme, route sketch or venue photo, RSVP), which the user can delete | Most formal cards have a programme on the back. Some don't need one |
| Printing | Save gains **cards per sheet**: as many as fit on A4 or A3 with room for cutting (for example 2 A6 on A4, 8 A7 on A4, 10 visiting-card passes on A4), or fewer if the user chooses. Thin cut marks are added. The back is placed so that it lines up when the printer flips on the long edge | Units print on office printers on card stock, not at a press |
| Bleed for a press | **Not in the first version.** It can come later as one tick box | Not needed for office printing |
| Guest names | **A blank line to write in** by default. **From a guest list** makes one PDF with a card per guest | Writing names by hand is common. A list saves hours for 100+ guests |
| Guest list input | Paste names, one a line (a column copied from Excel pastes this way), or pick a .csv or .txt file. **No .xlsx reader** | No new library. Copy and paste from Excel covers the same need |
| Guest list size | At most **300 names** in one PDF. Cards are drawn one at a time and let go | Keeps browser memory flat. Page count and an estimated file size are shown before Save |
| Where guest names are kept | In the draft, in this browser only, with a **Clear names** button. Never sent to PRISM | The page's promise stays: nothing the user makes reaches PRISM |
| Hindi | The **Bilingual** design has Hindi fields (occasion, invitation line, host). Any design can take Hindi in any field | Hindi fonts and typing already work |
| New fonts | **Cinzel** (engraved capitals) and **Pinyon Script** (formal script), from @fontsource, Latin only, under the SIL Open Font License | They make the formal designs look official. They ship with PRISM like the other fonts |

## Card sizes

No Army-wide standard size for function cards could be found. Units and mess secretaries set
their own, so PRISM offers every size in common use. Sizes were checked against printers'
guides on 18 Nov 2026 (see Sources). The size can be changed at any time from the page size
menu, and the design re-places itself for the new shape.

| Group | Size | mm | Typical use |
| --- | --- | --- | --- |
| Standard | **A5** (default) | 148 × 210 | Formal invitations, dinner cards |
| Standard | **A6** | 105 × 148 | Smaller invitations, At Home cards; fits a C6 envelope |
| Standard | **5 × 7 in** | 127 × 178 | The most common printed invitation size |
| Standard | **4 × 6 in** | 102 × 152 | Postcard-size invitations |
| Long | **DL** | 99 × 210 | Slim cards; fits a DL envelope |
| Military | **Correspondence card, 4 × 5 in** | 102 × 127 | White card with the crest at the top, the traditional service format |
| Square | **5 in square** and **6 in square** | 127 × 127, 152 × 152 | Modern designs |
| Pocket | **A7** | 74 × 105 | Pocket invitation or programme card |
| Pocket | **3.5 × 5 in** | 89 × 127 | RSVP or reply card |
| Pocket | **Visiting card, 3.5 × 2 in** | 89 × 51 | Entry card or car pass |
| Folded | **A5 folding to A6** | A5 sheet, A6 when folded | Folded invitation with wording inside |
| Folded | **A4 folding to A5** | A4 sheet, A5 when folded | Larger folded card, programme inside |
| Custom | **Your own size** | 50 to 300 mm a side | Anything else a unit already uses |

Every size can be portrait or landscape.

Pocket sizes take fewer words. The pocket designs keep only:

- the logo;
- the occasion;
- date, time and venue;
- one line for a name or vehicle number.

Anything else can still be added.

## What the user sees

### Start screen

Two new rows sit above **Layouts**:

- **Invitation cards**: Classic ivory, Regimental, Navy and gold, Ceremonial photo, Bilingual
  and Modern. Each small picture is drawn from the design itself, with the SDD logo. A size
  menu beside the row (A5 by default) shows the designs at the chosen size.
- **Pocket cards**: Entry card or car pass, RSVP card, Programme card and Pocket invitation.
- **Posters**: Event, Notice, With a photo, and Talk or seminar. Each shows the "+ Add logo"
  box.

Cards and posters are drafts like any collage. They're listed under Drafts with a small
"Card" or "Poster" word.

### Card editor

- **Card details** opens on the left (on a phone, as a sheet from the rail). It holds:
  - occasion, with quick picks for Raising Day Dinner, Regimental Dinner, Change of Command,
    Farewell, Barakhana, At Home, Ladies' Night and Passing Out Parade;
  - host and invitation line;
  - guest's name, either a blank line or from a guest list;
  - date, time and venue;
  - dress, with quick picks for Lounge Suit, Mess Dress, Smart Casuals and Ceremonial;
  - RSVP and the note at the bottom;
  - Hindi wording;
  - the back's programme, one line each.

  Typing in the form changes the card at once. Clicking words on the card and typing changes the
  form.
- A field left empty takes its item off the card. Typing in it again brings the item back in the
  design's place.
- **Designs** strip under the page: one click changes the look, and the words stay. Undo goes
  back.
- **Front / Back** above the page, with **Add a back** and **Delete the back**.
- Selecting a part shows its settings, as in any collage:
  - **logo**: Change logo (own file or Crests and logos), Add second logo, Remove;
  - **colour band** (Regimental): the unit's colours, top to bottom;
  - **text**: font, size, colour and alignment.
- The left rail (Text, Stickers, Background, Photos) still adds anything.

### Poster editor

- **Poster details**: heading, line under it, date, time, venue, details one line each, and
  contact.
- Choosing a poster first asks **Which logo goes on this poster?**
  - Use my own logo
  - Crests and logos (SDD among them)
  - No logo
  - Decide later

  With **No logo**, the design moves the heading up into the space.

### Save

- Save asks first what it's for, as now. For a card, **Printing** adds:
  - **Cards per sheet**: on A4 or A3, as many as fit (shown as a number and a small picture of
    the sheet), or fewer;
  - **Print the back on the reverse**.

  A two-sided card saved as a PDF has the back as page 2. As PNG or JPG, Save asks for front,
  back or both (two files).
- **One card for each guest**: paste names, see "Makes 42 cards in one PDF (about 18 MB)" and a
  preview of the first three, then Save. The cards per sheet setting applies here too.

## How it is built

All of it runs in the browser. **There's no server change, no migration and no new API.**

- **Designs as data**: `wwwroot/js/collage/collage-designs.js` (`window.PrismCollageDesigns`).
  Each design gives:
  - page size and background;
  - its decorations (borders, rules, bands, corners);
  - where each field goes, and how it looks.

  One pure function, `applyDesign(collage, design, details)`, turns a design and the details into
  ordinary collage items. It's unit-tested: every design places every field, nothing lands off
  the page, and the words survive a design change.
- **Field links**: a text item may carry `field` (for example `"occasion"`). The Card details form
  reads and writes those items through the model editor (`collage-model.js`). So typing in the
  form is one Undo step, kept in the draft, like typing on the page. Items the user added have no
  field.
- **Fitting long words**: a text item may carry a `maxWidth`. Its words wrap within it, then
  shrink down to a smallest size, using the existing text measuring. So "Change of Command
  Parade and Reception" never runs off a card.
- **A new `shape` kind** for rules, borders, double borders, bands, corners and the guest line.
  It's a rectangle, line or ellipse with fill, stroke, width and rounding. It stretches without
  distortion, which a drawn sticker cannot. It's drawn by `collage-draw.js` (so Save matches the
  screen) and is available to every collage from a new **Shapes** group.
- **Logos**:
  - the SDD logo is a built-in sticker, `logo:sdd`, drawn from
    `/img/logos/sdd-hd.webp`. It's read like the Twemoji files, a same-site static file with no
    `fetch`, so the page contract test still allows only `collage-library.js` to fetch;
  - an own logo is a photo item with `fit: "contain"`, no frame and a clear background, kept in
    the draft's photo store;
  - an empty logo place is an empty frame with `role: "logo"`. Like any empty frame, Preview and
    Save leave it out.
- **Two sides**: the collage model gains an optional `back` page (its own background and items).
  `readDraftModel` reads old drafts unchanged, as single-sided. The model version goes up by one.
- **Print sheets and guest cards**: `collage-print.js` places 1, 2 or 4 cards on a sheet, with cut
  marks, and turns the back's order for a long-edge flip. It writes the PDF with the vendored
  pdf-lib. Guest cards draw one card at a time from the originals, put it in the PDF and let the
  canvas go.
- **Fonts**: Cinzel and Pinyon Script are added to `wwwroot/fonts/collage/` with `OFL.txt`, and to
  `collage-fonts.css` and the Credits dialog.

### Order with the fifth review plan

[collage-review-5-fixes.md](collage-review-5-fixes.md) is still open. Its PR 9 (one way to
change the collage) and PR 12 (smaller files) reshape `collage-app.js` and `collage-core.js`.
This plan touches the same files. So:

- PRs 2 and 3 here (designs as data, shapes) can be built at any time. They add files and add to
  the model.
- PRs 4 and later, which touch the editor, start after fifth-plan PRs 9 and 12 are merged.

## Build order (small PRs)

Each PR is merged only when Codex has no findings on its latest commit and CI is green. Each
comes with tests that fail without it.

| PR | What | Done when |
| --- | --- | --- |
| 0 | HD SDD logo `wwwroot/img/logos/sdd-hd.webp` (#3428) | Merged |
| 1 | This plan and the mockup | Merged |
| 2 | **Shapes**: the `shape` kind (rectangle, line, ellipse; fill, stroke, rounding) in core, drawing, page, settings, Layers and drafts, plus a Shapes group in the Stickers panel | A border drawn on the page saves in the same place in the PNG and the PDF; old drafts open unchanged |
| 3 | **Designs as data**: `collage-designs.js`, `applyDesign`, field links on text items, `maxWidth` fitting, all flat card sizes in [Card sizes](#card-sizes) with portrait and landscape plus a custom size, designs that place themselves by proportion so that one design fits every size, and the SDD logo as a built-in sticker. Two designs (Classic ivory, Navy and gold) to prove it | Unit tests: every field placed, nothing off the page at every size and shape, words kept across a design and size change, a long occasion fits |
| 4 | **Start screen and Card details**: the Invitation cards row, the Card details form, the Designs strip and the quick picks, with phone sheet and Dark | Typing in the form and on the card change the same item, with one Undo step each (browser test) |
| 5 | **The other card designs and fonts**: Regimental (with the colour band setting), Ceremonial photo, Bilingual and Modern, plus Cinzel and Pinyon Script with Credits | Each design opens, takes all fields in English and Hindi, and saves (browser test) |
| 6 | **Logos**: Change logo (own file or Crests and logos), Add second logo, Remove, and the empty logo place left out of Save | An own PNG logo keeps its clear background in the saved PDF; a removed logo stays removed across a design change |
| 7 | **Front and back**: the optional back page, Add and Delete the back, Front / Back switch, the programme field, a two-page PDF, and front, back or both for PNG and JPG | A two-sided card saves a two-page PDF of the right paper size; an old draft opens as one-sided |
| 8 | **Print sheets**: as many cards as fit on A4 or A3 for any size, or fewer, with cut marks, and the back placed for a long-edge flip | The PDF's card positions and cut marks match the paper for every size (unit test on the placement numbers); 8 A7 and 10 visiting cards fit on A4 |
| 8b | **Folded cards and pocket cards**: A5 folding to A6 and A4 folding to A5 (outside and inside pages, fold line shown in the editor and never saved), and the Entry card or car pass, RSVP card, Programme card and Pocket invitation designs | A folded card saves a two-page PDF whose panels land on the right side of the fold when printed both sides |
| 9 | **Guest list**: paste or a .csv or .txt file, up to 300 names, a preview of the first three, page count and size estimate, one PDF, Clear names | 300 names make one PDF without the browser's memory growing past one card (browser memory test, as for Save) |
| 10 | **Posters**: the Posters row, Poster details, the four designs, the logo question, and No logo moving the heading up | A poster opened and saved without choosing a logo has no "Add logo" box in the file |
| 11 | **Docs**: the guide [../collage-studio.md](../collage-studio.md) gains Cards and posters, and the page contract test lists the new scripts, fonts and logo path | Docs match what is built |
| 12 | **Cold review** (first-time use, editor code, tests) with a test account that is not an admin | Scores reported; findings planned |

## Deployment

- Only static files change: scripts, styles, fonts and one image. There's no database
  migration, no new setting and no new network dependency. Everything ships with PRISM and works
  offline on the LAN.
- Scripts are linked with `asp-append-version`, so a browser loads the new scripts on its next
  page load, even while old and new servers overlap.
- Drafts are in each browser:
  - a draft from before this change opens as before;
  - a card draft opened by an older copy of the page (an old cached tab) is read safely by the
    existing draft reader, which drops parts it does not know rather than failing.
- Rollback: revert the PRs. Card and poster drafts made in between keep their photos and text.
  They open in the older page as collages without their shapes.

## Not in this plan

- A2 or larger posters, and bleed for a press.
- Reading .xlsx files directly.
- Envelopes, place cards, table plans and menu cards. These could reuse the same designs later.
- A QR code for the venue.
- Administrator-made designs for a whole unit.

## Sources

Card sizes were checked on 18 Nov 2026 against:

- [Paperlust: wedding invitation size guide](https://paperlust.co/blog/?p=11538)
- [4over4: standard invitation sizes and envelopes](https://v2.4over4.com/guide/invitation-sizes-the-complete-guide)
- [Papercard: paper and card size guide](https://www.papercard.co.uk/paper-card-size-guide)
- [Zola: a guide to wedding invite sizes](https://www.zola.com/expert-advice/wedding-paper-guide/how-to-invitations/a-guide-to-wedding-invite-sizes)
- [Proper invitations: a Washington primer](https://whcinsider.com/2009/05/24/proper-invitations-a-washington-primer/) (the 4 × 5 in service correspondence card with crest)
