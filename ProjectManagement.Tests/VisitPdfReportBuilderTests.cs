using System;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests;

public class VisitPdfReportBuilderTests
{
    [Fact]
    public void Build_WithCoverPhoto_DoesNotThrow()
    {
        var builder = new VisitPdfReportBuilder();
        var context = new VisitPdfReportContext(
            Sections: new[]
            {
                new VisitPdfReportSection(
                    Guid.NewGuid(),
                    DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    "Industry Visit",
                    "Prof. Ada",
                    12,
                    3,
                    "Highlights from the visit.",
                    new byte[]
                    {
                        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
                        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
                        0xDE, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
                        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
                        0x00, 0x04, 0xBF, 0x02, 0xFE, 0xA7, 0xC5, 0xBF,
                        0xBB, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
                        0x44, 0xAE, 0x42, 0x60, 0x82
                    })
            },
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            StartDate: null,
            EndDate: null);

        var pdf = builder.Build(context);

        Assert.NotNull(pdf);
        Assert.NotEmpty(pdf);
    }
}
