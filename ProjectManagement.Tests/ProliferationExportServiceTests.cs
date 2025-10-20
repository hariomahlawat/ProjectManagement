using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProliferationExportServiceTests
{
    [Fact]
    public async Task ExportAsync_WithYearsAndRange_ReturnsFailure()
    {
        using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 8, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var tracker = new ProliferationTrackerReadService(context);
        var service = new ProliferationExportService(
            context,
            tracker,
            new ProliferationExcelWorkbookBuilder(),
            clock,
            audit,
            NullLogger<ProliferationExportService>.Instance);

        var request = new ProliferationExportRequest(
            Years: new[] { 2024 },
            FromDate: new DateOnly(2024, 1, 1),
            ToDate: new DateOnly(2024, 12, 31),
            Source: ProliferationSource.Sdd,
            ProjectCategoryId: null,
            TechnicalCategoryId: null,
            Search: null,
            RequestedByUserId: "user-1");

        var result = await service.ExportAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.File);
        Assert.Contains(result.Errors, error => error.Contains("either", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task ExportAsync_WithFilters_ProducesWorkbookAndAudit()
    {
        using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 9, 30, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();

        var projectCategory = new ProjectCategory
        {
            Id = 1,
            Name = "Readiness",
            SortOrder = 1,
            IsActive = true,
            CreatedByUserId = "seed",
            CreatedAt = clock.UtcNow.UtcDateTime
        };

        var technicalCategory = new TechnicalCategory
        {
            Id = 7,
            Name = "Simulation",
            SortOrder = 1,
            IsActive = true,
            CreatedByUserId = "seed",
            CreatedAt = clock.UtcNow.UtcDateTime
        };

        var project = new Project
        {
            Id = 42,
            Name = "Simulator Expansion",
            CaseFileNumber = "SIM-042",
            CreatedByUserId = "seed",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CategoryId = projectCategory.Id,
            TechnicalCategoryId = technicalCategory.Id
        };

        var yearly = new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Source = ProliferationSource.Sdd,
            Year = 2024,
            TotalQuantity = 120,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime,
            LastUpdatedOnUtc = clock.UtcNow.UtcDateTime,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        var granular = new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Source = ProliferationSource.Sdd,
            UnitName = "Alpha",
            ProliferationDate = new DateOnly(2024, 3, 15),
            Quantity = 70,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime,
            LastUpdatedOnUtc = clock.UtcNow.UtcDateTime,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        var preference = new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Source = ProliferationSource.Sdd,
            Year = 2024,
            Mode = YearPreferenceMode.UseGranular,
            SetByUserId = "pref",
            SetOnUtc = clock.UtcNow.UtcDateTime
        };

        context.ProjectCategories.Add(projectCategory);
        context.TechnicalCategories.Add(technicalCategory);
        context.Projects.Add(project);
        context.ProliferationYearlies.Add(yearly);
        context.ProliferationGranularEntries.Add(granular);
        context.ProliferationYearPreferences.Add(preference);
        await context.SaveChangesAsync();

        var tracker = new ProliferationTrackerReadService(context);
        var service = new ProliferationExportService(
            context,
            tracker,
            new ProliferationExcelWorkbookBuilder(),
            clock,
            audit,
            NullLogger<ProliferationExportService>.Instance);

        var request = new ProliferationExportRequest(
            Years: new[] { 2024 },
            FromDate: null,
            ToDate: null,
            Source: ProliferationSource.Sdd,
            ProjectCategoryId: projectCategory.Id,
            TechnicalCategoryId: technicalCategory.Id,
            Search: "Alpha",
            RequestedByUserId: "user-9");

        var result = await service.ExportAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.File);
        var file = result.File!;
        Assert.Equal(ProliferationExportFile.ExcelContentType, file.ContentType);
        Assert.EndsWith(".xlsx", file.FileName, StringComparison.OrdinalIgnoreCase);

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);

        Assert.Equal("Proliferation Overview", worksheet.Name);
        Assert.Equal("Year", worksheet.Cell(1, 1).GetString());
        Assert.Equal("Project", worksheet.Cell(1, 2).GetString());

        var firstDataRow = worksheet.Row(2);
        Assert.Equal(2024, firstDataRow.Cell(1).GetValue<int>());
        Assert.Equal("Simulator Expansion (SIM-042)", firstDataRow.Cell(2).GetString());
        Assert.Equal("SDD", firstDataRow.Cell(3).GetString());
        Assert.Equal("Granular", firstDataRow.Cell(4).GetString());
        Assert.Equal("Alpha", firstDataRow.Cell(5).GetString());
        Assert.Equal(new DateTime(2024, 3, 15), firstDataRow.Cell(6).GetDateTime().Date);
        Assert.Equal(70, firstDataRow.Cell(7).GetValue<int>());
        Assert.Equal(70, firstDataRow.Cell(8).GetValue<int>());
        Assert.Equal("Approved", firstDataRow.Cell(9).GetString());
        Assert.Equal("UseGranular", firstDataRow.Cell(10).GetString());

        var secondDataRow = worksheet.Row(3);
        Assert.Equal("Yearly", secondDataRow.Cell(4).GetString());
        Assert.Equal(120, secondDataRow.Cell(7).GetValue<int>());
        Assert.Equal(70, secondDataRow.Cell(8).GetValue<int>());

        Assert.Equal("Export generated", worksheet.Cell(5, 1).GetString());
        Assert.Equal("Requested by", worksheet.Cell(6, 1).GetString());
        Assert.Equal("user-9", worksheet.Cell(6, 2).GetString());
        Assert.Equal("Years", worksheet.Cell(7, 1).GetString());
        Assert.Equal("2024", worksheet.Cell(7, 2).GetString());
        Assert.Equal("Date range", worksheet.Cell(8, 1).GetString());
        Assert.Equal("(not set)", worksheet.Cell(8, 2).GetString());
        Assert.Equal("Source", worksheet.Cell(9, 1).GetString());
        Assert.Equal("SDD", worksheet.Cell(9, 2).GetString());
        Assert.Equal("Project category", worksheet.Cell(10, 1).GetString());
        Assert.Equal("Readiness", worksheet.Cell(10, 2).GetString());
        Assert.Equal("Technical category", worksheet.Cell(11, 1).GetString());
        Assert.Equal("Simulation", worksheet.Cell(11, 2).GetString());
        Assert.Equal("Search", worksheet.Cell(12, 1).GetString());
        Assert.Equal("Alpha", worksheet.Cell(12, 2).GetString());

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ProjectOfficeReports.Proliferation.ExportGenerated", entry.Action);
        Assert.Equal("user-9", entry.UserId);
        Assert.Equal("2024", entry.Data["Years"]);
        Assert.Equal("SDD", entry.Data["Source"]);
        Assert.Equal("Sdd", entry.Data["SourceValue"]);
        Assert.Equal("1", entry.Data["ProjectCategoryId"]);
        Assert.Equal("Readiness", entry.Data["ProjectCategoryName"]);
        Assert.Equal("7", entry.Data["TechnicalCategoryId"]);
        Assert.Equal("Simulation", entry.Data["TechnicalCategoryName"]);
        Assert.Equal("Alpha", entry.Data["SearchTerm"]);
        Assert.Equal("2", entry.Data["RowCount"]);
        Assert.Equal(file.FileName, entry.Data["FileName"]);
    }

    [Fact]
    public async Task ExportAsync_WithYearFilter_RelationalProvider_Succeeds()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 10, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();

        var project = new Project
        {
            Id = 100,
            Name = "Relational Project",
            CreatedByUserId = "seed",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        };

        var yearly = new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Source = ProliferationSource.Sdd,
            Year = 2024,
            TotalQuantity = 10,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime,
            LastUpdatedOnUtc = clock.UtcNow.UtcDateTime,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        context.Projects.Add(project);
        context.ProliferationYearlies.Add(yearly);
        await context.SaveChangesAsync();

        var tracker = new ProliferationTrackerReadService(context);
        var service = new ProliferationExportService(
            context,
            tracker,
            new ProliferationExcelWorkbookBuilder(),
            clock,
            audit,
            NullLogger<ProliferationExportService>.Instance);

        var request = new ProliferationExportRequest(
            Years: new[] { 2024 },
            FromDate: null,
            ToDate: null,
            Source: null,
            ProjectCategoryId: null,
            TechnicalCategoryId: null,
            Search: null,
            RequestedByUserId: "relational-user");

        var result = await service.ExportAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.File);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
