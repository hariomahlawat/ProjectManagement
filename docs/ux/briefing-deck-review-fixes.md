# Briefing decks: review fixes (16 Nov 2026)

A cold review of `/Workspace/BriefingDecks` after the blocks plan (`briefing-decks.md`, "Plan:
Blocks, Project Officer decks and focus view") was finished. It reviewed master at 28830977e plus
the PR 8 changes, which are since merged as #3376.

**Scores: UX 7/10, code 7.5/10.** No High findings.

- **Sound:** authorization (every change goes through the editable loader; download, duplicate
  and preview need view), row versions on every change, and rollback safety of the stored settings.
- **Weak:** loose ends of the rework (a second way to add slides, leftover handlers), a few
  wrong numbers and terms, and missing handler tests.

## Findings

**Medium**

1. **SDD profile cannot be added on a new deck.** The default profile includes the Partnerships
   module with no partners, so the server refuses it ("Add at least one MoU or partner…"). The row
   appears, then disappears, and keyboard focus is lost.
2. **Two ways to add a slide.** The outline's **+ Slide** opens the old "Slide library" dialog.
   - It posts the whole page.
   - Its list is drawn when the page loads, so after an Add or Remove in the Deck tab it is wrong.
   - It hides opening slides that are switched off but still stored in the slide order.
3. **Key points only offers capability text it never shows.** The inspector offers "Capability
   overview" and "Write for this deck only" for this design, but the text never reaches a slide.
   The flag is fixed when the page loads, so it also goes stale after a design change.
4. **Wrong slide count.** The estimate counts capability continuation slides for Key points
   only, which draws one slide per project. It also counts the stage-wise distribution as two
   slides, though its project list can run longer.
5. **Mixed terms.**
   - The library's Template column and Switch deck show the template the deck started from,
     not its design.
   - One overview slide has three names: "Project-category distribution" (Deck tab),
     "Project-category summary" (slide title) and "Category summary" (outline).
   - The Deck tab says "Add block" and "slide" side by side.
   - The SDD profile editor still says "Additional slide" and "Turn it on or off".
6. **Leftover code from the rework**, still reachable over HTTP but guarded by the access check:
   - template apply and undo, and its banner;
   - the old additional-slide handlers (toggle, move, reorder, remove) and `RemoveProject`;
   - script for markup that is gone;
   - `PresetAdjusted`, worked out on every page load.
7. **Test gaps.**
   - No page-handler tests per role. Plan PR 2 promised them; access is tested only in the service.
   - The block-list script is tested by matching its source, not by running it.

**Low**

1. Delete and Remove project answer a server error (500) when a command user posts against a
   Project Officer's deck (stale tab or crafted request), instead of a message.
2. Messages say "shared command deck" on Project Officers' decks too.
3. The preview does not jump to the affected slide for some changes:
   - stage, status or cost changes on Key points only;
   - adding the status table, which shows a project slide instead.
4. The cost chips sit under "Project slides" but also set the status table's columns, and the
   label does not say so.
5. Two rules are repeated in the script (design to settings; status table and project slides to
   "what the deck shows"). The server re-derives both, so they are harmless today but can drift.
6. Creating a deck from a template saves twice; if the second save fails, a deck with default
   settings is left (not reproduced).

## Owner decisions

The owner chose every recommended option on 16 Nov 2026: A, B, C and D as recommended below.

- **A (finding 1).** Either the default SDD profile leaves Partnerships out (recommended: it can
  be ticked in the editor), or adding an unconfigured profile opens its editor first.
- **B (finding 2).** **+ Slide** opens the Deck tab's Add block library, and the old Slide
  library dialog and its handlers go (recommended). Or keep both and refresh the dialog.
- **C (finding 5).** The library's "Template" column shows the design (Capability, Key points,
  Brief, Fact sheet, Key points only), renamed **Design** (recommended).
- **D (finding 5).** One word for the Deck tab's pieces: **slide** everywhere ("Add slide",
  recommended) or **block**.

## PRs

1. This plan.
2. **SDD profile from Add block** (finding 1, decision A). On a refused add, focus returns to
   Add block and the message offers the editor. Test: a new deck can add the SDD profile.
