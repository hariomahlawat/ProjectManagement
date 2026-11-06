using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Analytics.Presentation;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;

namespace ProjectManagement.Tests;

/// <summary>Analytics › ARPP progress slides: what each slide carries, and a long column continuing on a further slide.</summary>
public sealed class ArppProgressSlideComposerTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    [Fact]
    public void Compose_TwoSlides_CardsChartAndEveryProjectInItsColumn()
    {
        var (content, slideCount) = CreateComposer().Compose(Sample(), Today);

        Assert.Equal(2, slideCount);
        var slides = SlideTexts(content);
        Assert.Equal(2, slides.Count);

        // Slide 1: the title, the five cards (the supply-order card reads as the column it feeds) and the chart.
        Assert.Contains("ARPP FY 2026-27: progress as of 6 Nov 2026", slides[0]);
        Assert.Contains("Under development", slides[0]);
        Assert.Contains("AoN to SO", slides[0]);
        Assert.Contains("Pre-AoN", slides[0]);
        Assert.Contains("TEC", slides[0]);
        Assert.Contains("DCD Projects 2", slides[0]); // legend with counts, only categories present
        Assert.DoesNotContain("Other R&D Projects", slides[0]);

        // Slide 2: the columns, full names, the PDC (red when overdue) without a stage tag, completion month and year.
        Assert.Contains("ARPP FY 2026-27: where each project stands", slides[1]);
        Assert.Contains("Under development", slides[1]);
        Assert.Contains("VR based Physiological Response Evaluation System (Stress Detection Simulator)", slides[1]);
        Assert.Contains("PDC 22 Jun 2026 · overdue", slides[1]);
        Assert.Contains("PDC 20 Feb 2027", slides[1]);
        Assert.Contains("Jan 2026", slides[1]);
        Assert.Contains("Cancelled: VR based SAKO Sniper Sml", slides[1]);
        Assert.Contains("DCD Projects", slides[1]); // the legend in the header
    }

    [Fact]
    public void Compose_AColumnThatDoesNotFit_ContinuesOnAThirdSlide()
    {
        var many = Enumerable.Range(1, 20)
            .Select(index => Line(index, $"Pre-AoN project number {index} with a fairly long name to wrap", "CoE", ArppProgressBucket.PreAon, StageCodes.AON))
            .ToList();
        var arpp = new ArppAnalyticsVm
        {
            FinancialYearStart = 2026,
            FinancialYearDisplay = "2026-27",
            ListedCount = many.Count,
            ProjectLines = many,
            ByStageByCategory = new[] { new OngoingStageByParentCategoryPoint(StageCodes.AON, "Acceptance of Necessity", "CoE", many.Count) },
            StageCategoryOrder = new[] { "CoE" }
        };

        var (content, slideCount) = CreateComposer().Compose(arpp, Today);

        Assert.Equal(3, slideCount);
        var slides = SlideTexts(content);
        Assert.Contains("ARPP FY 2026-27: where each project stands (continued)", slides[2]);
        Assert.Contains("Pre-AoN (cont.)", slides[2]);
        Assert.Contains("Pre-AoN project number 20 with a fairly long name to wrap", string.Join(" ", slides.Skip(1)));
    }

    [Theory]
    [InlineData("NIRBHAY", 2.3, 10, 1)]
    [InlineData("VR based Physiological Response Evaluation System (Stress Detection Simulator)", 2.3, 10, 3)]
    public void WrapCount_EstimatesTheLinesANameTakes(string name, double width, double points, int expected)
    {
        Assert.Equal(expected, ArppProgressSlideComposer.WrapCount(name, width, points));
    }

    // SECTION: Fixtures
    internal static ArppProgressSlideComposer CreateComposer() =>
        new(new TestEnvironment(Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot")));

    internal static ArppAnalyticsVm Sample()
    {
        var lines = new List<ArppProjectLineVm>
        {
            Line(1, "VR based Physiological Response Evaluation System (Stress Detection Simulator)", "CoE", ArppProgressBucket.PreAon, StageCodes.AON),
            Line(2, "NIRBHAY", "Other R&D Projects-not-listed", ArppProgressBucket.NotKnown, null),
            Line(3, "VR Next Gen Sml for Tk T72 & T90", "CoE", ArppProgressBucket.AonToSupplyOrder, StageCodes.BID),
            Line(4, "05 x MUUGV (Ethiopia 2025)", "DCD Projects", ArppProgressBucket.AonToSupplyOrder, StageCodes.TEC),
            Line(5, "Wpn Trg Sml (Tavor Rifle)", "CoE", ArppProgressBucket.UnderDevelopment, StageCodes.DEVP, pdc: new DateOnly(2026, 6, 22), overdue: true),
            Line(6, "VR based T-55 Armd Tk Sml (Sri Lanka 2025)", "DCD Projects", ArppProgressBucket.UnderDevelopment, StageCodes.DEVP, pdc: new DateOnly(2027, 2, 20)),
            Line(7, "VR Based Map Reading (VR MaRS) Sml", "CoE", ArppProgressBucket.UnderDevelopment, StageCodes.PAYMENT),
            Line(8, "81 mm Mor (Sri Lanka 2025)", "DCD Projects", ArppProgressBucket.Completed, null, completed: "Jan 2026", completedSort: new DateOnly(2026, 1, 1)),
            Line(9, "VR based SAKO Sniper Sml", "CoE", ArppProgressBucket.Cancelled, null)
        };
        return new ArppAnalyticsVm
        {
            FinancialYearStart = 2026,
            FinancialYearDisplay = "2026-27",
            AvailableFinancialYears = new[] { 2026 },
            IsCurrentFinancialYear = true,
            ListedCount = 9,
            ListedIpaValue = 300_000_000m,
            NewCount = 5,
            CommittedLiabilityCount = 3,
            CarryForwardCount = 1,
            CompletedCount = 1,
            CancelledCount = 1,
            OngoingCount = 6,
            SupplyOrderCount = 3,
            AonToSupplyOrderCount = 2,
            PreAonCount = 1,
            CardNotKnownCount = 1,
            ProjectLines = lines,
            ByStageByCategory = new[]
            {
                new OngoingStageByParentCategoryPoint(StageCodes.AON, "Acceptance of Necessity", "CoE", 1),
                new OngoingStageByParentCategoryPoint(StageCodes.BID, "Bidding/ Tendering", "CoE", 1),
                new OngoingStageByParentCategoryPoint(StageCodes.TEC, "Technical Evaluation", "DCD Projects", 1),
                new OngoingStageByParentCategoryPoint(StageCodes.DEVP, "Development", "CoE", 1),
                new OngoingStageByParentCategoryPoint(StageCodes.DEVP, "Development", "DCD Projects", 1),
                new OngoingStageByParentCategoryPoint(StageCodes.PAYMENT, "Payment", "CoE", 1)
            },
            StageCategoryOrder = new[] { "CoE", "DCD Projects" }
        };
    }

    internal static ArppProjectLineVm Line(int id, string name, string category, ArppProgressBucket bucket, string? stage, DateOnly? pdc = null, bool overdue = false, string? completed = null, DateOnly? completedSort = null) =>
        new(id, name, category, bucket, stage, stage, stage is null ? -1 : Array.IndexOf(StageCodes.All, stage), pdc, overdue, completed, completedSort);

    internal static List<string> SlideTexts(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var document = PresentationDocument.Open(stream, false);
        var part = document.PresentationPart!;
        return part.Presentation.SlideIdList!.Elements<DocumentFormat.OpenXml.Presentation.SlideId>()
            .Select(id => (SlidePart)part.GetPartById(id.RelationshipId!.Value!))
            .Select(slide => string.Join("\n", slide.Slide.Descendants<A.Text>().Select(text => text.Text)))
            .ToList();
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string root)
        {
            ContentRootPath = root;
            WebRootPath = Path.Combine(root, "wwwroot");
        }

        public string ApplicationName { get; set; } = "ProjectManagement.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    // END SECTION
}
