# PRISM ERP current state

This document is a code-derived orientation guide. It intentionally describes the current repository rather than the historical sequence of implementation phases.

## Application shape

PRISM ERP is a .NET 8 ASP.NET Core modular monolith. The primary web application combines:

- Razor Pages and Areas for the main UI
- HTTP API endpoints
- SignalR for real-time notification/update flows
- EF Core with PostgreSQL
- ASP.NET Core Identity and policy-based authorization
- hosted/background services
- document, spreadsheet, PDF, presentation, image and media processing
- a Node/esbuild build step for Notebook frontend assets

The primary composition root is `Program.cs`. The primary persistence model is `Data/ApplicationDbContext.cs`.

## Major functional domains visible in current code

The current EF model and service registrations show these broad domains:

- Project master data and project access
- Stage/workflow execution, templates, dependencies and checklist governance
- Plan versions, stage plans, approvals, snapshots and realignment
- Authoritative project facts such as SOW, AON, benchmark, commercial, PNC, supply-order and production-cost facts
- ARPP
- Transfer of Technology (ToT)
- Proliferation
- FFC
- IPR
- Project Office visits, social-media events and training
- Activities
- Industry partners
- Project ideas
- Documents, document repository, OCR/text and attachments
- Project photos/videos and the enterprise Media Library
- Remarks, comments, mentions and notifications
- Notebook
- Action tasks and sprints
- Calendars and celebrations
- Publications, brochures, compendiums and briefing decks
- Search
- Usage/analytics
- Audit and administration

This list is descriptive, not a declaration that every domain has a clean module boundary today.

## Persistence

`ApplicationDbContext` is a large shared context containing Identity plus most PRISM operational data. The enterprise Media Library also has dedicated data/migration concerns registered from startup.

Because many modules share the same database boundary, changes must be evaluated for cross-module invariants, cascade behavior, query cost, migration safety and transaction scope.

## Project as the central reference

`Project` is the central business reference for a large portion of PRISM, but many authoritative values are intentionally stored in dedicated fact or workflow entities rather than as columns on the project row.

Examples include dedicated project fact entities and separate planning/workflow entities. Consumers should not infer that a convenient display field is necessarily the authoritative source.

## Workflow and planning

Lifecycle behavior is represented by more than a single status field. The codebase contains stage templates/dependencies, project stages, checklist templates/audits, stage change records, plan versions, stage plans, approval logs, plan snapshots, realignment records and stage-shift history.

When changing lifecycle behavior, trace the command path and its validation/authorization/audit/transaction behavior end to end. Do not implement a parallel transition path in a page or export.

## Authorization

PRISM uses ASP.NET Core Identity plus a substantial policy/capability model. `Program.cs` registers application policies and module-specific policies.

Authorization must be checked on the server for the actual resource being accessed. A role/capability that permits a class of action does not automatically imply access to every project/document/entity instance.

## Background and real-time behavior

The application uses hosted services and SignalR. Background operations should be designed for cancellation, retry and duplicate execution. Where an authoritative database change causes asynchronous work, prefer an outbox-style handoff over a non-transactional side effect.

## Search

Both `Services/Search` and `Services/SearchV2` are currently present in the composition root. Treat this as an active convergence area. Before adding search behavior, determine which implementation is authoritative for that use case and avoid introducing a third path.

## Reporting and generated artifacts

PRISM generates multiple output forms using libraries such as ClosedXML, OpenXML and QuestPDF. Business rules should be resolved before rendering. Output-specific code should format a canonical semantic/read model rather than independently recalculate domain facts.

## Documentation status

The repository contains many historical phase notes, patch files, replacement-package instructions and validation manifests. They are historical evidence unless current code or current durable docs explicitly reference them.

See `docs/housekeeping.md` for the cleanup policy and `AGENTS.md` for agent rules.
