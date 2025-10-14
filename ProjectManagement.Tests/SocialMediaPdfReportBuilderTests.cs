using System;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Tests;

public sealed class SocialMediaPdfReportBuilderTests
{
    [Fact]
    public void Build_WithCoverPhoto_DoesNotThrow()
    {
        var builder = new SocialMediaPdfReportBuilder();
        var context = new SocialMediaPdfReportContext(
            Sections: new[]
            {
                new SocialMediaPdfReportSection(
                    Guid.NewGuid(),
                    new DateOnly(2024, 4, 15),
                    "Campaign Launch",
                    "Launch day coverage",
                    "Instagram",
                    1200,
                    4,
                    "Highlights from the launch.",
                    new byte[] { 0x01, 0x02, 0x03 })
            },
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            StartDate: null,
            EndDate: null,
            PlatformFilter: null);

        var pdf = builder.Build(context);

        Assert.NotNull(pdf);
        Assert.NotEmpty(pdf);
    }
}
