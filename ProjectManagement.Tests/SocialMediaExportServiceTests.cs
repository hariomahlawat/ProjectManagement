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

namespace ProjectManagement.Tests;

public sealed class SocialMediaExportServiceTests
{
    [Fact]
    public async Task ExportAsync_InvalidDateRange_ReturnsFailure()
    {
        using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var photoService = new StubSocialMediaEventPhotoService();
        var eventService = new SocialMediaEventService(context, clock, photoService);
        var exportService = new SocialMediaExportService(
            eventService,
            new SocialMediaExcelWorkbookBuilder(),
            new SocialMediaPdfReportBuilder(),
            photoService,
            clock,
            audit,
            NullLogger<SocialMediaExportService>.Instance);

        var request = new SocialMediaExportRequest(
            EventTypeId: null,
            StartDate: new DateOnly(2024, 5, 2),
            EndDate: new DateOnly(2024, 5, 1),
            SearchQuery: null,
            PlatformId: null,
            PlatformName: null,
            OnlyActiveEventTypes: true,
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

        var eventType = SocialMediaTestData.CreateEventType(name: "Milestone Update");
        var platform = SocialMediaTestData.CreatePlatform(name: "Instagram");
        var photoId = Guid.NewGuid();
        var socialEvent = SocialMediaTestData.CreateEvent(
            eventType.Id,
            platform.Id,
            dateOfEvent: new DateOnly(2024, 4, 18),
            title: "Robotics showcase",
            description: "Student innovations highlighted.",
            coverPhotoId: photoId,
            timestamp: clock.UtcNow,
            platform: platform);

        var photo = SocialMediaTestData.CreatePhoto(
            socialEvent.Id,
            id: photoId,
            caption: "Show floor",
            isCover: true,
            createdAtUtc: clock.UtcNow);

        context.SocialMediaEventTypes.Add(eventType);
        context.SocialMediaPlatforms.Add(platform);
        context.SocialMediaEvents.Add(socialEvent);
        context.SocialMediaEventPhotos.Add(photo);
        await context.SaveChangesAsync();

        var photoService = new StubSocialMediaEventPhotoService();
        var eventService = new SocialMediaEventService(context, clock, photoService);
        var exportService = new SocialMediaExportService(
            eventService,
            new SocialMediaExcelWorkbookBuilder(),
            new SocialMediaPdfReportBuilder(),
            photoService,
            clock,
            audit,
            NullLogger<SocialMediaExportService>.Instance);

        var request = new SocialMediaExportRequest(
            EventTypeId: eventType.Id,
            StartDate: new DateOnly(2024, 4, 1),
            EndDate: new DateOnly(2024, 4, 30),
            SearchQuery: " innovation  ",
            PlatformId: platform.Id,
            PlatformName: " instagram ",
            OnlyActiveEventTypes: true,
            RequestedByUserId: "user-99");

        var result = await exportService.ExportAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.File);
        var file = result.File!;
        Assert.Equal("social-media-events-20240401-to-20240430-20240501T083000Z.xlsx", file.FileName);
        Assert.Equal(SocialMediaExportFile.ExcelContentType, file.ContentType);
        Assert.NotEmpty(file.Content);

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);

        Assert.Equal("Social Media Events", worksheet.Name);
        var lastColumn = worksheet.LastColumnUsed();
        Assert.NotNull(lastColumn);
        Assert.Equal(8, lastColumn!.ColumnNumber());
        var lastRow = worksheet.LastRowUsed();
        Assert.NotNull(lastRow);
        Assert.Equal(2, lastRow!.RowNumber());

        Assert.Equal(1, worksheet.Cell(2, 1).GetValue<int>());
        Assert.Equal(new DateTime(2024, 4, 18), worksheet.Cell(2, 2).GetDateTime().Date);
        Assert.Equal("Milestone Update", worksheet.Cell(2, 3).GetString());
        Assert.Equal("Robotics showcase", worksheet.Cell(2, 4).GetString());
        Assert.Equal("Instagram", worksheet.Cell(2, 5).GetString());
        Assert.Equal(1, worksheet.Cell(2, 6).GetValue<int>());
        Assert.Equal("Yes", worksheet.Cell(2, 7).GetString());
        Assert.Equal("Student innovations highlighted.", worksheet.Cell(2, 8).GetString());

