# Photos library (`/Photos`)

The Photos library brings every picture and video in PRISM (projects, visits, events,
activities and approved external folders) into one place, with albums and people. This document
records the review of 11 Nov 2026, the direction the owner approved, the build plan, and what
was built (12 Nov 2026).

It is separate from Photo Tools (`/Tools/Photo`, see [photo-tools.md](photo-tools.md)), which
works only on a user's own files.

## Owner direction (11 Nov 2026)

| Topic | Decision |
| --- | --- |
| Goal | Raise the library to the standard of Apple Photos and Google Photos |
| Look | The clickable mockup [mockups/photos-library.html](mockups/photos-library.html) is approved as the direction |
| Scope | Browsing, viewing, selecting, searching, albums, the people directory and collections |

Assumed until the owner says otherwise (each is cheap to change later):

| Topic | Assumption |
| --- | --- |
| Memories, maps, favourites, statistic cards | Left out. They do not fit an offline office library, and pages should stay simple |
| Years / Months / Days zoom levels | Left out of the first build. The month scrubber covers fast travel through time |
| Sections | Four plain sections: Photos, Albums, People, Collections. Albums is no longer under Collections |
| Less-used filters | Project, year and classification move to "More filters". Type and source become chips |
| People review and identity pages | Not redesigned here. They were not reviewed with real data; they get their own review |

## What the review found

The library was run locally with 18 demo items, no people and no albums, so nothing was
measured at real library size.

Already good and kept: rows justified by each picture's shape, keyboard and swipe in the
viewer, a link to one picture, shift-click and drag selection, ZIP download, dark theme.

| # | Finding | Evidence |
| --- | --- | --- |
| 1 | About 185px of a 900px screen is used before the first picture | Label, large title, description, tabs, search card, count line |
| 2 | Pages of 120 with Previous / Next; no continuous scroll, no way to jump to a month | `PageSize = 120` in `Pages/Photos/Index.cshtml.cs` |
| 3 | The viewer has no open or slide animation, no preloading, button-only zoom, no thumbnail strip, and only steps through the current page. On a phone, download and zoom are not shown | `wwwroot/js/widgets/media-viewer.js` |
| 4 | Tiles carry badges (cover bookmark on 12 of 18 demo tiles, people badge) and hover text | `Pages/Photos/Index.cshtml` |
| 5 | Selecting needs a Select button first, cannot take a whole day, and is lost on the next page | `wwwroot/js/pages/photos-library.js` |
| 6 | Search and every filter reload the page; no suggestions; filters sit in a drawer with an Apply button | Same files |
| 7 | Albums are under Collections, beside "Source collections"; several labels are system terms | "Unified organisational media", "awaiting catalogue intelligence" |
| 8 | Thumbnails load slowly and pop in. Tiles about 215px tall request the `md` size (1200 x 900) | `ProjectPhotoOptions` also defines `sm` (800 x 600) and `xs` (400 x 300) |
| 9 | Custom controls sit beside plain Bootstrap buttons; about 750 hard-coded colours across the five photo stylesheets, with layers of "Phase" overrides | `wwwroot/css/pages/photos-*.css` |

## The approved design

| Area | What changes |
| --- | --- |
| Top of page | One slim sticky bar: sections, search, Select. Pictures start directly under it |
| Timeline | One continuous scroll. Month headings, then day headings that name the project, visit or event. A month list on the right edge jumps to any month (desktop) |
| Tiles | Clean pictures. Caption on hover. Video length shown as a small pill |
| Selecting | A tick appears on hover. A tick beside each day takes the whole day. Shift-click takes a range; drag selection stays. The top bar becomes the action bar. The selection is kept while scrolling |
| Filters | Chips under the bar (Everything, Photos, Videos, Projects, Visits, Events, Activities) apply at once. An opened person or album shows as a chip |
| Search | Suggestions for people, projects, visits, events and years as you type |
| Viewer | Opens from the thumbnail and closes back to it. Slides between pictures. Mouse wheel, pinch and double-click zoom with drag to move. Thumbnail strip. Controls fade when idle. Details panel with people, albums and file facts. Download on phones |
| Albums | Own section. Cover made of three pictures, a "New album" tile first |
| People | Round portraits with name and picture count |
| Collections | Grouped under Projects, Visits, Events and Activities |
| Wording | Plain words: "Details" not "Information", "Collections" not "Source collections", no eyebrow labels |

## Rules every step keeps

- **Access.** Every new handler (next part of the timeline, month list, search suggestions)
  uses the same visibility rules and filters as the page it extends. Nothing a user cannot see
  today becomes reachable. Each new handler gets an authorization test.
- **Offline.** No new library, no CDN, no remote font. The mockup's web font is for the mockup
  only; the app keeps its shipped Mulish.
- **Existing links keep working.** Current URLs and query keys (`View`, `CollectionTab`,
  `PersonIds`, `AlbumId`, `Page`, `#media=`) stay valid. Without JavaScript the timeline still
  shows pages with Previous / Next.
