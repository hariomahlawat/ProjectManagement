# Command desk

The Command desk (`/Workspace/CommandDesk`) is a read-only page for the **HoD** and the
**Comdt**. It sits first under **Command oversight** in the workspace rail and is their **workspace's default 
page**: `/Workspace` (and the rail's Command lens) opens it for a command user unless a view is 
asked for (Officer workload, Project portfolio, …). The Dashboard stays their landing page after sign-in.

Both roles see the same data. Only **Waiting on you** depends on who is viewing.

## Where it lives

| Concern | Code |
| --- | --- |
| Rules: windows, ordering, thresholds and labels (pure, unit-tested) | `Services/Workspace/CommandDeskBuilder.cs` |
| Data loading | `Services/Workspace/CommandDeskService.cs` |
| Directions read model | `IOfficerConferenceReadService.GetAllForCommandAsync` (the Conference view without record health or ERP activity) |
| Page | `Pages/Workspace/CommandDesk.cshtml(.cs)`, `_CommandDeskPdcRow`, `_CommandDeskDirectionRow`, `_CommandDeskTaskRow` |
| View models | `ViewModels/Workspace/CommandDeskViewModels.cs` |
| Styles | `wwwroot/css/command-desk.css` (site tokens only; no page script) |

## Sections, in order

1. **What moved forward**: the last 15 or 30 days (`?window=15`; the default is 30).
   - Counts of stages completed, projects completed, ToT completed, IPR filed or granted, approved proliferation units, FFC deliveries and installations, visits, people trained and activities (all activity types, including industry and academia interactions).
   - Visits, people trained and activities use the Dashboard's own rules, shared in code so the two pages agree: a visit by its date; a training's headcount and reference date (`TrainingReach`); an activity's occurrence, excluding deleted and future ones (`ActivityOccurrence`).
   - Up to five recent highlights.
   - Auto-completed (backfilled) stages and future-dated events do not count.
2. **Waiting on you**: hidden when nothing waits on the viewer.
   - Viewers who can approve (HoD, Admin) see the Decision Centre queue from `IApprovalQueueService`, so the two never disagree.
   - Any viewer sees tasks they assigned that were submitted for closure, and tasks they assigned that are **blocked** (with the blocking reason; a blocked task usually needs its assigner). Blocked tasks also stay under Tasks below, which everyone sees.
   - Items are listed oldest first, up to 8.
3. **What's lagging**:
   - **PDC watch**: assigned active projects whose current-stage PDC is overdue or due within 30 days.
     - Overdue items come first.
     - A plan pending approval is marked. To a viewer who can decide it, the mark reads "Revised plan · waiting on you" and opens its review; to others, "Revised plan with HoD".
   - **Directions awaiting progress**: the latest conference direction on a project, idea or task with no reply yet from the officer or task assignee. When chat is on, each offers **Message** to the officer (`/Chat?with=…`), except to the viewer themselves.
     - The rule matches the Conference view: an MCO note or a placeholder entry is not a reply.
     - Directions older than 7 days are highlighted.
   - **Tasks**: blocked or overdue tasks first, showing the latest blocking reason from the task audit log. Tasks due within 7 days follow.
     - Submitted, closed and backlog tasks are left out.
   - **Needs attention**: counts with a by-officer breakdown. Each officer links to their conference review, lists the projects behind the count (each opens its Overview) and, when chat is on, offers **Message**.
     - Items returned by the HoD and not yet corrected: a plan, stage update or project-details request. This is the command-wide form of the PO desk rule, with the same 180-day horizon.
     - Projects with no PDC on their current stage. A project whose plan is waiting for approval is not counted: the plan is the officer's answer, as on the officer's own desk, and it is under Waiting on you.
     - Projects with no update (any remark) from their officer for 10+ days.
   - Each list shows 8 rows. Any more sit under **Show all**.
4. **What's gone quiet**: registers whose latest dated entry is older than the expected frequency.

   | Register | Expected every | Date used |
   | --- | --- | --- |
   | Visits | 15 days | date of visit |
   | Misc activities | 15 days | scheduled start (not deleted) |
   | Training | 30 days | start date, or the month for month-only records |
   | Proliferation | 30 days | proliferation date (granular, not rejected) |
   | Social media | 30 days | date of event |

   IPR, ToT and FFC are irregular, so they appear only as good news.

Project scope, officer assignment and current stage come from `IOfficerWorkloadReadService`, the
same source as the Officer workload view. Dates are IST.
