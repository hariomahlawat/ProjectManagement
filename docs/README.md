# Documentation index

These guides describe PRISM as it is in the code. They were re-verified against the source on 2026-09-27.
When code and a guide disagree, the code wins: fix the guide in the same change.

## Start here

| Guide | Scope |
| --- | --- |
| [Development](development.md) | Prerequisites, build/run/test commands, seeding, migrations helper, CI workflows and current test status. |
| [Architecture](architecture.md) | Solution layout, startup sequence and middleware order, roles and all authorization policies, hosted workers, minimal APIs/controllers, SignalR, database startup gate. |
| [Configuration reference](configuration-reference.md) | Every configuration key and environment variable the code reads, with code defaults and appsettings values. |
| [Data and domain](data-domain.md) | Both DbContexts, entity groups per module, concurrency, soft delete/archival, delete behaviours, file storage. |
| [Infrastructure and services](infrastructure-services.md) | Cross-cutting services: clocks, storage and download tokens, transactions, notifications, workers registry. |
| [Extending the platform](extending.md) | How to add roles, policies, pages, workers and migrations safely. |
| [Migrations policy](../MIGRATIONS-POLICY.md) | Immutable migration manifests, automatic startup migration, CI gate. |
| [Code review 2026-09](review-2026-09.md) | Findings from the audit that produced this documentation set, ranked by severity. |

## UI and users

| Guide | Scope |
| --- | --- |
| [Razor Pages catalogue](razor-pages.md) | Every routable page, its handlers and the policy/roles that gate it. |
| [UI access audit](ui-access-audit.md) | Navigation structure, role × module reachability, nav/policy mismatches. |
| [User guide](user-guide/README.md) | Role-oriented guide for end users. |
| [Legacy project entry](ux/legacy-entry.md) | Legacy/completed project entry form behaviour. |
| [Manual tests](manual-tests/README.md) | Repeatable QA scripts. |

## Modules

| Guide | Scope |
| --- | --- |
| [Projects module](projects-module.md) | Project lifecycle, stages, documents, media, remarks, guardrails. |
| [Timeline pipeline](timeline.md) | Plan versions, approvals, stage schedule and checklist integration. |
| [Project Office Reports](ProjectOfficeReports_Directions.md) | Visits, Social Media, Proliferation, ToT, IPR, Training, FFC, ARPP, Progress Review, including Repeat Build rules. |
| [FFC architecture](ffc-module-architecture.md) · [FFC widget](ffc-widget-spec.md) | FFC records, project linking, map widget, exports. |
| [Action Tracker](action-tracker-development-note.md) | Task management / sprints, UTC persistence with IST display. |
| [Progress review verification](progress-review-verification.md) | Progress review report checks. |
| [Visit Excel export](VisitExcelExportPlan.md) | Visits export. |
| [Module guides](modules/README.md) | Publications & Compendiums, Briefing decks & presentations, ARPP, Industry Partners, Project Ideas, Reports/Workspace/Dashboard, Admin/Usage/Activities. |
| [Notebook](notebook.md) | Notebook API, data model, committed front-end bundle, trash retention. |
| [Media Library (Photos)](media-library.md) | Media DbContext, sources and ingestion, derivatives, face models, workers, permissions. |
| [Global search](global-search.md) | Search V2 gateway, projection index, rollout flags, adding a module. |
| [Search and OCR architecture](search-and-ocr-architecture.md) · [FTS note](fts_implementation_note.md) | OCR workers and pipeline, full-text columns and triggers. |
| [Remarks API (OpenAPI)](openapi/remarks.yaml) · [Postman collection](postman/remarks.postman_collection.json) | Project remarks endpoints. |

## Operations

| Guide | Scope |
| --- | --- |
| [Offline deployment on Windows Server 2022](deployment/offline-ws2022.md) | Prerequisites, publish, IIS, configuration, first start, verification. |
| [Disaster recovery](disaster-recovery.md) | Backup/restore scripts for database, files and data-protection keys. |
| [Backup readiness](production-readiness-backup.md) | What must be backed up and where each store lives. |
| [Notebook on PostgreSQL](notebook-postgresql-deployment.md) · [HTTP 415 capture](notebook-http415-capture.md) | Notebook deployment and troubleshooting notes. |
| [Storage hardening](storage-hardening.md) · [Storage migration plan](storage-migration-plan.md) · [Archive/trash](archive-trash-plan.md) | Design records with an implementation-status line at the top of each. |

## Historical records

- `superpowers/specs` and `superpowers/plans`: Search V2 design and plan records, each marked with its implementation status. Their checklists were never ticked, so ignore them.
- `bugs/`: past bug investigations with a per-item Fixed/Open status.
- Root-level `README-*`, `PHASE*`, `_FFC_PHASE*`, `PRISM_*` and `*.patch` files: unmaintained delivery notes (see the root [README](../README.md#repository-hygiene)).

## Keeping docs current

When changing a module, update its guide with (1) the behaviour and main flows, (2) the edge cases: validation, concurrency,
authorization and audit, and (3) any new configuration keys, which also go in [configuration-reference.md](configuration-reference.md).
Refer to code by file path and type/member name rather than line numbers.
