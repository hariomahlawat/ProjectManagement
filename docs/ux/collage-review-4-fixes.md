# Collage Studio: fourth review fixes (plan, 16 Nov 2026)

All fifteen PRs of [collage-review-3-fixes.md](collage-review-3-fixes.md) were merged on 16 Nov
2026. A fresh cold review followed. Three new reviewers took part, none of whom had seen the work
or the earlier plans:

- one read the editor code;
- one read the server and the tests;
- one used the real page as a first-time user, through the browser test harness, at 1366 and
  1920 wide, on a 390 px touch phone, in the Dark theme and with the keyboard only.

It scored:

| Area | 1st | 2nd | 3rd | 4th review |
| --- | --- | --- | --- | --- |
| First-time use (UX) | 7.5 | 7.5 | 7 | **7.5** |
| Editor code | 7 | 7.5 | 7.5 | **7.5** |
| Server and tests | 8 | 8 | 8 | **8.5** |

There was no High finding in the code. The UX review found one High problem, and it was checked
against its screenshot before this plan was written. The owner's aim is still **at least 9**.

The guide is [../collage-studio.md](../collage-studio.md).

## What the review found

### First-time use

**High**

- **U1. A kept locked photo can empty the new layout.**
  - Steps: lock the big photo of "1 big, 2 small", choose "4 grid", then "Keep it where it is".
  - The big frame is a little wider than half the page, so it touches all four grid frames. All
    four are left out, and half the page goes blank.
  - The rule leaves out any frame the photo covers by more than 5%. That is far too strict.

**Medium**

- **U2. Toasts cover the selected item's toolbar.** Both sit at the bottom centre, so Delete,
  Lock and the rest cannot be reached until the toast goes.
- **U3. Stale toasts.**
  - After Undo of "Make it casual", its "Undo" toast is still showing.
  - The note that the collage is named after its words follows the user to the start screen.
  - Toasts show over the Save dialog without being dimmed.
- **U4. "Make it casual" pushes photos off the page.** Prints are cut at the page edge and
  overlap heavily. Preview, and so Save, show the same.
- **U5. On a phone, the toolbar covers the photos.** A tap meant for a photo lands on the
  toolbar. With a sheet open, the page gets about 40% of the screen.
- **U6. On a phone, adding text puts focus in the Words box at once.** The keyboard covers the
  page, and the Hindi hint speaks of the Windows keyboard.
- **U7. A hidden item is gone from the editor.** There is no outline, so a reopened draft looks
  as if a photo was lost. Save does not say that hidden items are left out.
- **U8. New words can be hard to read.** Headings still land over photos, and the dark default
  colours (navy, maroon) are hard to read on them.

**Low**

- Delete on a locked photo says "It is locked" only to a screen reader.
- In Save, the purposes not chosen have pale text and look disabled.
- A note about frames going with the Square shape sits under the frame buttons, not by the shape
  just chosen.
- While moving a photo in its frame, the part outside the frame is not shown dimmed.
- Filling frames ignores orientation: a tall photo goes into a wide frame and is cut hard.
- On the start screen:
  - after a draft is deleted, "Your drafts" stays over an empty gap;
  - a draft card reads "Saved just now ·", with a dot and nothing after it.
- In the editor:
  - the collage name looks like grey placeholder text, not like something to edit;
  - the crest tile has no label;
  - the Stickers help sends the user to "Credits on the start screen".
- Undo history ends when a draft is reopened, without saying so.
- Empty "Add photo" frames are not Tab stops; the keyboard reaches them only through Layers.

### Editor code

**Medium**

- **E1. The read-only state still hands out the live collage.** `state.collage` returns the
  model itself, and most parts change it directly, as do the `changes` log and the `photos` map
  they are given. A part that forgets `commit()` reaches neither Undo nor the draft. Each part
  is also handed 20 to 45 loose helpers.
- **E2. Drawing text changes the model.** `layoutTextItem` sets the width and shape while it
  draws, and again when a font finishes loading. Neither goes through the change log, so the
  next unrelated commit carries the change, and Undo can bring back a width measured with a
  fallback font.
- **E3. One frame's corners become the layout's.** Changing one frame's corners sets
  `layout.radius`, so the next layout rounds every frame.
- **E4. Some sliders skip the change log while they move.** These are the layout's space and
  corners sliders and the background colour picker. Switching apps on a phone mid-drag keeps the
  old values in the draft.
- **E5. A closed database is never noticed.** There is no `db.onclose`. If the browser closes the
  connection, every later write fails until the page is reloaded.
- **E6. Leaving a collage does not free its photos.** `resetEditor` clears the map but does not
  release the working copies. The same is true for a photo decoded just before a stale import
  returns.

**Low**

- Backspace or Delete removes the selected item even when focus is on a button.
- A commit that changes nothing still counts as a change, so an Undo toast can say "something
  else has changed".
- Leaving gives up after five save rounds and drops a change still pending.
- Dead code and stale comments:
  - an orphan doc comment and an unused helper in `collage-start.js`;
  - `thumbnailOf` taken by the tray and not used;
  - two comment blocks saying the same thing;
  - module headers that list helpers the module is not given;
  - `prepareNew`, which only wraps `makeRoom`.
- The later parts work only because of declaration order.
- Test gaps:
  - two-finger zoom, and moving a photo in its frame;
  - leaving the editor and closing the page;
  - a draft written back across tabs;
  - a closed database;
  - a font load changing the model;
  - nothing checks that every model change goes through the change log.

### Server and tests

**Medium**

- **S1. The EF model snapshot has no `CollageSticker`.** The next generated migration would try
  to create the table again. This is a repo-wide gap: migrations are written by hand because the
  snapshot lags. Collage adds to it.
