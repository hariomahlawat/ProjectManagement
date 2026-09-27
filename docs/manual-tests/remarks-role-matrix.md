# Remarks Role Matrix – Manual Acceptance Checklist

Use this checklist to check role-specific behaviour of project remarks (`/api/projects/{projectId}/remarks`, the remarks panel on the project overview). Run it in a non-production environment with seeded users who each hold one of the roles below. The expected results come from `Features/Remarks/RemarkApi.cs` and `Services/Remarks/RemarkService.cs`. See `docs/openapi/remarks.yaml` for the endpoint contract.

## Rules under test

- **Who can post:** Admin, HoD, Comdt, MCO, TA, Project Office and Main Office can post on any project. A Project Officer can post only on the project where they are the `LeadPoUserId`.
- **External remarks:** only HoD, Comdt or Admin.
- **Conference remarks:** only HoD or Comdt, and only they can edit or delete them. Admin cannot.
- **Edit and delete:** HoD, Comdt and Admin can edit or delete any non-Conference remark at any time. Everyone else can edit or delete only their own remark, within 3 hours of creating it.
- **Reading:** every authenticated user can list remarks. If a user has no remark role, a read-only viewer context is used.
- **Notifications** (`RemarkNotificationService`): the project PO, the project HoD, mentioned users and every Comdt user are notified. External remarks also notify every MCO user. The author is never notified, and users who have opted out are filtered.
- **Event date:** cannot be later than today's IST date.

## Common setup

- [ ] Pick a project with stage rows and an assigned PO (`LeadPoUserId`) and HoD (`HodUserId`).
- [ ] Make sure in-app notifications are visible, so you can observe delivery.

## Project Officer (PO)

- [ ] On the assigned project, create an Internal remark. It succeeds, and the project HoD and all Comdt users are notified.
- [ ] On a project where you are not the lead PO, attempt to create a remark. It is denied (403) unless you also hold another remark role.
- [ ] Attempt an External remark. It is denied. **Known drift:** the API returns 400, not 403, because the message is not mapped in `MapServiceException`.
- [ ] Edit or delete your own remark within 3 hours. It is allowed and an audit row is written.
- [ ] Edit or delete your own remark after 3 hours. It is denied with "You can edit/delete your remark within 3 hours of posting."
- [ ] Check that the filters (type, scope, role, stage, date range, "Mine") narrow the list.

## Head of Department (HoD)

- [ ] Create Internal, External and Conference remarks. All succeed. External remarks also notify MCO users.
- [ ] Edit or delete another user's remark after 3 hours. It is allowed, and the audit shows HoD as the actor role.
- [ ] Check that the "Show deleted" toggle has no effect (the server honours `includeDeleted` only for Admin).

## MCO

- [ ] Create an Internal remark on any project. It succeeds; no project assignment is required.
- [ ] Attempt an External remark. It is denied (400, see the known drift above).
- [ ] Check that you receive a notification when a HoD posts an External remark.

## Commandant (Comdt)

- [ ] Create an External remark with a stage reference. It succeeds and the stage label appears in the list.
- [ ] Create a Conference remark. The stage is recorded automatically as the project's present stage, or "Completed".
- [ ] Edit or delete a PO remark after the 3-hour window. It is allowed.
- [ ] Try a future event date. The API returns 400 ("Event date cannot be in the future.").

## Administrator

- [ ] Create, edit and delete non-Conference remarks regardless of owner or age. All succeed.
- [ ] Attempt to edit or delete a Conference remark. It is denied.
- [ ] Turn on "Show deleted". Soft-deleted remarks appear (Admin only).
- [ ] Open a remark's audit trail. It needs Admin and returns structured snapshot entries.

## Global regression

- [ ] Check that the remarks panel works under the Content-Security-Policy (`script-src 'self'`; no inline scripts).
- [ ] Check the structured logs: every decision writes a `RemarkDecision` entry with action, allowed flag, reason, user id and role.
- [ ] If metrics are scraped, check that these counters move: `remarks.create.count`, `remarks.delete.count`, `remarks.edit.denied.window_expired`, `remarks.permission.denied`.
- [ ] Check that the notification badge updates after remark create, edit and delete.
- [ ] Note: the remark JSON endpoints do not validate antiforgery tokens server-side. Do not expect a 400 or 403 when the `X-CSRF-TOKEN` header is missing. CSRF protection comes from the `SameSite=Strict` auth cookie.
