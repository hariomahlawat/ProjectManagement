# IT projects (`/ItProjects`)

A small, separate module for the organisation's own IT procurement projects (the IT ARPPP cases):
what each one is, which stage it has reached, and what the ITO, MCO, HoD and Comdt have said
about it. It is deliberately not the R&D Projects module: no plan, no approvals, no facts
engine, no photos, no decks. This document records the owner decisions of 8 Nov 2026 and the
approved plan.

## Why a separate module

The R&D `Project` row has about 45 dependent tables and every consumer (register, Command desk,
analytics, approvals, compendium, briefing decks, ARPP, ToT, proliferation) assumes R&D
procurement semantics. The only "kind" switch is `IsBuild`, which works by each consumer opting
out. Putting IT projects into that table would mean excluding them everywhere. So IT projects get
their own tables, services, pages and policies, and never reference `Projects`, `ProjectStages`,
`Remarks` or `ProjectDocuments`.

## Owner decisions (8 Nov 2026)

| Topic | Decision |
| --- | --- |
| Scope | IT projects only (no kind list, no SRE/infra for now) |
| Required fields | Title, description, **IT ARPPP reference number** (free text, not unique, not a case file number), **Financial year** |
| Financial year | The FY the IT ARPPP belongs to (April to March, shown as "2026-27"). Stored as the start year with `FinancialYearHelper`, as ARPP does. Picked from a list, defaulting to the current FY; the board filters and groups by it |
| Other fields | None for users. Only bookkeeping: created/updated by and at, row version, soft delete, Dropped |
| Stages | Nine fixed stages, in order (below). No time plan, no planned dates |
| Stage completion | A stage is marked complete; its completion date is optional |
| Auto-completion | Completing a stage completes every earlier non-skipped stage; reopening a stage reopens every later one |
| Optional stages | Benchmarking and PNC are skipped by default and can be included per project |
| SO stage | Carries an optional SO date (its completion date) and an optional cost in lakhs |
| Approval | None. Manage roles edit directly; every change is audited |
| Tasks | None. No link to the Action tracker |
| Photos, videos, time plan | None |
| Remarks | Yes, like the R&D remarks panel but simpler: no Internal/External type, no @mentions, no notifications. Since 9 Nov 2026: not tied to a stage, and the Comdt / HoD can write **conference** remarks as on projects, ideas and tasks, on the project page or from Workspace › Conference, where the live IT projects sit under each officer who holds the ITO role; a conference remark records the stage when it was issued. Since 10 Nov 2026 the Officer workload view lists them on each ITO's card too (docs/ux/command-desk.md) |
| Remark writers | ITO, MCO, HoD, Comdt. Admin may delete any remark (moderation) but does not write |
| Attachments | Yes: PDF and Office documents, Industry partners storage pattern |
| Visibility | Every signed-in user can read the board, the project page, remarks and attachments |
| Manage | Admin, HoD, ITO |
| Dropped | A project can be marked Dropped with a reason (assumed; easy to remove) |
| Timeline | A board that shows where every project is, stage by stage |

## Stages

| Order | Code | Label | Default |
| --- | --- | --- | --- |
| 1 | `PPP` | IT ARPPP / IPA | active |
| 2 | `AON` | AoN | active |
| 3 | `BID` | Bid | active |
| 4 | `TEC` | TEC | active |
| 5 | `COB` | COB | active |
| 6 | `BM` | Benchmarking | **skipped** |
| 7 | `PNC` | PNC | **skipped** |
| 8 | `SO` | SO | active (date + cost) |
| 9 | `DLV` | Delivery and ATP | active |

The list lives in code (`ItProjectStages` catalogue), not in a template table. Nine stage rows
are created with the project so every stage can hold its own flag, date and skip state and the
board is one query. The codes are the module's own constants; they do not reuse `StageCodes` or
the R&D workflow metadata.

### Stage rules (one place: `ItProjectStageRules`)

1. **Complete** stage N: every earlier non-skipped stage becomes complete too. Their dates stay
   empty unless the user enters them. The confirm dialog names the stages that will be completed.
2. **Reopen** stage N: every later stage is reopened too (flag cleared, date cleared). The
   confirm dialog names them.
3. **Dates** may be left empty. Where dates exist they must not go backwards in stage order; a
   violating date is rejected with a message naming the two stages. Nothing is clamped.
