# Briefing decks: third review fixes (17 Nov 2026)

A third cold review of `/Workspace/BriefingDecks` ran on the second fix plan's work
(`briefing-deck-review-2-fixes.md`, PRs 4, 5 and 8 still open as #3390–#3392, merged with master).

**Scores: UX 7.5/10, code 8/10.** No High findings. The owner's target is at least 8 for both, so
this plan runs under the standing instruction: merge when CI and Codex are green, then move on.

## Findings

**Medium**

1. **Saving an editor could ask about leaving the page.** The in-place editor save went through the
   leave-the-page settlement, so a failed settings save asked "Leave without saving the settings?".
   "Leave anyway" then switched off unsaved-change protection for the rest of the visit.
   Fixed in #3390 before it merged (also raised by Codex): an editor save now only waits for a
   pending settings save, and refuses with a message if that fails.
2. **The codec still drops newer values inside arrays and enums.** Unknown keys survive (review 2,
   PR 2), but owned arrays (`updateSheet.rows`, `overviewOrder`, `additionalSlides`,
   `institutionalProfile.modules`) and enum values a newer build added are rebuilt from what this
   build understands, so a save during a rolling deploy or after a rollback loses them.
3. **Role & Charter and FFC footprint saves say nothing.** The drawer closes; only the SDD profile
   shows "saved".
4. **Add projects resolves the selection before checking the deck.** The per-role handler test did
   not cover it.

**Low**

1. The SDD profile's footer rule is checked by the server always, by the page only while the slide
   is in the deck, so Save can be enabled and then refused.
2. Hidden "include" boxes in the three editors are posted and ignored; their script is dead.
3. A stale comment says the profile editor saves with a reload.
4. A failed Duplicate blames a shared-deck update; Duplicate takes no version.
5. The ajax check is written out three times.
6. The Project Officer rail and project count read roles from the sign-in cookie, while wording
   reads database roles. **Kept:** the rail follows the app shell, which uses the cookie on every
   workspace page; changing one page alone would make it disagree with the others.
7. The editor-save script is tested only by source-text matches.

## PRs

1. **This plan, and the server items** (M4, Low 4, Low 5). Add projects checks the deck before it
   resolves the selection, and is in the per-role handler test; Duplicate's database error says
   so; one ajax check.
2. **Codec keeps newer list values** (M2). Owned lists (update-sheet rows, profile modules,
   additional slides, overview order) keep stored entries this build does not know, after its
   own. A single choice (brief layout, key points, preset) is saved as the person picked it: the
   page shows this build's reading, and picking that value cannot be told apart from leaving it
   (Codex on #3394), so keeping the stored one would undo a real choice. Round-trip test with an
   unknown row and block type.
3. **Editors confirm and agree with the server** (M3, Low 1, 2, 3, 7), after #3390–#3392 merge.
   All three editors show their saved message in the slide list; the footer rule matches the
   server; the dead include boxes and comment go; a jsdom test covers an editor save while a
   settings save fails.

Then a fourth cold review.
