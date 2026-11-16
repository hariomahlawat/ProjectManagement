# Briefing decks: sixth review (17 Nov 2026)

A sixth cold review of `/Workspace/BriefingDecks` ran on master (2b75f176f) after the fifth fix
plan (`briefing-deck-review-5-fixes.md`) was merged.

**Scores: UX 8/10, code 8/10.** No High or Medium findings, and no earlier "fixed" claim was found
false. This meets the owner's target of at least 8 for both (16 Nov 2026).

## Fixed in this PR

- **Low 2:** a library Delete that hits a conflict or a database error returns to the library,
  not into that deck's Studio (`from=library`).
- **Low 3:** Role & Charter is checked as the server checks it: only a slide in the deck, using its
  own content, needs entries. The placeholder counts for shared content are gone.
- **Low 4:** reads (preview, photographs, project search) say "Your sign-in has expired … reload
  this page" rather than "nothing was saved"; a person sent to Access denied is told they no longer
  have access.
- **Low 7:** `SetFromPreset` returns nothing and its comment no longer mentions applying a
  template; two comments that named the removed "Additional Slides workspace" are updated.

## Open

- **Low 5, owner decision:** audit records for edits (create, duplicate, settings, blocks,
  editors, membership, reorder, notes, photo) are written after the save, best effort. Delete,
  restore and the purge save with a required record in one transaction. Making every edit
  transactional is possible with the same pattern; it adds a transaction to every Studio change.
- **Low 1:** the Studio header's "Updated … by …" is drawn once and does not follow saves.
- **Low 6:** the SDD footer-strip rule is checked in the page model, not the service.
- **Low 8:** the library Download has no behaviour test; many page-script tests still match
  source text rather than running it.
- Not measured: Studio speed with 120 projects with photos on production-sized data.
- Review 4, Low 1 (editors redraw from the server's normalised values) remains deferred.
