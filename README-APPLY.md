# PRISM Photos — Bulk Export Reliability & Workflow Consolidation

This package is a **ready-to-paste delta over the Photos Next Phase v2 implementation** currently shown in your screenshots.

## Apply

Copy the contents of this folder into the **ProjectManagement project root**, preserving folders, and overwrite matching files.

No EF migration is required.

## What this fixes

1. **Download ZIP synchronous-I/O failure**
   - ZIP archives are now fully built and finalised in a private temporary file under `MediaLibrary:CacheRoot/bulk-downloads`.
   - `ZipArchive.Dispose()` never writes to `Response.Body`.
   - ASP.NET Core receives a seekable `FileStreamResult` and performs the HTTP transfer.
   - `AllowSynchronousIO` is **not** enabled.

2. **Archive integrity and resource protection**
   - bounded item count and source-byte limit;
   - canonical visibility policy is reapplied server-side;
   - unreadable files are skipped only before an entry is created;
   - a mid-copy source failure aborts the entire ZIP rather than returning a knowingly truncated entry;
   - duplicate file names are made unique;
   - ZIP entry names are normalised and sanitised independently of the host OS to prevent archive-path traversal;
   - successful temporary archives use `DeleteOnClose`;
   - abandoned `.zip.partial` files older than 24 hours are cleaned up opportunistically.

3. **Selection-mode UI**
   - selection check no longer collides with the People badge;
   - workspace reserves bottom space for the fixed bulk-action bar;
   - `Select all visible` is renamed to the unambiguous `Select page`.

4. **Canonical visibility across People/identity workflows**
   - People directory photo counts and person details;
   - candidate reference search;
   - candidate refresh queue;
   - identity grouping;
   - face-review mutations/reference handling;
   - face thumbnails and person portraits;
   - Photos People-filter counts.

5. **People workload presentation**
   - known-person suggestions, suggested groups, individual review, and matching failures are exposed as separate queue links;
   - the page no longer presents overlapping workloads as one misleading aggregate.

## Optional configuration

Defaults require no `appsettings` change:

```json
"MediaLibrary": {
  "BulkDownload": {
    "MaxItems": 120,
    "MaxSourceBytes": 2147483648
  }
}
```

`MaxSourceBytes` is the total **uncompressed source bytes read** while constructing one ZIP. It is more useful than estimating ZIP size because photographs/videos are already compressed.

## Verification on development machine

```powershell
dotnet clean
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

node --check .\wwwroot\js\pages\photos-library.js
node --check .\wwwroot\js\pages\photos-people-review.js
```

Then verify in browser:

- select two or more catalogue-backed photographs;
- click **Download ZIP**;
- confirm the ZIP opens and all selected files are present;
- select a photograph carrying a People badge and confirm both the selection check and People badge remain visible;
- scroll to the final row while Select mode is active and confirm it can be brought fully above the action bar;
- disable/hide an external media source (in a test environment) and verify its media no longer contributes to Photos/People counts or identity review.

## Environment validation performed here

- JavaScript syntax checked with Node for both Photos scripts.
- All changed C# files passed lexical delimiter/string/comment balance checks.
- The generated patch was dry-run successfully against a clean v2 baseline.
- `.NET SDK` is not installed in this execution environment, so `dotnet build`/xUnit could not be executed here.
