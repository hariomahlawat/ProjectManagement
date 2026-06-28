using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Data.Migrations;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaLibraryMigrationMetadataTests
{
    [Fact]
    public void UnifiedQueryIndexMigration_IsDiscoverableByMediaLibraryContext()
    {
        var migrationType = typeof(AddUnifiedLibraryQueryIndexes);

        var contextAttribute = migrationType
            .GetCustomAttributes(typeof(DbContextAttribute), inherit: false)
            .Cast<DbContextAttribute>()
            .Single();
        var migrationAttribute = migrationType
            .GetCustomAttributes(typeof(MigrationAttribute), inherit: false)
            .Cast<MigrationAttribute>()
            .Single();

        Assert.Equal(typeof(MediaLibraryDbContext), contextAttribute.ContextType);
        Assert.Equal("20260628090000_AddUnifiedLibraryQueryIndexes", migrationAttribute.Id);
        Assert.Equal("__EFMigrationsHistory_MediaLibrary", MediaLibraryDbContext.MigrationsHistoryTable);
    }
}
