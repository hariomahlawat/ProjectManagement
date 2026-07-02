PRISM PRODUCTION-SAFE RECONCILIATION V1
=======================================

BASELINE
--------
Apply these files to the current source archive:
ProjectManagement-aarav (3)(1).zip

Do not layer the earlier Optional Data Protection Keys or Production Startup Migration Fix
packages over this package. This package supersedes their Program.cs/configuration changes.

FILES TO REPLACE
----------------
Program.cs
appsettings.json
appsettings.Production.json
appsettings.Development.json
Features/MediaLibrary/Options/MediaLibraryOptionsValidator.cs
ProjectManagement.Tests/MediaLibraryOptionsValidatorTests.cs

WHAT IS CORRECTED
-----------------
1. Restores all three appsettings files byte-for-byte from the known-working
   "publish 01 Jul 2026" production publish, including the existing connection strings,
   storage paths and environment-specific values.
2. Restores the deployed Data Protection key location behaviour so existing cookies and
   antiforgery keys are not moved to a new repository during this upgrade.
3. Stops Program.cs from rewriting Include Error Detail or any other connection-string option.
4. Restores the established migration switch:
   - Development always applies migrations.
   - Production follows Database:ApplyMigrationsOnStartup (currently true in Production).
5. Keeps ApplicationDbContext migration/schema closure startup-critical.
6. Isolates Media Library/Photos migration or readiness failure from the core ERP. A Photos
   failure is logged and diagnosed, but it no longer returns HTTP 500.30 for the whole system.
7. Accepts the existing MediaLibrary:AutoMigrate setting as a backward-compatible legacy key;
   the effective policy is Database:ApplyMigrationsOnStartup.
8. Preserves all current Notebook notification, SignalR, Calendar, Photos and UI fixes already
   present in the supplied current codebase.

MIGRATIONS
----------
This reconciliation package does not add, delete, rename or edit any migration file and does
not modify either immutable migration manifest. Do not edit __EFMigrationsHistory manually.
The current Production setting remains ApplyMigrationsOnStartup=true.

BUILD AND DEPLOY
----------------
1. Replace the files in the source tree.
2. Run:

   npm ci
   npm test
   dotnet clean
   dotnet restore
   dotnet build -c Release
   dotnet test -c Release
   dotnet publish -c Release -o E:\Publish\PRISM

3. Stop the IIS application pool.
4. Back up the existing published folder and database.
5. Deploy the complete fresh publish output, not loose .cs files.
6. Start the application pool.

If core database startup still fails, inspect the newest diagnostic under:
F:\ProjectManagementData\startup-diagnostics

VALIDATION PERFORMED IN THE PREPARATION ENVIRONMENT
---------------------------------------------------
- appsettings files confirmed byte-for-byte identical to the live publish.
- JSON validation passed for all appsettings files.
- Application migration manifest: 61 unique IDs, exact assembly-source order.
- Media migration manifest: 10 unique IDs, exact assembly-source order.
- Modified C# structural/delimiter checks passed.
- JavaScript regression suite: 135/135 passed after npm ci.

The preparation environment does not contain the .NET SDK; the Release C# build must be run
on the development machine before deployment.
