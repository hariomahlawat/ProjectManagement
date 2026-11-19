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
  Card details; a phone test covers the field being focused and in view.