4. **Skip / include** applies only to Benchmarking and PNC. Including a skipped stage inserts it
   into the chain as not complete, unless a later stage is already complete, in which case rule 1
   makes it complete (date empty). Skipping leaves the other stages alone.
5. **SO cost** is optional, in lakhs, two decimals, stored on the SO stage row. It may be entered
   whether or not the SO stage is complete.
6. The stage editor saves all nine stages in one post so a historic project can be entered in one
   go; rules 1 to 4 run on the final state.

### Derived status (never stored)

| Status | When |
| --- | --- |
| Not started | no stage complete |
| In progress, *current stage* | the first non-skipped stage that is not complete |
| Completed | Delivery and ATP complete |
| Dropped | `DroppedOn` set; wins over the others and keeps the stage flags as they were |

## Data model (`Models/ItProjects`)

- `ItProject`: `Id`, `Title` (200), `PppReferenceNumber` (100), `FinancialYearStart` (int,
  indexed), `Description` (text), `CreatedByUserId`, `CreatedAtUtc`, `UpdatedByUserId`, `UpdatedAtUtc`, `DroppedOn` (date?),
  `DroppedReason` (500?), `DroppedByUserId`, `IsDeleted`, `DeletedAtUtc`, `DeletedByUserId`,
  `RowVersion`, `StageMovedAtUtc` (added by the redesign, PR 1).
- `ItProjectStage`: `Id`, `ItProjectId`, `StageCode` (8), `SortOrder`, `IsCompleted`,
  `CompletedOn` (date?), `IsSkipped`, `CostLakhs` (decimal(14,2)?, SO only), `RowVersion`.
  Unique `(ItProjectId, StageCode)`.
- `ItProjectRemark`: `Id`, `ItProjectId`, `AuthorUserId`, `AuthorRole` (Comdt/HoD/MCO/ITO,
  the senior one the author holds), `Body` (4000), `EventDate`, `StageCode` (optional),
  `CreatedAtUtc`, `LastEditedAtUtc`, `IsDeleted`, `DeletedAtUtc`, `DeletedByUserId`, `RowVersion`.
- `ItProjectRemarkAudit`: immutable snapshot per create/edit/delete (same idea as `RemarkAudit`).
- `ItProjectAttachment`: `Id` (Guid), `ItProjectId`, `OriginalFileName`, `StorageKey`,
  `ContentType`, `SizeBytes`, `UploadedByUserId`, `UploadedAtUtc`, `IsDeleted`, `DeletedAtUtc`,
  `DeletedByUserId`.

All FKs cascade from `ItProjects`. Soft-deleted projects are filtered in the read service, not by
a global query filter (same as `Projects`).

## Authorization (`Configuration/Policies.cs`, `Policies.ItProjects`)

| Policy | Roles | Grants |
| --- | --- | --- |
| `ItProjects.View` | any signed-in user | board, project page, remarks, attachment download |
| `ItProjects.Manage` | Admin, HoD, ITO | create, edit, stage editor, SO cost, attachments upload/delete, Dropped, delete |
| `ItProjects.Remark` | ITO, MCO, HoD, Comdt | add a remark; edit/delete own remark |

Admin may delete any remark (audited with the author kept). Resource checks are per project in
`ItProjectAccess` even though today every project is readable by every signed-in user, so a later
narrowing is one change.

## Pages (`Pages/ItProjects`)

### The board (`/ItProjects`)

1. **Header**: "IT projects", one line of description, **New IT project** (Manage).
2. **Summary strip**: In progress, Completed this FY, Dropped, and the stage with the most
   projects. Each card filters the board.
