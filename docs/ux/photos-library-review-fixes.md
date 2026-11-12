# Photos library: review after the redesign (12 Nov 2026) and fix plan

The redesign in `photos-library.md` shipped as #3103–#3116. The module was then reviewed again
on master:

- **UX:** every page (Photos, viewer, selecting, filters, search, Albums, an album, Collections,
  People, a person) at 1440 px, at phone width (390 px) and in dark theme, on the local demo
  library.
- **Code:** a separate cold read of the pages, the query service, the scripts and the
  stylesheets against `AGENTS.md`. The main findings were checked against the source.

Limits of the review: the local library has 18 items and the seeded people have no portraits.
The findings about large libraries come from reading the code, not from measurement. Animations
were not watched live.

**Verdict:** usability 7.5/10, code health 7/10.

**Keep:** the slim bar and four sections; month and day headings; selecting (tick, shift-range,
day select, actions in the bar); the viewer (keys, zoom, strip, details, focus returns to the
tile); search suggestions; the filter drawer; dark theme on the timeline. No remote assets, no
inline scripts and no authorization gaps were found.

## Findings

### Usability

| # | Level | Finding |
| --- | --- | --- |
| U1 | High | On a phone the first picture starts about halfway down the screen. Above it: sections, search, a controls row, two wrapped rows of chips, a counts line, month, day and the source line. |
| U2 | High | The phone grid is two large tiles per row. |
| U3 | High | Phones have no month jump; the month list shows only at 1200 px and wider. |
| U4 | High | A person's page still has the old large header above the bar, so the bar sits in a different place from every other page. |
| U5 | Medium | Count lines repeat what the page shows: "18 items · 17 photos · 1 video", "1 album · 0 album items · 0 photos · 0 videos", and on People "0 direct person suggestions · 0 suggested groups · 0 individual review". |
| U6 | Medium | Search suggestions list people with 0 photos. |
| U7 | Medium | Collections leaves out a project that has only one picture, which reads as missing data. |
| U8 | Medium | Wording is uneven: "Add pictures" and "Add media" on one page; "No media found", "grouped items", "Only human-confirmed appearances are included", "Set up matching", "Not classified". |
| U9 | Medium | Albums has two "New album" buttons (the tile and the bar). |
| U10 | Medium | The People bar is crowded: search, sort, accounts filter, Hidden, Select, Review people. |
| U11 | Medium | Tap targets on phones are small: section tabs 33 px high, chips 30 px. |
| U12 | Low | The page shows faintly through the viewer backdrop. |
| U13 | Low | A video tile is a plain gradient with a play icon, no poster frame. |
| U14 | Low | An empty search result has no clear-search button in the empty state itself. |
| U15 | Low | The timeline has no end marker. |

### Code

| # | Level | Finding | Where |
| --- | --- | --- | --- |
| C1 | High | Each scrolled part runs the whole page load: counts, people, albums, statistics, project and year lists, source status. | `Pages/Photos/Index.cshtml.cs` `OnGetTimelinePartAsync` |
| C2 | High | Every grid is laid out again for every part, and again when the page grows; tiles are never dropped. | `photos-library.js` `layoutAll` |
| C3 | Medium | Escape always ends selecting, also when it was pressed to close a dialog or the filter drawer. | `photos-library.js` keydown handler |
| C4 | Medium | The viewer wraps around a partly loaded list, and its counter shows the loaded count as the total. | `media-viewer.js` |
| C5 | Medium | Viewer keys ignore Ctrl, Alt and Meta, so browser zoom shortcuts are swallowed. | `media-viewer.js` keydown handler |
| C6 | Medium | The year filter uses UTC; months and days use local time. | `MediaLibraryQueryService.cs` |
| C7 | Medium | Paging is by page number with no check for tiles already on the page, so pictures added during scrolling can appear twice and be posted twice. Not reproduced. | `photos-library.js` `appendTimelinePart` |
| C8 | Medium | After a drag-select that ends off a tile, the next click can be ignored. Not reproduced. | `photos-library.js` lasso |
| C9 | Low | Viewer controls fade while keyboard focus is on them. | `media-viewer.css` |
| C10 | Low | Dark-theme hover and pressed states in the viewer are close to invisible. Not checked in the browser. | `media-viewer.css` |
| C11 | Low | Month links land under the sticky bar; link names have no year; the current month is not kept in view in a long list. | page and script |
| C12 | Low | A day joined across parts updates its count but not its source line. | `photos-library.js` |
| C13 | Low | ARIA slips: a label on a role-less strip, tiles that say "Open" while they act as toggles. | views |
| C14 | Note | The page model is about 2,800 lines in three files; `photos-people.css` is 1,976 lines; most colours are still written twice (literal plus dark override). | |

## Fix plan

Small PRs, each mergeable alone. No migration in any of them. Routes, query strings and handler
names stay as they are.

