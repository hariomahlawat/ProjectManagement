# PRISM project portfolio — replacement instructions

This package contains path-preserved replacement files for the remaining
Project Overview and Project Repository presentation defects.

## Apply

1. Back up the current solution and database.
2. Stop the running application.
3. Extract the archive into the solution root that contains
   `ProjectManagement.csproj`.
4. Allow the files to overwrite the matching relative paths.
5. Confirm that `Pages/Projects/Overview.cshtml`,
   `Pages/Projects/_ProjectCommandHeader.cshtml`, and
   `wwwroot/css/pages/project-portfolio.css` were all replaced together.
6. Clean, restore, build, test, and publish from the updated source.

Recommended commands:

```powershell
dotnet clean
dotnet restore
dotnet build -c Release
dotnet test -c Release --no-build
dotnet publish -c Release -o .\publish
```

Deploy the new publish output as one unit. Do not mix it with an older publish
directory because compiled Razor views and fingerprinted static assets must
come from the same build.

## Database impact

No database schema or data migration is required.

## Preserved business rule

The first unresolved applicable workflow stage remains the project's
**current stage**, including when its status is `NotStarted`. The change only
makes the accompanying action and schedule wording status-aware.

## Integrity and scope

- `REPLACEMENT-MANIFEST.txt` lists every source and regression-test file to
  replace.
- `SHA256SUMS.txt` provides package integrity hashes.
- `STATIC-VALIDATION.txt` records the checks completed in the supplied
  environment.
