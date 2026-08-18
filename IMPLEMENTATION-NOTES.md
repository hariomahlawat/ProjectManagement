# Implementation Notes

## Access policy
`IMediaAssetVisibilityPolicy` is the single read-visibility rule for catalogue media. The same rule is now used by the Photos catalogue query, direct `/Photos/Media`, People review visibility, Collections, and ZIP download.

## Collections
Collections are queried independently by `CollectionKey`; they are no longer derived from the current Photos timeline page. Single-item project-only collections are hidden by default, while one-item Visit/Event/Activity collections remain visible. The underlying collection remains intact and can be exposed with the singleton toggle.

## Selection and bulk export
Select mode is intentionally non-destructive. ZIP export accepts asset IDs only and revalidates them server-side. Items temporarily shown through the PRISM live fallback are selectable visually but are not eligible for catalogue bulk actions until catalogued; the selection bar states this explicitly.

## Identity governance
The phase preserves the existing human-confirmation boundary. Suggested identity groups start with zero selected members. Known-person group suggestions only populate/reject against the reviewer-selected faces. “Similarity” is never presented as identity probability.

## Review triage
Bulk Leave Unidentified and Not a Face are transaction-backed service operations with batch limits, stale-selection checks, audit records, and one candidate/grouping refresh after the batch rather than one refresh per face.

## Compatibility
No schema changes are introduced. Existing `IMediaPeopleQueryService.GetReviewQueueAsync(kind, page, size, token)` remains available and delegates to the extended query contract, reducing breakage risk for existing callers.
