# UI access audit

Lists which pages each role can reach, how users find them (top tabs, drawer, contextual links), and where the navigation and the page authorization disagree. Checked against `Program.cs` (`AddRazorPages` conventions and policies), the page model attributes, `Services/Navigation/RoleBasedNavigationProvider.cs`, `Services/Navigation/ModuleNav/*` and `Pages/Shared/_Layout.cshtml`. For the full per-page list, see [razor-pages.md](razor-pages.md).

## Entry points

| Surface | Source | Items | Gating |
| --- | --- | --- | --- |
| Top tabs | `_Layout.cshtml` | Dashboard, My Workspace, Calendar, Notebook, Photos, Projects (`/Projects/Ongoing`), FFC (`/ProjectOfficeReports/FFC/MapTableDetailed`), Documents, Search | Shown to every signed-in user. There are no role checks on the tabs. |
| Account menu | `_LoginPartial.cshtml` | Profile, My photos, Admin (only if the user has the Admin role) | — |
| Navigation drawer | `RoleBasedNavigationProvider` | Calendar, Dashboard, Photos, Miscellaneous activities, Task Management, Project Ideas, Proliferation compendium, Progress review; Projects group (`ProjectModuleNavDefinition`); Documents group; Project office reports group; Administration (Admin only) or Activity types (HoD without Admin) | Each item is trimmed by `RequiredRoles` and/or `AuthorizationPolicy`. |
| Project sub-nav | `ProjectModuleNavDefinition` | Projects repository, Ongoing, Completed summary, Process, ARPP/PPP, Reports (`ViewArpp`), Analytics, Publications, Industry directory, Create project (`Project.Create`), Pending approvals (Admin, HoD) | Per-item policy or roles. |
| Admin sidebar | `AdminNavigationCatalog` | All Admin area pages, plus ERP usage, Holidays, Celebrations, Legacy import | Per-item `AdminPolicies.*` or `Policies.*`. |
| Footer | `_Layout.cshtml` | "System information", linking to `/Developer` | Anonymous page. |

After sign-in, `DefaultLandingPageResolver` sends Comdt and HoD users to `/Dashboard`, Project Officers (without Comdt or HoD) to `/Workspace`, and everyone else to `/Dashboard`.

## Reachability by role

✓ means the role can open the page. "view" means the page opens but editing is limited by handler checks.

| Module | Admin | HoD | Comdt | Project Officer | Project Office | MCO | TA | ITO | Main Office / MC / IT Cell clerks |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Dashboard, Calendar, Notebook, Tasks, Notifications, Search, Photos, Projects list and overview, Analytics, Process, Publications, Industry directory, ARPP library (`/Projects/Arpp`), Project ideas, Activities, FFC, Visits and Social media (view), ToT, Proliferation and IPR (view), Document repository (view) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| My Workspace (`/Workspace`) | redirect to Dashboard | ✓ command | ✓ command | ✓ PO view | redirect | redirect | redirect | redirect | redirect |
| Conference review, briefing decks | — | ✓ | ✓ | — | — | — | — | — | — |
| Task Management (`/ActionTasks`) | — | ✓ | ✓ | ✓ | — | ✓ | ✓ | ✓ | — |
| Create project | ✓ | ✓ | — | — | — | — | — | — | — |
| Decision Centre (`/Approvals/Pending`) | ✓ | ✓ | — | — | — | — | — | — | — |
| Project Reports (`/Projects/Reports`), ARPP admin (`/ProjectOfficeReports/ARPP`) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | — | — | — |
| ARPP create/manage/reconcile | ✓ | ✓ | — | — | ✓ | — | — | — | — |
| Visits and Social media create/edit | ✓ | ✓ | — | — | ✓ | — | — | — | — |
| Training tracker (view) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | — | Main Office only |
| Training manage | ✓ | ✓ | — | — | ✓ | — | — | — | — |
| Progress review | ✓ | ✓ | ✓ | — | ✓ | — | — | — | — |
| FFC records manage / countries | ✓ | ✓ | ✓ | — | — | — | — | ✓ | — |
| IPR edit | ✓ | ✓ | — | — | ✓ | — | — | — | — |
| Document upload / soft-delete | ✓ | ✓ | — | — | ✓ | — | — | — | ✓ |
| Document metadata edit | ✓ | ✓ | — | — | — | ✓ | ✓ | ✓ | — |
| Celebrations | ✓ | — | — | — | — | — | ✓ | — | Main Office only |
| Holidays, Activity types, Media admin, People review | ✓ | ✓ | — | — | — | — | — | — | — |
| ERP usage | ✓ | ✓ | ✓ | — | — | — | — | — | — |
| Admin area (other than Activity types) | ✓ | — | — | — | — | — | — | — | — |

Notes:
- Admin does **not** hold `ActionTracker.Access`, `ConferenceRemarks.Manage` or `ProjectBriefingDecks.Manage`.
- Project-level editing (timeline, stages, documents, photos, videos, ToT) is limited by role attributes and by `ProjectAccessGuard`, which checks the assigned HoD/PO. See the Projects section of [razor-pages.md](razor-pages.md).

## Lifecycle and recovery tooling

All of these are in the Admin sidebar and are gated by `Admin.Recovery.Manage` (Admin only): Recovery centre, Project trash, Document recycle bin, Deleted events (`/Admin/Calendar/Deleted`), Archived projects. The document repository's own trash (`/DocumentRepository/Admin/Trash`, `DocRepo.Purge`) is in the Documents drawer group.

## Navigation vs page policy inconsistencies (verified)

| Item | Navigation gate | Page gate | Effect |
| --- | --- | --- | --- |
| Social media tracker (`/ProjectOfficeReports/SocialMedia`) | `ManageSocialMediaEvents` (Admin, HoD, Project Office) | `[Authorize]` on Index/Details | Other roles can open the page but have no drawer link. |
| Social media event types and platforms | `RequiredRoles = Admin` | `[Authorize(Roles = "Admin,HoD")]` | HoD has access but no link. |
| Holidays (`/Settings/Holidays`) | Admin sidebar only (Admin) | `Admin.Holidays.Manage` (Admin, HoD) | HoD has access but no navigation entry. HoDs who are not Admins get only "Activity types" in the drawer. |
| My Workspace top tab | Everyone | Only Comdt, HoD and Project Officer see content | Other roles are silently redirected to the Dashboard. |
| Proliferation compendium drawer item | `/Projects/Compendium/Index` | Redirects to `/Projects/Publications/Compendium` | Works, but the label and route are legacy. |
| Project office reports group header | Links to area `/Index` | No such page | The link cannot be resolved, so the header renders as plain text (not broken). |

## Pages without navigation hooks

These are reached only by contextual links or by typing the URL:
- `/Tasks`: personal to-do list. The dashboard widget links to the Notebook instead.
- `/Admin/Help`: in the Admin sidebar.
- `/Admin/MediaSources` and `/Admin/MediaIntelligence*`: linked from the Photos pages.
- `/DocumentRepository/Admin/MissingFiles`: no inbound link found.
- `/Usage`: in the Admin sidebar and the command workspace header.

## Anonymous surface

`/`, `/Identity/Account/Login`, `/Identity/Account/AccessDenied`, `/Error` and `/Developer`. `/Developer` has no `[Authorize]`, and no convention covers it. It shows the developer's name, email and phone number and the app version.
