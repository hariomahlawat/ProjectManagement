# Remark notification access regression

**Status: Fixed** (verified against code, 2026-09-27).

## Original problem
A Project Officer who posted a remark and was later replaced as the project's `LeadPoUserId` could still see the remark notification, but opening it failed: the remarks list API returned `403 Forbidden` because the actor context could no longer be built for that project.

## Current behaviour (evidence)
- Every authenticated user may view project information. `ProjectAccessGuard.CanViewProject` / `CanViewProjectInformation` (`Services/Projects/ProjectAccessGuard.cs`) return `true` for any authenticated principal.
- `RemarkApi.BuildActorContextAsync` (`Features/Remarks/RemarkApi.cs`) takes an `allowViewerFallback` flag. It still removes `ProjectOfficer` from the role set when the caller is not the project's `LeadPoUserId`. If no roles remain and the fallback is allowed, it returns a read-only viewer context instead of `403`.
- Only the read endpoints pass `allowViewerFallback: true`: `ListRemarksAsync` (`GET /api/projects/{projectId}/remarks`) and `GetRemarkAuditAsync` (the audit endpoint still requires the Administrator role).
- Create, update and delete call `BuildActorContextAsync` without the fallback, so a former PO with no other remark role still gets `403` when posting, editing or deleting.

## Plan items from the original report
| Item | Status | Evidence |
| --- | --- | --- |
| 1. Read-only remark access path | Fixed | `allowViewerFallback` in `RemarkApi.BuildActorContextAsync` |
| 2. Separate read and mutate checks | Fixed | Only `ListRemarksAsync` and `GetRemarkAuditAsync` use the fallback |
| 3. Regression tests | Not verified | This audit did not check test coverage for the former-PO scenario |
| 4. Friendly UI message for inaccessible remarks | Not verified | No code was traced for this |
