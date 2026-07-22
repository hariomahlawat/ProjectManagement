# Project Briefing Deck — Test Build Fix

This package corrects the remaining `ProjectManagement.Tests` build failures after the duplicate-tree repair.

## What it fixes

- Updates the technical-category hierarchy test for the command-service architecture.
- Supplies the required `IAdminAuditService` dependency in activity-type tests.
- Removes the ambiguous public capability-paginator overload.
- Uses the EF Core `IMigrator` API for migration-to-target integration testing.
- Resolves EF metadata nullability warnings in FFC integrity tests.
- Replaces the xUnit substring assertion pattern with `Assert.StartsWith`.

## Apply

From PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\APPLY-FIX.ps1 `
  -ProjectRoot "E:\Dot Net Web Development\ProjectManagement"
```

The script backs up the six affected files, applies the replacements, verifies SHA-256 hashes, and removes stale test `bin`/`obj` output.

Then reopen Visual Studio and select **Build > Rebuild Solution**.
