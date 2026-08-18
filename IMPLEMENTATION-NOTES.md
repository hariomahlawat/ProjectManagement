# Implementation Notes

## ZIP pipeline

The previous handler created `ZipArchive` directly on `Response.Body`. Although media copying used `CopyToAsync`, `ZipArchive.Dispose()` writes the central directory synchronously. Kestrel correctly rejected that synchronous response write.

The revised design is:

`selected asset IDs -> canonical visibility -> content resolution -> private temp ZIP -> finalise ZIP -> reopen read-only -> FileStreamResult`

This keeps synchronous ZIP finalisation on a local `FileStream`, where it is valid, and never weakens ASP.NET Core's synchronous-I/O protection.

## Failure semantics

- A source that cannot be opened **before** its ZIP entry exists is skipped.
- A source that fails **after** its entry exists aborts the archive. This avoids returning a ZIP that knowingly contains a truncated member.
- A stale/hidden/archived/disabled-source asset is excluded again on the server, irrespective of what the browser selected earlier.
- If all selected assets are no longer eligible/readable, no download is started.

## Temporary-file ownership

Archives are created under:

`<MediaLibrary CacheRoot>/bulk-downloads/`

They are not static web assets. A successful archive is reopened with `DeleteOnClose`; failure/cancellation paths delete it explicitly. A bounded best-effort sweep removes abandoned `.zip.partial` files older than 24 hours.

## Visibility policy

`IMediaAssetVisibilityPolicy` is now propagated further into People/identity reads and mutations so disabling/hiding a media source cannot leave that source contributing to identity matching or directory statistics after it has disappeared from Photos.

## Database

No schema change and no EF migration.
