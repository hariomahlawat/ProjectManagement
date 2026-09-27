# Module Reference

These pages document modules that had no maintained documentation before. Each page was checked against the code. Where anything disagrees, the code (`Program.cs`, `Configuration/Policies.cs`, `[Authorize]` attributes and the services) is authoritative.

The root-level delivery notes are historical and must not be used as specifications. This includes `PRISM_Publications_Phase*_README.md`, `README-IPR-*.md`, `_FFC_PHASE*_README_FIRST.md`, `README_ADMIN_PHASE_*.md` and the `*.patch` files.

| File | Scope |
|---|---|
| [publications-and-compendiums.md](publications-and-compendiums.md) | Capability Brochure and Simulators Compendium PDF pipeline (QuestPDF), presets, page planners, cover, fonts, self-test |
| [briefings-and-presentations.md](briefings-and-presentations.md) | Project Briefing Deck Builder (OpenXML `.pptx` from a template) and the FFC Global Footprint PowerPoint |
| [arpp.md](arpp.md) | ARPP issue register: Original/Addenda, verification and publication, unlock, attachments, reconciliation, IPA stage authority, published library |
| [industry-partners.md](industry-partners.md) | Industry organisation directory, contacts, attachments, multi-JDP project links |
| [project-ideas.md](project-ideas.md) | Project Idea governance lifecycle, typed discussion, notes, documents, soft-delete recovery |
| [reports-workspace-dashboard.md](reports-workspace-dashboard.md) | `/Projects/Reports` (ARPP FY and FFC Projects Update exports), Command and Project Officer Workspace, Conference review, Dashboard widgets |
| [admin-usage-activities.md](admin-usage-activities.md) | Administration centre capability policies and services, user lifecycle, audit/log exports, ERP usage, Activities, Conference remarks, Calendar/Celebrations/Todo |

**Authorization baseline.** No fallback authorization policy is configured in `Program.cs`. Pages are protected only by explicit `[Authorize]` attributes and Razor conventions:

- `/Dashboard`, `/Projects/Publications` and the whole `Admin` area require authentication.
- `/Index`, `/Privacy` and `Identity/Account/Login` are anonymous.

Any new page must carry its own `[Authorize]`.
