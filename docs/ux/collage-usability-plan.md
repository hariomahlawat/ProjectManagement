# Collage Studio: fast and easy for new users (plan, 19 Nov 2026)

Collage Studio (`/Tools/Collage`) makes invitation cards, posters and collages. All three are
needed, and **cards and posters may be used more than collages**. This plan keeps every feature.
It changes only how quickly a new user reaches the part they came for, and how much they see
once they are there.

The guide is [../collage-studio.md](../collage-studio.md). The card and poster plan is
[invitations-posters.md](invitations-posters.md).

## How the review was done

On 19 Nov 2026 the page was used as a first-time user through the browser test server
(`tests/browser/serve.js`, no sign-in), at 1440 × 900 and on a 375 px phone. The reviewer:

- opened "4 grid" and dropped in six photos, some wide and some tall;
- changed to "6 grid";
- opened Save;
- opened the "Classic ivory" invitation card and clicked its words;
- selected one photo and counted the settings shown.

The owner then confirmed that cards, posters and collages are all required, and that cards and
posters come first.

| Task | Score now | Aim |
| --- | --- | --- |
| Make an invitation card | 7.5 | 8.5 |
| Make a quick collage | 6.5 | 8.5 |
| Depth for a confident user | 8.5 | keep 8.5 |

## What works and must stay

- **Save** asks "What is it for?" (WhatsApp or email, Printing, A slide or a screen), with type
  and quality folded under More options. It needs no change.
- **Card details** is a form (Host, Occasion, Date and time, Venue, Dress and the rest) with
  ready-made phrases. It suits users who are not designers.
- The rail, the floating toolbar, the drafts that save themselves, Undo, and Preview.

## What makes it slow

1. **F1: the start page asks too much at once.** It has 38 buttons in five sections: drafts,
   cards (with a size list, a custom size and four pocket-card buttons), posters (with a size
   list), 11 layouts and 7 blank sizes. On a phone the collage layouts start about two screens
   down. A new user has to read everything to find their way in.
2. **F2: a collage starts with a guess.** The user must choose how many frames before adding
   photos. Six photos on "4 grid" left two out, with the message "choose a layout with more
   frames", but no button for it. Nothing in Layouts says which layouts fit the photos.
3. **F3: photos land out of order.** New photos are matched to empty frames by shape, tall to
   tall (`assignToSlots` in `collage-core.js`). When every frame has the same shape, as in a grid,
   a batch of mixed wide and tall photos is scattered: the top row read P1, P3, then P5. The
   photos left out were the tall ones, not the last ones.
4. **F4: card words have two places to edit.** Clicking the occasion on a card opens a second
   "Words" box with a font panel on the right, while Card details on the left edits the same
   words. Users do not know which one counts.
5. **F5: one photo still shows a long panel.** Five groups are already folded and closed at
   first (Angle, See-through, Order, Line up, and Brightness and the other adjustments;
   `fold()` in `collage-settings.js`). About 30 controls still show at once: In its frame, Zoom,
   Caption, Frame, Shape, Look and Size, with 54 in all. The toolbar already has Order, Lock and
   Delete.
6. **F6: the help is a fixed list.** With nothing selected, the right panel shows a "How to" list
   about dragging photo corners and turning handles, even on a card, where it does not apply.

## The PRs, in order

Each PR is small, changes the browser only, and adds or updates its unit tests
(`npm test`) and browser tests (`npm run test:browser`). None changes the server, the database,
the draft format in IndexedDB, or what Save makes. Drafts made before and after each PR open the
same way. Each can be reverted on its own.

### PR 1: this plan

### PR 2: a start page with three clear choices (F1)

- The top of the start page shows three large tiles of equal weight, in this order:
  **Invitation card**, **Poster**, **Collage**. Each has one line saying what it makes. Drafts
  stay above them, as now.
- Choosing a tile opens only that section below it:
  - **Invitation card:** the designs, with the card size, custom size and pocket cards beside the
    designs, not above them.
  - **Poster:** the designs, with the poster size.
  - **Collage:** a large "Add photos" area (PR 3), then the layouts and the blank page sizes.
- The last tile chosen is remembered in this browser (`localStorage`, read and written inside
  `try`/`catch`; the page works the same without it). The first visit opens with Invitation card.
- Credits stay at the foot of the page.
- Phones: the three tiles sit in one row of compact buttons, so the chosen section starts on
  the first screen.
