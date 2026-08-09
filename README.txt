Action Tasks UX V2 - RowVersion compile hotfix

Replace:
  Pages/ActionTasks/Index.cshtml.cs

Fix:
  Restores the missing UpdateSprintInput.RowVersion property used by:
  - OnPostUpdateSprintAsync
  - _PlanningCommandStrip.cshtml
  - sprint edit read-model hydration
  - UpdateSprintInput.FromSprint(...)

No database migration is required.
