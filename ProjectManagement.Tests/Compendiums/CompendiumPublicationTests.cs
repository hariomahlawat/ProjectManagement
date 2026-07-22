using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Services.Compendiums;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests.Compendiums;

public sealed class CompendiumPublicationTests
{
    [Fact]
    public void Preflight_ComputesWarningsAndReadiness()
    {
        var preflight = new CompendiumPreflightDto(
            CompletedProjectCount: 3,
            EligibleProjectCount: 2,
            CategoryCount: 1,
            ExcludedNotAvailableCount: 1,
            MissingAvailabilityStatusCount: 0,
            PhotoSelectedCount: 1,
            MissingPhotoCount: 1,
            MissingArmServiceCount: 0,
            MissingCostCount: 1,
            ZeroCostCount: 0,
            MissingDescriptionCount: 0,
            MissingCompletionYearCount: 0,
            PossibleTitleTypoCount: 0,
            Projects: new[]
            {
                new CompendiumProjectReadinessDto(
                    1,
                    "Simulator A",
                    "AR / VR",
                    "2026",
                    new[]
                    {
                        CompendiumPublicationIssue.MissingPhoto,
                        CompendiumPublicationIssue.MissingProliferationCost
                    }),
                new CompendiumProjectReadinessDto(
                    2,
                    "Simulator B",
                    "AR / VR",
                    "2025",
                    Array.Empty<CompendiumPublicationIssue>())
            });

        Assert.True(preflight.CanGenerate);
        Assert.False(preflight.IsPublicationReady);
        Assert.Equal(1, preflight.ProjectsWithWarnings);
        Assert.Equal(2, preflight.TotalWarningCount);
    }

    [Fact]
    public void PdfBuilder_GeneratesPdfWhenPhotoIsMissing()
    {
        using var root = new TemporaryDirectory();
        var environment = new TestWebHostEnvironment(root.Path);
        var builder = new CompendiumPdfReportBuilder(
            environment,
            NullLogger<CompendiumPdfReportBuilder>.Instance);

        var context = new CompendiumPdfReportContext(
            Title: "SDD Simulators Compendium",
            Subtitle: "Available for Proliferation",
            UnitDisplayName: "Simulator Development Division",
            IssuerDisplayName: "Simulator Development Division",
            HandlingMarking: "RESTRICTED",
            GeneratedAtUtc: new DateTimeOffset(2026, 7, 21, 6, 0, 0, TimeSpan.Zero),
            Categories: new[]
            {
                new CompendiumPdfCategorySection(
                    "AR / VR",
                    new[]
                    {
                        new CompendiumPdfProjectSection(
                            ProjectId: 1,
                            ProjectName: "Virtual Reality Training Simulator",
                            CaseFileNumber: "30102/VR/SDD/26",
                            CategoryName: "AR / VR",
                            CompletionYearDisplay: "2026",
                            ArmServiceDisplay: "All Arms / Services",
                            ProliferationCostDisplay: "3.5",
                            ProliferationCostRemarks: "Software is supplied without cost; amount represents COTS hardware.",
                            DescriptionMarkdown: "A simulator for **structured training**.\n\n1. Offline operation.\n2. After Action Review.",
                            CoverPhoto: null,
                            PhotoWasSelected: false)
                    })
            },
            ShowMissingPhotoPlaceholder: true);

        var bytes = builder.Build(context);

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes.Take(4).ToArray()));
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string root)
        {
            ApplicationName = "ProjectManagement.Tests";
            EnvironmentName = "Testing";
            ContentRootPath = root;
            WebRootPath = root;
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "prism-compendium-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
