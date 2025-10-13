using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class VisitExportServiceTests
{
    [Fact]
    public async Task ExportAsync_InvalidDateRange_ReturnsFailure()
    {
        using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var visitService = new VisitService(context, clock, audit, new StubVisitPhotoService());
        var exportService = new VisitExportService(visitService, new VisitExcelWorkbookBuilder(), clock, audit, NullLogger<VisitExportService>.Instance);

        var request = new VisitExportRequest(
            VisitTypeId: null,
            StartDate: new DateOnly(2024, 5, 2),
            EndDate: new DateOnly(2024, 5, 1),
            RemarksQuery: null,
            RequestedByUserId: "user-1");

        var result = await exportService.ExportAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.File);
        Assert.Contains(result.Errors, error => error.Contains("start date", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task ExportAsync_WithValidInput_ReturnsWorkbookAndAudit()
    {
        using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 8, 30, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var visitType = new VisitType
        {
            Id = Guid.NewGuid(),
            Name = "Industry Visit",
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = "seed"
        };

        var visitId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var visit = new Visit
        {
            Id = visitId,
            VisitTypeId = visitType.Id,
            DateOfVisit = new DateOnly(2024, 4, 15),
            VisitorName = "Brig. Rao",
            Strength = 12,
            Remarks = "Focus on labs",
            CoverPhotoId = photoId,
            CreatedByUserId = "seed",
            CreatedAtUtc = clock.UtcNow,
            LastModifiedByUserId = "editor",
            LastModifiedAtUtc = clock.UtcNow.AddDays(1)
        };

        context.VisitTypes.Add(visitType);
        context.Visits.Add(visit);
        context.VisitPhotos.Add(new VisitPhoto
        {
            Id = photoId,
            VisitId = visitId,
            StorageKey = "visits/photos/original.jpg",
            ContentType = "image/jpeg",
            Width = 640,
            Height = 480,
            VersionStamp = "v1",
            CreatedAtUtc = clock.UtcNow
        });

        await context.SaveChangesAsync();

        var visitService = new VisitService(context, clock, audit, new StubVisitPhotoService());
        var exportService = new VisitExportService(visitService, new VisitExcelWorkbookBuilder(), clock, audit, NullLogger<VisitExportService>.Instance);

        var request = new VisitExportRequest(
            VisitTypeId: null,
            StartDate: new DateOnly(2024, 4, 1),
            EndDate: new DateOnly(2024, 4, 30),
            RemarksQuery: null,
            RequestedByUserId: "user-99");

        var result = await exportService.ExportAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        var file = Assert.NotNull(result.File);
        Assert.Equal("visits-20240401-to-20240430-20240501T083000Z.xlsx", file.FileName);
        Assert.Equal(VisitExportFile.ExcelContentType, file.ContentType);
        Assert.NotEmpty(file.Content);

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);

        Assert.Equal("Visits", worksheet.Name);
        Assert.Equal("S.No.", worksheet.Cell(1, 1).GetString());
        Assert.Equal("Visit type", worksheet.Cell(1, 2).GetString());
        Assert.Equal("Industry Visit", worksheet.Cell(2, 2).GetString());
        Assert.Equal("Brig. Rao", worksheet.Cell(2, 4).GetString());
        Assert.Equal("12", worksheet.Cell(2, 5).GetString());
        Assert.Equal("Yes", worksheet.Cell(2, 7).GetString());
        Assert.Equal("Focus on labs", worksheet.Cell(2, 8).GetString());
        Assert.Equal(8, worksheet.LastColumnUsed().ColumnNumber());
        Assert.Equal("Export generated", worksheet.Cell(4, 1).GetString());
        Assert.Equal("2024-04-01", worksheet.Cell(5, 2).GetString());
        Assert.Equal("2024-04-30", worksheet.Cell(6, 2).GetString());

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ProjectOfficeReports.VisitExported", entry.Action);
        Assert.Equal("user-99", entry.UserId);
        Assert.Equal("1", entry.Data["Count"]);
        Assert.Equal("2024-04-01", entry.Data["StartDate"]);
        Assert.Equal("2024-04-30", entry.Data["EndDate"]);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class StubVisitPhotoService : IVisitPhotoService
    {
        public Task<IReadOnlyList<VisitPhoto>> GetPhotosAsync(Guid visitId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<VisitPhoto>>(Array.Empty<VisitPhoto>());

        public Task<VisitPhotoUploadResult> UploadAsync(Guid visitId, Stream content, string originalFileName, string? contentType, string? caption, string userId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<VisitPhotoDeletionResult> RemoveAsync(Guid visitId, Guid photoId, string userId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<VisitPhotoSetCoverResult> SetCoverAsync(Guid visitId, Guid photoId, string userId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task RemoveAllAsync(Guid visitId, IReadOnlyCollection<VisitPhoto> photos, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<VisitPhotoAsset?> OpenAsync(Guid visitId, Guid photoId, string size, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
