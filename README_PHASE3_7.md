# PRISM Conference Phase 3.7 — Inline Idea Creation

## Purpose

This package enables Comdt/HoD to create a Project Idea for the officer currently under review without leaving the Officer Conference Review page.

It must be applied **after Phase 3.6**.

## Implemented behaviour

- Adds **Add idea** to the Ideas section header.
- Opens a compact inline form on the current officer's Conference Review page.
- Captures only:
  - Idea title (maximum 200 characters)
  - Concept / problem statement (maximum 2,000 characters)
- Fixes the assigned Project Officer to the officer currently under review. The browser cannot choose or alter the assignee.
- Creates the record in the existing Project Ideas module with status **Active**.
- HoD oversight is resolved as follows:
  - A reviewer holding the HoD role is assigned as the overseeing HoD.
  - A Comdt-only reviewer selects an active HoD from the inline form.
  - A sole available HoD is preselected.
- Revalidates the reviewer, selected officer, officer role, account status and HoD role on the server.
- Uses the existing `ProjectIdeaCommandService`; no parallel Conference Idea table or duplicate persistence is introduced.
- Does not automatically create a Conference direction. A direction may be added separately after the idea is created.
- Inserts the newly created Idea into the page immediately without navigation or reload.
- Updates the Ideas count in both the section header and officer summary.
- The new row supports **Add direction** and **Open** immediately.
- Supports anti-forgery validation, inline field errors, double-submit protection, `Ctrl/Cmd + Enter` to create and `Esc` to cancel.
- Records the operation in structured application logs as originating from Officer Conference Review.

## HoD assignment rule

The Project Idea domain requires both an assigned Project Officer and HoD oversight. The inline workflow therefore does not silently omit or invent the HoD assignment:

- HoD reviewer: current reviewer is used.
- Comdt-only reviewer: an active HoD must be selected.
- Disabled, pending-deletion, currently locked or non-HoD accounts are rejected server-side.

## Files

The archive preserves project-relative paths. Copy its contents into the ProjectManagement project root and allow replacement of existing files.

No database migration is required.

## Validation completed in the preparation environment

- JavaScript syntax check passed.
- Targeted Officer Conference JavaScript suite: **22 passed, 0 failed**.
- Structural presence and null-byte checks passed for all 16 source files.
- Patch clean-application check against Phase 3.6 passed.
- Package overlay comparison passed.
- ZIP integrity and SHA-256 verification passed.

The .NET SDK was unavailable in the preparation environment, so the .NET build and xUnit suite were not executed there.

## Local validation

Run from the ProjectManagement project root:

```powershell
dotnet clean
dotnet build -c Release
dotnet test -c Release
node --check .\wwwroot\js\pages\officer-conference.js
node --test .\wwwroot\js\pages\officer-conference.test.js
```

## Manual verification

1. Sign in as HoD and open an officer's Conference Review page.
2. Select **Add idea**.
3. Confirm that the reviewed officer is shown as the fixed assignee and the current HoD is shown as oversight.
4. Enter a title and concept, then create the Idea.
5. Confirm that the row appears immediately, counts increase, and **Open** reaches the normal Idea Details page.
6. Confirm that **Add direction** works on the newly inserted row.
7. Sign in as a Comdt-only user and verify that HoD selection is required.
8. Attempt a manipulated request with another officer or invalid HoD ID and confirm that the server rejects it.
