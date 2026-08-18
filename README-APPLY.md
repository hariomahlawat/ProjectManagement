# Apply first — People Review Workflow Integrity

**Baseline:** apply this over the current Photos v3 / Bulk Export Hardening implementation already running in your project.

Copy the contents of this package into:

`E:\Dot Net Web Development\ProjectManagement\`

and overwrite the matching files. Directory structure is already preserved.

No database migration is required.

After copying, run:

```powershell
dotnet clean
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
node --check .\wwwroot\js\pages\photos-people-review.js
node --check .\wwwroot\js\pages\photos-people-directory.js
```

Key manual regression checks: open Individual review while matching is active; Close and Reopen an unidentified face; confirm bulk Not-a-face prompt; open Groups during a refresh and verify the retained snapshot is read-only; reject a candidate and confirm only the affected face returns to matching; change a trusted reference/hide a person and confirm unresolved matching is requeued; verify People directory remains usable if review workload data is unavailable.
