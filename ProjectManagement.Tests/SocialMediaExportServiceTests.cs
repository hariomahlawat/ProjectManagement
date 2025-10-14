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
            Platform: null,
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
        var photoId = Guid.NewGuid();
        var socialEvent = SocialMediaTestData.CreateEvent(
            eventType.Id,
            dateOfEvent: new DateOnly(2024, 4, 18),
            title: "Robotics showcase",
            platform: "Instagram",
            reach: 820,
            description: "Student innovations highlighted.",
            coverPhotoId: photoId,
            timestamp: clock.UtcNow);

        var photo = SocialMediaTestData.CreatePhoto(
            socialEvent.Id,
            id: photoId,
            caption: "Show floor",
            isCover: true,
            createdAtUtc: clock.UtcNow);

        context.SocialMediaEventTypes.Add(eventType);
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
            Platform: " instagram ",
            OnlyActiveEventTypes: true,
            RequestedByUserId: "user-99");

        var result = await exportService.ExportAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        var file = Assert.NotNull(result.File);
        Assert.Equal("social-media-events-20240401-to-20240430-20240501T083000Z.xlsx", file.FileName);
        Assert.Equal(SocialMediaExportFile.ExcelContentType, file.ContentType);
        Assert.NotEmpty(file.Content);

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);

        Assert.Equal("Social Media Events", worksheet.Name);
        Assert.Equal(9, worksheet.LastColumnUsed().ColumnNumber());
        Assert.Equal(2, worksheet.LastRowUsed().RowNumber());

        Assert.Equal(1, worksheet.Cell(2, 1).GetValue<int>());
        Assert.Equal(new DateTime(2024, 4, 18), worksheet.Cell(2, 2).GetDateTime().Date);
        Assert.Equal("Milestone Update", worksheet.Cell(2, 3).GetString());
        Assert.Equal("Robotics showcase", worksheet.Cell(2, 4).GetString());
        Assert.Equal("Instagram", worksheet.Cell(2, 5).GetString());
        Assert.Equal(820, worksheet.Cell(2, 6).GetValue<int>());
        Assert.Equal(1, worksheet.Cell(2, 7).GetValue<int>());
        Assert.Equal("Yes", worksheet.Cell(2, 8).GetString());
        Assert.Equal("Student innovations highlighted.", worksheet.Cell(2, 9).GetString());

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
        Assert.Equal("Instagram", entry.Data["Platform"]);
        Assert.Equal("true", entry.Data["OnlyActiveEventTypes"]);
    }

    [Fact]
    public async Task ExportPdfAsync_WithValidInput_ProducesReportAndAudit()
    {
        using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 9, 45, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();

        var eventType = SocialMediaTestData.CreateEventType(name: "Community Engagement");
        var photoId = Guid.NewGuid();
        var socialEvent = SocialMediaTestData.CreateEvent(
            eventType.Id,
            dateOfEvent: new DateOnly(2024, 4, 12),
            title: "STEM workshop",
            platform: "YouTube",
            reach: 1250,
            description: "Workshop recap and highlights.",
            coverPhotoId: photoId,
            timestamp: clock.UtcNow);

        var photo = SocialMediaTestData.CreatePhoto(
            socialEvent.Id,
            id: photoId,
            caption: "Keynote", 
            isCover: true,
            createdAtUtc: clock.UtcNow);

        context.SocialMediaEventTypes.Add(eventType);
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
            Platform: null,
            OnlyActiveEventTypes: false,
            RequestedByUserId: "user-88");

        var result = await exportService.ExportPdfAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        var file = Assert.NotNull(result.File);
        Assert.Equal("social-media-events-20240401-to-20240430-20240501T094500Z.pdf", file.FileName);
        Assert.Equal(SocialMediaExportFile.PdfContentType, file.ContentType);
        Assert.NotEmpty(file.Content);

        var contextSnapshot = Assert.NotNull(pdfBuilder.CapturedContext);
        Assert.Single(contextSnapshot.Sections);
        var section = contextSnapshot.Sections[0];
        Assert.Equal(socialEvent.Id, section.EventId);
        Assert.Equal("Community Engagement", section.EventTypeName);
        Assert.Equal("STEM workshop", section.Title);
        Assert.Equal("YouTube", section.Platform);
        Assert.Equal(1250, section.Reach);
        Assert.Equal("Workshop recap and highlights.", section.Description);
        Assert.Equal(coverBytes, section.CoverPhoto);
        Assert.Equal(1, photoService.OpenRequests.Count);
        Assert.Equal((socialEvent.Id, photoId, "feed"), photoService.OpenRequests[0]);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ProjectOfficeReports.SocialMediaEventExported", entry.Action);
        Assert.Equal("user-88", entry.UserId);
        Assert.Equal("1", entry.Data["Count"]);
        Assert.Equal("false", entry.Data["OnlyActiveEventTypes"]);
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
