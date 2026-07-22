using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcDataIntegrityModelTests
{
    [Fact]
    public void Model_EnforcesOneActiveCountryYearAndOneLinkedProjectPerRecord()
    {
        using var db = CreateDbContext();

        var recordType = db.Model.FindEntityType(typeof(FfcRecord));
        Assert.NotNull(recordType);
        var activeRecordIndex = Assert.Single(recordType!.GetIndexes().Where(index =>
            index.GetDatabaseName() == "UX_FfcRecords_CountryId_Year_Active"));
        Assert.True(activeRecordIndex.IsUnique);
        Assert.Equal("\"IsDeleted\" = FALSE", activeRecordIndex.GetFilter());

        var projectType = db.Model.FindEntityType(typeof(FfcProject));
        Assert.NotNull(projectType);
        var linkedProjectIndex = Assert.Single(projectType!.GetIndexes().Where(index =>
            index.GetDatabaseName() == "UX_FfcProjects_Record_LinkedProject"));
        Assert.True(linkedProjectIndex.IsUnique);
        Assert.Equal("\"LinkedProjectId\" IS NOT NULL", linkedProjectIndex.GetFilter());

        var rowVersion = projectType.FindProperty(nameof(FfcProject.RowVersion));
        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken);
        Assert.False(rowVersion.IsNullable);
    }

    [Fact]
    public void Model_EnforcesDeliveryInstallationConsistency()
    {
        using var db = CreateDbContext();
        var projectType = db.Model.FindEntityType(typeof(FfcProject));
        Assert.NotNull(projectType);

        var constraints = projectType!.GetCheckConstraints()
            .ToDictionary(constraint => constraint.Name, constraint => constraint.Sql);

        Assert.Equal(
            "\"IsInstalled\" = FALSE OR \"IsDelivered\" = TRUE",
            constraints["CK_FfcProjects_Installed_RequiresDelivered"]);
        Assert.Equal(
            "\"DeliveredOn\" IS NULL OR \"InstalledOn\" IS NULL OR \"InstalledOn\" >= \"DeliveredOn\"",
            constraints["CK_FfcProjects_InstallationDate_NotBeforeDeliveryDate"]);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=prism_metadata_only;Username=unused;Password=unused")
            .Options;

        return new ApplicationDbContext(options);
    }
}
