PRISM Photos - Matching Recovery Options Compatibility Hotfix

Replace these two files under the project root:
  Features/MediaLibrary/Options/MediaLibraryOptions.cs
  Features/MediaLibrary/Options/MediaLibraryOptionsValidator.cs

This fixes a regression in the Matching Recovery package where option declarations from earlier Photos phases were accidentally omitted.

Restored and preserved together:
- MediaBulkDownloadOptions + BulkDownload bind point
- ReviewTriageBatchLimit
- GroupingReviewModerateSimilarityThreshold
- GroupingReviewStrongSimilarityThreshold
- CandidateSearchTimeoutSeconds
- CandidateProcessingStaleSeconds
- CandidateFailureRetryDelaySeconds
- validation for all of the above

No EF migration or appsettings change is required.