3. **Board**: one row per project, nine stage columns. Column headers carry the stage label and
   how many projects are *at* that stage. Cells: done (filled, date on hover or "date not
   recorded"), current (primary tint, pulsing ring), pending (hollow), skipped (dimmed dash). The
   SO cell shows the cost when present. Rows sort by current stage (PPP first), then title.
   Completed projects form a band after the active ones; Dropped projects a muted band at the
   bottom. Search filters title and reference in place. Status filter: Active / Completed /
   Dropped / All. **Financial year** filter (All years by default, one FY when chosen; the URL
   carries `fy=2026`) and a **Group by FY** switch that puts a band per FY, newest first, with
   the FY label and its project count.
4. The row's title opens the project page; clicking a stage cell for Manage users opens the
   project page with the stage editor on that stage.
5. Phones: the board becomes a list of cards, each with a compact nine-dot stepper.

### The project page (`/ItProjects/{id}`)

1. **Header**: title, IT ARPPP reference number, FY chip, status chip, **Edit** and **Stages** (Manage),
   **Mark dropped** / **Reinstate** (Manage).
2. **Stepper**: the nine stages across the top; done, current, pending, skipped as on the board,
   with the date under each done stage and the cost under SO.
3. **Description**.
4. **Remarks**: the R&D remarks panel look (author, role chip, event date, body, edited mark).
   Composer for Remark roles with event date and optional stage. Author edits/deletes own.
5. **Attachments**: list with name, size, uploaded by/at, download; upload and delete for Manage.
6. **Stage editor** (drawer, Manage): nine rows with a complete toggle, date, and for BM/PNC an
   include switch; SO row has the cost. One Save. Confirm dialog (PrismConfirm) when the save
   will auto-complete or reopen other stages.
7. **Edit** (drawer): title, reference, financial year, description.

### Create (`/ItProjects/Create`, Manage)

Title, IT ARPPP reference number, financial year (list, current FY selected), description. Saves and opens the project page with the stage
editor, so stages reached so far can be entered at once.

## Wiring

| Concern | Where |
| --- | --- |
| Services | `Services/ItProjects`: `IItProjectReadService`, `IItProjectCommandService`, `ItProjectStageRules`, `IItProjectRemarkService`, `IItProjectAttachmentService` (plus storage and validator copied from Industry partners), `ItProjectAccess` |
| Registration | `Program.cs` beside the other module services; policies in the `AddAuthorization` block |
| Navigation | one left-drawer entry "IT projects" (`RoleBasedNavigationProvider`), policy `ItProjects.View`; no top tab, no sub-nav |
| Access governance | `AdminRoleAccessCatalog` entries for Manage and Remark |
| Usage | `ErpUsageModuleCatalog` module for `/ItProjects` |
| Audit | `ItProjects.Created/Updated/StagesChanged/Dropped/Reinstated/Deleted`, `ItProjects.RemarkAdded/Edited/Deleted`, `ItProjects.AttachmentUploaded/Deleted`; rows in `AuditActionPresentationCatalog` |
| Storage | `{upload root}/it-projects/{projectId}/{guid}{ext}` (`FileSystemItProjectAttachmentStorage`); `IFileSecurityValidator` on every key and stored file, `IFileScanner` on every upload; downloads go through the project page's `Attachment` handler (View policy, resolved by project and attachment id), not a token URL. Allowed: PDF, Word, Excel, PowerPoint, within `ProjectDocuments:MaxSizeMb`; the bytes are checked against the declared type. Soft delete keeps the file |
| Migration | hand-written (`tools/Add-PrismMigration.ps1` for the id), manifest line, tail test in `AdminDataJobTidyUpTests` |
| Styles | `wwwroot/css/pages/it-projects.css`, tokens only (theme coverage check) |
| Script | `wwwroot/js/pages/it-projects.js` (board filter and search, stage editor confirm text) |
| Docs | this file; `docs/data-domain.md` entities; `docs/razor-pages.md` routes |

Out of scope until asked: Search V2 indexing, notifications, Command desk or dashboard tiles,
Restore deleted items entry, export.

## Plan

| PR | Content | Tests |
| --- | --- | --- |
| 1 | Entities, migration, stage catalogue, `ItProjectStageRules`, read/command services, policies, access governance entries | rules (auto-complete, reopen, date order, skip/include, SO cost), command service audit, migration manifest |
| 2 | Board page, create page, nav entry, usage module, styles | board ordering and counts, status derivation, page policy |
| 3 | Project page: header, stepper, description, stage editor drawer, edit drawer, Dropped/Reinstate | page handlers, confirm text, concurrency |
| 4 | Remarks: table, audit, service, panel and composer | role resolution, own-edit rule, Admin delete, audit snapshots |
| 5 | Attachments: table, storage, validator, upload/download/delete (done) | safety tests mirrored from Industry partners |

Each PR is reviewed by Codex and merged when CI and Codex are clean. The mockup
[mockups/it-projects.html](mockups/it-projects.html) (board, project page, stage editor) is the
reference for PRs 2 and 3.

## Redesign (8 Nov 2026)

A UI and UX redesign of the board, project page and stage editor is proposed in
[it-projects-redesign.md](it-projects-redesign.md) with the mockup
[mockups/it-projects-redesign.html](mockups/it-projects-redesign.html). It changes presentation
only; the decisions above stand.
