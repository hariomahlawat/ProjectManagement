# Manual test scripts

This directory holds repeatable QA checklists for features that need human verification. Each document covers a single module and lists the expected behaviour per role or scenario.

| File | Purpose |
| --- | --- |
| [remarks-role-matrix.md](remarks-role-matrix.md) | Acceptance checklist for remark permissions, the 3-hour author edit/delete window, Conference and External rules, notifications, audit and metrics across the PO, HoD, MCO, Comdt and Admin roles. |
| [tot-tracker-view-modes.md](tot-tracker-view-modes.md) | Regression checklist for the Transfer of Technology tracker list/detail workspace: filters, selection persistence, submit/decide flows and export. |

When you add a manual test plan, follow the existing format: prerequisites, per-role steps, and a regression section. Take the expected results from the code, and cite the type or member that enforces each rule. Link the new file here.
