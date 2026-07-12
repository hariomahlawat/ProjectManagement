PRISM PROJECTSTAGES STARTUP ROOT-CAUSE FIX — READY TO REPLACE
============================================================

WHAT THIS FIXES
---------------
1. Removes the legacy ordinary-startup behaviour that recreated
   CK_ProjectStages_CompletedHasDate with the obsolete ActualStart requirement.
2. Adds a new forward-only EF migration:
     20261201160000_FinalizeProjectStageCompletionConstraint
3. Repairs production databases where migrations through 20261201150000 are already
   recorded but the physical constraint was later regressed by an older application build.
4. Makes deployed migrations mandatory before requests are accepted.
5. Aligns ASP.NET Core and IIS upload transport ceilings and removes the IIS
   maxAllowedContentLength mismatch warning.
6. Prevents old publish/artifacts folders from being copied into a later publish.

REQUIRED SOURCE BASELINE
------------------------
Use the current production-ready source containing all 61 existing ApplicationDbContext
migrations through:
  20261201150000_ReconcileProjectStageCompletionConstraint

It must also contain:
  Infrastructure/DatabaseStartupMigrator.cs
  Infrastructure/ApplicationDatabaseSchemaValidator.cs
  Migrations/immutable-migration-ids.txt

The uploaded ProjectManagement-master (1)(4).zip is an older 51-migration source tree.
DO NOT publish that older tree to the present production database.

REPLACEMENT
-----------
Copy every file and folder from this package into the ProjectManagement application root.
Allow folder merge and replace matching files. Preserve the relative folder structure.

Do not delete, rename, or edit any historical migration. Do not delete rows from
__EFMigrationsHistory.

BUILD AND PUBLISH
-----------------
From the application root run:

  npm ci
  dotnet restore
  dotnet build -c Release
  dotnet test -c Release
  .\ops\publish\create-publish-folder.ps1

The validated output is created at:
  artifacts\publish\ProjectManagement

Deploy the CONTENTS of that folder into a new empty IIS application directory. Do not
copy over an old publish folder and do not deploy the nested source 'publish' directory.
Preserve the production connection string and other server-specific settings.

EXPECTED FIRST START
--------------------
ApplicationDbContext should report:
  Known=62
  Applied=61
  Pending=1

It should then apply:
  20261201160000_FinalizeProjectStageCompletionConstraint

After migration, physical schema validation should pass. The final constraint must be:
  Status <> Completed OR CompletedOn IS NOT NULL OR RequiresBackfill IS TRUE

ActualStart remains optional.

SUPERUSER WARNING
-----------------
No change has been made to the PostgreSQL superuser warning, as directed.
