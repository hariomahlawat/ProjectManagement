# PRISM Project Officer Multi-Stage Updates — Ready to Replace

This cumulative package is prepared against **ProjectManagement-master (95)** and includes the preceding workflow-consistency and Project Officer stage-update UX files together with the new multi-stage proposal implementation.

## What this implementation does

- The assigned Project Officer may submit updates for **one or many different stages in one submission**.
- The officer may continue submitting updates while earlier stage proposals are awaiting HoD approval.
- A pending completion/skip proposal for a predecessor is recognised when validating a downstream stage proposal.
- All rows in the current submission are evaluated as one **projected lifecycle**, independent of their visual row order.
- The approved lifecycle, progress count and current stage remain unchanged until HoD approval.
- Every submitted proposal is visible immediately on its stage as awaiting approval.
- A later update for the same stage supersedes only that stage's previous pending proposal. Pending proposals for other stages remain active.
- Each stage may appear once in a single submission. The same stage may be revised again through any later submission, preserving the audit history.
- Routine multi-stage updates do not require repetitive notes. Notes remain mandatory for block, skip, resume and reopen actions.
- Projected dates are checked against predecessor completion dates.
- Revising a predecessor cannot silently invalidate an untouched downstream pending update. The officer is asked to include the affected downstream stage in the same submission and revise it.
- The current-stage card uses one primary Update/Review action and avoids duplicate visible actions.
- Planned-completion warnings are suppressed while completion is already awaiting HoD approval.

## Database impact

**No database migration, new table or new column is required.** Existing `StageChangeRequest` and audit records are used.

## Replacement steps

1. Stop the application.
2. Copy the contents of this folder over the project root, preserving the included folder structure.
3. Delete `bin` and `obj` folders from `ProjectManagement` and `ProjectManagement.Tests`.
4. Rebuild the main project first, then rebuild the test project.
5. Restart the application and perform a hard refresh (`Ctrl+F5`).

## Recommended acceptance checks

1. Keep Acceptance of Necessity officially **In progress**.
2. Submit its completion as the Project Officer.
3. Before HoD approval, submit Bidding/Tendering as **Start stage** with a date after the proposed AoN completion.
4. Confirm both stages display their pending proposals while official lifecycle progress is unchanged.
5. Open a single submission, add AoN completion and Bidding/Tendering start, and confirm it is accepted as a projected sequence.
6. Revise AoN only to a later date that would invalidate the pending Bidding start; confirm the system asks for Bidding to be revised in the same submission.
7. Revise AoN and Bidding together with valid dates; confirm only the prior AoN and Bidding proposals are superseded and other pending stages remain untouched.
8. Confirm a normal completion proposal hides the current-stage planned-completion warning while it awaits approval.

## Validation performed in this environment

- `node --check wwwroot/js/projects/stages.js` — passed.
- Focused Project Officer stage-update JavaScript tests — **5/5 passed**.
- ZIP archive integrity — verified.
- The broader JavaScript command could not complete because the source environment does not have the existing `jsdom` dependency installed; unaffected non-jsdom tests and the focused stage-update tests passed.
- A full .NET build could not be executed because the .NET SDK is not installed in this environment. Rebuild locally after replacement.
