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
   - **Each count opens its register filtered to the same window** (`CommandDeskRoutes`), using
     the register's own query names, so the number on the chip is the number of rows that open:

     | Count | Opens |
     | --- | --- |
     | Stages completed | Projects list, `StageCompletedFrom`/`To` (any stage completed in the window, backfilled stages excluded) |
     | Projects completed | Projects list, Completed tab, `CompletedFrom`/`To` |
     | ToT completed | ToT tracker, status Completed, `CompletedFrom`/`To` |
     | IPR filed / granted | IPR register, `from`/`to` on the filed date, or on the grant date with `statuses=Granted&dateBasis=Protected` |
     | FFC deliveries / installations | FFC portfolio, `deliveredFrom`/`To` or `installedFrom`/`To` |
     | Visits, activities | their registers' existing From/To filters |
     | People trained | Training register, From/To with `DateBasis=Reference`: each training placed on its reference date (start, else end, else the first of its month), as the desk counts it; the register's default From/To lists any training overlapping the window |
     | Units proliferated | the Proliferation records page for the year the window ends in: that page is a per-year calculation and has no date window, so this one count is a near miss, not exact |

     The windows are inclusive, `yyyy-MM-dd`, and each register shows the window as a removable
     active-filter chip. The IPR register filters in the browser, so its rows carry
     `data-filed-on` / `data-granted-on` and `register-view.js` applies the window like the others.
2. **Waiting on you**: hidden when nothing waits on the viewer.
   - Viewers who can approve (HoD, Admin) see the Decision Centre queue from `IApprovalQueueService`, so the two never disagree.
   - Any viewer sees tasks they assigned that were submitted for closure, and tasks they assigned that are **blocked** (with the blocking reason; a blocked task usually needs its assigner). Blocked tasks also stay under Tasks below, which everyone sees. Who assigned a task is its creator, or whoever later put it from the backlog into a sprint with a responsible person or reassigned it (`ActionTaskAssigners`, shared with Task Management's "assigned by").
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

## Layout and screen width

- All My Workspace pages (the two desks, Conference review, Briefing decks and the workspace views) use the `desk` page shell: at most 1840px wide (`--pm-shell-desk`) and centred. On these pages the top bar, sub-nav and footer use the same width (`pm-chrome-shell--desk`), so the side rail lines up with the menu button above it. Other modules that set the `workspace` shell (Process, Reports, Publications, Completed summary) stay full width.
- The Command desk reads as one column, at most 72rem wide. When the desk area is at least 84rem wide (a window about 1660px wide or more, with the rail open), What's lagging moves to a second column beside the other three sections. The reading order in the page does not change.
- The Conference review officer list sits in the same 72rem column, aligned with the rail, not centred.
