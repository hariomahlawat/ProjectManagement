using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Services;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class IprExportServiceTests
{
    [Fact]
    public async Task ExportAsync_BuildsWorkbookWithRows()
    {
        var rows = new List<IprExportRowDto>
        {
            new(
                "IPR-001",
                "Alpha",
                IprStatus.Filed,
                "Alice",
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 2, 10, 0, 0, 0, TimeSpan.Zero),
                "Project Beacon",
                "Initial filing"),
            new(
                "IPR-002",
                null,
                IprStatus.FilingUnderProcess,
                null,
                null,
                null,
                null,
                null)
        };

        var filter = new IprFilter();
        var readService = new StubIprReadService(rows);
        var workbookBuilder = new IprExcelWorkbookBuilder();
        var clock = new StubClock(new DateTimeOffset(2024, 5, 12, 10, 30, 0, TimeSpan.Zero));
        var service = new IprExportService(readService, workbookBuilder, clock);

        var file = await service.ExportAsync(filter, CancellationToken.None);

        Assert.Equal("patent-records-20240512-103000.xlsx", file.FileName);
        Assert.Equal(IprExportFile.ExcelContentType, file.ContentType);
        Assert.NotNull(file.Content);
        Assert.NotEmpty(file.Content);

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("IPR Records");

        Assert.Equal("Ser", worksheet.Cell(1, 1).GetString());
        Assert.Equal("Name of the product", worksheet.Cell(1, 2).GetString());
        Assert.Equal("IPR filing no", worksheet.Cell(1, 3).GetString());

        Assert.Equal("Alpha", worksheet.Cell(2, 2).GetString());
        Assert.Equal("IPR-001", worksheet.Cell(2, 3).GetString());
        Assert.Equal("Filed", worksheet.Cell(2, 4).GetString());
        Assert.Equal("Alice", worksheet.Cell(2, 5).GetString());
        Assert.Equal(new DateTime(2024, 1, 1), worksheet.Cell(2, 6).GetDateTime());
        Assert.Equal(new DateTime(2024, 2, 10), worksheet.Cell(2, 7).GetDateTime());
        Assert.Equal("Project Beacon", worksheet.Cell(2, 8).GetString());
        Assert.Equal("Initial filing", worksheet.Cell(2, 9).GetString());

        Assert.Equal("", worksheet.Cell(3, 2).GetString());
        Assert.Equal("IPR-002", worksheet.Cell(3, 3).GetString());
        Assert.Equal("Filing under process", worksheet.Cell(3, 4).GetString());
        Assert.Equal("", worksheet.Cell(3, 5).GetString());
        Assert.True(worksheet.Cell(3, 6).IsEmpty());
        Assert.True(worksheet.Cell(3, 7).IsEmpty());
        Assert.Equal("", worksheet.Cell(3, 8).GetString());
        Assert.Equal("", worksheet.Cell(3, 9).GetString());
    }

    [Fact]
    public async Task ExportAsync_ThrowsWhenFilterIsNull()
    {
        var readService = new StubIprReadService(Array.Empty<IprExportRowDto>());
        var workbookBuilder = new IprExcelWorkbookBuilder();
        var clock = new StubClock(DateTimeOffset.UtcNow);
        var service = new IprExportService(readService, workbookBuilder, clock);

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ExportAsync(null!, CancellationToken.None));
    }

    private sealed class StubIprReadService : IIprReadService
    {
        private readonly IReadOnlyList<IprExportRowDto> _rows;

        public StubIprReadService(IReadOnlyList<IprExportRowDto> rows)
        {
            _rows = rows;
        }

        public Task<PagedResult<IprListRowDto>> SearchAsync(IprFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResult<IprListRowDto>(Array.Empty<IprListRowDto>(), 0, 1, 25));

        public Task<IprRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<IprRecord?>(null);

        public Task<IprKpis> GetKpisAsync(IprFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(new IprKpis(0, 0, 0, 0, 0, 0));

        public Task<IReadOnlyList<IprExportRowDto>> GetExportAsync(IprFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(_rows);
    }

    private sealed class StubClock : IClock
    {
        private readonly DateTimeOffset _value;

        public StubClock(DateTimeOffset value)
        {
            _value = value;
        }

        public DateTimeOffset UtcNow => _value;
    }
}
