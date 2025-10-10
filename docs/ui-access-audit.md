# UI Access Audit

## Overview
This document inventories navigation entry points and highlights implemented features that currently lack a discoverable user interface path. The review covered the primary navigation provider, admin dashboard, Razor Pages, and partials.

## Primary navigation
The global navigation drawer renders a fixed set of top-level items (`Projects`, `Process`, `Calendar`, `Dashboard`) and, for admins, an `Admin Panel` branch. The branch now exposes lifecycle tooling alongside the existing analytics and lookup managers: `Project trash`, `Archived projects`, `Document recycle bin`, `Calendar deleted events`, and the holiday/celebration maintenance pages.【F:Services/Navigation/RoleBasedNavigationProvider.cs†L42-L156】

## Feature access matrix
| Feature | Implementation | Current entry point(s) | Gap / Risk |
| --- | --- | --- | --- |
| Project Trash | `Areas/Admin/Pages/Projects/Trash.cshtml` | Listed under the admin drawer (`Admin Panel → Project trash`) and surfaced as a dashboard lifecycle card linking directly to trash review.【F:Areas/Admin/Pages/Projects/Trash.cshtml†L1-L173】【F:Areas/Admin/Pages/Index.cshtml†L16-L168】 | Covered. Dashboard call-to-action keeps retention work visible. |
| Document recycle bin | `Areas/Admin/Pages/Documents/Recycle.cshtml` | Discoverable via admin drawer (`Admin Panel → Document recycle bin`), dedicated dashboard card, and per-project document tab for contextual access.【F:Areas/Admin/Pages/Documents/Recycle.cshtml†L1-L173】【F:Services/Navigation/RoleBasedNavigationProvider.cs†L70-L122】【F:Areas/Admin/Pages/Index.cshtml†L78-L168】 | Covered. Multiple entry points available. |
| Calendar deleted events | `Areas/Admin/Pages/Calendar/Deleted.cshtml` | Linked from admin drawer (`Admin Panel → Calendar deleted events`) and dashboard metrics card with restore CTA.【F:Areas/Admin/Pages/Calendar/Deleted.cshtml†L1-L24】【F:Services/Navigation/RoleBasedNavigationProvider.cs†L70-L122】【F:Areas/Admin/Pages/Index.cshtml†L106-L168】 | Covered. |
| Admin Help hub | `Areas/Admin/Pages/Help/Index.cshtml` | Reached through contextual help icons on Users, Logs, and Analytics pages.【F:Areas/Admin/Pages/Help/Index.cshtml†L1-L107】【F:Areas/Admin/Pages/Logs/Index.cshtml†L16-L22】 | Intentionally hidden from drawer; acceptable if help icons remain. Consider optional drawer link if admins request it. |
| Task manager | `Pages/Tasks/Index.cshtml` | Linked from the dashboard attention banner and Todo widget controls.【F:Pages/Dashboard/Index.cshtml†L21-L48】【F:Pages/Shared/_TodoWidget.cshtml†L43-L63】 | No drawer entry; acceptable because tasks are personal, but add a drawer shortcut if usability feedback demands it. |
| Archived project discovery | Archive toggle and badges on the project list (`Include archived`), admin drawer shortcut, and dashboard lifecycle metrics summarising archived counts.【F:Pages/Projects/Index.cshtml†L60-L107】【F:Pages/Projects/_ProjectModerationActions.cshtml†L60-L109】【F:Areas/Admin/Pages/Index.cshtml†L78-L168】【F:Services/Navigation/RoleBasedNavigationProvider.cs†L70-L122】 | Covered. |

## Dead-link / orphan scan notes
1. `find Pages Areas -name "*.cshtml"` confirmed only the items above lack explicit navigation hooks; all others are reachable via drawer or contextual flows (e.g., approvals, procurement editors).【2c8eee†L1-L86】
2. Search for `asp-page` references revealed no inbound links for `Areas/Admin/Pages/Calendar/Deleted.cshtml`, confirming its orphaned status.【ea64f5†L1-L6】
3. Documentation promises an admin-only calendar recycle bin and trash view, so missing links represent incomplete delivery rather than unused prototypes.【F:docs/razor-pages.md†L40-L48】【F:docs/archive-trash-plan.md†L37-L83】

## Recommended follow-ups
The original remediation items for navigation and dashboard visibility have been implemented. Remaining optional enhancements include:

1. **Enhance contextual signposting**
   - Consider adding inline links from the calendar and project list headers to reinforce the new shortcuts for users who miss the drawer updates.【F:Pages/Projects/Index.cshtml†L60-L107】【F:docs/razor-pages.md†L40-L48】
   - Continue monitoring admin feedback to decide whether the Help hub should graduate to the drawer.【F:Areas/Admin/Pages/Help/Index.cshtml†L1-L107】

2. **Operational reviews**
   - Regression-test admin roles periodically to ensure permission checks remain correct as new lifecycle tooling is added.
   - Keep governance documentation in sync with the live navigation map so administrators know where to start lifecycle clean-up tasks.【F:docs/archive-trash-plan.md†L37-L83】

With the drawer shortcuts and dashboard lifecycle cards live, every implemented archive, trash, and recycle feature now has an obvious UI path.
