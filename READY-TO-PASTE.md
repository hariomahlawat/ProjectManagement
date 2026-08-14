# READY TO PASTE — PRISM Publications Phase 29

Copy the project-relative files in this ZIP into the root of your current **Phase 28** PRISM ProjectManagement source tree and allow them to overwrite files with the same path.

No database migration is required.

Then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase29.ps1
```

Primary functional checks:

1. Candidate project rows toggle selection when the row is clicked; Shift-click selects a visible range.
2. Load/save a Compendium and use **Publication Structure → Structure editor**.
3. Verify project search/filtering, multi-select, bulk Move to section, drag/drop, section drag/order, collapse/expand and section navigator.
4. Save and return; the compact rail should reflect the editor state.
5. Preview PDF to confirm export ordering remains unchanged.

See `PRISM_Publications_Phase29_README.md` for implementation details and validation notes.
