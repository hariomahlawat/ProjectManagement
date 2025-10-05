# Manual test scripts

This directory stores repeatable QA checklists for features that require human verification. Each document focuses on a single module and enumerates the expected behaviour per role or scenario.

| File | Purpose |
| --- | --- |
| [remarks-role-matrix.md](remarks-role-matrix.md) | Step-by-step acceptance checklist for verifying remark permissions, edit/delete windows, notifications, and audit trails across Admin, HoD, PO, MCO, and Comdt roles. |

When adding new manual test plans, follow the existing format: include prerequisites, per-role steps, and a regression section that covers security headers, audit logging, and notifications. Link the new file here and mention it in the relevant feature documentation.
