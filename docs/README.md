# PRISM ERP documentation

Use this index to navigate current engineering guidance. The repository also contains older implementation notes; those are useful historical evidence but may not describe the present code.

## Start here

| Document | Purpose |
| --- | --- |
| [../AGENTS.md](../AGENTS.md) | Rules and guardrails for Codex and other code agents. |
| [Current state](current-state.md) | Code-derived map of the present application and major domains. |
| [Engineering invariants](engineering-invariants.md) | Correctness, security, workflow, persistence and architecture constraints. |
| [Build and test](build-and-test.md) | Current restore/build/test commands and migration validation. |
| [Housekeeping policy](housekeeping.md) | How historical patch/readme/validation material should be classified and cleaned up. |
| [Housekeeping inventory](housekeeping-inventory.md) | Current root-clutter inventory, first-pass deletions, live dependencies and next cleanup candidates. |

## Detailed references

The following documents contain useful subsystem detail, but they predate the current documentation reset. Validate material claims against current code before relying on them for architectural changes.

| Guide | Scope |
| --- | --- |
| [Architecture and configuration](architecture.md) | Earlier high-level wiring, request pipeline, authorization and startup description. |
| [Data and domain](data-domain.md) | Earlier entity/domain overview. |
| [Infrastructure and services](infrastructure-services.md) | Cross-cutting helpers, workers and service abstractions. |
| [Razor Pages catalogue](razor-pages.md) | UI/page inventory and role checks. |
| [Configuration reference](configuration-reference.md) | Configuration keys and environment overrides. |
| [Projects module](projects-module.md) | Project workspace behavior and lifecycle narrative. |
| [Project Office Reports](ProjectOfficeReports_Directions.md) | Visits, Social Media, Proliferation, ToT, IPR and related reports. |
| [Timeline pipeline](timeline.md) | Plan versions, approvals and checklist integration. |
| [Search and OCR architecture](search-and-ocr-architecture.md) | Search/OCR implementation notes. |
| [Extending the platform](extending.md) | Older extension patterns and conventions. |
| [Action Tracker development note](action-tracker-development-note.md) | Action Tracker implementation detail. |
| [User guide](user-guide/README.md) | Operator-facing guidance. |
| [Manual tests](manual-tests/README.md) | Repeatable manual QA scripts. |

## Documentation maintenance rules

When changing behavior:

1. update a durable document only when it describes the affected behavior;
2. state important authorization, concurrency, transactional and audit invariants;
3. document configuration keys that operators must provide or understand;
4. prefer references to stable symbols/paths over fragile line-number citations;
5. do not create a new root-level phase README, patch-application guide or validation transcript as the long-term documentation for a feature;
6. when documentation conflicts with current executable code, fix the documentation unless the code itself is the defect.

Historical cleanup is tracked by the policy in [housekeeping.md](housekeeping.md).
