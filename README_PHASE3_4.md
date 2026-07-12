# PRISM Conference Phase 3.4 — Responsible Functionary Attribution Fix

Apply this package **after Phase 3.3 Operational Progress Alignment**.

## Purpose

This phase corrects the attribution of Project progress responses for officers who hold more than one Identity role.

A remark entered by the currently assigned Project Officer must be treated as the Project Officer's response even when the source remark stores a higher-precedence role snapshot such as HoD or Comdt.

## Implemented changes

### Assigned Project Officer response

- The current Project assignment (`LeadPoUserId`) is now authoritative for the Project Officer response.
- The stored remark role is no longer required to be `ProjectOfficer`.
- Internal and External remarks entered after the latest Conference direction remain eligible.
- Remarks entered by another user carrying the Project Officer role remain excluded.
- When `LeadPoUserId` is unavailable, the canonical officer-workload assignment is used as the safe fallback.

### MCO response

- An MCO response is recognised from either:
  - the historical remark role snapshot `Mco`; or
  - the author's current ASP.NET Identity membership in the `MCO` role.
- This handles multi-role MCO users whose remark was stored under another higher-precedence role.
- Remarks authored by the assigned Project Officer are excluded from the MCO block so the same person's response is never shown twice.
- No MCO placeholder is rendered when there is no qualifying MCO response.

### Query behaviour

- Project progress remarks remain loaded in one bounded batch query.
- Only remarks from assigned Project Officers, MCO role snapshots or current MCO members are loaded.
- Conference remarks and deleted remarks remain excluded.
- Existing post-direction timestamp and ID ordering rules are retained.

### Regression coverage

The updated tests verify that:

- a dual-role assigned Project Officer is recognised by assignment even when the remark is stored as HoD;
- a remark from another Project Officer-role user is excluded;
- the latest assigned-PO remark is selected;
- MCO work is recognised through both the historical role snapshot and current Identity role membership;
- the latest MCO response is selected;
- the existing empty Project Officer response state remains intact.

## Files

Copy the contents of this archive into the project root and replace the listed files.

## Database

No migration is required.

## Recommended validation

```powershell
dotnet clean
dotnet build -c Release
dotnet test -c Release --filter OfficerConferenceReadServiceTests
```

## Functional checks

1. Open a Project where the assigned Project Officer also holds HoD or Comdt.
2. Add a non-Conference Project remark after the latest Conference direction.
3. Confirm that the Conference page displays it under `PROJECT OFFICER` irrespective of the source-page role badge.
4. Add a later remark from another user who merely holds the Project Officer role and confirm it is not selected.
5. Add an MCO remark after the direction and confirm it appears under `MCO`.
6. Confirm that one remark is not displayed in both Project Officer and MCO blocks when the same user holds both appointments.
