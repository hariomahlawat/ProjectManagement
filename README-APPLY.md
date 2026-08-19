# PRISM Photos candidate visibility compatibility hotfix

Copy the project files in this package over the project root and overwrite the matching file.

This hotfix restores the canonical `IMediaAssetVisibilityPolicy` dependency and the optional `visibleAssetIds` argument on `FaceCandidateSearchService.BuildReferenceRowsQuery` while retaining the cancellation checks added by the matching-recovery phase.

No database migration or configuration change is required.