- **S2. Sticker addresses ignore the app's path base.** Library addresses use it. If PRISM were
  served under a sub-path, crests would be lost. Production is not known to use a sub-path.
- **S3. The browser harness still differs from production at the edges.**
  - Mocked library answers carry none of PRISM's headers.
  - Nothing tests a session that ends mid-edit (401 or a sign-in redirect), or an API failure.

**Low**

- The "60 already in use" refusal is not audited.
- A failed refusal audit gives a 500 page instead of the plain message.
- Only Add and Rename are tested on real PostgreSQL. Move, Restore and Retire are not.
- The library page number can ask for an offset of about 480,000 rows.
- Leftover picture files are only logged.
- `Version` is never raised, which matters only if pictures can one day be replaced.
- An oversize upload sent again writes another audit entry.

## Decisions taken in this plan

These follow the owner's earlier choices, and none changes anything the owner decided:

- **A kept locked photo** leaves out only the frames it covers by more than half. The question
  says how many frames that is.
- **Make it casual** keeps every print on the page. A corner may go past the edge by no more than
  the page's own bleed.
- **On a phone**, the toolbar docks above the bottom bar instead of floating over the page.
- **Hidden items** show as a faint dashed outline in the editor only, never in Preview or Save.
  Save says how many were left out.

## Build order (small PRs)

Each PR is merged only when Codex has no findings on its head and CI is green, and each fix comes
with a test that fails without it. PRs that touch different files are built at the same time,
each in its own worktree.

| PR | What | Done when |
| --- | --- | --- |
| 1 | This plan | Merged |
| 2 | **Locked layout (U1, Low)**: a kept locked item leaves out only frames it covers by more than half, and the question names how many. Delete on a locked item shows a short toast | "1 big, 2 small" with the big photo kept, then "4 grid", keeps at least two frames |
| 3 | **Toasts (U2, U3)**: toasts stay clear of the selected item's toolbar and the phone's bottom bar. A toast whose Undo has been used, or whose step is undone, goes. Editor toasts close when the user leaves the editor and are dimmed under a dialog | No toast covers the toolbar at 1366, 1920 or 390 |
| 4 | **Casual, filling and words (U4, U8, Low)**: casual prints stay on the page and overlap less. Frames are filled by matching orientation. New words go to free space, with a readable default (a light outline or label) over photos | Casual Preview shows every print whole; three headings are readable over photos |
| 5 | **Phones (U5, U6)**: the toolbar docks above the bottom bar. Adding text does not focus the Words box on a touch screen. The Hindi hint fits the device | The toolbar never covers a photo at 390 px |
| 6 | **Hidden items and Save (U7, Low)**: a faint outline for hidden items in the editor. Save says "N hidden items left out". Purposes not chosen read clearly. A reopened draft says Undo starts again from here | A hidden photo is visible as an outline and named by Save |
| 7 | **Polish (Low)**: the shape note sits by the shape; the part outside the frame shows dimmed while moving a photo; the empty drafts state; the draft card's trailing dot; the name field looks editable; the crest tile and the Credits link; empty frames are Tab stops | Every UX Low is fixed or written down as a decision |
| 8 | **Editor bugs (E3, E4, E5, E6, Low)**: one frame's corners stay its own; the space, corners and colour sliders hold their edit; `db.onclose` reopens the database; leaving frees photos (stale imports too); Backspace on a button does nothing; a no-op commit does not count; leaving keeps a pending change | Each bug has a test that fails without the fix |
| 9 | **Model operations (E1)**: a `collage-model.js` holds the named changes to the collage (place, delete, reorder, set background, set layout and so on). Each change clones, changes and commits. Parts are given these operations, not `state.collage`, `changes` or `photos` | A test finds no part that changes the model directly |
| 10 | **Text measured outside the model (E2)**: measurements live in a side cache. The model's width changes only inside a commit, and a font load records its change properly | A font load never changes the model unseen (test) |
| 11 | **Grouped parts (E1, Low)**: the loose helpers become a few grouped interfaces (page, panels, drafts, messages). Parts are created in an order that needs no lazy later-parts view. Dead code and stale comments go | No part is handed more than about ten names |
| 12 | **Editor tests (gaps)**: two-finger zoom and moving a photo in its frame; leaving and closing the page; cross-tab write-back; a closed database; font loads | Each gap has a browser or unit test |
| 13 | **Server (S1, S2, Low)**: `CollageSticker` added to the model snapshot, with a test that the Collage tables match it. Sticker addresses built with the path base on the server and in the scripts. The 60-limit refusal audited, and a failed refusal audit shows the plain message. A lower page cap. PostgreSQL tests for Move, Restore and Retire | Each server finding has a test |
| 14 | **Harness edges (S3)**: mocked answers carry the production headers. Browser tests cover a session ending mid-edit (401 or sign-in redirect) and an API failure. The editor handles both with a plain message | Both cases have browser tests |
| 15 | **Docs**, then a **fresh cold review** | Scores reported; anything short of 9 gets its own plan |

**What can run at the same time:**

- PRs 2 to 7 (UX), PR 8 (editor bugs) and PRs 13 and 14 (server and harness) touch mostly
  different code, so they can be built together.
- PRs 9, 10 and 11 rework the editor's parts, so they follow one another after the UX PRs.
- PR 12 comes after them.

## Deployment

- **Browser changes:** no migration and no new setting.
- **Server changes:**
  - The snapshot change adds no migration.
  - Sticker addresses are unchanged when PRISM is served from the site root. How production is
    actually served has not been checked: confirm the deployed path base before calling this change
    production-neutral. Under a sub-path, crests that fail today would start to work.
  - The rest is additive: more audit entries, a lower page cap, and tests.
- **Internet:** nothing needs it.