        Assert.Equal("Export generated", worksheet.Cell(5, 1).GetString());
        Assert.Equal("2024-04-01", worksheet.Cell(6, 2).GetString());
        Assert.Equal("2024-04-30", worksheet.Cell(7, 2).GetString());
        Assert.Equal("Instagram", worksheet.Cell(8, 2).GetString());

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ProjectOfficeReports.SocialMediaEventExported", entry.Action);
        Assert.Equal("user-99", entry.UserId);
        Assert.Equal("1", entry.Data["Count"]);
        Assert.Equal(eventType.Id.ToString(), entry.Data["SocialMediaEventTypeId"]);
        Assert.Equal("innovation", entry.Data["Query"]);
        Assert.Equal(platform.Id.ToString(), entry.Data["PlatformId"]);
        Assert.Equal("Instagram", entry.Data["Platform"]);
        Assert.Equal("true", entry.Data["OnlyActiveEventTypes"]);
    }

    [Fact]
    public async Task ExportAsync_TrimsPlatformNameBeforeBuildingWorkbook()
    {
        using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 7, 45, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();

        var eventType = SocialMediaTestData.CreateEventType(name: "Town Hall");
        var platform = SocialMediaTestData.CreatePlatform(name: "Facebook");
        var socialEvent = SocialMediaTestData.CreateEvent(
            eventType.Id,
            platform.Id,
            dateOfEvent: new DateOnly(2024, 5, 20),
            title: "Town hall recap",
            description: "Key takeaways shared with the community.",
            timestamp: clock.UtcNow,
            platform: platform);

        context.SocialMediaEventTypes.Add(eventType);
        context.SocialMediaPlatforms.Add(platform);
        context.SocialMediaEvents.Add(socialEvent);
        await context.SaveChangesAsync();

        var photoService = new StubSocialMediaEventPhotoService();
        var eventService = new SocialMediaEventService(context, clock, photoService);
        var workbookBuilder = new RecordingSocialMediaWorkbookBuilder();
        var exportService = new SocialMediaExportService(
            eventService,
            workbookBuilder,
            new SocialMediaPdfReportBuilder(),
            photoService,
            clock,
            audit,
            NullLogger<SocialMediaExportService>.Instance);

        var result = await exportService.ExportAsync(
            new SocialMediaExportRequest(
                EventTypeId: eventType.Id,
                StartDate: null,
                EndDate: null,
                SearchQuery: null,
                PlatformId: platform.Id,
                PlatformName: "   Facebook  ",
                OnlyActiveEventTypes: false,
                RequestedByUserId: "auditor"),
            CancellationToken.None);

        Assert.True(result.Success);
        var file = Assert.IsType<SocialMediaExportFile>(result.File);
        Assert.Equal(RecordingSocialMediaWorkbookBuilder.ExpectedBytes, file.Content);

        var captured = Assert.NotNull(workbookBuilder.CapturedContext);
        Assert.Equal("Facebook", captured.PlatformFilter);
        var row = Assert.Single(captured.Rows);
        Assert.Equal("Facebook", row.Platform);

        var auditEntry = Assert.Single(audit.Entries);
        Assert.Equal("Facebook", auditEntry.Data["Platform"]);
    }

    [Fact]
    public async Task ExportPdfAsync_WithValidInput_ProducesReportAndAudit()
    {
        using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 9, 45, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();

        var eventType = SocialMediaTestData.CreateEventType(name: "Community Engagement");
        var platform = SocialMediaTestData.CreatePlatform(name: "YouTube");
        var photoId = Guid.NewGuid();
        var socialEvent = SocialMediaTestData.CreateEvent(
            eventType.Id,
            platform.Id,
            dateOfEvent: new DateOnly(2024, 4, 12),
            title: "STEM workshop",
            description: "Workshop recap and highlights.",
            coverPhotoId: photoId,
            timestamp: clock.UtcNow,
            platform: platform);

        var photo = SocialMediaTestData.CreatePhoto(
            socialEvent.Id,
            id: photoId,
            caption: "Keynote", 
            isCover: true,
            createdAtUtc: clock.UtcNow);

        context.SocialMediaEventTypes.Add(eventType);
        context.SocialMediaPlatforms.Add(platform);
        context.SocialMediaEvents.Add(socialEvent);
        context.SocialMediaEventPhotos.Add(photo);
        await context.SaveChangesAsync();

        var coverBytes = Enumerable.Range(1, 10).Select(i => (byte)i).ToArray();
        var photoService = new StubSocialMediaEventPhotoService(
            new Dictionary<Guid, IReadOnlyList<SocialMediaEventPhoto>>(),
            new Dictionary<(Guid, Guid), byte[]> { { (socialEvent.Id, photoId), coverBytes } });

        var eventService = new SocialMediaEventService(context, clock, photoService);
        var pdfBuilder = new CapturingSocialMediaPdfBuilder();
        var exportService = new SocialMediaExportService(
            eventService,
            new SocialMediaExcelWorkbookBuilder(),
            pdfBuilder,
            photoService,
            clock,
            audit,
            NullLogger<SocialMediaExportService>.Instance);

        var request = new SocialMediaExportRequest(
            EventTypeId: null,
            StartDate: new DateOnly(2024, 4, 1),
            EndDate: new DateOnly(2024, 4, 30),
            SearchQuery: null,
            PlatformId: null,
            PlatformName: null,
            OnlyActiveEventTypes: false,
            RequestedByUserId: "user-88");

        var result = await exportService.ExportPdfAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.File);
        var file = result.File!;
        Assert.Equal("social-media-events-20240401-to-20240430-20240501T094500Z.pdf", file.FileName);
        Assert.Equal(SocialMediaExportFile.PdfContentType, file.ContentType);
        Assert.NotEmpty(file.Content);

        Assert.NotNull(pdfBuilder.CapturedContext);
        var contextSnapshot = pdfBuilder.CapturedContext!;
        Assert.Single(contextSnapshot.Sections);
        var section = contextSnapshot.Sections[0];
        Assert.Equal(socialEvent.Id, section.EventId);
        Assert.Equal("Community Engagement", section.EventTypeName);
        Assert.Equal("STEM workshop", section.Title);
        Assert.Equal("YouTube", section.Platform);
        Assert.Equal("Workshop recap and highlights.", section.Description);
        Assert.Equal(coverBytes, section.CoverPhoto);
        Assert.Single(photoService.OpenRequests);
        Assert.Equal((socialEvent.Id, photoId, "feed"), photoService.OpenRequests[0]);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ProjectOfficeReports.SocialMediaEventExported", entry.Action);
        Assert.Equal("user-88", entry.UserId);
        Assert.Equal("1", entry.Data["Count"]);
        Assert.Equal("false", entry.Data["OnlyActiveEventTypes"]);
        Assert.Null(entry.Data["PlatformId"]);
        Assert.Null(entry.Data["Platform"]);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
