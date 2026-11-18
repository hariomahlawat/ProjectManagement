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
   the ticked projects through AddSelection (Undo as before); Remove on an "In deck" row has its
   own Undo; Escape closes an open filter and returns to its chip; "Clear filters" resets all.
   Checked in the browser on a copy of the dev data (ARPP FY 2026-27 37, with Driving Smls 6;
   Tick all, Add, Undo; ticks kept when a filter is cleared; Remove and Undo; completed FY
   2023-24 to 2025-26 16).
3. **Phones, Dark, keyboard, guide.** Chips wrap, the list full width on phones; keyboard order and
   labels; the Studio guide and this document updated. Built: checked at 375 px in the dark theme
   (no sideways scroll, chips wrap, one column, Add at the foot); the search's placeholder is
   short enough for a phone ("Search name, officer or file no."); the empty Studio's prompt names
   the new filters; the old tabs' styles (48 selectors) and the quick-add "Added" code are gone.
