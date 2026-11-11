# MOLTI projects and SRE projects (`/MoltiProjects`, `/SreProjects`)

Two more modules that work like IT projects ([it-projects.md](it-projects.md)): the same nine
stages, the same reference number and financial years, the same remarks and attachments. The one
real difference is who runs a project. The ITO runs every IT project; a MOLTI or SRE project is
run by a **Project Officer who is assigned to it** and can be unassigned again.

For users these are three separate modules. In code they are one engine.

This document records the owner decisions of 11 Nov 2026 and the proposed plan. Nothing here is
built yet.

## Owner decisions (11 Nov 2026)

| Topic | Decision |
| --- | --- |
| Names | "MOLTI projects" and "SRE projects", written exactly so. The abbreviations are not expanded anywhere |
| Separation | Three modules for the user: own menu entry, own board, own pages, own access rows. A project belongs to exactly one module and never moves |
| Stages | The same nine as IT projects, same rules, same optional Benchmarking and PNC, same IPA and SO cost |
| Reference and years | The same: a reference number, a financial year, and New / CL / CF when a case is carried into a later year |
| Who runs a project | One assigned Project Officer per project. May be empty (unassigned) |
| Who can be assigned | Any active user who holds the Project Officer role |
| Who assigns and unassigns | Admin and HoD |
| Who creates, drops, deletes | Admin and HoD |
| What the assigned officer may do | Everything the ITO may do on an IT project, but only on their own project: edit details, update stages, attachments, drop and reinstate |
| Who can read | Every signed-in user, as for IT projects |
| Remark writers | Comdt, HoD, MCO and the project's assigned officer. Delete rules as for IT projects |
| IT projects | Unchanged. The ITO still runs all of them; no officer is assigned |

## Why one engine

The IT projects module is about 7,000 lines plus 19 test files. Copying it twice would add about
14,000 lines and twelve tables, and every later fix would have to be made three times. The rules
(stage chain, status, timing, year types) are identical by owner decision, so they must stay in
one place.

The engine therefore gains two things:

1. **A module on every project** (`IT`, `MOLTI`, `SRE`).
2. **An optional assigned officer** on every project (used by MOLTI and SRE, always empty for IT).

Class and table names stay `ItProject…`. Renaming shipped tables would break migration lineage
for no user benefit; a code-only rename can be a later clean-up PR if wanted.

## Data model changes

One additive, hand-written migration on `ItProjects`:

| Column | Type | Notes |
| --- | --- | --- |
| `Module` | varchar(8), not null, default `'IT'` | Every existing row becomes IT. Indexed with `FinancialYearStart` |
| `ProjectOfficerUserId` | varchar(450), null | FK to users, `SET NULL` on delete. Indexed |
| `ProjectOfficerAssignedAtUtc` | timestamp, null | For "assigned since" on the page |

No other table changes: stages, financial years, remarks, remark audits and attachments hang off
the project and need no module of their own. Attachment files stay under
`{upload root}/it-projects/{projectId}/…` for all three modules (project ids are unique across
modules), so no file moves.

Deployment and rollback:

- **Overlap.** The columns are nullable or defaulted, so a process of the previous build that is
  still running keeps working while the migration is applied. It would list a MOLTI or SRE row
  on the IT board, but no such row can exist until the new build is serving, and the deployment
  SOP's outage step covers the rest.
