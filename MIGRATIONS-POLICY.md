# PRISM EF Core Migration Policy

## Deployment rule

`ApplicationDbContext` and `MediaLibraryDbContext` form one deployment boundary. The
application startup gate acquires one PostgreSQL advisory lock, validates immutable
migration lineage, applies both migration sets, validates critical physical schema, and
only then permits hosted workers and HTTP traffic.

Runtime pages and hosted workers must never execute `Database.Migrate()`.

## Immutable identity

Once a migration identifier has been applied to any shared database, its complete ID is
permanent. Do not rename, delete, reuse, or edit an applied migration. Historical lineage
bridges must remain in source even when later migrations supersede their original DDL.

The authoritative manifests are:

- `Migrations/immutable-migration-ids.txt`
- `Features/MediaLibrary/Data/Migrations/immutable-migration-ids.txt`

CI verifies that the manifests exactly match EF Core discovery.

## Creating a migration

The repository already contains migration timestamps later than the current calendar date.
Creating a normal EF migration directly can therefore place new work in the middle of the
existing chain. Use the repository helper from the project root:

```powershell
./tools/Add-PrismMigration.ps1 -Name DescribeTheChange -Context ApplicationDbContext
```

For the media catalogue:

```powershell
./tools/Add-PrismMigration.ps1 -Name DescribeTheChange -Context MediaLibraryDbContext
```

The helper generates the migration, advances its timestamp beyond the current lineage tail
when necessary, updates the generated metadata, and appends the immutable manifest.

## Verification before deployment

1. `npm ci`
2. `dotnet tool restore`
3. `dotnet restore`
4. `dotnet build -c Release`
5. `dotnet test -c Release`
6. Run the guarded PostgreSQL migration-chain test or rely on the
   `Production Database Migration Gate` CI workflow.
7. Compare production history using `PRODUCTION-MIGRATION-INVENTORY.sql`.
8. Deploy a complete Release publish; never copy source files into the IIS publish folder.

Database downgrades are unsupported. Do not delete rows from either EF migration-history
table to bypass the startup gate.

The repository-local `dotnet-ef` tool is pinned to the EF Core 8 patch used by the
application. Do not upgrade it independently of the EF Core runtime/tooling packages.
