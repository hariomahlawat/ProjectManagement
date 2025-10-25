# UI Access Audit

## Overview
This document inventories navigation entry points and highlights implemented features that currently lack a discoverable user interface path. The review covered the primary navigation provider, admin dashboard, Razor Pages, and partials.

## Primary navigation
The global navigation drawer renders a fixed set of top-level items (`Projects`, `Process`, `Calendar`, `Dashboard`) and, for admins, an `Admin Panel` branch. The branch now exposes lifecycle tooling alongside the existing analytics and lookup managers: `Project trash`, `Archived projects`, `Document recycle bin`, `Calendar deleted events`, and the holiday/celebration maintenance pages. (see Services/Navigation/RoleBasedNavigationProvider.cs lines 42-156)

## Feature access matrix
| Feature | Implementation | Current entry point(s) | Gap / Risk |
| --- | --- | --- | --- |
| Project Trash | `Areas/Admin/Pages/Projects/Trash.cshtml` | Listed under the admin drawer (`Admin Panel → Project trash`) and surfaced as a dashboard lifecycle card linking directly to trash review. (see Areas/Admin/Pages/Projects/Trash.cshtml lines 1-173) (see Areas/Admin/Pages/Index.cshtml lines 16-168) | Covered. Dashboard call-to-action keeps retention work visible. |
| Document recycle bin | `Areas/Admin/Pages/Documents/Recycle.cshtml` | Discoverable via admin drawer (`Admin Panel → Document recycle bin`), dedicated dashboard card, and per-project document tab for contextual access. (see Areas/Admin/Pages/Documents/Recycle.cshtml lines 1-173) (see Services/Navigation/RoleBasedNavigationProvider.cs lines 70-122) (see Areas/Admin/Pages/Index.cshtml lines 78-168) | Covered. Multiple entry points available. |
| Calendar deleted events | `Areas/Admin/Pages/Calendar/Deleted.cshtml` | Linked from admin drawer (`Admin Panel → Calendar deleted events`) and dashboard metrics card with restore CTA. (see Areas/Admin/Pages/Calendar/Deleted.cshtml lines 1-24) (see Services/Navigation/RoleBasedNavigationProvider.cs lines 70-122) (see Areas/Admin/Pages/Index.cshtml lines 106-168) | Covered. |
| Admin Help hub | `Areas/Admin/Pages/Help/Index.cshtml` | Reached through contextual help icons on Users, Logs, and Analytics pages. (see Areas/Admin/Pages/Help/Index.cshtml lines 1-107) (see Areas/Admin/Pages/Logs/Index.cshtml lines 16-22) | Intentionally hidden from drawer; acceptable if help icons remain. Consider optional drawer link if admins request it. |
| Task manager | `Pages/Tasks/Index.cshtml` | Linked from the dashboard attention banner and Todo widget controls. (see Pages/Dashboard/Index.cshtml lines 21-48) (see Pages/Shared/_TodoWidget.cshtml lines 43-63) | No drawer entry; acceptable because tasks are personal, but add a drawer shortcut if usability feedback demands it. |
| Archived project discovery | Archive toggle and badges on the project list (`Include archived`), admin drawer shortcut, and dashboard lifecycle metrics summarising archived counts. (see Pages/Projects/Index.cshtml lines 60-107) (see Pages/Projects/_ProjectModerationActions.cshtml lines 60-109) (see Areas/Admin/Pages/Index.cshtml lines 78-168) (see Services/Navigation/RoleBasedNavigationProvider.cs lines 70-122) | Covered. |

## Dead-link / orphan scan notes
1. `find Pages Areas -name "*.cshtml"` confirmed only the items above lack explicit navigation hooks; all others are reachable via drawer or contextual flows (e.g., approvals, procurement editors). (see log 2c8eee lines 1-86)
2. Search for `asp-page` references revealed no inbound links for `Areas/Admin/Pages/Calendar/Deleted.cshtml`, confirming its orphaned status. (see log ea64f5 lines 1-6)
3. Documentation promises an admin-only calendar recycle bin and trash view, so missing links represent incomplete delivery rather than unused prototypes. (see docs/razor-pages.md lines 40-48) (see docs/archive-trash-plan.md lines 37-83)

## Recommended follow-ups
The original remediation items for navigation and dashboard visibility have been implemented. Remaining optional enhancements include:

1. **Enhance contextual signposting**
   - Consider adding inline links from the calendar and project list headers to reinforce the new shortcuts for users who miss the drawer updates. (see Pages/Projects/Index.cshtml lines 60-107) (see docs/razor-pages.md lines 40-48)
   - Continue monitoring admin feedback to decide whether the Help hub should graduate to the drawer. (see Areas/Admin/Pages/Help/Index.cshtml lines 1-107)

2. **Operational reviews**
   - Regression-test admin roles periodically to ensure permission checks remain correct as new lifecycle tooling is added.
   - Keep governance documentation in sync with the live navigation map so administrators know where to start lifecycle clean-up tasks. (see docs/archive-trash-plan.md lines 37-83)

With the drawer shortcuts and dashboard lifecycle cards live, every implemented archive, trash, and recycle feature now has an obvious UI path.