- **Rollback.** The previous build does not start against the migrated database: startup
  (`DatabaseStartupMigrator`) refuses a migration history that holds a migration the deployed
  build does not know. Going back therefore follows the offline update and rollback SOP
  ([deployment/offline-ws2022.md](../deployment/offline-ws2022.md), section D2): restore the
  database backup taken before the update, or run the reviewed reverse migration (the
  migration's `Down` drops the two indexes and three columns), then start the previous build.
  Either way MOLTI and SRE projects entered since the update are lost (a restore also loses
  every other change since the backup), so the release note must say so.

## One catalogue for what differs (`ItProjectModules`)

A small code catalogue, like `ItProjectStages`, holds everything that differs per module:

| | IT | MOLTI | SRE |
| --- | --- | --- | --- |
| Route | `/ItProjects` | `/MoltiProjects` | `/SreProjects` |
| Menu and page title | IT projects | MOLTI projects | SRE projects |
| Run by | ITO role | assigned officer | assigned officer |
| Policies | `ItProjects.*` | `MoltiProjects.*` | `SreProjects.*` |
| Reference wording | IT ARPPP | see open question | see open question |

Pages, presentation classes and scripts read wording from the catalogue. No page text names a
module directly.

## Authorization

| Policy (per module) | IT | MOLTI and SRE |
| --- | --- | --- |
| View | any signed-in user | any signed-in user |
| Manage (create, assign, delete) | Admin, HoD, ITO | Admin, HoD |
| Work on a project (edit, stages, attachments, drop) | Admin, HoD, ITO | Admin, HoD, **the assigned officer of that project** |
| Remark | Comdt, HoD, MCO, ITO | Comdt, HoD, MCO, **the assigned officer of that project** |

"The assigned officer" is a resource check, not a role check. `ItProjectAccess` already receives
the project on every call, so it gains the rule there and every handler and service keeps calling
it. A Project Officer who is not assigned to a project can read it and nothing more. An officer
who is unassigned loses the rights at once; their remarks stay, with their name.

Every query is scoped by module. There are eleven `db.ItProjects` query sites today (read,
command, remark and attachment services, `LiveItProjects`, conference remarks); each takes the
module, and a project opened through another module's route is "not found". A test walks the
services to prove an IT call never returns a MOLTI or SRE row.

## Pages

One set of pages serves the three routes; the module comes from the route, never from a form
field. Board, create and project page look and behave as the IT ones do today. Additions for
MOLTI and SRE only:

1. **Project page header**: the assigned officer's name, or "No officer assigned". Admin and HoD
   get **Assign officer** / **Change officer** / **Unassign** (a PrismConfirm form modal with a
   list of active Project Officers). Every change is audited with the old and new officer.
2. **Board**: the officer's name on each row, an **Unassigned** filter for Admin and HoD, and a
   **My projects** filter for a Project Officer.
3. **Create** (Admin, HoD): the IT form plus an optional officer.

IT projects show none of these.

## Workspace

| View | IT today | MOLTI and SRE |
| --- | --- | --- |
| Officer workload card | every live IT project on each ITO's card | each live project on its assigned officer's card |
| Conference review | live IT projects under each ITO | live projects under their assigned officer, one section per module |
| Conference remark | officer must hold the ITO role | officer must be the project's assigned officer |
| Officer's own workspace | ITO sees an IT projects link and count | a Project Officer sees a link and count for each module they hold projects in |

Unassigned live projects appear in no officer's review. The board's Unassigned filter is where
Admin and HoD find them.

## Wiring

| Concern | Change |
| --- | --- |
| Navigation | two more left-drawer entries, each behind its own View policy |
| Access governance | `AdminRoleAccessCatalog` groups for MOLTI projects and SRE projects, with a line saying the assigned officer also has access |
| Usage | two more `ErpUsageModuleCatalog` modules |
| Audit | the existing `ItProjects.*` actions carry the module; the audit log names the right module at display time. New action: officer assigned / changed / unassigned |
| Docs | this file, `it-projects.md` (decision row "IT projects only" is superseded), `docs/data-domain.md`, `docs/razor-pages.md` |

Out of scope until asked: Dashboard cards for MOLTI and SRE, notifications on assignment, Search
indexing, export, moving a project between modules.

## Plan

| PR | Content | Tests |
| --- | --- | --- |
| 1 | This document | none |
| 2 | Engine: migration, `ItProjectModules` catalogue, module scoping on all query sites, policies, `ItProjectAccess` with the assigned-officer rule. No visible change | IT behaviour unchanged (existing suite), module isolation, access matrix per role and assignment, migration manifest |
| 3 | The two modules on screen: routes, wording from the catalogue, menu entries, access governance, usage. Projects can be created and run by Admin and HoD | page policy per route, wrong-module id is not found, wording never says "IT" on a MOLTI or SRE page |
| 4 | Officer assignment: assign, change, unassign, audit; officer on the page and board; Unassigned and My projects filters; assigned officer can work on and remark on their project | only Project Officers can be chosen, rights follow assignment and end on unassign, concurrency |
| 5 | Workspace: workload cards, conference review and conference remarks by assigned officer; officer's own workspace links | each project under its officer only, unassigned under nobody, IT feeds unchanged |
| 6 | Phones, dark theme, live check with realistic data in a throwaway database | none new |

Each PR is reviewed by Codex and merged when CI and Codex are clean.

## Open question

**Reference wording.** IT pages say "IT ARPPP reference number", "IT ARPPP / IPA" (first stage)
and "this year's IT ARPPP". What should MOLTI and SRE pages say in those places? Until the owner
decides, the plan uses the neutral "Reference number", "ARPPP / IPA" and "this year's list".
