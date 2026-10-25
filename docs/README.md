# PRISM ERP documentation

Use this index to navigate current engineering guidance. Historical package notes and patch-delivery instructions have been removed from the repository root; Git history remains the archive for those materials.

## Start here

| Document | Purpose |
| --- | --- |
| [../AGENTS.md](../AGENTS.md) | Rules and guardrails for Codex and other code agents. |
| [Current state](current-state.md) | Code-derived map of the present application and major domains. |
| [Engineering invariants](engineering-invariants.md) | Correctness, security, workflow, persistence and architecture constraints. |
| [Build and test](build-and-test.md) | Current restore/build/test commands and migration validation. |
| [Migration policy](engineering/migrations.md) | Immutable EF migration lineage, creation and deployment rules. |
| [Production readiness](deployment/production-readiness.md) | Current production startup/deployment contract. |
| [Production checklist](deployment/production-checklist.md) | Operator checklist for offline/LAN deployment. |
| [Offline WS2022 deployment](deployment/offline-ws2022.md) | Air-gapped Windows Server deployment guidance. |
| [Publication fonts](deployment/publication-fonts.md) | Preparing and validating publication fonts for offline deployment. |
| [Media Library external folders](media-library/external-folders.md) | LAN/NAS filesystem source architecture and failure behavior. |
| [Media Library operator guide](media-library/operator-guide.md) | Adding and operating optional LAN/NAS media sources. |
| [Housekeeping policy](housekeeping.md) | Rules for keeping durable guidance separate from delivery residue. |
| [Housekeeping inventory](housekeeping-inventory.md) | Record of the repository-root cleanup and retained exceptions. |

## Detailed references

The following documents contain useful subsystem detail. Some predate the current documentation reset, so validate material claims against current code before making architectural changes.

| Guide | Scope |
| --- | --- |
| [Architecture and configuration](architecture.md) | Earlier high-level wiring, request pipeline, authorization and startup description. |
| [Data and domain](data-domain.md) | Earlier entity/domain overview. |
| [Infrastructure and services](infrastructure-services.md) | Cross-cutting helpers, workers and service abstractions. |
| [Razor Pages catalogue](razor-pages.md) | UI/page inventory and role checks. |
| [Dark theme palette](ux/dark-theme-palette.md) | Neutral-graphite dark theme: palette, principles, the opt-in Light/Dark/System switch, what is covered and how to convert a page. |
| [Theme coverage](ux/theme-coverage.md) | Generated per-stylesheet count of literal colours and dark-theme gaps, grouped into conversion waves; enforced by `npm test`. |
| [Search page and search box](ux/search.md) | What users see: the accessible search box, recent searches, shortcuts, failure banners, result cards, filters and paging. |
| [Configuration reference](configuration-reference.md) | Configuration keys and environment overrides. |
| [Chat](chat.md) | Person-to-person chat: buddy list, docked windows, API/hub contract, privacy position and configuration. |
| [Projects module](projects-module.md) | Project workspace behavior and lifecycle narrative. |
| [Project Officer briefing desk](ux/project-officer-briefing-desk.md) | PO landing page: brief-ready rules, inline posts and the services they reuse. |
| [Command desk](ux/command-desk.md) | HoD/Comdt desk: what moved forward, what waits on the viewer, what is lagging and which registers have gone quiet. |
| [Training tracker](ux/training.md) | The training register (filters, sort, month groups, phone cards), type colours, the Officers / JCOs / ORs split and the theme-aware trend chart. |
| [Visits to SDD](ux/visits.md) | The Visits register, record page and editor: what each shows, which figures follow the filters, messages and styles. |
| [IPR Register](ux/ipr-register.md) | The IPR workspace: four-figure summary, instant-filter register with Group by, record panel, and the remaining redesign steps. |
| [Public pages](ux/public-pages.md) | Signed-out landing and sign-in pages: the prism concept, where the copy comes from, tokens and the signed-out theme switch. |
| [Top bar, Dashboard and workspace headers](ux/app-shell-and-dashboard.md) | What a signed-in user sees first: top-bar fit rules by width, the Dashboard greeting header, workspace page headers and minimum type sizes. |
| [Project Office Reports](ProjectOfficeReports_Directions.md) | Visits, Social Media, Proliferation, ToT, IPR and related reports. |
| [Timeline pipeline](timeline.md) | Canonical timeline rules, implemented plan revision behavior and remaining target semantics. |
| [Timeline implementation roadmap](timeline-proposal-roadmap.md) | Completed B/C milestones, historical gaps and open D/E boundaries. |
| [Phase D handoff](phase-d-handoff.md) | Code-grounded starting points, invariants, research checklist and implementation acceptance gates for approved Actual correction/backfill. |
| [Phase D implementation plan](phase-d-implementation-plan.md) | Approved design, release sequence, test matrix, acceptance criteria and implementation status for Actual correction/backfill (D-1 to D-3 merged). |
| [Phase D release runbook](phase-d-release-runbook.md) | Coordinated D-2 + D-3 release: topology and writer inventory (K-12), migration-execution decision record (K-13), runbook, staging acceptance and recovery, rollback, blocker register and go/no-go. |
| [Phase D release readiness](phase-d-release-readiness.md) | D-3e acceptance evidence, release-blocker register (#2683, K-12, K-13), UX observations and remaining steps before the coordinated D-2 + D-3 release. |
| [Search and OCR architecture](search-and-ocr-architecture.md) | Search/OCR implementation notes. |
| [Search V2 ranking](search-v2-ranking.md) | Normalisation, retrieval tiers, clustering, keyset paging, autocomplete/did-you-mean and how to measure relevance. |
| [Extending the platform](extending.md) | Older extension patterns and conventions. |
| [Action Tracker development note](action-tracker-development-note.md) | Action Tracker implementation detail. |
| [User guide](user-guide/README.md) | Operator-facing guidance. |
| [Manual tests](manual-tests/README.md) | Repeatable manual QA scripts. |
| [Compendium cover-slot tests](manual-tests/compendium-cover-slots.md) | Manual acceptance scenarios for publication cover-slot stability. |

## Operational assets

Durable operator scripts and queries live outside the repository root:

- `ops/database/production-migration-inventory.sql`
- `ops/database/production-startup-diagnostic.sql`
- `tools/Add-PrismMigration.ps1`
- `tools/Verify-SearchV2.ps1`
- `tools/Install-PrismPublicationFonts.ps1`
- `tools/Test-PrismPublicationFonts.ps1`

## Documentation maintenance rules

When changing behavior:

1. update a durable document only when it describes the affected behavior;
2. state important authorization, concurrency, transactional and audit invariants;
3. document configuration keys that operators must provide or understand;
4. prefer references to stable symbols/paths over fragile line-number citations;
5. do not create a new root-level phase README, patch-application guide or validation transcript as long-term documentation;
6. when documentation conflicts with current executable code, fix the documentation unless the code itself is the defect.

Historical cleanup is tracked by the policy in [housekeeping.md](housekeeping.md).
