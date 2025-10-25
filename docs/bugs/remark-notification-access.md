# Remark notification access regression

## Summary
Opening a remark notification after the recipient has been unassigned from the project causes the notification page to fail. The remark list endpoint returns `403 Forbidden`, so the notification drawer shows an error instead of the remark content.

## What happens today
1. A project officer is set as the `LeadPoUserId` on a project and posts a remark, which sends a notification whose route points to `/projects/remarks/{projectId}?remarkId={remarkId}`. (see Services/Remarks/RemarkNotificationService.cs lines 273-307)
2. The officer is removed from the project (the `LeadPoUserId` now points to a different user).
3. The notification service still exposes the notification to the officer because `ProjectAccessGuard` allows any principal with the "Project Officer" role to view project notifications. (see Services/Notifications/UserNotificationService.cs lines 112-210) (see Services/Projects/ProjectAccessGuard.cs lines 21-36)
4. When the officer opens the notification, the page calls the remarks API which requires building an actor context scoped to the project. `BuildActorContextAsync` rejects the user, because they are no longer listed as the project's lead PO and do not carry an explicit remark role claim, returning `403 Forbidden`. (see Features/Remarks/RemarkApi.cs lines 56-214) (see Features/Remarks/RemarkApi.cs lines 358-432)

## Why it happens
* Notification visibility uses `ProjectAccessGuard`, which trusts global role assignments. This means former project officers retain read access to notifications for historical projects.
* The remarks API uses `BuildActorContextAsync` to derive the actor's remark roles from both global roles and project assignments. Once the user is removed from the project, the fallback that mapped `LeadPoUserId` to `RemarkActorRole.ProjectOfficer` no longer applies, so the actor context cannot be created. (see Features/Remarks/RemarkApi.cs lines 380-425)
* Without an actor context, every remark endpoint returns `403`, so the front-end cannot load the remark thread associated with the notification.

## Recommended plan
1. **Introduce a read-only remark access path** – Allow `BuildActorContextAsync` (or a new helper) to fall back to the caller's global project-access check when only read operations are required. Listing remarks should succeed if `ProjectAccessGuard` grants view rights, even when the user is no longer the assigned PO.
2. **Separate read vs. mutate operations** – Keep the stricter role checks for create/edit/delete, but let read-only endpoints (`ListRemarksAsync`, audit listing, etc.) accept the read-only actor context described above.
3. **Update remark service tests** – Extend `RemarkApiTests` to cover the regression: a user who loses the `LeadPoUserId` assignment must still be able to list remarks but should continue to be blocked from posting.
4. **Tidy up notification handling** – Add defensive UI handling so that, if a remark truly becomes inaccessible (e.g., project deleted), the notification displays a friendly message instead of a raw error.

This plan keeps authoring permissions locked to current project leadership while ensuring historical notifications remain useful for previous assignees.
