using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services.Activities;
using Xunit;

namespace ProjectManagement.Tests.Activities;

public sealed class ActivityExportServiceTests
{
    [Fact]
    public async Task ExportAsync_WritesRedesignedHeadersAndRemarksPreview()
    {
        // SECTION: Arrange an activity export row with remarks and attachment counts.
        var createdAt = new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero);
        var item = CreateItem(
            remarksPreview: "Coordinated stakeholder brief on readiness.",
            createdAt: createdAt,
            pdfAttachmentCount: 1,
            photoAttachmentCount: 2,
            videoAttachmentCount: 3,
            attachmentCount: 6);
        var repository = new StubActivityRepository(new ActivityListResult(
            new[] { item },
            TotalCount: 1,
            Page: 1,
            PageSize: 0,
            Sort: ActivityListSort.ScheduledStart,
            SortDescending: true));
        var exportService = new ActivityExportService(repository, new StubActivityAttachmentManager());

        // SECTION: Act by exporting and loading the generated workbook.
        var result = await exportService.ExportAsync(new ActivityExportRequest(
            Sort: ActivityListSort.ScheduledStart,
            SortDescending: true,
            FromDate: null,
            ToDate: null,
            ActivityTypeId: null,
            CreatedByUserId: null,
            Search: null,
            MediaFilter: ActivityMediaFilter.Any));

        // SECTION: Assert headers, remarks, and shifted attachment indexes remain correct.
        Assert.NotNull(result);
        using var stream = new MemoryStream(result!.Content);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Activities");