- **No database change.** No step needs a migration.
- **Shared viewer.** The viewer is also used on project and visit pages. Viewer steps must keep
  those pages working and are checked there too.
- **Theme.** New CSS uses theme tokens only, in light and dark.

## Build plan

Small PRs, each shippable on its own. Order puts the cheapest visible gains first.

| PR | Step | Main content | Risk |
| --- | --- | --- | --- |
| 1 | Plan | This document and the mockup | None |
| 2 | Faster, cleaner tiles | Request a thumbnail that fits the tile (`sm` / `xs` with `srcset`), fade in without a jump, remove tile badges, caption on hover | Low |
| 3 | Slim top bar and four sections | Replace the header, tabs and search card with one sticky bar; Albums becomes a top-level section; old URLs redirect or keep working | Low |
| 4 | Month and day headings, filter chips | Month headings, day headings naming the source, chips for type and source, "More filters" for the rest, plain wording | Low |
| 5 | Continuous scroll and month scrubber | Handler that returns the next part of the timeline as HTML, loaded as the user nears the end; handler for month counts; Previous / Next kept without JavaScript | Medium: new read handlers, large libraries |
| 6 | Selecting | Hover tick, whole-day tick, selection kept across loaded parts, top action bar, existing ZIP and album actions unchanged | Medium: touches bulk download and album forms |
| 7 | Viewer: movement and zoom | Open and close animation, slide, preload the next and previous picture, wheel / pinch / double-click zoom with drag, controls fade when idle | Medium: shared widget |
| 8 | Viewer: strip, details, phones | Thumbnail strip, redesigned details panel, download and details on phones, viewer continues past the loaded part | Medium: shared widget |
| 9 | Search suggestions | Suggestion handler for people, projects, visits, events and years; keyboard support | Medium: new read handler |
| 10 | Albums and Collections pages | Three-picture covers, "New album" tile, collections grouped by type, album page header in the new style | Low |
| 11 | People directory and person page | Round portraits, person page header in the new style; review and identity pages untouched | Low |
| 12 | Clean-up and phones | Move photo stylesheets to tokens, remove the override layers, one button style, phone pass on every screen | Low, wide |

## What was built, and where it differs from the plan

All twelve steps were built as separate PRs. Differences from the plan above:

| Step | Difference | Why |
| --- | --- | --- |
| 2 Tiles | Tiles ask for the `sm` size only, not `sm` / `xs` with `srcset` | `xs` (400 x 300) is too small for the tallest rows. A tile falls back once to the `md` thumbnail if `sm` cannot be loaded, so a library without that size keeps working |
| 5 Scroll | The next part comes from a `TimelinePart` handler on the Photos page that runs the page's own loading | Same filters, paging and visibility as the page, with no second copy of the rules. A redirected answer (expired sign-in) or a failed part brings back the page links, pointing at the part that failed |
| 5 Month list | Month counts are grouped in the server's local time (standard offset) | The timeline shows local dates. In a zone with daylight saving, a picture within an hour of a month boundary in summer can be counted in the neighbouring month |
| 8 Viewer | The thumbnail strip shows ten pictures each side of the current one | A long timeline then costs no more than a short one. Project and visit pages show no strip because they give no thumbnails |
| 9 Search | Suggestions cover people, projects and years, from the lists the page already holds. No new handler; visits and events are found by typing and pressing Enter | Nothing new to secure, and nothing offered that the filter drawer does not already offer |
| 10 Albums | Covers stay single pictures | Three-picture covers need a change to the album service |
| 11 People | The person's own page keeps its header | It already matched the new bar once the sections changed |
| 12 Clean-up | Unused rules were removed from the four photo stylesheets (about 480 lines). The remaining hard-coded colours were not all moved to tokens | New rules use tokens only and the literal-colour check stops the count growing; a full conversion is a separate, purely visual job |

A review after these twelve steps, its fixes and a check against a library of 6,000 items are in
`photos-library-review-fixes.md`. That check closes the first open item below.

Not done, and still open:

- A check against a library copy with several thousand items (scroll and memory behaviour). The
  local data has 18 items; continuous scroll was exercised with the page size lowered to 5.
- The viewer's animations were not watched live (the test browser throttles animation); their
  end states and the logic are covered by tests.
- People review and identity pages (`/Photos/People/Review`, `/Photos/People/Details`).

## How each step is checked

- `npm test`, `dotnet build`, `dotnet test`; a focused test for each new handler (access and
  paging) and for the script logic that can be unit-tested (row layout, selection, zoom maths).
- A browser check on desktop, phone width and dark theme, signed in as a user without
  administrator rights as well as one with them.
- Steps 5 to 8 are also checked against a library copy with several thousand items before
  merging, because the local demo data is too small to show scroll and memory behaviour.

## Not in this plan

- People review and identity management pages (`/Photos/People/Review`, `/Photos/People/Details`).
- Upload into the library, editing pictures, sharing outside PRISM.
- Any change to how media is discovered, classified or matched to people.
