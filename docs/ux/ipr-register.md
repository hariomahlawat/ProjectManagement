# IPR Register

The IPR Register lists the patents and copyrights filed for SDD projects. Everyone signed in can
read it (`Ipr.View`); Admin, HoD and Project Office roles edit it (`Ipr.Edit`). Which records count
is decided in one place, `Application/Ipr/IprStatusPolicy.cs` (see `docs/data-domain.md`): Filed
and Granted are the positive count, Rejected and Withdrawn are closed history.

## Where it lives

| Concern | Code |
| --- | --- |
| Page | `Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml(.cs)` and its partial classes (`Index.Loading.cs`, `Index.SelectLists.cs`, `Index.RecordCommands.cs`, `Index.AttachmentCommands.cs`) |
| Register rows | `IIprReadService.ListRegisterAsync` (every record, all statuses, register order) projected to `IprRecordRowViewModel` |
| Editor | `_RecordFormPartial.cshtml` in the editor panel (`mode=edit&id=…`, `mode=create`); one form saves the record and an optional evidence PDF (`Index.RecordCommands.cs`, `CompleteSaveAsync`) |
| Styles | `wwwroot/css/site-ipr.css`; the "Register workspace" section uses tokens only |
| Scripts | `wwwroot/js/project-office-reports/ipr/register-view.js` (register filters, grouping, record panel); `index.js` (editor, project picker, drawers, density); `ipr-register.js` (Analytics chart) |

## Layout

1. **Header**: title, a one-line description, **Patent or copyright?** (guidance), **Export**
   (follows the register's current filters), **Add IPR record** (editors).
2. **Summary strip**, four figures for the whole register, whatever the filters:
   **Filed** (patent/copyright split), **Granted** (share of filed), **Pending** (how many are past
   3 years) and **Closed** (rejected and withdrawn, kept as history). Each card filters the register
   to that status; on the Register view it does so in place.
3. **Views**: **Register**, **Action list** (`?tab=followup`, with the count of filings needing
   action) and **Analytics**. The old Projects tab is gone: `?tab=project` opens the register
   grouped by project.

## The register (`/ProjectOfficeReports/Ipr`)

- Every record loads at once; there is no paging. Filters apply instantly in the browser:
  search (title, filing number, project), **All / Patents / Copyrights**, **Filed / Pending /
  Granted / Closed** (each with its count), **Missing evidence**, **Not linked to a project**,
  **Filed in** (year) and **Group by** (project, status or filing year). A project filter arriving
  from a link shows as a removable chip.
- With no status chosen the list shows Filed and Granted records. Closed records appear only under
  **Closed**; a link to a closed record selects its status so it is visible.
- The browser does not re-derive the counting rule: each row carries `data-counted` and
  `data-closed` from `IprStatusPolicy`.
- The URL keeps the server's parameter names (`query`, `types`, `statuses`, `year`, `dateBasis`,
  `linkage`, `evidence`, `projectId`, `group`, `selectedRecordId`), so reloads, shared links and
  Export agree with what is on screen.
- Columns: record (title and filing number), project, category, status (with the pending age),
  filed, granted, evidence ("1 PDF" or "Missing"). On phones only the record and status show;
  the record panel carries the rest.
- **Record panel**: choosing a row opens it over the page (Escape or the backdrop closes it):
  facts, filed-to-outcome progress, evidence PDFs and notes. **Edit**, **Record grant** (pending
  records) and **Add evidence** open the editor in the same place.

## The editor

- **Add IPR record** and **Edit** open the editor where the record panel opens, as a wider panel
  over the page (no separate drawer). Escape, the backdrop, **Cancel** or the close button leave
  it; with unsaved changes it asks first. Closing an edit returns to that record's panel.
- One form, one **Save**: record details, status and dates, project, notes and an optional
  **Evidence** PDF, also when creating a record. The record saves first, then the PDF is attached.
  If the PDF is rejected (not a PDF, too large) the record stays saved and the editor reopens on
  it with the reason, so nobody re-creates a duplicate.
- Existing PDFs are listed in the Evidence section; each has its own remove button.
- After a save the register opens with the saved record's panel and a confirmation.

## The Action list (`?tab=followup`)

- Each filing that needs action appears once, under its most urgent issue, in this order:
  **Long-pending filings** (pending more than 3 years), **Details to correct** (missing filing
  number or date, or a granted record without its date), **Not linked to a project**, **Missing
  evidence**. Its other issues show as tags.
- Issue filter (**All issues**, **Long pending**, **Details to correct**, **Not linked**,
  **Missing evidence**) with the number of filings carrying each issue; a filtered view lists
  every filing with that issue.
- Each row: title (opens the record's panel on the register), category and project, the issue
  tags, how long it has been pending (or its filing date) and one fix for editors: **Record
  outcome**, **Fix details**, **Assign project** or **Upload PDF**. The fix opens the editor at
  the right field; after saving, the Action list reloads so the next item is one click away.
  Viewers get **Open** instead.
- The Filters drawer still scopes this view until the clean-up step.

## Planned changes

The redesign lands in small PRs; this section lists what is still to come.

1. ~~Register workspace: summary strip, instant filters, Group by, record panel.~~ (done)
2. ~~Edit inside the record panel with one Save; attach the evidence PDF while creating a record.~~ (done)
3. ~~**Action list** replaces Follow-up: each filing once, under its most urgent issue, with a direct
   fix (assign project, upload evidence, record outcome).~~ (done; "log follow-up" comes with step 5)
4. **Insights** replaces Analytics: grant rate, time to grant, outcome by filing year, portfolio
   over time and pending by age; every chart filters the register.
5. Follow-up log (last followed up / next follow-up) for long-pending filings; needs a migration.
6. Clean-up: retire the Filters drawer, the old ribbon, inspector and project-dossier styles left in
   `site-ipr.css`, and the paging parameters still bound for compatibility.
