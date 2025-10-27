using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class SocialMediaExcelWorkbookBuilderTests
{
    [Fact]
    public void Build_WithNullsAndMultipleRows_ProducesWorkbookWithDefaults()
    {
        var rows = new List<SocialMediaEventExportRow>
        {
            new(
                new DateOnly(2024, 5, 3),
                "Community Outreach",
                "Robotics demo",
                "Instagram",
                4,
                true,
                "Visitors explored student-led prototypes."),
            new(
                new DateOnly(2024, 5, 7),
                "Media Briefing",
                "Quarterly update",
                null,
                0,
                false,
                null)
        };

        var generatedAt = new DateTimeOffset(2024, 5, 10, 5, 30, 0, TimeSpan.Zero);
        var context = new SocialMediaExcelWorkbookContext(rows, generatedAt, null, null, null);
        var builder = new SocialMediaExcelWorkbookBuilder();

        var bytes = builder.Build(context);
        Assert.NotEmpty(bytes);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);

        Assert.Equal("Social Media Events", worksheet.Name);
        Assert.Equal(3, worksheet.LastRowUsed()?.RowNumber());

        Assert.Equal(1, worksheet.Cell(2, 1).GetValue<int>());
        Assert.Equal(new DateTime(2024, 5, 3), worksheet.Cell(2, 2).GetDateTime().Date);
        Assert.Equal("Community Outreach", worksheet.Cell(2, 3).GetString());
        Assert.Equal("Robotics demo", worksheet.Cell(2, 4).GetString());
        Assert.Equal("Instagram", worksheet.Cell(2, 5).GetString());
        Assert.Equal(4, worksheet.Cell(2, 6).GetValue<int>());
        Assert.Equal("Yes", worksheet.Cell(2, 7).GetString());
        Assert.Equal("Visitors explored student-led prototypes.", worksheet.Cell(2, 8).GetString());

        Assert.Equal(2, worksheet.Cell(3, 1).GetValue<int>());
        Assert.Equal(new DateTime(2024, 5, 7), worksheet.Cell(3, 2).GetDateTime().Date);
        Assert.Equal("Media Briefing", worksheet.Cell(3, 3).GetString());
        Assert.Equal("Quarterly update", worksheet.Cell(3, 4).GetString());
        Assert.Equal(string.Empty, worksheet.Cell(3, 5).GetString());
        Assert.Equal(0, worksheet.Cell(3, 6).GetValue<int>());
        Assert.Equal("No", worksheet.Cell(3, 7).GetString());
        Assert.Equal(string.Empty, worksheet.Cell(3, 8).GetString());

        var metadataRow = rows.Count + 3;
        Assert.Equal("Export generated", worksheet.Cell(metadataRow, 1).GetString());
        Assert.Contains("IST", worksheet.Cell(metadataRow, 2).GetString());
        Assert.Equal("(not set)", worksheet.Cell(metadataRow + 1, 2).GetString());
        Assert.Equal("(not set)", worksheet.Cell(metadataRow + 2, 2).GetString());
        Assert.Equal("(all platforms)", worksheet.Cell(metadataRow + 3, 2).GetString());

        Assert.True(worksheet.Column(4).Width <= 50);
        Assert.True(worksheet.Column(8).Width <= 70);
    }
}
