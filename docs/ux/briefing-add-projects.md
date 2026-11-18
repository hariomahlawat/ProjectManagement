# Briefing decks: Add projects redesign

Owner request, 17 Nov 2026: Add projects "needs to be more user-friendly". Wanted: all ARPP-listed
projects of a financial year at once; technical category; project category; completed within a year
range; no "My projects"; individual projects; several values at once; mixing filtered sets; a
chip-based way of choosing. Mockup agreed in the session ("Yes, build it").

## Today

The dialog has five tabs (Quick add, Recently completed, Project category, Technical category, Manage
individually). Each tab adds one kind of set; they cannot be combined ("ARPP FY 2026-27 and VR"
is impossible), there is no ARPP choice, completion is by calendar year, and individual adding is
a separate search with its own Apply.

## Design

One screen instead of tabs:

- A search box (name, PO, case file number, category).
- Filter chips, added and removed: **ARPP** (financial years), **Technical category**, **Project
  category**, **Status** (ongoing, completed), **Completed between** (financial years, from and to),
  **Proliferation** (available). Values inside one chip widen the list (any of them); different chips
  narrow it (all of them). A parent category includes its children.
- The matching projects as a list with a tick on each row; "Tick all not in deck"; rows already in the
  deck are shown as "In deck" with **Remove**.
- **Add n projects** saves at once with Undo, as today. Changing the chips and adding again mixes
  sets ("ARPP FY 2026-27", then "Completed FY 2023-24 to 2025-26").

Owner choices (the recommendations, accepted with "Yes, build it"):

1. A fixed list: adding copies today's matching projects into the deck; the deck does not follow
   the filter later.
2. "Completed between" in financial years (April to March), as ARPP is.
3. "ARPP FY X": every project in any published ARPP issue of that year (the main issue and its
   addenda), unless its latest entry in that year is Delisted.
4. Removing happens in the same list ("In deck" rows have Remove).
5. "My projects" goes.

## Plan

1. **Server filter** (built, #3420). `ProjectBriefingProjectFilter` and `FilterAsync` on the selection
   service: every chip as a condition, values within a chip any-of, chips all-of; ARPP financial
   years and their counts in the options; a page handler returning the matching projects with
   "in deck". Tests for each filter and their combination.
2. **The dialog.** Chips, search, the tick list, Tick all, Remove, Add with Undo; the five tabs and
   "My projects" removed. Built: ticks are kept when the filters change, so sets mix; Add posts
   the ticked projects through AddSelection (Undo as before); Remove on an "In deck" row asks
   first, as the project's notes, photo choice and place in the deck go with it (no Undo: adding
   it back starts afresh); Escape closes an open filter and returns to its chip; "Clear filters" resets all.
   Checked in the browser on a copy of the dev data (ARPP FY 2026-27 37, with Driving Smls 6;
   Tick all, Add, Undo; ticks kept when a filter is cleared; Remove; completed FY
   2023-24 to 2025-26 16).
3. **Phones, Dark, keyboard, guide.** Chips wrap, the list full width on phones; keyboard order and
   labels; the Studio guide and this document updated. Built: checked at 375 px in the dark theme
   (no sideways scroll, chips wrap, one column, Add at the foot); the search's placeholder is
   short enough for a phone ("Search name, officer or file no."); the empty Studio's prompt names
   the new filters; the old tabs' styles (48 selectors) and the quick-add "Added" code are gone.

## Cold review (18 Nov 2026)

UX 7, code 7.5. Fixed in two PRs.

**PR A, the dialog:**
- Add waits for its save, so a double click no longer re-adds the same projects and reports that nothing was added (High).
- The status and Undo moved to the footer, and the search, filters and count stay in view while the list scrolls (M1).
- "n ticked · Review" lists every ticked project, including those the filters hide (M2).
- Escape on a chip closes its filter, not the dialog.
- A failed load clears the list.
- Row text is larger.
- An open chip is shaded.
- Option counts are labelled as totals.
- One Add is limited by the deck: a deck holds at most 120 projects (second cold review).

**PR B, the server:**
- Categories in tree order.
- The dialog's counts load when it opens.
- The old search handler and selection kinds removed.
- Comments corrected.
- Handler and edge-case tests added.


## Second cold review (18 Nov 2026)

UX 7, code 7.5, on new findings. Fixed in one PR.

**The dialog script**
- It moved to its own module, `project-briefing-add-projects.js`, with behaviour tests in a simulated browser (`project-briefing-add-projects.test.js`), instead of tests that matched the code's text.

**The deck limit (120 projects)**
- The footer says "Room for N more in this deck". When more are ticked, it reads "untick M" before Add.
- Add sends nothing when the ticks do not fit.
- The server's message now names the room left.

**Loading the choices**
- The choices say they are loading while they load.
- A failed load says so, with Try again.

