using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectTotExportServiceTests
{
    [Fact]
    public async Task ExportAsync_InvalidDateRange_ReturnsFailure()
    {
        await using var context = CreateContext();
        var trackerService = new ProjectTotTrackerReadService(context);
        var workbookBuilder = new RecordingProjectTotWorkbookBuilder();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotExportService(trackerService, workbookBuilder, clock);

        var request = new ProjectTotExportRequest(
            TotStatus: null,
            RequestState: null,
            OnlyPendingRequests: false,
            StartedFrom: new DateOnly(2024, 5, 10),
            StartedTo: new DateOnly(2024, 5, 1),
            CompletedFrom: null,
            CompletedTo: null,
            SearchTerm: null,
            RequestedByUserId: "user-1");

        var result = await service.ExportAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.File);
        Assert.NotEmpty(result.Errors);
        Assert.Null(workbookBuilder.Context);
    }

    [Fact]
    public async Task ExportAsync_WithFilters_ReturnsWorkbookWithFilteredRows()
    {
        await using var context = CreateContext();
        context.Projects.AddRange(
            new Project
            {
                Id = 10,
                Name = "Project Helios",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                Tot = new ProjectTot
                {
                    ProjectId = 10,
                    Status = ProjectTotStatus.Completed,
                    StartedOn = new DateOnly(2024, 1, 10),
                    CompletedOn = new DateOnly(2024, 2, 15),
                    LastApprovedByUserId = "hod-1",
                    LastApprovedOnUtc = new DateTime(2024, 2, 20, 6, 0, 0, DateTimeKind.Utc)
                },
                TotRequest = new ProjectTotRequest
                {
                    ProjectId = 10,
                    ProposedStatus = ProjectTotStatus.Completed,
                    DecisionState = ProjectTotRequestDecisionState.Pending,
                    SubmittedByUserId = "po-1",
                    SubmittedOnUtc = new DateTime(2024, 2, 22, 8, 0, 0, DateTimeKind.Utc)
                }
            },
            new Project
            {
                Id = 11,
                Name = "Project Iris",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                Tot = new ProjectTot
                {
                    ProjectId = 11,
                    Status = ProjectTotStatus.Completed,
                    StartedOn = new DateOnly(2023, 12, 1),
                    CompletedOn = new DateOnly(2024, 1, 5)
                },
                TotRequest = new ProjectTotRequest
                {
                    ProjectId = 11,
                    ProposedStatus = ProjectTotStatus.Completed,
                    DecisionState = ProjectTotRequestDecisionState.Approved,
                    SubmittedByUserId = "po-2",
                    SubmittedOnUtc = new DateTime(2024, 1, 6, 8, 0, 0, DateTimeKind.Utc),
                    DecidedByUserId = "hod-2",
                    DecidedOnUtc = new DateTime(2024, 1, 7, 9, 0, 0, DateTimeKind.Utc)
                }
            });

        await context.SaveChangesAsync();

        var trackerService = new ProjectTotTrackerReadService(context);
        var workbookBuilder = new RecordingProjectTotWorkbookBuilder();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotExportService(trackerService, workbookBuilder, clock);

        var request = new ProjectTotExportRequest(
            TotStatus: ProjectTotStatus.Completed,
            RequestState: ProjectTotRequestDecisionState.Approved,
            OnlyPendingRequests: true,
            StartedFrom: null,
            StartedTo: null,
            CompletedFrom: new DateOnly(2024, 2, 1),
            CompletedTo: new DateOnly(2024, 2, 28),
            SearchTerm: " Helios ",
            RequestedByUserId: "export-user");

        var result = await service.ExportAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.File);
        var file = result.File!;
        Assert.Equal("tot-export-20240501T080000Z.xlsx", file.FileName);
        Assert.Equal(ProjectTotExportFile.ExcelContentType, file.ContentType);
        Assert.Equal(workbookBuilder.Payload, file.Content);

        var contextSnapshot = workbookBuilder.Context;
        Assert.NotNull(contextSnapshot);
        Assert.Equal(new DateOnly(2024, 2, 1), contextSnapshot!.Filter.CompletedFrom);
        Assert.Equal(new DateOnly(2024, 2, 28), contextSnapshot.Filter.CompletedTo);
        Assert.True(contextSnapshot.Filter.OnlyPendingRequests);
        Assert.Null(contextSnapshot.Filter.RequestState);
        Assert.Equal("Helios", contextSnapshot.Filter.SearchTerm);
        var row = Assert.Single(contextSnapshot.Rows);
        Assert.Equal(10, row.ProjectId);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class RecordingProjectTotWorkbookBuilder : IProjectTotExcelWorkbookBuilder
    {
        private static readonly byte[] _payload = { 1, 2, 3, 4 };

        public ProjectTotExcelWorkbookContext? Context { get; private set; }

        public byte[] Payload => _payload;

        public byte[] Build(ProjectTotExcelWorkbookContext context)
        {
            Context = context;
            return _payload;
        }
    }
}

