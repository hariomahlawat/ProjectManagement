using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports.Training;

public sealed class TrainingExportServiceTests
{
    [Fact]
    public async Task ExportAsync_MissingUserId_ReturnsFailure()
    {
        await using var context = CreateContext();
        var readService = new TrainingTrackerReadService(context);
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 7, 0, 0, TimeSpan.Zero));
        var service = new TrainingExportService(readService, new TrainingExcelWorkbookBuilder(), clock);

        var request = new TrainingExportRequest(
            TrainingTypeId: null,
            Category: null,
            ProjectTechnicalCategoryId: null,
            From: null,
            To: null,
            Search: null,
            IncludeRoster: false,
            RequestedByUserId: "");

        var result = await service.ExportAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.File);
        Assert.Contains(result.Errors, error => error.Contains("requesting user", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportAsync_InvalidDateRange_ReturnsFailure()
    {
        await using var context = CreateContext();
        var readService = new TrainingTrackerReadService(context);
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 7, 30, 0, TimeSpan.Zero));
        var service = new TrainingExportService(readService, new TrainingExcelWorkbookBuilder(), clock);

        var request = new TrainingExportRequest(
            TrainingTypeId: null,
            Category: null,
            ProjectTechnicalCategoryId: null,
            From: new DateOnly(2024, 5, 15),
            To: new DateOnly(2024, 5, 1),
            Search: null,
            IncludeRoster: false,
            RequestedByUserId: "export-user");

        var result = await service.ExportAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.File);
        Assert.Contains(result.Errors, error => error.Contains("start date", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportAsync_WithValidInput_ReturnsWorkbook()
    {
        await using var context = CreateContext();
        var trainingTypeId = Guid.NewGuid();
        var trainingId = Guid.NewGuid();
        var projectId = 100;
        var parentCategory = new TechnicalCategory
        {
            Id = 10,
            Name = "Engineering",
            CreatedByUserId = "seed"
        };
        var technicalCategory = new TechnicalCategory
        {
            Id = 11,
            Name = "Networking",
            ParentId = parentCategory.Id,
            CreatedByUserId = "seed"
        };
        context.TechnicalCategories.AddRange(parentCategory, technicalCategory);

        var project = new Project
        {
            Id = projectId,
            Name = "Project Atlas",
            CreatedByUserId = "creator",
            TechnicalCategoryId = technicalCategory.Id
        };
        context.Projects.Add(project);

        var trainingType = new TrainingType
        {
            Id = trainingTypeId,
            Name = "Infantry Bootcamp",
            CreatedByUserId = "creator",
            CreatedAtUtc = new DateTimeOffset(2024, 3, 1, 6, 0, 0, TimeSpan.Zero)
        };
        context.TrainingTypes.Add(trainingType);

        var training = new Training
        {
            Id = trainingId,
            TrainingTypeId = trainingTypeId,
            TrainingType = trainingType,
            StartDate = new DateOnly(2024, 4, 5),
            EndDate = new DateOnly(2024, 4, 12),
            LegacyOfficerCount = 0,
            LegacyJcoCount = 0,
            LegacyOrCount = 0,
            Notes = "Field readiness drills.",
            CreatedByUserId = "creator",
            CreatedAtUtc = new DateTimeOffset(2024, 4, 1, 8, 0, 0, TimeSpan.Zero),
            ProjectLinks =
            {
                new TrainingProject
                {
                    TrainingId = trainingId,
                    ProjectId = projectId,
                    Project = project
                }
            },
            Counters = new TrainingCounters
            {
                TrainingId = trainingId,
                Officers = 5,
                JuniorCommissionedOfficers = 4,
                OtherRanks = 3,
                Total = 12,
                Source = TrainingCounterSource.Current,
                UpdatedAtUtc = new DateTimeOffset(2024, 4, 12, 12, 0, 0, TimeSpan.Zero)
            }
        };
        context.Trainings.Add(training);

        context.TrainingTrainees.Add(new TrainingTrainee
        {
            TrainingId = trainingId,
            Training = training,
            ArmyNumber = "A123",
            Rank = "Capt",
            Name = "R. Iyer",
            UnitName = "45 Signals",
            Category = 0
        });

        await context.SaveChangesAsync();

        var readService = new TrainingTrackerReadService(context);
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 8, 30, 0, TimeSpan.Zero));
        var service = new TrainingExportService(readService, new TrainingExcelWorkbookBuilder(), clock);

        var request = new TrainingExportRequest(
            TrainingTypeId: trainingTypeId,
            Category: TrainingCategory.Officer,
            ProjectTechnicalCategoryId: technicalCategory.Id,
            From: new DateOnly(2024, 4, 1),
            To: new DateOnly(2024, 4, 30),
            Search: "  Atlas  ",
            IncludeRoster: true,
            RequestedByUserId: "export-user");

        var result = await service.ExportAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        var file = Assert.NotNull(result.File);
        Assert.Equal("training-tracker-20240501-083000.xlsx", file.FileName);
        Assert.Equal(TrainingExportFile.ExcelContentType, file.ContentType);
        Assert.NotEmpty(file.Content);

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var trainingsSheet = workbook.Worksheet("Trainings");

        Assert.Equal("Trainings", trainingsSheet.Name);
        Assert.Equal("Infantry Bootcamp", trainingsSheet.Cell(2, 2).GetString());
        Assert.Equal("Field readiness drills.", trainingsSheet.Cell(2, 11).GetString());

        var metadataStartRow = 4;
        Assert.Equal("Export generated", trainingsSheet.Cell(metadataStartRow, 1).GetString());
        Assert.Equal("From", trainingsSheet.Cell(metadataStartRow + 1, 1).GetString());
        Assert.Equal("2024-04-01", trainingsSheet.Cell(metadataStartRow + 1, 2).GetString());
        Assert.Equal("To", trainingsSheet.Cell(metadataStartRow + 2, 1).GetString());
        Assert.Equal("2024-04-30", trainingsSheet.Cell(metadataStartRow + 2, 2).GetString());
        Assert.Equal("Search", trainingsSheet.Cell(metadataStartRow + 3, 1).GetString());
        Assert.Equal("Atlas", trainingsSheet.Cell(metadataStartRow + 3, 2).GetString());
        Assert.Equal("Include roster", trainingsSheet.Cell(metadataStartRow + 4, 1).GetString());
        Assert.Equal("Yes", trainingsSheet.Cell(metadataStartRow + 4, 2).GetString());
        Assert.Equal("Training type", trainingsSheet.Cell(metadataStartRow + 5, 1).GetString());
        Assert.Equal("Infantry Bootcamp", trainingsSheet.Cell(metadataStartRow + 5, 2).GetString());
        Assert.Equal("Category", trainingsSheet.Cell(metadataStartRow + 6, 1).GetString());
        Assert.Equal("Officers", trainingsSheet.Cell(metadataStartRow + 6, 2).GetString());
        Assert.Equal("Technical category", trainingsSheet.Cell(metadataStartRow + 7, 1).GetString());
        Assert.Equal("Networking", trainingsSheet.Cell(metadataStartRow + 7, 2).GetString());
        Assert.Equal("Technical category display", trainingsSheet.Cell(metadataStartRow + 8, 1).GetString());
        Assert.Equal("— Networking", trainingsSheet.Cell(metadataStartRow + 8, 2).GetString());

        var rosterSheet = workbook.Worksheet("Roster");
        Assert.Equal("Roster", rosterSheet.Name);
        Assert.Equal("Infantry Bootcamp", rosterSheet.Cell(2, 1).GetString());
        Assert.Equal("Project Atlas", rosterSheet.Cell(2, 3).GetString());
        Assert.Equal("45 Signals", rosterSheet.Cell(2, 7).GetString());
        Assert.Equal(0, rosterSheet.Cell(2, 8).GetValue<int>());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
