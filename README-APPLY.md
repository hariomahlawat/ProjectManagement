# Apply — PRISM Photos Identity Matching Bootstrap & Reference Readiness

This package is a ready-to-paste delta for the current PRISM Photos codebase after the Person Profile / Find More Photos phase and its recent compatibility hotfixes.

## Apply

Copy the package contents over the project root:

`E:\Dot Net Web Development\ProjectManagement\`

Allow Windows to overwrite matching files. The folder structure in this package matches the project structure exactly.

Do **not** restore an older full Photos package after applying this delta; doing so may roll back the recent Album DI, matching-recovery, options, candidate-visibility and Person Profile compatibility fixes.

## Database/configuration

- EF migration: **not required**
- appsettings change: **not required**

The implementation reuses existing `MediaProcessingJobs`, face embeddings, reference status, candidate-search status and identity-audit tables.

## Recommended verification

```powershell
dotnet clean
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

node --check .\wwwroot\js\pages\photos-library.js
node --check .\wwwroot\js\pages\photos-curation.js
node --check .\wwwroot\js\pages\photos-person-profile.js
node --test .\wwwroot\js\pages\photos-person-profile-contract.test.js
```

Restart PRISM after the build. Existing candidate faces that have been stuck in `Processing` will be recovered by the candidate worker's existing stale-processing logic; when the trusted-reference corpus is empty they will now complete deterministically with zero known-person candidates and become eligible for individual review.