        Assert.Equal("Activity title", worksheet.Cell(1, 1).GetString());
        Assert.Equal("Activity type", worksheet.Cell(1, 2).GetString());
        Assert.Equal("Remarks / Brief", worksheet.Cell(1, 3).GetString());
        Assert.Equal("Event date", worksheet.Cell(1, 4).GetString());
        Assert.Equal("Coordinated stakeholder brief on readiness.", worksheet.Cell(2, 3).GetString());
        Assert.Equal("PDF attachments", worksheet.Cell(1, 8).GetString());
        Assert.Equal(1, worksheet.Cell(2, 8).GetValue<int>());
        Assert.Equal("Photo attachments", worksheet.Cell(1, 9).GetString());
        Assert.Equal(2, worksheet.Cell(2, 9).GetValue<int>());
        Assert.Equal("Video attachments", worksheet.Cell(1, 10).GetString());
        Assert.Equal(3, worksheet.Cell(2, 10).GetValue<int>());
        Assert.Equal("Total attachments", worksheet.Cell(1, 11).GetString());
        Assert.Equal(6, worksheet.Cell(2, 11).GetValue<int>());
        Assert.Equal("Attachment links", worksheet.Cell(1, 12).GetString());
    }

    [Fact]
    public async Task ExportAsync_KeepsAttachmentLinksAlignedAfterRemarksColumn()
    {
        // SECTION: Arrange an activity with generated attachment link metadata.
        var item = CreateItem(remarksPreview: "Includes media evidence.");
        var repository = new StubActivityRepository(new ActivityListResult(
            new[] { item },
            TotalCount: 1,
            Page: 1,
            PageSize: 0,
            Sort: ActivityListSort.ScheduledStart,
            SortDescending: true));
        repository.ActivityById[item.Id] = new Activity { Id = item.Id, Title = item.Title, CreatedByUserId = item.CreatedByUserId };
        var attachmentManager = new StubActivityAttachmentManager(new[]
        {
            new ActivityAttachmentMetadata(
                Id: 10,
                FileName: "brief.pdf",
                ContentType: "application/pdf",
                FileSize: 1024,
                DownloadUrl: "/files/brief.pdf",
                InlineUrl: "/files/brief.pdf?inline=true",
                StorageKey: "activities/1/brief.pdf",
                UploadedAtUtc: DateTimeOffset.UtcNow,
                UploadedByUserId: "user-1")
        });
        var exportService = new ActivityExportService(repository, attachmentManager);

        // SECTION: Act by exporting and loading the generated workbook.
        var result = await exportService.ExportAsync(new ActivityExportRequest(
            Sort: ActivityListSort.ScheduledStart,
            SortDescending: true,
            FromDate: null,
            ToDate: null,
            ActivityTypeId: null,
            CreatedByUserId: null,
            Search: null,
            MediaFilter: ActivityMediaFilter.Any));

        // SECTION: Assert the attachment formula is written to the shifted link column.
        Assert.NotNull(result);
        using var stream = new MemoryStream(result!.Content);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Activities");

        Assert.Equal("Attachment links", worksheet.Cell(1, 12).GetString());
        Assert.Contains("HYPERLINK", worksheet.Cell(2, 12).FormulaA1, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("brief.pdf", worksheet.Cell(2, 12).FormulaA1, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(worksheet.Cell(2, 13).GetString()));
    }

    private static ActivityListItem CreateItem(
        string? remarksPreview,
        DateTimeOffset? createdAt = null,
        int pdfAttachmentCount = 0,
        int photoAttachmentCount = 0,
        int videoAttachmentCount = 0,
        int attachmentCount = 0) => new(
            Id: 1,
            Title: "Readiness Review",
            ActivityTypeName: "Briefing",
            ActivityTypeId: 2,
            Location: "HQ",
            RemarksPreview: remarksPreview,
            ScheduledStartUtc: new DateTimeOffset(2024, 5, 2, 3, 30, 0, TimeSpan.Zero),
            ScheduledEndUtc: null,
            CreatedAtUtc: createdAt ?? new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero),
            CreatedByUserId: "user-1",
            CreatedByDisplayName: "Avery Manager",
            CreatedByEmail: "avery@example.test",
            AttachmentCount: attachmentCount,
            PdfAttachmentCount: pdfAttachmentCount,
            PhotoAttachmentCount: photoAttachmentCount,
            VideoAttachmentCount: videoAttachmentCount,
            MediaPreviews: Array.Empty<ActivityMediaPreview>(),
            HasPendingDelete: false);

    private sealed class StubActivityRepository : IActivityRepository
    {
        private readonly ActivityListResult _result;

        public StubActivityRepository(ActivityListResult result)
        {
            _result = result;
        }

        public Dictionary<int, Activity> ActivityById { get; } = new();

        public Task<Activity?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActivityById.TryGetValue(id, out var activity) ? activity : new Activity { Id = id, Title = "Readiness Review", CreatedByUserId = "user-1" });

        public Task<IReadOnlyList<Activity>> ListByTypeAsync(int activityTypeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Activity>>(Array.Empty<Activity>());

        public Task<ActivityListResult> ListAsync(ActivityListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);

        public Task<bool> ExistsByTypeAndTitleAsync(int activityTypeId, string title, int? excludingActivityId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<ActivityReviewSummaryResult> GetReviewSummaryAsync(ActivityListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActivityReviewSummaryResult(0, 0, 0, 0, 0));

        public Task AddAsync(Activity activity, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Activity activity, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Activity activity, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ActivityAttachment?> GetAttachmentByIdAsync(int attachmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityAttachment?>(null);

        public Task AddAttachmentAsync(ActivityAttachment attachment, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAttachmentAsync(ActivityAttachment attachment, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubActivityAttachmentManager : IActivityAttachmentManager
    {
        private readonly IReadOnlyList<ActivityAttachmentMetadata> _metadata;

        public StubActivityAttachmentManager(IReadOnlyList<ActivityAttachmentMetadata>? metadata = null)
        {
            _metadata = metadata ?? Array.Empty<ActivityAttachmentMetadata>();
        }

        public Task<ActivityAttachment> AddAsync(Activity activity, ActivityAttachmentUpload upload, string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(ActivityAttachment attachment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAllAsync(Activity activity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IReadOnlyList<ActivityAttachmentMetadata> CreateMetadata(Activity activity) => _metadata;
    }
}
