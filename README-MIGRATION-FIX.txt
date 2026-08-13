PRISM Publications Phase 22 — Migration discovery fix

Problem
-------
DatabaseStartupMigrator reports:
  Missing from assembly: 20261208130000_AddSharedCompendiumPresets

Root cause
----------
The Phase 22 migration source contained Up/Down methods and the immutable manifest entry,
but it omitted the EF Core migration discovery metadata normally generated in a migration
Designer partial:
  [DbContext(typeof(ApplicationDbContext))]
  [Migration("20261208130000_AddSharedCompendiumPresets")]

Because this project intentionally uses single-file hand-authored migrations, those attributes
must be present on the migration class itself. Without them, the class compiles but EF Core's
IMigrationsAssembly does not expose the migration ID, so PRISM's startup preflight correctly
rejects the build as internally inconsistent.

Replacement
-----------
Replace:
  Migrations/20261208130000_AddSharedCompendiumPresets.cs

Do NOT remove the migration ID from Migrations/immutable-migration-ids.txt.
Do NOT create a second migration with a new ID.
Do NOT change the database manually.

Then perform a clean rebuild:
  Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
  dotnet build .\ProjectManagement.csproj

Run the application again. The startup migrator should now see
20261208130000_AddSharedCompendiumPresets in both the compiled migration assembly and the
immutable manifest and can proceed to normal pending-migration handling.
