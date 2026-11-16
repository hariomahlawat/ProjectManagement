# Briefing decks: fourth review fixes (17 Nov 2026)

A fourth cold review of `/Workspace/BriefingDecks` ran after the third fix plan
(`briefing-deck-review-3-fixes.md`) was merged.

**Scores: UX 7.5/10, code 8/10.** No High findings. The owner's target is at least 8 for both.

## Findings

**Medium**

1. **Newer single values were lost on saves that never touched them.** Review 3 kept newer list
   values only. Add projects, a slide switch or an editor save rebuilt every section from this
   build's reading, so a newer brief layout, key point placement or preset was lost although nobody
   changed it.
2. **Feedback and focus went to the hidden Deck tab.** The Studio opens on "This slide". An editor
   opened from the inspector reported its save in the Deck tab's status and returned focus to a
   hidden button. A refused rename showed its reason only in the hidden settings status.
3. **A delete could go unrecorded.** The delete was saved, then audited on a best-effort basis;
   a restore and the purge already save with a required record.

**Low**

1. Editors keep the typed values after a save although the server normalised them.
2. A Role & Charter entry without a lead phrase was split on `|`.
3. The server's footer-strip check could never fire (Normalize switched an empty strip off first).
4. Dead input (`ApplyPresetInput`) and a misplaced comment.
5. The FFC editor copies the countries-per-slide number.
6. The FFC editor has no leave-the-page guard.
7. Download was neither recorded nor shown as in progress.
8. A database error other than a conflict answered an in-page change with an error page.

## PRs

1. **This plan, and the server items** (M1, M3, Low 3, 4, 7 record, 8).
   - The codec writes only what a save changed, measured against what this build would write for
     the stored deck unchanged. A value this build cannot read stays unless the save sets it to
     something else; a changed list keeps its newer values after its own.
   - A delete and its record are one transaction, like a restore.
   - The footer strip is checked on what was posted.
   - `ApplyPresetInput` and the misplaced comment go.
   - A download is recorded (`ProjectBriefing.PowerPointDownloaded`).
   - An in-page change that fails in the database answers with a message.
2. **Feedback where the person is** (M2, Low 1, 2, 5, 6, 7 progress). Every in-place save,
   rename and editor reports where it can be seen whatever tab is open; a refused rename says why
   under the name; an editor returns focus to what opened it; editors redraw from the saved deck;
   the Role & Charter split, FFC constant and FFC guard are fixed; Download shows progress.

Then a fifth cold review.