**After an Add**
- A refused Add leaves the deck "Saved" rather than "Not saved".
- "In deck" always comes from the deck as the page knows it, so a late list reply cannot offer projects just added.
- Ticked projects that the server could not add (archived, deleted or cancelled) are unticked and named.
- Undo after an Add ticks those projects again.

**Reviewing the ticks**
- The filters wait while the ticks are reviewed.

**Filter details**
- The completed range is put the right way round where it shows.
- The range lists the last ten financial years and the earliest.
- The range panel says how many completed projects have no completion date.
- The proliferation filter shows its count.
- Each filter panel has a Close button, which helps on a phone.

**Not changed**
- The list reads ARPP entries only when the ARPP filter is set, and the choices once per opening; at about 200 projects that is cheap.

## Third cold review (18 Nov 2026)

UX 7.5, code 8; no High. Fixed in one PR.

**When projects cannot be added**
- If none of the ticked projects can be added (all archived, deleted or cancelled since the list was shown), the refusal names them. The dialog unticks them and stops offering them, so Add does not fail the same way again.
- The wording is about ticked projects, not the old "selected criteria".
- An Add that gets no answer within a minute stops, says so, and enables Add again.

**A full deck**
- When the ticks fill the room left, projects not ticked cannot be ticked; ticked ones can still be unticked.
- A full deck says so ("This deck is full") and offers no Tick all.
- Tick all ticks only as many as fit ("Tick the first N that fit").

**Loading**
- A list that fails to load offers Try again.
- The list loads each time the dialog opens, so new, archived or re-categorised projects show as they are now. Ticks stay.
- "Finding projects…" shows only when an answer takes longer than 0.4 s, so screen readers do not read it on every keystroke.
- A failed load of the choices shows the sign-in or access message when there is one.

**Accuracy**
- Category counts take in inactive sub-categories, as the filter does. Inactive categories are still not offered.
- "%" and "_" in the search match themselves.
- "Ongoing" with "Completed between" explains why nothing matches.

**Screen readers and phones**
- The room line is read out only when the ticks go over the room or come back within it.
- The "n ticked" button no longer has a pressed state on top of its changing label.
- On a phone the search and chips stay in view: the chips scroll sideways, and an open filter scrolls within 38% of the screen.
- Remove's question always names the project.

## Fourth cold review (18 Nov 2026)

UX 8, code 8; no High (target met). The remaining findings are fixed in one PR.

**Keyboard focus is never dropped**
- After "Tick the first N that fit" hides itself, focus goes to the "n ticked" button.
- Unticking the last project in Review moves focus to the search.
- A filter opened while its choices load keeps focus on its chip; the choices take it when they arrive.

**The list request**
- Completed projects (most of the list) no longer read their stages: they show "Completed".
- A search that is unchanged after trimming (an added space) sends nothing; the wait after typing is 0.3 s.
- A PostgreSQL test (`ProjectBriefingFilterPostgresTests`, run in the database workflow) covers the search with escaped wildcards, officer name and rank, the category tree, ARPP, proliferation and combined filters.

**Screen readers**
- Going over the room, and back, is announced once each through an always-live hidden region; the room line itself is not live.
- A held-back tick is described by the room line.
- Each tick is named by the project's name; its details are its description.
- The filter panel has its id in the markup, so the chips' `aria-controls` always points at it.

**Smaller fixes**
- When none of the ticked projects can be added, the names follow the server's message without repeating the reason. When some were added, the note about the rest comes before Undo.
- The choices are fetched again, quietly, each time the dialog opens, so their counts follow the list. A failed choices load shows the server's message.
- The completed range note says a completion year with no date counts from April of that year. "Show all N years" lists every year, and a range set to an older year shows them all.

## Fifth cold review (18 Nov 2026)

UX 8, code 8; no High. The findings are fixed in one PR.

**Undo**
- Undo keeps the keyboard in the dialog: focus goes to the "n ticked" button (or Add). The message says the projects are ticked again.
- The rows each Add sent are kept by project across Adds, so Undo ticks the right projects whatever was added or refused since.

**Load**
- The list draws the first 200 rows, with "Show all N projects". The count and Tick all cover every match.
- The choices are fetched again on opening only when they are over two minutes old.

**Smaller fixes**
- Choice counts show only when no other filter or search is set; otherwise the note says so. The counts are of all projects and would contradict the list.
- Escape anywhere in the dialog body closes an open filter first, the search included.
- The smallest filter text is now 0.78rem.
- Wording:
  - "These ticks use the deck's last N places."
  - The Review count reads "Showing the N ticked projects, whatever the filters."
- The completed range lists only the years some project was completed in.
- The list reply carries the deck's size, so the room line follows another editor's changes until the page has a newer deck.
- A list rebuilt under someone in it keeps them on the same row.

**Tests**
- A behaviour test of the page's own Add and Undo (`project-briefing-add-undo.behaviour.test.js`).
- A full deck takes projects it already holds again and refuses a new one.

**Open question for the owner**
- A completion year with no date counts from April of that year. If `CompletedYear` holds a calendar year, a project finished in January–March lands one financial year late.