| PR | What | Findings |
| --- | --- | --- |
| 1 | This document | |
| 2 | Selecting and viewer fixes: Escape leaves selecting only when no dialog or drawer is open; the drag-select flag is cleared; the viewer does not wrap while more parts can load and shows "of many" honestly; modifier keys pass through; controls stay visible while focused; opaque backdrop; visible dark hover states | C3 C4 C5 C8 C9 C10 U12 |
| 3 | Lean timeline part: when a part is requested, load only the rows and what a tile needs; skip statistics, lists and status | C1 |
| 4 | Scroll cost in the browser: lay out only new or changed grids, all of them only when the width changes; skip tiles already on the page; refresh the source line of a joined day | C2 C7 C12 |
| 5 | Large-library check: a development-only script that fills a throwaway database with several thousand items; scroll, memory and query timings recorded in this document; fixes for what it shows | open item from the first plan |
| 6 | Year filter in local time, matching the month and day headings | C6 |
| 7 | Phones: one compact header (chips in one scrolling row, no counts line), three tiles per row, 44 px targets, a month jump that works on phones | U1 U2 U3 U11 C11 |
| 8 | Counts and wording: count lines removed; one "New album" entry; "pictures" in place of "media"; plain empty states with a clear-search button; an end marker; no suggestions for people with 0 photos | U5 U6 U8 U9 U14 U15 C13 |
| 9 | Person page and People bar: the person's name moves into the same bar position as the other pages; sort, accounts and Hidden move into one filter menu | U4 U10 |
| 10 | Record what was built here and in `photos-library.md` | |

Order: 2, 3, 4 first (bugs and cost), 5 to confirm them, then 6 to 9.

## Large-library check (PR 5)

Run on 12 Nov 2026 against a throwaway copy of the local database filled by
`tools/dev-data/photos-bulk-demo-seed.sql`: 6,018 visible items (5,834 photos, 184 videos) over
30 months and 552 days, up to 82 in a day. The copies reuse the 18 local pictures, so image
decoding and network cost are lighter than a real library's. The browser was the desktop app's
built-in pane at 1440 px; it throttles painting, so times are script and layout time, not
what a person sees frame by frame.

| What | Result |
| --- | --- |
| First page | 0.5 s to first byte, 510 KB of HTML for 120 tiles |
| A scrolled part, server | about 25 ms to first byte, 35 ms to download, 390 KB |
| A scrolled part, browser | about 200 ms in all, steady from the first part to the fiftieth |
| Scrolling to the end | all 6,018 pictures loaded in 50 parts, none twice, "No more pictures" shown |
| Page size at the end | 85,000 elements, 9 to 18 MB of script memory |
| Database (EXPLAIN ANALYZE) | first page 0.35 ms, page 40 5.7 ms, total count 4.1 ms, month counts 6.0 ms |

Two things were slow and are fixed in the same PR:

| What | Before | After |
| --- | --- | --- |
| "Select all shown" | 7.3 s with 6,018 loaded | 0.27 s with 3,000 loaded |
| Opening or closing the viewer | 0.46 s with 6,018 loaded | 0.24 to 0.34 s with 3,000 loaded |

- **Select all:** a hidden field for every selected picture was written into three forms on every
  change of selection. The fields are now written when the form is sent, and the review link when
  it is used.
- **Viewer:** hiding the page's scrollbar widened the page behind, so every grid was laid out
  again. The scrollbar's space is now kept while the viewer is open.

Still true, and accepted for now:

- Tiles are never removed, so with thousands loaded each change that touches the whole page
  (opening the viewer, starting to select) costs about 0.3 s. Removing off-screen days would fix
  it and is a larger change than this plan covers.
- A tile is about 2.4 KB of HTML, most of it data attributes the viewer reads. A leaner tile
  would cut the 390 KB per part.
- Paging is by offset. It is fast at this size (5.7 ms at page 40); a keyset cursor would matter
  only for libraries far larger than this.
- `IX_MediaAssets_LibraryTimeline` was not chosen by the planner for any of the three queries.

## Owner decisions

| # | Question | Recommendation |
| --- | --- | --- |
| D1 | Should Collections list a project that has only one picture? (U7) | Yes. Hiding it looks like missing data. Built in PR 8 if agreed. |
| D2 | Remove the count lines on Photos, Albums and People? (U5) | Yes; the plan assumes this. The People review counts stay on the Review page, where they are acted on. |
| D3 | Video poster frames (U13) | Leave for now. It needs a frame taken on the server when the video is catalogued, which is a change to media processing, outside this plan. |

## How each step is checked

- `npm test`, `dotnet build`, `dotnet test`; a focused test for each behaviour changed.
- A browser check at desktop width, phone width and dark theme.
- PRs 3 and 4 are checked again in PR 5 against the large throwaway library.

## Not in this plan

- Splitting the page model and moving the remaining literal colours to tokens (C14). Worth doing,
  but as its own piece of work with no visible change.
- People review and identity pages.
- Upload, editing pictures, sharing outside PRISM.
