# PRISM Media Catalogue Governance

This package finalises the post-reconciliation administration experience without changing original media files or the media database schema.

## Improvements

- Fixes the malformed `Last checked` Razor rendering by formatting timestamps in the PageModel.
- Uses the organisational India timezone consistently on Windows and Linux hosts.
- Adds search, status filtering and pagination for unavailable media.
- Adds a failure-category summary.
- Replaces internal enum values with clear user-facing status labels.
- Moves raw provider reasons, IDs and origins into expandable technical details.
- Hides historical reconciliation once no candidates remain.
- Renames recovery actions around administrator intent rather than implementation batch size.
- Clarifies lifetime processed totals versus current application-runtime counters.
- Keeps single-item photo collections visually balanced.

## Deployment

No database migration is required.

```powershell
dotnet clean
dotnet restore
dotnet build
dotnet test
```
