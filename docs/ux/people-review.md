# People review and identity pages: findings, approved design and plan

`/Photos/People/Review` (naming the faces PRISM finds) and `/Photos/People/Details/{id}` (one
person's confirmed pictures and identity) were out of scope in `photos-library.md` and
`photos-library-review-fixes.md`, because the local data had no faces to judge them with. On
12 Nov 2026 the two approved face models were installed locally and 28 public-domain photographs
of people were added, so both pages were reviewed against real faces. The owner approved the
mockup the same day.

- Clickable mockup: `mockups/people-review.html` (open it in a browser; it needs no network).
- Reviewed at 1440 px and 390 px, signed in as an identity manager, on a throwaway copy of the
  local database: 32 detected faces, three confirmed people, one suggested group, five single
  faces.

## What is wrong today

### Review page

| # | Finding |
| --- | --- |
| R1 | It does not look like Photos. It has its own large header and none of the slim bar the other Photos pages share. |
| R2 | It speaks in system terms: "Human-confirmed identity intelligence", "Direct person suggestions", "Identity groups", "Identity-group snapshot is current", "Known-match queue clear", "appearances grouped", "ungrouped appearances". |
| R3 | Scores sit on every face: "Strong 0.891", "Quality 0.78", "Group similarity 0.783". A reviewer cannot act on them. |
| R4 | Four figure tiles repeat what the tabs already count. |
| R5 | Naming works differently in each place. A group has two fields side by side (pick an existing person, or type a new name). Single faces have two layouts, Triage and Detailed, and Triage can only assign to an existing person. |
| R6 | A decision cannot be taken back where it was made; correcting it means opening the person's page and giving a reason. |
| R7 | On a phone the header and the tiles fill the first screen before any face appears, and the active tab can be off-screen. |

### A person's identity page

| # | Finding |
| --- | --- |
| P1 | Every face card repeats a block of policy text: "Matching readiness: Preferred reference. Current face quality and embedding satisfy the preferred trusted-reference policy." Ten pictures show it ten times. |
| P2 | Every card carries three buttons (Use as cover, Use as matching reference, Correct appearance). |
| P3 | Internal values are shown, such as "ManualAssignment". |
| P4 | Managing the person (rename, hide, merge, link an account) is spread across a panel titled "Identity governance". |
| P5 | It too has its own header instead of the slim bar. |

## Approved design

**Review people**

- The slim bar of Photos, with People active, and one plain heading: "Who is in these pictures?"
  One sentence under it says the rule: PRISM finds faces, a person decides who they are.
- Three queues as chips, each with its count: **Same person?** (suggested groups), **Is this …?**
  (a face PRISM thinks is someone already known), **Unnamed faces** (faces on their own).
- **One name field everywhere.** Typing offers the people already known, with their portraits,
  and "New person: …" when the name is not one of them.
- **Same person?** A group is one card: a row of faces, the name field, **Confirm N faces** and
  **Not the same person**. Ticks choose which faces are included.
- **Is this …?** The face beside the known person's portrait, with **Yes**, **No** and
  **Someone else**.
- **Unnamed faces.** One grid. Ticking faces brings a bar with the name field, **Not a face**
  and **Leave unnamed**. Triage and Detailed become this one layout.
- A decision collapses to one line (or a toast) with **Undo**.
- No scores on faces. A weak likeness shows the words "Less sure". The numbers are behind
  "How sure is PRISM?".
- The figure tiles and the snapshot line go. A queue that is rebuilding or has failed still says
  so, in plain words.

**A person's page**

- The slim bar, then a compact header: portrait, name, "In N pictures · last seen …" and whether
  a PRISM account is linked.
- One **Manage** menu: Rename, Link a PRISM account, Merge with another person, Hide this
  person. The same confirmations and reasons as today.
- A grid of the person's pictures. Ticking pictures brings a bar: **Use as portrait**,
  **Move to another person**, **Not this person**.
- One badge, "Used to find more", on the pictures PRISM matches against, replaces the policy
  text. The detail stays available to identity managers behind "Details".

## Rules that do not change

- PRISM never names a person by itself. Every name comes from a reviewer's explicit action.
- Who may review, manage identities, merge, hide or link accounts: unchanged, checked on the
  server as today.
- Corrections, merges and account links are recorded with who did them and why.
- No migration. The existing handlers, routes and query strings keep working
  (`Mode=matches|unidentified|groups|closed`; `Layout=detail` is accepted and ignored).
- Offline: no remote assets. The page works without script for the basic decisions.
- People are named from what a reviewer knows, never from a likeness alone, in test data too.

## Build plan

Small PRs, each mergeable alone, each checked in the browser on real faces at desktop and phone
width and in dark theme.

| PR | What | Findings |
| --- | --- | --- |
| 1 | This document and the mockup | |
| 2 | Review page frame: slim bar, plain heading, the three queues as chips with counts, tiles and snapshot line removed, wording throughout. No handler changes | R1 R2 R4 |
| 3 | One name field: a shared partial and script that offer known people or a new name and post to the existing handlers; a plain select and text box remain when script is off | R5 |
| 4 | "Same person?" cards: face row with ticks, Confirm N faces, Not the same person; "Less sure" in place of scores; the numbers behind "How sure is PRISM?" | R3 R5 |
| 5 | "Is this …?" cards: face beside the known person, Yes / No / Someone else | R3 |
| 6 | "Unnamed faces": one grid in place of Triage and Detailed, the selection bar with the name field, Not a face, Leave unnamed; filters in one Sort and filter menu; the set-aside faces reachable from the bar | R5 |
| 7 | Undo: each decision answers with Undo, built on the existing reopen and correction services, recorded like any other change | R6 |
| 8 | A person's page: slim bar, compact header, Manage menu, picture grid with a selection bar, the "Used to find more" badge, details for managers | P1 P2 P3 P4 P5 |
| 9 | Phones, dark theme and keyboard (arrows and Space to tick, Enter to confirm) across both pages | R7 |
| 10 | Remove the style rules left unused in `photos-people.css`; record what was built here | |

Order: 2, 3, then 4 to 6 (each builds on the name field), 7, 8, 9, 10.

## Owner decisions

The plan assumes the recommendation unless the owner says otherwise.

| # | Question | Recommendation |
| --- | --- | --- |
| D1 | Should the faces in a group start ticked? Today the reviewer ticks each one. | Strong likenesses start ticked; faces marked "Less sure" start unticked. Faster, and the doubtful ones still need a deliberate tick. |
| D2 | Should scores stay visible on every face for identity managers? | No. Behind "How sure is PRISM?" for identity managers only. |
| D3 | Undo takes back a naming without asking for a reason. Corrections need a reason today. | Allow it only from the Undo offered straight after the decision, and record the reason automatically as "Undone by the reviewer straight after naming". Later corrections still ask for a reason. |
| D4 | The page is called "Identify people" today. | "Review people", matching the button that opens it. |

## How each step is checked

- `npm test`, `dotnet build`, and the People and face tests in `ProjectManagement.Tests`; a
  focused test for each behaviour changed, and for PR 7 a test that an Undo is recorded.
- A browser check on the local database that holds real faces, as an identity manager and as a
  user who may only review, at 1440 px and 390 px, light and dark.

## Not in this plan

- How faces are detected, matched or grouped, and the thresholds.
- The administration pages for media intelligence and sources.
- Splitting `photos-people.css` further than removing what is unused.