- Done when the start page shows at most 3 choices plus drafts before anything is picked, and
  every existing way in (each design, each pocket card, each layout, each blank size) is still
  reachable in at most two clicks.

### PR 3: add photos first, and layouts that fit (F2)

- The Collage section starts with an **Add photos** area: click to choose, or drop. The
  photos open in the layout made for their number. Today's layouts have 2, 3, 4, 5, 6, 7, 8, 9
  and 12 frames. Where none has exactly that many (10 or 11 photos), the next larger one is used.
  One photo fills an A4 landscape page in one frame. Where two layouts have the same count, the
  plain one is used ("1 big, 2 small", "4 grid"): the layout is chosen before the photos are
  read, and photos are still matched to its frames as they come in. More than 12 photos use
  "12 grid", and the rest wait in Photos, as now.
- Starting from a layout or a blank page still works as it does today.
- **Layouts panel:** layouts with as many frames as the collage has photos are marked
  "Fits all 6" (short, so it stays on one line; the button's name says "fits your 6 photos").
- **When photos are left out**, the message gets a button such as "Use a 6-photo layout". One
  click applies it, and one Undo returns.
- Done when six photos dropped on the Collage section open as a full 6-photo collage with
  nothing left out, and the same six dropped on "4 grid" can be fixed with one click.

### PR 4: photos keep their order (F3)

- When the empty frames all have about the same shape, as in a grid, photos fill them in the
  order the user picked them: left to right, then top to bottom.
- Shape matching (tall photo to tall frame) stays only for layouts whose frames differ, such as
  "1 big, 2 small" and "Mosaic of 7".
- When photos are left out, they are the last ones picked.
- Done when unit tests in `collage-core.test.js` cover a grid with mixed photos (order kept) and a
  mosaic (shapes matched), and a browser test drops six mixed photos on "6 grid" and reads
  P1 to P6 in reading order.

### PR 5: one place to type a card's words (F4)

- When a selected text item comes from Card details (it has an `item.field`), the right panel
  opens with a short note: "These words come from Card details: Occasion", and a button
  **Edit in Card details** that opens the Card panel and puts the cursor in that field.
- The font, size, colour and spacing settings for that text stay, under a **Style** heading
  shown below the note.
- Text the user added themselves, outside Card details, keeps its Words box as now.
- Done when no text from Card details shows a second Words box, and a browser test checks that
  **Edit in Card details** puts the cursor in the right field.

### PR 6: help that fits the page (F6)

- With nothing selected, the right panel shows the page size and **one line** of help that fits
  what is open:
  - Card or poster: "Type the words in Card details, or click anything on the card to move or
    restyle it."
  - Collage: "Click a photo to change it; drag it to move it."
- A small **?** button beside it opens a short help dialog. The dialog holds today's tips,
  for the mouse and for touch, and the keyboard keys.
- Done when a card no longer shows photo-dragging tips, and every tip in today's list can still
  be found through **?**.

### PR 7: a shorter photo panel (F5)

- A selected photo shows the essentials first:
  - Replace, and Move the photo in its frame, with Zoom;
  - Caption;
  - Frame (with Shadow);
  - Look.
- Shape and Size join the groups that are already folded (`fold()` in `collage-settings.js`,
  `<details>`, remembered in this browser). The five existing folds stay as they are.
- The Order fold is removed from the panel, because the toolbar already has it. Keyboard users
  keep `[` and `]`, and Layers.
- Phones get the same folding in the settings sheet.
- Done when a selected photo shows at most about 20 controls before a fold is opened, and
  nothing that existed before has gone.

### PR 8: guide, tests and a first-time check

- Update [../collage-studio.md](../collage-studio.md) and its screenshots' descriptions: the
  start page, adding photos first, and the shorter panels.
- Check two timed tasks with someone who has not used Collage Studio, at desktop size and on a
  phone:
  1. Make a Raising Day dinner invitation and save a PDF for printing. Aim: under 2 minutes.
  2. Make one picture of 6 photos for WhatsApp. Aim: 4 steps and under 30 seconds (choose
     Collage, add photos, Save, Save).
- Then run a fresh cold review. The aim is 8.5 or more for both tasks above.

## First-time check after PR 7 (19 Nov 2026)

The guide was checked against PRs 2 to 7, and nothing in it was out of date. Then a reviewer who
had not seen the work used the page as a first-time user through the browser test server, at
1366 × 768 and on a 390 × 844 touch phone, and timed the two tasks.

| Task | Before | After PR 7 | Aim |
| --- | --- | --- | --- |
| Make an invitation card | 7.5 | **8** | 8.5 |
| Make a quick collage | 6.5 | **7** | 8.5 |
| Depth for a confident user | 8.5 | **8** | keep 8.5 |

- **Invitation for a dinner, saved as a print PDF:** about 8 actions, well under a minute, with
  nothing hard to find. The PDF was right. The card's sample date happened to be the task's
  date, so a real user would need about 3 more actions.
- **Six photos, wide and tall, as one picture for WhatsApp:** 4 actions, under 30 seconds, the
  photos in the order picked. Same on the phone.
- No page or console errors in any run.

Both High findings were checked in the code before this plan was written.

- **R1 (High): phones hide the page size.** `.clg-bar .clg-size` is hidden on phones; the size is
  reached only through Layers, Settings, then a list of 30 sizes. A collage for WhatsApp stays on
  A4 landscape, where every frame is nearly square and photos are cut hard.
- **R2 (High): Layouts on a card covers the card.** The rail shows Layouts on cards and posters;
  "4 grid" lays four empty frames over the words.
- **R3: the sample date is fixed.** Every card opens with "Friday, 12 December 2026 at 1930 h".
  A user who skips the field prints the wrong date, and the sample goes stale.
- **R4: Printing gives an A5 PDF.** It defaults to each card on its own page, which office A4
  printers scale or place without cut marks; some print wording reads like jargon.
- **R5: two size menus on desktop** (top bar and Page settings), each with all 30 sizes, cards and
  collages mixed.
- **R6: poster wording.** A poster's help line says "Card details"; the panel is "Poster details".
- **R7: the phone rail is cut off** on a card: "Layers" runs off the right edge.
- **R8: a busy panel after adding photos.** The last photo stays selected, so its long settings
  open.
- **Low:** the Save dialog stays open after saving; Stickers opens on a group of two crests with a
  cut-off name; a poster draft's picture shows the "Add logo" placeholder; a short poster heading
  leaves a gap; the PDF is a picture (the words cannot be selected; not checked closely); the
  phone header's shield has no words.

### Owner decisions (19 Nov 2026)

- A new card's **Date and time** starts empty, with the example as a grey hint. Save says so if it
  is still empty.
- A new collage: a **For: WhatsApp (square) / Printing (A4)** choice beside **Add photos**,
  remembered in this browser. The page size can be reached in the phone's top bar.
- **Printing** a card defaults to **A4 sheets** with cut marks.
- **Layouts** is shown for collages only, not on cards or posters.

### Round 2 PRs, in order

Each is browser-only and small, with unit and browser tests, and changes no server, database or
draft format. Drafts made before keep working: a card opened from a draft keeps its date.

1. **R2 Layouts for collages only.** The rail hides Layouts on cards and posters; Photos, Text,
   Stickers and Background stay. This also frees room for R7.
2. **R1 and R5 page size.** Beside **Add photos**, a choice **For: WhatsApp (square) / Printing (A4
   landscape)**, remembered (`localStorage`, inside `try`/`catch`); the layout made for the photos
   fits that page. One size menu, in the top bar, also on phones (compact), listing the sizes for
   what is open: pages and screens for a collage; card sizes for a card. Page settings keep the
   custom size only.
3. **R3 empty date.** New cards and posters open with Date and time empty and the example as the
   box's hint; the card shows a short dotted line where the date goes. Save names any empty date
   before saving, with **Save anyway**. Drafts keep the date they have.
4. **R4 print.** Save > Printing for a card picks A4 sheets with cut marks; each card on its own
   page stays one click away. The sheet notes say plainly how many cards fit and that cut marks
   are added; no pixel counts on a PDF.
5. **R6, R7, R8 and the Lows.** "Poster details" on posters; Tips lead with what is open; the
   phone rail fits its buttons; after adding photos nothing is left selected; Save closes after
   saving and says so in a toast; Stickers opens on the emoji group; a draft's picture leaves out
   placeholders, as Save does; the phone shield gets its words for screen readers.
6. **Fresh first-time check** with the same two tasks, and a real date to type. Aim: 8.5 or more
   for both.

## Second first-time check, after round 2 (20 Nov 2026)

A new reviewer who had not seen the work used the page as a first-time user on master after round
2 (#3474 to #3479), at 1366 × 768 and on a 390 × 844 touch phone, with a date to type this time.

| Task | First check | Second check | Aim |
| --- | --- | --- | --- |
| Make an invitation card | 8 | **8** | 8.5 |
| Make a quick collage | 7 | **7** | 8.5 |
| Depth for a confident user | 8 | **8.5** | keep 8.5 |

- **Invitation, typed date and venue, PDF for an A4 printer:** 9 actions on a desktop, 10 on a
  phone, about a minute. One A5 card centred on an A4 sheet with cut marks, words right.
- **Six photos, wide and tall, for WhatsApp:** 6 actions, under 30 seconds, a square 1,920-pixel
  JPG in reading order. Wide photos lost about 45% of their width in the tall frames.
- No High finding; no page or console errors.

Mediums (the first was checked in the code: Save opens on the purpose last used):

- **S1. Save does not follow what it is.** It opens on "WhatsApp or email" on a card, and on a
  collage started for printing.
- **S2. Photos first ignores their shapes.** Three wide and three tall photos go into six equal
  frames.
- **S3. Saved, but not seen.** With More options open, the "Saved" line is below the dialog's
  scroll on a desktop.
- **S4. Print choices are many and wordy**; A3 is offered to an A4 office.
- **S5. A poster's date band is empty** while its date is, and looks unfinished.
- **S6. Card size has 23 choices** on the start screen.

Lows: start pictures show a sample date the card does not; the Card details panel is narrow;
the photo toolbar has icons only; on a phone the card is small while its sheet is open; the
header's "Photos are not saved in PRISM" beside "Draft saved in this browser"; no share to
WhatsApp on phones (a download, then attach); "Used" on every tray photo; a comma in the default
file name; 11 font styles.

### Owner decisions (20 Nov 2026)

- Save opens on the purpose that fits: **Printing** for a card or poster; a collage on its
  **For** choice; the user's own pick is kept for that piece.
- Photos first picks the layout that suits the photos' shapes.
- A poster's date band stays hidden until a date is typed.
- Fewer choices: **Card size** shows the common sizes, with **More sizes…**; **Print on** shows A4
  sheets and own pages, with A3 and edge to edge under **More**.

### Round 3 PRs, in order

1. **S1 and S3 Save.** The purpose follows what is open (card or poster: Printing; collage: its
   For choice), kept per piece once the user picks; the "Saved" line comes into view.
2. **S2 shapes.** For photos chosen first, among layouts with as many frames, the one whose frames
   best fit the photos' shapes on the chosen page; then tall to tall as now on mixed layouts.
3. **S5 poster band.** The date band and its shape are hidden while the date is empty.
4. **S4 and S6 fewer choices.** Card size: the common sizes, then **More sizes…**; Print on: A4
   sheets and own pages, then **More** for A3 and edge to edge; plainer notes.
5. **Lows** that are quick and safe (labels on the photo toolbar's titles, the header wording, the
   file name), each checked against the tests.
6. **Third first-time check.** Aim 8.5 for both new-user tasks.

## Not in this plan

- No feature is removed, and cards, posters and collages stay in one Collage Studio.
- No change to designs, sizes, stickers, fonts, print sheets, guest lists or the two sides of a
  card.
- No server, database or API change, and no new library. Everything still works offline on the
  LAN.

## Status

- PR 1: this plan, #3461, merged 19 Nov 2026. It corrected one finding: five photo groups were
  already folded (F5).
- PR 2: the three choices on the start page, with the pocket cards moved under the card
  designs. The remembered choice uses the `localStorage` key `prism-collage-start`. #3466,
  merged 19 Nov 2026.
- PR 3: photos first. The Collage section's **Start with your photos** area (choose or drop)
  opens the layout made for their number (`layoutForCount` in `collage-core.js`), one photo in
  one frame filling the page (`withPageFrame`). Layouts marks the ones that fit
  (`layoutsThatFit`), and both "did not fit" messages carry a **Use a 6-photo layout** button.
  Codex found two cases, both fixed: no Fits mark past 12 photos, and a photo used twice counts
  twice (`photosToLayOut`). #3467.
- PR 4: photos keep their order. `assignToSlots` fills frames of one shape (within 10%) in the
  order picked, in reading order, and leaves the last ones over; mixed layouts still match by
  shape. One older browser test expected the shape match with a single frame left; it now expects
  the first photo picked. #3469, merged 19 Nov 2026.
- PR 5: one place to type a card's words. Settings show the field's name and **Edit in Card
  details** instead of a Words box, with the style under **Style**; a double-click or Enter on the
  words goes to the field too. Five card browser tests that typed in the Words box now type in
  Card details; a phone test covers the field being focused and in view. #3470, merged 19 Nov
  2026.
- PR 6: help that fits the page. The "How to" list became one line (`helpLine()` in
  `collage-panels.js`) with a **Tips** button opening a dialog (`data-help-dialog`) that keeps
  every tip, grouped, with the keys. #3471.
- PR 7: a shorter photo panel. Shape (titled with the photo's shape, and open by itself while
  a shape other than Square turns the frames off) and Size join the folded groups. A selected
  photo now shows 23 controls before a fold is opened (tabs and fold headings not counted; about
  30 before), so the aim of about 20 is not quite met: the eight Look tiles are the largest group
  left, and Look is one of the essentials. The Order fold stays, against this plan's first draft:
  its "To the front" and "To the back" are on no toolbar, and the plan keeps everything that
  existed. #3472, merged 19 Nov 2026.
- PR 8: guide checked; first-time check done (invitation 8, collage 7, depth 8). Round 2 planned
  above, with the owner's decisions. #3473, merged 19 Nov 2026.
- Round 2, PR 1: Layouts for collages only. #3474, merged 19 Nov 2026.
- Round 2, PR 2: page size. **For: WhatsApp (square) / Printing (A4)** beside Add photos picks the
  page photos and layouts open on; Printing (A4 landscape, as before) is chosen at first, since no
  default was decided and it keeps earlier behaviour. The top bar's size menu shows on phones,
  narrow, and lists the sizes for what is open; Page settings keep Custom size only. #3476, merged
  19 Nov 2026.
- Round 2, PR 3: the date starts empty. New cards and posters open with Date and time empty, the
  example as the hint; Save's note names it until it has words. Not done from the plan's first
  wording: no dotted line on the card where the date goes (the owner's decision did not ask for
  one; the space shows where it goes), and Save names the empty date in its note rather than
  asking with Save anyway. #3477, merged 19 Nov 2026.
- Round 2, PR 4: Printing a card starts on A4 sheets with cut marks, until the user picks another
  sheet in that dialog; a PDF's summary names its paper, not pixels; the sheet note reads "With
  room left for cutting only one fits; A4, edge to edge fits 2". The flip hint is worked out after
  the sheet is set (Codex). Three card browser tests that check a card's own pages now pick "Each
  card on its own page". #3478, merged 19 Nov 2026.
- Round 2, PR 5: wording and smaller fixes. A poster's help line says Poster details; Tips lead
  with what is open; several photos added at once leave nothing chosen (one photo stays chosen).
  Left as they are, on purpose: Save stays open after saving with its "Saved" line (many flows
  save twice, front and back or picture and PDF); Stickers opens on Crests and logos when the unit
  has some (an earlier owner choice); the phone header's shield already has words for screen
  readers. Not done yet: a poster draft's small picture still shows the "Add logo" place; a short
  poster heading's gap; a PDF whose words can be selected. #3479, merged 20 Nov 2026.
- Second first-time check (20 Nov): invitation 8, collage 7, depth 8.5. Round 3 planned above,
  with the owner's decisions of 20 Nov.
- Round 3, PR 1: Save opens on the purpose that fits what is open (Printing for a card or poster;
  for a collage, the one its page was made for), keeps a pick for that piece while the page is
  open, and brings the "Saved" line into view. The browser-wide memory of the last purpose
  (`prism-collage-save-purpose`) is no longer read. Three Save tests now expect an A4 collage to
  open on Printing. #3481, merged 20 Nov 2026.
- Round 3, PR 2: photos first suit their shapes. With the owner's choice of 20 Nov, five row
  versions are added and shown ("2 in rows", "3 in rows", "6 in rows", "8 in rows", "12 in rows";
  Layouts 11 to 16). Photos chosen first go into the layout for their number whose frames fit
  their shapes best, widest with widest (`layoutForPhotos`); an evenly mixed or tall set keeps the
  plain layout. A mixed set is still cropped somewhere: equal frames cannot fit both shapes.
  #3482, merged 20 Nov 2026.
- Round 3, PR 3: a poster's date band waits for the date. The Event poster's band is hidden by the
  design while the date is empty and shows once it has words (`followBand`); a band the user hid
  or locked is left as it is, and Save names it only when the user hid it (Codex).
