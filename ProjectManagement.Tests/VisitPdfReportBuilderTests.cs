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
                    new byte[] { 0x01, 0x02, 0x03 })
            },
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            StartDate: null,
            EndDate: null);

        var pdf = builder.Build(context);

        Assert.NotNull(pdf);
        Assert.NotEmpty(pdf);
    }
}
