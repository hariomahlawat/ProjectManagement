# Briefing decks: second review fixes (17 Nov 2026)

A second cold review of `/Workspace/BriefingDecks` ran after the first fix plan
(`briefing-deck-review-fixes.md`) was merged, at dd021ba4b.

**Scores: UX 7.5/10, code 7.5/10.** No High findings. The owner's target is at least 8 for both
(16 Nov 2026), so this plan runs without waiting for approval, under the standing instruction:
merge when CI and Codex are green, then move to the next PR.

What holds the scores back:
- **UX:** the three institution-slide editors still save with a full page reload. Choosing Fact
  sheet reloads the page. A few states are stale or silent (the editor's "Not in this deck", the
  library's "up to date" on a view-only deck, keyboard moves).
- **Code:**
  - the stored-settings codec drops keys it does not know;
  - a short window where a save adopts another user's row version;
  - design rules in the page model and repeated in script;
  - dead codec and command surface;
  - page-level tests for only some handlers.

## Findings

**Medium**

1. **The codec drops keys it does not know.** `ProjectBriefingDeckConfigurationCodec.Write` builds
   a new document from the typed record, and every `With*` call reads, then writes. Saved by an
   older build during a rolling deploy or after a rollback, a deck loses `keyPointsOnly` and
   `overviewOrder`.
2. **A save can adopt another user's row version.** Handlers save, then re-read the deck for the
   reply, and the page takes the re-read deck's row version. If someone else saves in between, the
   page holds their version with its own old values, and its next save reverts their change.
3. **The institution-slide editors use page-load state.** Their hidden "in this deck" box and text
   are never updated after a block save, so the drawer can say "Not in this deck" for a slide that
   is in it, and client checks pass what the server then refuses. They are also the only Studio
   saves that reload the page.
4. **Design rules in two places.**
   - The design-to-settings mapping and key-point placement live in the page model; the
     design/key-points chip rule and hints are repeated in script.
   - The speaker-notes limit is copied in script.
   - The Brief-layout visibility rule is in both Razor and script.
5. **"Up to date" on a moved view-only deck.** The library shows "up to date" on a Project
   Officer's deck that a command user can only view, even when its data has moved.
6. **Page-level role tests are thin.** Not covered per role: AddSelection, UpdateMembership,
   Reorder, UpdateDescription, ItemPresentation, SpeakerNotes, ItemPhoto, and the three editor
   saves. The view-only SaveSettings test does not check which message is shown.

**Low**

1. SaveSettings tells a view-only command user "not found" instead of the view-only message.
2. Dead code:
   - codec `With*` methods used only by tests;
   - settings-command fields (`RoleCharterOptions`, `FfcGlobalFootprintOptions`,
     `AdditionalSlideOrder`) filled in but ignored.
3. Expired decks are purged only inside someone's delete, with no audit entry.
4. A conflict while adding projects shows no conflict banner.
5. "Command user" comes from cookie claims for wording but from database roles for access.
6. Project search runs before the deck access check.
7. Keyboard moves at the edge of a group do nothing silently; a move only says "Saved."
8. One slide, several names ("SDD profile" vs "SDD Institutional Profile", "FFC footprint" vs
   "FFC Global Footprint").
9. Opening a link with `?generate=1` records a generation without asking.

## PRs

1. This plan.
2. **Codec keeps what it does not know** (M1). Write over the parsed document so unknown keys
   survive, plus a round-trip test with an extra key and with a newer schema.

   Built (17 Nov 2026).
   - `Write` builds this build's document as before, then lays it over the stored one. Keys this
     build does not own, at the top or inside its sections, are kept; keys it owns but leaves out
     (`preset`, `overviewOrder`) are removed.
   - A stored document with another `schema` value is refused with a message rather than nested.
   - Legacy selection rules (no `schema`) are still upgraded.
3. **A save keeps its own row version** (M2, Low 4). Handlers return the version their own save
   produced, and the page keeps it rather than a re-read deck's. A conflict while adding projects
   shows the conflict banner.
4. **Institution editors save in place** (M3, Low 8). The SDD profile, Role & Charter and FFC
   footprint editors save over JSON like the rest of the Studio, and follow the slide list's
   in/out state. Each slide has one name everywhere.

   Built (17 Nov 2026).
   - The SDD profile, Role & Charter and FFC footprint editors save through one page function
     (`saveEditorInPlace`), in the deck's one-at-a-time queue after any pending settings save.
     The handlers answer an in-page request with the message, the version their own save
     produced and the refreshed deck (`EditorReplyAsync`); a plain post still redirects.
   - After a save the drawer closes, the Studio rebuilds and the form's saved values become its
     defaults, so Discard returns to them.
   - The drawers' "In this deck" text and the SDD profile's checks follow the slide list.
   - Editors, messages and confirmations say "SDD profile", "Role & Charter" and "FFC footprint".
5. **Design rules in one place** (M4). The design mapping and key-point placement move into
   `ProjectBriefingProjectSlideDesigns`. The page reads the chip rules, hints and notes limit
   from data the server writes.

   Built (17 Nov 2026).
   - `ProjectBriefingProjectSlideDesigns` holds key-point placement and `Resolve` (from the page
     model), and each design's card rules: layout, key points when chosen, locked, the chip's swap,
     brief layout, key point slide choice and hint.
   - Each design card carries those rules as data attributes; the page script names no design.
   - The Brief-layout and key-point-slide visibility in Razor uses the same rules.
   - The Studio reads the speaker-notes limit from the page (`SpeakerNotesMaxLength`).
6. **Small honest fixes** (M5, Low 1, 5, 6, 7, 9):
   - the library shows the change count on view-only decks;
   - SaveSettings gives the view-only message;
   - the role resolver decides wording too;
   - search runs after the access check;
   - keyboard moves say where the slide went;
   - `?generate=1` asks first.
7. **Handler tests for every role** (M6). A table-driven test over every POST handler for a
   command user on a Project Officer's deck, asserting the exact message.
8. **Dead code and purge** (Low 2, 3). Remove the unused codec methods and command fields; move
   the expired-deck purge to its own audited step. Then a third cold review.

   Built (17 Nov 2026).
   - Removed the codec's single-section writers (`WithUpdateSheetOptions`,
     `WithStandardSlideOptions`, `WithInstitutionalProfileOptions`, `WithRoleCharterOptions`,
     `WithFfcGlobalFootprintOptions`); tests write through `WithPresentationOptions`
     (`CodecTestWrites`).
   - Removed the settings command's `RoleCharterOptions`, `FfcGlobalFootprintOptions` and
     `AdditionalSlideOrder`; each editor saves its own slide.
   - The expired-deck purge runs after the delete's save, writes `ProjectBriefing.DeckPurged` per
     deck, and is logged rather than thrown when it fails.

PR 2 and PR 7 touch their own files and run in parallel with PR 3. PR 4 follows PR 3, since both
change the save path in the page script. PR 5 follows PR 4, and PR 6 can run beside PR 5.
