# PRISM Media Derivative Commit and Retry Fix

## Corrected defects

- SkiaSharp no longer writes directly into the destination `FileStream`.
- Encoded WebP bytes are written by .NET only.
- The temporary file handle is fully disposed before the final rename.
- Cache-versioned derivatives are committed atomically and never overwritten in place.
- Cross-process races accept an already completed non-empty derivative.
- Transient sharing violations use bounded retry with jitter.
- Zero-byte derivative remnants are treated as invalid and regenerated.
- Missing source content is marked unavailable and moved directly to dead-letter.
- Routine PRISM reconciliation does not revive unchanged permanently unavailable assets.
- Content changes clear stale hashes and analysis output and correctly queue reprocessing.
- Bulk retry now retries recoverable failures only.
- Permanent failures require an explicit confirmed force-retry operation.
- Worker retry scheduling includes jitter to prevent retry storms.

## Deployment

1. Copy this folder into the ProjectManagement root and replace existing files.
2. Run `dotnet clean`, `dotnet restore`, `dotnet build`, and `dotnet test`.
3. Restart PRISM.
4. Use **Retry recoverable jobs** once for previous `IOException` and `ObjectDisposedException` failures.
5. Do not force-retry missing-content jobs unless the files have been restored.

No database migration is required.
