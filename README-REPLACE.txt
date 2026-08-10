PRISM ERP — Action Tasks UX V2.2.1 Hardening
============================================

Purpose
-------
This is a focused hardening pass on top of Task UX V2.2. It does not redesign
the task workspace. It fixes the Peek remark-posting defect and removes the
remaining UX inconsistencies identified during live review.

Production changes
------------------
1. Fix Peek remark posting
   - The legacy task RowVersion requirement has been removed from the remark
     request contract.
   - A task remark is an append-only collaboration record and no longer carries
     the optimistic-concurrency token used for task workflow mutations.
   - General, Conference, and attachment-only General remarks now use the same
     request contract in Peek and Full Task.

2. Shared remark input contract
   - NEW: Pages/ActionTasks/TaskRemarkInput.cs
   - Both IndexModel (Peek) and DetailsModel (Full Task) bind this same contract,
     preventing validation drift between the two surfaces.

3. Failure recovery
   - Rare server-side remark failures reopen/focus the remark composer.
   - Typed text and remark type are retained across the redirect using task-
     scoped TempData. Browser file handles cannot and should not be retained.

4. Compact Peek action trays
   - At the normal 40rem desktop Peek width, action forms use the same compact
     horizontal layout as the Full Task workspace.
   - Medium/mobile layouts still stack vertically.

5. Activity-history cleanup
   - Redundant raw status text (for example, an extra "in progress" line after
     "Assigned -> In Progress") is suppressed.

6. Heading consistency
   - Full Task uses the single heading "Updates", matching the Peek.

Files to replace/add
--------------------
Pages/ActionTasks/Index.cshtml.cs
Pages/ActionTasks/Details.cshtml.cs
Pages/ActionTasks/Details.cshtml
Pages/ActionTasks/_TaskDetails.cshtml
Pages/ActionTasks/TaskRemarkInput.cs                 [NEW]
Services/ActionTasks/ActionTaskPresentation.cs
wwwroot/css/action-task-peek.css

No database migration is required.

Replacement order
-----------------
1. Stop/rebuild the local application if it is running under IIS Express.
2. Replace the files above, preserving their project paths.
3. Clean Solution.
4. Rebuild Solution.
5. Run ProjectManagement.Tests.
6. Start PRISM and use Ctrl+F5 to avoid stale CSS.

Functional smoke test
---------------------
Peek:
- As Comdt, Conference should be selected by default.
- Post a Conference text remark: succeeds and count increments.
- Switch to General and post a text remark: succeeds.
- Post General with attachment only: succeeds if service upload rules allow it.
- Open Block / Change date / Return to backlog / Close directly: desktop forms
  should remain compact and horizontal without page movement.

Full Task:
- General and Conference posting both succeed.
- Activity history shows "Assigned -> In Progress" without a redundant
  standalone "in progress" line.
- Heading reads "Updates".

Validation performed in this environment
----------------------------------------
- TaskRemarkInput has no RowVersion field.
- No AddTaskUpdateInput or RemarkInputModel legacy type remains in the package.
- Both PageModels use TaskRemarkInput.
- Peek desktop action tray uses horizontal grid and medium/mobile stacking.
- task-interaction.js passes Node syntax validation.
- Coarse C# brace-balance checks pass for all modified C# files.

The .NET SDK is not available in this execution environment, therefore a real
C# build/xUnit run must be completed in Visual Studio after replacement.
