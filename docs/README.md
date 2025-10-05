# Documentation index

Use these guides to navigate the codebase and extend the platform safely. Each document focuses on a particular slice of the system so teams can update them independently.

| Guide | Scope |
| --- | --- |
| [Architecture and Configuration](architecture.md) | High-level layering, request pipeline, authentication/authorization policies, background services, and startup configuration. |
| [Data and Domain](data-domain.md) | Entity relationships, key domain models (tasks, celebrations, projects, notifications), and how persistence concerns are organised. |
| [Infrastructure and Services](infrastructure-services.md) | Cross-cutting helpers, hosted workers, and service abstractions available to page models and APIs. |
| [Razor Pages Catalogue](razor-pages.md) | Feature-by-feature walkthrough of the UI layer, including available actions, filters, and role checks. |
| [Configuration Reference](configuration-reference.md) | Exhaustive list of configuration keys, defaults, and environment overrides. |
| [Projects Module Storyboard](projects-module.md) | Narrative guide to the procurement-heavy projects workspace, including personas, lifecycle, and guardrails. |
| [Timeline Pipeline](timeline.md) | Deep dive into plan versions, approvals, and checklist integration for the project timeline editor. |
| [Extending the Platform](extending.md) | Patterns and guardrails for adding roles, properties, services, or Razor Pages without breaking conventions. |
| [Storyboard User Guide](user-guide/README.md) | Persona-oriented walkthrough for operators and project teams covering dashboard, tasks, calendar, celebrations, projects, admin, and notifications. |
| [Manual Tests](manual-tests/README.md) | Repeatable QA scripts for accessibility and regression checks (kept in sync with UI updates). |

When modifying a module, update the relevant guide with:

1. **Feature summary** – describe the intent and main flows.
2. **Edge cases** – call out validation, concurrency, or audit behaviours that future maintainers must preserve.
3. **Configuration touchpoints** – mention any new appsettings or environment flags introduced.

Keeping these documents current ensures onboarding engineers can reason about the code without spelunking through every service.