3. **One way to add slides** (finding 2, decision B; Low 3). **+ Slide** opens Add block; the old
   dialog, `ToggleAdditionalSlide` and the other additional-slide handlers and their script go.
   Adding the status table jumps the preview to the table.

   Built (16 Nov 2026). The outline's **+ Slide** opens the Deck tab on Slides in this deck with
   the Add slide library open and focus on its first entry. Gone:
   - the Slide library dialog and its styles;
   - the page handlers `ToggleInstitutionalProfile`, `ToggleAdditionalSlide`, `MoveAdditionalSlide`,
     `ReorderAdditionalSlides` and `RemoveAdditionalSlide`, with the service methods only they used;
   - the script for the additional-slide list, its order form and remove form.

   Adding the status table now shows the table in the preview (a `StatusTableBlock` setting key).
4. **Key points only follow-through** (findings 3, 4; Low 3). The inspector's capability offer
   follows the design from each deck payload; the slide estimate branches on the design and counts
   the stage-wise list's pages. The preview follows stage, status and cost changes on Key points
   only.
5. **Terms** (finding 5, decisions C and D; Low 2, 4).
   - Library and Switch deck show the design.
   - One name per overview slide.
   - One word in the Deck tab.
   - The profile editor's wording.
   - "Project Officer's deck" in messages.
   - The cost chips say "(slides and status table)".

   Built (16 Nov 2026).
   - The deck library's **Design** column (was Template), its Continue card and Switch deck name
     each deck's project slide design.
   - The category slides are titled "Project-category distribution" and
     "Technical-category distribution", as in the Deck tab, and the outline calls them
     "Category distribution". The block equivalence fixture was re-recorded for the new titles.
   - The Deck tab says **Add slide** throughout.
   - The SDD profile, Role & Charter and FFC footprint editors say "Slide content" and "Add or
     remove it, and move it, in the Deck tab under Slides in this deck".
   - Messages say "The briefing deck was not found." rather than "shared command deck".
   - Under the parts chips: "The cost choice also sets the status table's cost columns."
6. **Leftover code** (finding 6). Remove template apply and undo, `PresetAdjusted`, the unused
   handlers and dead script, with the contract test that pins them. The Delete and Remove project
   errors become messages (Low 1).
7. **Tests** (finding 7). Page-handler tests for a Project Officer and a command user on a
   personal deck: UpdateBlocks, SaveSettings, Generate, PhotoOptions, Delete. Plus one test that
   runs the block-list save: a refused change puts the block back and focus lands sensibly.
   - **Built** (16 Nov). `ProjectBriefingDeckPageHandlerTests` posts to the page handlers with
     the real deck service: the owner can change, generate, delete and pick photos; a command user
     gets a message (never a 500) on each, and can still Download and Duplicate; a command deck
     reads as not found to a Project Officer. Delete and Remove project now catch the view-only
     refusal, and Delete a failed save (Low 1).
     `project-briefing-deck-blocks.behaviour.test.js` runs `saveBlocks` in jsdom: a refused switch
     or Remove puts the slide back with the server's message, a 409 shows the conflict banner.
   - Found: a command user's settings save says "The shared command deck was not found." rather
     than the view-only message (for PR 5's wording).
8. **Smaller code tidy** (Low 5, 6). Save replies return the derived settings so the script stops
   repeating the rules, and create-from-template saves once. Then a fresh cold review.

   Built (16 Nov 2026).
   - Creating a deck from a template applies the template in memory (`SetFromPreset`) before the
     deck's one save, with one audit entry; a template that fails leaves no deck behind.
   - The page no longer repeats the server's rules. The hidden "mirror" inputs, and the
     table-and-slides-to-mode rule that fed them, are gone: nothing read them any more. The
     design card sets only the layout (which decides the reload); the server derives what each
     project gets.

PRs 2, 4 and 7 touch different files and can run in parallel. PR 3 goes before PR 5, because both
touch the slide-adding wording. PR 6 goes after PR 3, because PR 3 removes some of the same
handlers.