public sealed class ProjectTotExcelWorkbookBuilderTests
{
    [Fact]
    public void Build_WithRows_ProducesWorkbookWithMetadata()
    {
        var rows = new List<ProjectTotTrackerRow>
        {
            new(
                ProjectId: 21,
                ProjectName: "Project Orion",
                SponsoringUnit: "Aerospace",
                TotStatus: ProjectTotStatus.Completed,
                TotStartedOn: new DateOnly(2024, 1, 10),
                TotCompletedOn: new DateOnly(2024, 2, 20),
                TotMetDetails: "MET signed off",
                TotMetCompletedOn: new DateOnly(2024, 2, 18),
                TotFirstProductionModelManufactured: true,
                TotFirstProductionModelManufacturedOn: new DateOnly(2024, 2, 19),
                TotLastApprovedBy: "hod-1",
                TotLastApprovedOnUtc: new DateTime(2024, 2, 21, 9, 0, 0, DateTimeKind.Utc),
                RequestState: ProjectTotRequestDecisionState.Pending,
                RequestedStatus: ProjectTotStatus.Completed,
                RequestedStartedOn: new DateOnly(2024, 1, 5),
                RequestedCompletedOn: new DateOnly(2024, 2, 25),
                RequestedMetDetails: "Awaiting sign-off",
                RequestedMetCompletedOn: new DateOnly(2024, 2, 24),
                RequestedFirstProductionModelManufactured: false,
                RequestedFirstProductionModelManufacturedOn: null,
                RequestedBy: "po-1",
                RequestedOnUtc: new DateTime(2024, 2, 26, 6, 0, 0, DateTimeKind.Utc),
                DecidedBy: null,
                DecidedOnUtc: null,
                RequestRowVersion: Array.Empty<byte>(),
                LatestExternalRemark: new ProjectTotRemarkSummary(
                    21,
                    "External insight",
                    new DateOnly(2024, 2, 15),
                    new DateTime(2024, 2, 16, 5, 30, 0, DateTimeKind.Utc),
                    RemarkType.External),
                LatestInternalRemark: new ProjectTotRemarkSummary(
                    21,
                    "Internal memo",
                    null,
                    new DateTime(2024, 2, 17, 7, 0, 0, DateTimeKind.Utc),
                    RemarkType.Internal),
                LeadProjectOfficerUserId: "lead-po")
        };

        var filter = new ProjectTotTrackerFilter
        {
            TotStatus = ProjectTotStatus.Completed,
            RequestState = null,
            OnlyPendingRequests = true,
            SearchTerm = "Innovation",
            StartedFrom = new DateOnly(2024, 1, 1),
            StartedTo = new DateOnly(2024, 1, 31),
            CompletedFrom = new DateOnly(2024, 2, 1),
            CompletedTo = new DateOnly(2024, 2, 29)
        };

        var generatedAt = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var context = new ProjectTotExcelWorkbookContext(rows, generatedAt, filter);
        var builder = new ProjectTotExcelWorkbookBuilder();

        var bytes = builder.Build(context);
        Assert.NotEmpty(bytes);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);

        Assert.Equal("ToT Tracker", worksheet.Name);
        Assert.Equal("S.No.", worksheet.Cell(1, 1).GetString());
        Assert.Equal("Project", worksheet.Cell(1, 2).GetString());
        Assert.Equal("Project Orion", worksheet.Cell(2, 2).GetString());
        Assert.Equal("Aerospace", worksheet.Cell(2, 3).GetString());
        Assert.Equal("Completed", worksheet.Cell(2, 5).GetString());
        Assert.Equal("Yes", worksheet.Cell(2, 10).GetString());
        Assert.Equal("hod-1", worksheet.Cell(2, 12).GetString());
        Assert.Contains("External insight", worksheet.Cell(2, 26).GetString());
        Assert.Contains("Internal memo", worksheet.Cell(2, 27).GetString());

        var lastColumn = worksheet.LastColumnUsed();
        Assert.NotNull(lastColumn);
        Assert.Equal(27, lastColumn!.ColumnNumber());

        var metadataRow = rows.Count + 3;
        Assert.Equal("Export generated", worksheet.Cell(metadataRow, 1).GetString());
        Assert.Equal("2024-03-01 17:30 IST", worksheet.Cell(metadataRow, 2).GetString());
        Assert.Equal("Completed", worksheet.Cell(metadataRow + 1, 2).GetString());
        Assert.Equal("Only pending approvals", worksheet.Cell(metadataRow + 2, 2).GetString());
        Assert.Equal("2024-01-01", worksheet.Cell(metadataRow + 3, 2).GetString());
        Assert.Equal("2024-02-29", worksheet.Cell(metadataRow + 6, 2).GetString());
        Assert.Equal("Innovation", worksheet.Cell(metadataRow + 7, 2).GetString());
    }
}
