# PRISM ERP engineering invariants

These invariants are intended to constrain maintenance and Codex-assisted changes. They should be converted into automated tests wherever practical.

## Authority and consistency

1. Executable code, EF mappings/migrations and tests outrank historical implementation notes.
2. A business fact should have one authoritative source and one canonical resolution path.
3. UI, Excel, PDF, PPT and other outputs must not independently reimplement business rules.
4. Cached, denormalized or display-oriented values must not silently become a second source of truth.
5. A project's cost is resolved only by `ProjectCostResolver`, taking the first figure above zero: a completed project uses PNC, then L1, then the cost recorded on the project; any other project uses PNC, then L1, then AoN, then IPA. Pages, reports, exports, briefing decks and analytics read it from there. The header's "Latest cost" tile, the supply-order amount, the IPA amount, proliferation cost and the Completed projects summary's "Recorded development cost" (the recorded figure itself) are different facts and keep their own rules.

## Authorization

5. Every state-changing HTTP/API/SignalR operation must enforce authorization server-side.
6. Project/document/entity access must be resource-aware where access differs by instance.
7. File and media downloads must authorize before opening or streaming protected content.
8. UI visibility is convenience only; it is never the security boundary.

## Workflow and transactions

9. A workflow transition must have one canonical command path.
10. Validation must occur before authoritative mutation.
11. Authoritative state, mandatory audit/history rows and transactional outbox records should commit atomically when they represent one business action.
12. Redundant representations of current workflow state must be kept consistent or eliminated.
13. Concurrency tokens/guards must be preserved on edit and approval flows that can be changed by more than one actor.

## Persistence and migrations

14. Existing migration lineage is immutable unless the migration policy explicitly allows a correction.
15. New schema changes require migration tests and must support a clean migration from the supported baseline.
16. Destructive data changes require an explicit migration/backfill/rollback strategy.
17. Avoid loading large object graphs when a projection can satisfy a read path.

## Background work

18. Hosted workers must honor cancellation.
19. Queue/outbox consumers must be idempotent or safely duplicate-tolerant.
20. Failed/poison work must be observable and must not create an unbounded hot retry loop.
21. Multi-instance execution must not allow two workers to claim the same exclusive work item without coordination.

## Files, documents and media

22. Never trust a browser-supplied filename, extension or MIME type as sufficient validation.
23. Storage paths must not be derivable from untrusted path fragments.
24. Upload size/type constraints belong at both transport and service/domain boundaries as appropriate.
25. Database failure after physical file creation must not silently leave unmanaged files; physical cleanup failure after authoritative deletion must be observable.

## Architecture

26. PRISM remains a modular monolith by default.
27. EF Core remains the persistence abstraction; do not introduce generic repositories.
28. Razor PageModels/controllers/endpoints orchestrate; they should not become alternate domain-service implementations.
29. New cross-cutting abstractions must reduce duplication across real call sites, not merely add another layer.
30. Parallel implementations (`Legacy`, `V2`, `New`) require a documented convergence/removal path.

## Offline/LAN deployment

31. Production runtime must remain functional without public Internet connectivity.
32. Do not introduce required public CDN, cloud API, SaaS, external identity, remote-font, public telemetry or Internet callback dependencies without an explicitly approved architecture/deployment change.
33. Required browser/runtime assets must be deployable locally or from approved LAN infrastructure.
34. Offline deployment does not weaken application security requirements; the LAN must not be treated as implicitly trusted.
35. Runtime failure caused solely by unavailable public Internet access is a defect for any non-optional production feature.
36. Deployment packages must document and include required native/runtime dependencies that cannot be restored from the Internet at runtime.

## Change discipline

37. Prefer small, independently testable changes.
38. For risky fixes, add a regression test that demonstrates the failure mode before or with the fix.
39. Update durable documentation when a change invalidates it.
40. Do not use historical patch/readme bundles as instructions without first validating them against current code.
41. Project timeline date/status semantics have one canonical business-rule source: [Canonical timeline business rules](timeline.md#canonical-timeline-business-rules). Razor/UI, JavaScript, PageModels/controllers, services and reports/exports must not independently redefine them; implementation deviations require separately reviewed production-safe correction.
42. PO timeline editing remains non-blocking while HoD controls publication of PO proposals. Preserve exact revision identity and supersession; reject stale decisions rather than locking the PO. The [canonical proposal rules](timeline.md#22-continuous-po-work-and-approval-scope) define the contract, while the [roadmap](timeline-proposal-roadmap.md) records deferred implementation gaps.
