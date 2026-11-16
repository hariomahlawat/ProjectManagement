# Briefing decks: fifth review fixes (17 Nov 2026)

A fifth cold review of `/Workspace/BriefingDecks` ran on master after the fourth fix plan
(`briefing-deck-review-4-fixes.md`) was merged.

**Scores: UX 7.5/10, code 7.5/10.** One High finding. The owner's target is at least 8 for both.

## Findings and what this plan does

**High**

1. **After the sign-in expires, in-place saves said "Saved".** The server answers an expired
   sign-in by redirecting to the sign-in page; `fetch` followed it and the page read the 200 page
   as success. Settings, editors, speaker notes and Add projects reported "Saved" and Generate
   downloaded the sign-in page.
   **Fix:** one reply check (`checkReply` in `project-briefing-decks.js`, shared with the Studio
   through the page root). A redirected reply, or a successful reply that is not the type asked
   for (JSON, or a PowerPoint for Generate), is "Your sign-in has expired, so nothing was saved".
   The server's sign-in redirect is unchanged (it is app-wide).

**Medium**

1. **Studio Delete failed after any in-place change.** The delete dialog's version field sits
   outside the page root, so it kept the page-load version. **Fix:** every version field on the
   page follows each save.
2. **The review 4 plan said editors redraw from the saved deck; they do not.** **Fix:** the plan
   now records that item as deferred.

**Low**

1. The Studio delete dialog said any Comdt/HoD can restore a Project Officer's own deck.
   **Fix:** it says "you" for a personal deck.
2. Download progress was a fixed timer. **Fix:** Download runs as a request; the button is busy
   until the file arrives or fails; an error or the sign-in page opens as a page.
3. The download record and the database-error answer had no tests. **Fix:** both are tested.
4. The project limit was declared twice; a settings database error blamed a duplicate name; two
   wordings of the name rule. **Fix:** one constant, a plain database message, one wording.

One PR. Then a sixth cold review.
