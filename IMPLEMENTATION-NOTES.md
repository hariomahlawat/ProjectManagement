# PRISM Photos — People Review Workflow Integrity & Throughput

This package is a ready-to-paste delta over the current PRISM Photos v3 / Bulk Export Hardening implementation.

## Implemented

- Canonical review workload semantics for known matches, individual review, active matching, matching failures, closed-unidentified appearances, total unresolved appearances, and identity-group snapshot metrics.
- Faces in Pending/Processing remain unresolved but are not shown as actionable individual review; queue-clear messaging no longer hides active matching.
- Evidence-driven candidate invalidation: routine review decisions only invalidate grouping; trusted-reference/person-visibility changes requeue the unresolved candidate corpus; candidate rejection/reopen uses bounded face-only rematching.
- Bounded rematching is batched server-side rather than issuing one database update per selected face.
- Identity grouping runtime retains the last successful snapshot, tracks invalidation generations, wakes promptly on mutations, exposes freshness, and protects against stale in-flight refreshes.
- Groups remain on the Groups workspace during background refresh. A stale snapshot is view-only until the refreshed snapshot is explicitly reloaded.
- Group metrics now distinguish ungrouped appearances from the live individual-review workload.
- Group mosaic thumbnails are the selection surface; no duplicate checkbox list and nothing is preselected.
- “Leave unidentified” is replaced by reversible “Close unidentified”; a dedicated Closed unidentified queue supports single/bulk reopen and bounded rematching.
- Not-a-face single/bulk actions require explicit client confirmation and retain server-side validation.
- Review workstation header/toolbars are more compact; batch toolbar is hidden until selection exists and uses a shell-aware sticky offset.
- Routine matching is automatic. Manual corpus-wide “Re-run matching” is demoted to More and requires confirmation.
- Lightweight workload polling updates counts/status while matching or grouping refresh is active without reloading the page.
- Media-scoped review does not mix global identity-group metrics into the scoped workload; the Groups workspace remains corpus-level.
- Existing-person controls are omitted when no confirmed person exists; explanatory copy adapts accordingly.
- Candidate matching and new close/suppress review mutations consistently respect the canonical media-visibility policy.
- People directory remains available if only the review-workload summary fails to load.
- Operational grouping is exposed only when the People worker and grouping worker are actually enabled.
- Deployment checklist terminology updated for Close/Reopen semantics.

## Persistence

No EF Core migration is required. Closed-unidentified state reuses the existing candidate-null `Ignored` review decision and audit infrastructure.

## Deliberately not included

This phase does not change recognition models, similarity thresholds, automatic identity-confirmation policy, or introduce a new identity schema. Searchable/typeahead person selection and deeper service/CSS decomposition remain future scalability/maintenance work; the current confirmed-person population does not justify destabilising this integrity phase.
