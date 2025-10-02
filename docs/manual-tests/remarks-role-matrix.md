# Remarks Role Matrix – Manual Acceptance Checklist

Use this checklist to validate role-specific behaviour for the remarks feature. Perform the steps in a non-production environment with seeded projects and users that map 1:1 to the roles below. Reset the project state between roles so that time-window checks start from a clean baseline.

## Common Setup

- [ ] Identify a project with at least one stage and participants mapped to PO, HoD, MCO, Comdt and Admin roles.
- [ ] Ensure remark notifications are routed (email/SMS/in-app) so delivery can be observed.
- [ ] Confirm system clock and timezone (IST) so edit/delete windows can be timed accurately.

## Project Officer (PO)

- [ ] Create internal remark while assigned to the project (should succeed, notification to HoD/Admin triggered).
- [ ] Attempt external remark (should be denied with privilege error).
- [ ] Edit own remark within 3 hours (allowed, remark updated, audit entry logged).
- [ ] Delete own remark within 3 hours (allowed, remark hidden, audit updated).
- [ ] Attempt edit after 3 hours (denied with window message, toast/snackbar rendered).
- [ ] Attempt delete after 3 hours (denied with window message).
- [ ] Verify filters (type, role, stage, date range, “Mine”) refine list for PO scope.

## Head of Department (HoD)

- [ ] Create internal and external remarks (both succeed, external notifies MCO and Comdt).
- [ ] Edit another user’s remark after 3 hours (allowed via override, audit shows HoD actor role).
- [ ] Delete another user’s remark after 3 hours (allowed, audit captures HoD as deleter).
- [ ] Verify notifications dispatched to PO, Admin and MCO for external remarks.
- [ ] Confirm remark filters show deleted toggle hidden (non-admin behaviour).

## MCO

- [ ] Create internal remark (allowed if project assigned).
- [ ] External remark attempt (should be denied—requires HoD/Comdt/Admin).
- [ ] Verify timeline/remarks toggle persists state per local storage.
- [ ] Confirm notifications received when HoD posts external remark.

## Commandant (Comdt)

- [ ] Create external remark with stage reference (allowed, stage label shown in list).
- [ ] Edit/delete PO remark after window (allowed via override).
- [ ] Validate event date cannot be set in future (UI blocks, API returns 400).
- [ ] Ensure filters correctly show role option for Comdt and can combine with stage/date filters.

## Administrator

- [ ] Create, edit and delete remarks (all actions allowed regardless of ownership/time).
- [ ] Toggle “Show deleted” to reveal soft-deleted remarks (works only for Admin).
- [ ] View audit trail for remark (requires admin, displays structured entries).
- [ ] Confirm metrics dashboard increments for create/delete and denied attempts (if surfaced).
- [ ] Validate filters persist after navigation refresh for admin.

## Global Regression

- [ ] Attempt remark composer without CSRF token (should be rejected 400/403).
- [ ] Use browser dev tools to confirm Content-Security-Policy blocks inline scripts (remarks panel functions via modules only).
- [ ] Inspect structured logs for remark actions; entries include user id, role, action, allow/deny reason.
- [ ] Scrape metrics endpoint (if enabled) and confirm counters: `remarks.create.count`, `remarks.delete.count`, `remarks.edit.denied.window_expired`, `remarks.permission.denied` move as actions are executed.
- [ ] Validate notifications panel/badge updates after remark lifecycle events.
