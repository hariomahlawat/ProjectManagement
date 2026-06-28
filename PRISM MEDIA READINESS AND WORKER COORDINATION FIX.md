# PRISM Media Readiness and Worker Coordination Fix

This package corrects the custom EF migration-history lookup, separates physical operational readiness from migration-history consistency, restores worker operation when the schema is physically usable, makes the index migration discoverable and idempotent, and prevents the availability-reconciliation worker from exiting permanently during a temporary schema outage.

## Deployment

1. Copy the package contents into the ProjectManagement root.
2. Run `dotnet clean` and `dotnet restore`.
3. Run `dotnet ef migrations list --context MediaLibraryDbContext`.
4. Run `dotnet ef database update --context MediaLibraryDbContext`.
5. Run `dotnet build` and `dotnet test`.
6. Restart PRISM.

The catalogue remains available when its physical schema is compatible even if migration-history metadata needs repair. Administrators receive a warning and an Apply migrations action; background processing is not unnecessarily disabled.
