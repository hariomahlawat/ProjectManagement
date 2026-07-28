using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.Utilities.Reporting;
using Xunit;
using TrainingEntity = ProjectManagement.Areas.ProjectOfficeReports.Domain.Training;

namespace ProjectManagement.Tests.ProjectOfficeReports.Training;

public sealed class TrainingExportServiceTests
{
    [Fact]
    public async Task ExportAsync_ProjectFilterIsHonoured_AndFilenameUsesIst()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var audit = new RecordingAudit();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var service = CreateService(context, clock, audit);

        var result = await service.ExportAsync(
            CreateRequest(projectId: seed.ProjectAId),
            CancellationToken.None);

        Assert.True(result.Success);
        var file = Assert.IsType<TrainingExportFile>(result.File);
        Assert.Equal("training-tracker-20240501-053000-IST.xlsx", file.FileName);

        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var trainings = workbook.Worksheet("Trainings");
        Assert.Equal(seed.TrainingAId.ToString(), trainings.Cell(2, 2).GetString());
        Assert.True(trainings.Cell(3, 2).IsEmpty());
        Assert.Equal("Project Alpha", workbook.Worksheet("Summary").Cell("F7").GetString());

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ProjectOfficeReports.TrainingExportGenerated", entry.Action);
        Assert.Equal("1", entry.Data["TrainingRowCount"]);
        Assert.Equal(seed.ProjectAId.ToString(), entry.Data["ProjectId"]);
    }

    [Fact]
    public async Task ExportAsync_SelectedCategoryRosterScope_FiltersOnlyRosterRows()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(
            context,
            FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 1, 0, 0, TimeSpan.Zero)),
            new RecordingAudit());

        var request = CreateRequest(
            projectId: seed.ProjectAId,
            category: TrainingCategory.Officer,
            includeRoster: true,
            rosterScope: TrainingRosterScope.SelectedTraineeCategoryOnly);

        var result = await service.ExportAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        using var workbook = new XLWorkbook(new MemoryStream(result.File!.Content));
        var trainings = workbook.Worksheet("Trainings");
        var roster = workbook.Worksheet("Roster");

        Assert.Equal(2, trainings.Cell(2, 12).GetValue<int>()); // complete event total
        Assert.Equal("Officer", roster.Cell(2, 10).GetString());
        Assert.True(roster.Cell(3, 10).IsEmpty());
    }

    [Fact]
    public async Task ExportAsync_OrdersTrainingRowsByTrainingDateDescending()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(
            context,
            FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 1, 0, 0, TimeSpan.Zero)),
            new RecordingAudit());

        var result = await service.ExportAsync(CreateRequest(projectId: null), CancellationToken.None);

        Assert.True(result.Success);
        using var workbook = new XLWorkbook(new MemoryStream(result.File!.Content));
        var trainings = workbook.Worksheet("Trainings");

        Assert.Equal(seed.TrainingAId.ToString(), trainings.Cell(2, 2).GetString());
        Assert.Equal(new DateTime(2024, 4, 10), trainings.Cell(2, 4).GetDateTime());
        Assert.Equal(new DateTime(2024, 3, 10), trainings.Cell(3, 4).GetDateTime());
    }

    [Fact]
    public async Task ExportAsync_ExceedingConfiguredTrainingLimit_ReturnsControlledFailure()
    {
        await using var context = CreateContext();
        await SeedAsync(context);
        var service = CreateService(
            context,
            FakeClock.AtUtc(DateTimeOffset.UtcNow),
            new RecordingAudit(),
            maxTrainingRows: 1);

        var result = await service.ExportAsync(CreateRequest(projectId: null), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.File);
        Assert.Contains(result.Errors, error => error.Contains("current limit is 1", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExportAsync_InvalidDateRange_ReturnsValidationFailure()
    {
        await using var context = CreateContext();
        var service = CreateService(
            context,
            FakeClock.AtUtc(DateTimeOffset.UtcNow),
            new RecordingAudit());

        var result = await service.ExportAsync(
            CreateRequest(
                projectId: null,
                from: new DateOnly(2024, 5, 2),
                to: new DateOnly(2024, 5, 1)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.File);
        Assert.Contains(result.Errors, error => error.Contains("start date", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportAsync_ExceedingConfiguredRosterLimit_ReturnsControlledFailure()
    {
        await using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(
            context,
            FakeClock.AtUtc(DateTimeOffset.UtcNow),
            new RecordingAudit(),
            maxRosterRows: 1);

        var result = await service.ExportAsync(
            CreateRequest(projectId: seed.ProjectAId, includeRoster: true),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.File);
        Assert.Contains(result.Errors, error => error.Contains("roster rows", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("current limit is 1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportAsync_SelectedRosterCategoryWithoutEventCategory_ReturnsValidationFailure()
    {
        await using var context = CreateContext();
        var service = CreateService(
            context,
            FakeClock.AtUtc(DateTimeOffset.UtcNow),
            new RecordingAudit());

        var request = CreateRequest(
            projectId: null,
            category: null,
            includeRoster: true,
            rosterScope: TrainingRosterScope.SelectedTraineeCategoryOnly);

        var result = await service.ExportAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("Select a trainee category", StringComparison.OrdinalIgnoreCase));
    }

    private static TrainingExportService CreateService(
        ApplicationDbContext context,
        IClock clock,
        RecordingAudit audit,
        int maxTrainingRows = 5000,
        int maxRosterRows = 50000)
        => new(
            new TrainingTrackerReadService(context),
            new TrainingExcelWorkbookBuilder(),
            clock,
            new StubOptionsSnapshot<TrainingTrackerOptions>(new TrainingTrackerOptions
            {
                Enabled = true,
                MaxExportTrainingRows = maxTrainingRows,
                MaxExportRosterRows = maxRosterRows,
                ExportTimeoutSeconds = 120
            }),
            audit,
            NullLogger<TrainingExportService>.Instance);

    private static TrainingExportRequest CreateRequest(
        int? projectId,
        TrainingCategory? category = null,
        bool includeRoster = false,
        TrainingRosterScope rosterScope = TrainingRosterScope.AllTraineesInMatchingEvents,
        DateOnly? from = null,
        DateOnly? to = null)
        => new(
            TrainingTypeId: null,
            Category: category,
            ProjectId: projectId,
            ProjectTechnicalCategoryId: null,
            From: from,
            To: to,
            Search: null,
            IncludeRoster: includeRoster,
            RosterScope: rosterScope,
            RequestedByUserId: "export-user",
            RequestedByDisplayName: "Export User",
            ApplicationBaseUrl: "https://prism.local");

    private static async Task<SeedResult> SeedAsync(ApplicationDbContext context)
    {
        var type = new TrainingType
        {
            Id = Guid.NewGuid(),
            Name = "Simulator",
            DisplayOrder = 1,
            IsActive = true,
            CreatedByUserId = "seed",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var category = new TechnicalCategory
        {
            Id = 10,
            Name = "AR / VR",
            IsActive = true,
            CreatedByUserId = "seed"
        };

        var projectA = new Project
        {
            Id = 101,
            Name = "Project Alpha",
            CreatedByUserId = "seed",
            TechnicalCategoryId = category.Id,
            TechnicalCategory = category
        };

        var projectB = new Project
        {
            Id = 102,
            Name = "Project Bravo",
            CreatedByUserId = "seed",
            TechnicalCategoryId = category.Id,
            TechnicalCategory = category
        };

        var trainingA = CreateTraining(type, new DateOnly(2024, 4, 10), officers: 1, ors: 1);
        var trainingB = CreateTraining(type, new DateOnly(2024, 3, 10), officers: 0, ors: 3);

        trainingA.ProjectLinks.Add(new TrainingProject
        {
            TrainingId = trainingA.Id,
            ProjectId = projectA.Id,
            Project = projectA
        });
        trainingB.ProjectLinks.Add(new TrainingProject
        {
            TrainingId = trainingB.Id,
            ProjectId = projectB.Id,
            Project = projectB
        });

        trainingA.Trainees.Add(new TrainingTrainee
        {
            TrainingId = trainingA.Id,
            Training = trainingA,
            ArmyNumber = "A001",
            Rank = "Capt",
            Name = "Officer One",
            UnitName = "Unit A",
            Category = (byte)TrainingCategory.Officer
        });
        trainingA.Trainees.Add(new TrainingTrainee
        {
            TrainingId = trainingA.Id,
            Training = trainingA,
            ArmyNumber = "A002",
            Rank = "Hav",
            Name = "Other Rank One",
            UnitName = "Unit A",
            Category = (byte)TrainingCategory.OtherRank
        });

        context.TrainingTypes.Add(type);
        context.TechnicalCategories.Add(category);
        context.Projects.AddRange(projectA, projectB);
        context.Trainings.AddRange(trainingA, trainingB);
        await context.SaveChangesAsync();

        return new SeedResult(projectA.Id, trainingA.Id);
    }

    private static TrainingEntity CreateTraining(
        TrainingType type,
        DateOnly startDate,
        int officers,
        int ors)
    {
        var id = Guid.NewGuid();
        return new TrainingEntity
        {
            Id = id,
            TrainingTypeId = type.Id,
            TrainingType = type,
            StartDate = startDate,
            EndDate = startDate.AddDays(1),
            Notes = "Export test",
            CreatedByUserId = "seed",
            CreatedAtUtc = startDate.ToDateTime(TimeOnly.MinValue),
            Counters = new TrainingCounters
            {
                TrainingId = id,
                Officers = officers,
                JuniorCommissionedOfficers = 0,
                OtherRanks = ors,
                Total = officers + ors,
                Source = TrainingCounterSource.Roster,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            }
        };
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed record SeedResult(int ProjectAId, Guid TrainingAId);

    private sealed class StubOptionsSnapshot<T> : IOptionsSnapshot<T> where T : class
    {
        private readonly T _value;
        public StubOptionsSnapshot(T value) => _value = value;
        public T Value => _value;
        public T Get(string? name) => _value;
    }
}
