using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ProjectManagement.Services.Publications;

namespace ProjectManagement.Utilities.Reporting;

public interface IBrochurePdfReportBuilder
{
    byte[] Build(BrochurePublicationData data);
}

/// <summary>
/// Publication-grade brochure composer. Digital/Comfortable retains the A4 editorial
/// renderer; Print/Compact delegates to the reference-format hard-copy compositor using
/// the effective dimensions and dense content-bearing structure of the approved brochure.
/// </summary>
public sealed class BrochurePdfReportBuilder : IBrochurePdfReportBuilder
{
    private const string Forest950 = "#0B2F2A";
    private const string Forest900 = "#103D35";
    private const string Forest800 = "#155447";
    private const string Forest700 = "#1B6B59";
    private const string Forest100 = "#E9F2EF";
    private const string Forest50 = "#F6FAF8";
    private const string Gold = "#CBA64A";
    private const string Ink = "#15231F";
    private const string Muted = "#5E6E68";
    private const string Border = "#CCD9D4";
    private const string WarmWhite = "#FBFAF6";

    private readonly IWebHostEnvironment _environment;
    private readonly IPublicationFontService _fontService;
    private readonly IBrochurePrintPagePlanner _printPagePlanner;

    static BrochurePdfReportBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public BrochurePdfReportBuilder(
        IWebHostEnvironment environment,
        IPublicationFontService fontService,
        IBrochurePrintPagePlanner printPagePlanner)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
        _printPagePlanner = printPagePlanner ?? throw new ArgumentNullException(nameof(printPagePlanner));
    }

    public byte[] Build(BrochurePublicationData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Projects.Count == 0)
        {
            throw new InvalidOperationException("A brochure requires at least one project.");
        }

        var fontStatus = _fontService.EnsureRegistered();
        var sddLogo = TryLoadAsset("img/logos/sdd.png");
        var artracLogo = TryLoadAsset("img/logos/artrac.png");
        var institutionalArtwork = TryLoadInstitutionalArtwork(data.Options.InstitutionalCoverArtwork);
        BrochurePrintCompactPlan? printPlan = null;
        BrochureDigitalPlan? digitalPlan = null;

        if (data.Options.PublicationProfile == BrochurePublicationProfile.PrintCompact)
        {
            // Resolve the compact plan exactly once. Preflight and the compositor use the same
            // planner/metrics contract; post-composition verification below then proves that
            // QuestPDF honoured the planned physical page membership.
            printPlan = _printPagePlanner.Plan(
                data.Projects,
                BrochurePrintPublicationPolicy.FromOptions(data.Options),
                data.Options.CoverStyle,
                data.Options.Strapline,
                !string.IsNullOrWhiteSpace(data.Options.HandlingMarking));
        }
        else
        {
            digitalPlan = BrochureDigitalPublicationPolicy.Plan(
                data.Projects,
                BrochurePrintPublicationPolicy.FromOptions(data.Options),
                data.Options.IntroductionText,
                data.Options.IncludeBackCover);
        }

        var document = Document.Create(container =>
        {
            if (printPlan is not null)
            {
                BrochurePrintCompactComposer.Compose(
                    container,
                    data,
                    fontStatus,
                    sddLogo,
                    artracLogo,
                    institutionalArtwork,
                    printPlan);
                return;
            }

            if (digitalPlan is null)
            {
                throw new InvalidOperationException("Digital brochure planning did not produce a composition plan.");
            }

            if (data.Options.CoverStyle == BrochureCoverStyle.Institutional)
            {
                ComposeInstitutionalCover(
                    container,
                    data,
                    fontStatus,
                    sddLogo,
                    artracLogo,
                    institutionalArtwork);
            }
            else
            {
                ComposeContemporaryCover(container, data, fontStatus, sddLogo, artracLogo);
            }

            var totalPages = digitalPlan.EstimatedTotalPageCount;
            var institutionalMatter = BrochurePrintPublicationPolicy.FromOptions(data.Options);

            if (digitalPlan.InstitutionalOpeningPageNumber.HasValue)
            {
                ComposeDigitalInstitutionalOpening(
                    container,
                    data,
                    fontStatus,
                    sddLogo,
                    institutionalArtwork,
                    institutionalMatter,
                    digitalPlan.InstitutionalOpeningPageNumber.Value,
                    totalPages);
            }

            for (var index = 0; index < digitalPlan.AdditionalIntroductionPages.Count; index++)
            {
                ComposeAdditionalIntroductionPage(
                    container,
                    data,
                    fontStatus,
                    sddLogo,
                    digitalPlan.AdditionalIntroductionPages[index],
                    index,
                    digitalPlan.AdditionalIntroductionPages.Count,
                    digitalPlan.AdditionalIntroductionPageNumbers[index],
                    totalPages);
            }

            for (var index = 0; index < digitalPlan.ProjectPages.Count; index++)
            {
                ComposeProjectPage(
                    container,
                    data,
                    fontStatus,
                    digitalPlan.ProjectPages[index],
                    digitalPlan.ProjectPageNumbers[index],
                    totalPages,
                    sddLogo);
            }

            if (digitalPlan.InstitutionalClosingPageNumber.HasValue)
            {
                ComposeDigitalInstitutionalClosing(
                    container,
                    data,
                    fontStatus,
                    sddLogo,
                    institutionalMatter,
                    digitalPlan.InstitutionalClosingPageNumber.Value,
                    totalPages);
            }

            if (digitalPlan.IncludesBackCover)
            {
                ComposeBackCover(container, data, fontStatus, sddLogo, artracLogo);
            }
        })
        .WithMetadata(new DocumentMetadata
        {
            Title = data.Options.Title,
            Author = data.Options.IssuerDisplayName,
            Subject = "Capability Publication",
            Keywords = "SDD, Capability, Simulators, Project Publication",
            Creator = "PRISM ERP",
            Producer = "PRISM Publications",
            CreationDate = data.Options.GeneratedAtUtc,
            ModifiedDate = data.Options.GeneratedAtUtc
        });

        var pdfBytes = document.GeneratePdf();
        if (printPlan is not null)
        {
            BrochurePdfCompositionVerifier.Verify(pdfBytes, data, printPlan);
        }
        else if (digitalPlan is not null)
        {
            BrochurePdfCompositionVerifier.VerifyDigital(pdfBytes, data, digitalPlan);
        }

        return pdfBytes;
    }

    private static void ComposeInstitutionalCover(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        byte[]? artracLogo,
        byte[]? institutionalArtwork)
    {
        var artworkContainsIdentity = institutionalArtwork is { Length: > 0 }
                                      && BrochureInstitutionalCoverArtworkCatalog.IdentityMode(
                                          data.Options.InstitutionalCoverArtwork)
                                      == BrochureInstitutionalArtworkIdentityMode.FullArtwork;
        var coverPhotos = data.Projects
            .Where(project => project.PrimaryPhoto is not null)
            .Take(3)
            .Select(project => project.PrimaryPhoto!.Content)
            .ToArray();

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => style.FontFamily(fonts.PrimaryFamily).FontColor("#FFFFFF"));

            page.Content().Layers(layers =>
            {
                layers.Layer().Background(Forest950);
                layers.Layer().AlignRight().Width(185).Height(842).Background(Forest900);
                layers.Layer().AlignRight().AlignTop().Width(330).Height(8).Background(Gold);

                layers.PrimaryLayer().Padding(36).Column(column =>
                {
                    column.Spacing(14);
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(lockup =>
                        {
                            if (data.Options.ShowFrontCoverKicker && !string.IsNullOrWhiteSpace(data.Options.FrontCoverKicker))
                            {
                                lockup.Item().Text(data.Options.FrontCoverKicker!)
                                    .FontSize(11)
                                    .SemiBold()
                                    .LetterSpacing(.7f)
                                    .FontColor("#DCE9E5");
                            }
                            if (data.Options.ShowFrontCoverDescriptor && !string.IsNullOrWhiteSpace(data.Options.FrontCoverDescriptor))
                            {
                                lockup.Item().PaddingTop(3).Text(data.Options.FrontCoverDescriptor!)
                                    .FontSize(7.5f)
                                    .LetterSpacing(1.1f)
                                    .FontColor("#90B6AA");
                            }
                        });

                        // Curated Cover A artwork already contains the official identity marks.
                        if (!artworkContainsIdentity)
                        {
                            row.AutoItem().Row(logos =>
                            {
                                logos.Spacing(9);
                                if (artracLogo is { Length: > 0 })
                                {
                                    logos.ConstantItem(42).Height(42).Image(artracLogo).FitArea();
                                }
                                if (sddLogo is { Length: > 0 })
                                {
                                    logos.ConstantItem(42).Height(42).Image(sddLogo).FitArea();
                                }
                            });
                        }
                    });

                    if (!string.IsNullOrWhiteSpace(data.Options.HandlingMarking))
                    {
                        column.Item().Text(data.Options.HandlingMarking!.ToUpperInvariant())
                            .FontSize(8)
                            .Bold()
                            .LetterSpacing(1.2f)
                            .FontColor("#F5D978");
                    }

                    if (data.Options.ShowFrontCoverTitle)
                    {
                        var coverTitle = column.Item().PaddingTop(30).Text(data.Options.Title)
                            .FontFamily(fonts.DisplayFamily)
                            .FontSize(35)
                            .LineHeight(1.02f)
                            .FontColor("#FFFFFF");
                        if (!fonts.AlatsiAvailable) coverTitle.Bold();
                        column.Item().Width(120).Height(3).Background(Gold);
                    }
                    if (data.Options.ShowFrontCoverSubtitle)
                    {
                        column.Item().Text(data.Options.Subtitle)
                            .FontSize(15)
                            .SemiBold()
                            .FontColor("#D6E8E2");
                    }
                    if (data.Options.ShowFrontCoverEdition)
                    {
                        column.Item().Text(data.Options.Edition)
                            .FontSize(10.5f)
                            .FontColor("#91B7AB");
                    }
                    if (data.Options.ShowFrontCoverStrapline && !string.IsNullOrWhiteSpace(data.Options.Strapline))
                    {
                        column.Item().PaddingTop(18).BorderLeft(3).BorderColor(Gold).PaddingLeft(16)
                            .Text(data.Options.Strapline)
                            .FontSize(17)
                            .SemiBold()
                            .LineHeight(1.18f)
                            .FontColor("#F7F3E5");
                    }

                    // Digital Cover A treats the institutional artwork as a deliberate editorial
                    // object, not as half of an empty framed montage. The surrounding dark field
                    // supplies the negative space.
                    column.Item().PaddingTop(24).AlignLeft().Element(box =>
                    {
                        if (institutionalArtwork is { Length: > 0 })
                        {
                            box.Width(268)
                                .Height(268)
                                .Border(1)
                                .BorderColor("#6C9489")
                                .Image(institutionalArtwork)
                                .FitArea();
                        }
                        else
                        {
                            box.Width(360).Height(220).Element(montage => ComposeCoverPhotoMontage(montage, coverPhotos));
                        }
                    });
                });
            });
        });
    }

    private static void ComposeContemporaryCover(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        byte[]? artracLogo)
    {
        var hero = data.CoverHeroImage?.Content;
        var titleSize = data.Options.Title.Length switch
        {
            > 78 => 29f,
            > 52 => 33f,
            _ => 38f
        };

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => style.FontFamily(fonts.PrimaryFamily).FontColor("#FFFFFF"));

            page.Content().Layers(layers =>
            {
                // QuestPDF Layers requires exactly one PrimaryLayer. Cover B previously
                // declared only overlay Layer() elements, which passed preflight but failed
                // at physical PDF composition with HTTP 500. The full-page institutional
                // field is the natural sizing layer; all decorative/content layers are
                // deliberately rendered above it.
                layers.PrimaryLayer().Background(Forest950);
                layers.Layer().AlignTop().Height(8).Background(Gold);
                layers.Layer().AlignRight().Width(120).Height(842).Background(Forest900);

                layers.Layer().PaddingHorizontal(38).PaddingTop(30).Row(row =>
                {
                    row.RelativeItem().Column(lockup =>
                    {
                        if (data.Options.ShowFrontCoverKicker && !string.IsNullOrWhiteSpace(data.Options.FrontCoverKicker))
                        {
                            lockup.Item().Text(data.Options.FrontCoverKicker!)
                                .FontSize(9.5f)
                                .Bold()
                                .LetterSpacing(.8f)
                                .FontColor("#DCE9E5");
                        }
                        if (data.Options.ShowFrontCoverDescriptor && !string.IsNullOrWhiteSpace(data.Options.FrontCoverDescriptor))
                        {
                            lockup.Item().PaddingTop(2).Text(data.Options.FrontCoverDescriptor!)
                                .FontSize(7.2f)
                                .LetterSpacing(1.05f)
                                .FontColor("#90B6AA");
                        }
                    });
                    row.AutoItem().Row(logos =>
                    {
                        logos.Spacing(8);
                        if (artracLogo is { Length: > 0 })
                        {
                            logos.ConstantItem(38).Height(38).Image(artracLogo).FitArea();
                        }
                        if (sddLogo is { Length: > 0 })
                        {
                            logos.ConstantItem(38).Height(38).Image(sddLogo).FitArea();
                        }
                    });
                });

                layers.Layer().PaddingHorizontal(38).PaddingTop(128).Column(title =>
                {
                    title.Spacing(8);
                    if (data.Options.ShowFrontCoverTitle)
                    {
                        title.Item().Text(data.Options.Title)
                            .FontFamily(fonts.DisplayFamily)
                            .FontSize(titleSize)
                            .Bold()
                            .LineHeight(1.0f)
                            .FontColor("#FFFFFF");
                        title.Item().Width(118).Height(3).Background(Gold);
                    }
                    if (data.Options.ShowFrontCoverSubtitle)
                    {
                        title.Item().Text(data.Options.Subtitle)
                            .FontSize(14.5f)
                            .SemiBold()
                            .FontColor("#D6E8E2");
                    }
                    if (data.Options.ShowFrontCoverEdition)
                    {
                        title.Item().Text(data.Options.Edition)
                            .FontSize(9.8f)
                            .FontColor("#9EC0B6");
                    }

                    if (!string.IsNullOrWhiteSpace(data.Options.HandlingMarking))
                    {
                        title.Item().PaddingTop(3).Text(data.Options.HandlingMarking!.ToUpperInvariant())
                            .FontSize(8)
                            .Bold()
                            .LetterSpacing(1.0f)
                            .FontColor("#F5D978");
                    }
                });

                // Cover B is intentionally image-led. It uses a large independent hero crop and
                // a restrained institutional band rather than the curated artwork treatment of A.
                layers.Layer()
                    .AlignBottom()
                    .PaddingBottom(108)
                    .PaddingHorizontal(26)
                    .Height(410)
                    .Element(heroBox =>
                    {
                        if (hero is { Length: > 0 })
                        {
                            heroBox.Border(1)
                                .BorderColor("#6C9489")
                                .Background(Forest900)
                                .Image(hero)
                                .FitArea();
                        }
                        else
                        {
                            ComposeGraphicPlaceholder(heroBox);
                        }
                    });

                var showFrontStrapline = data.Options.ShowFrontCoverStrapline
                    && !string.IsNullOrWhiteSpace(data.Options.Strapline);
                layers.Layer()
                    .AlignBottom()
                    .Height(showFrontStrapline ? 88 : 24)
                    .Background("#082A26")
                    .PaddingHorizontal(38)
                    .PaddingVertical(showFrontStrapline ? 18 : 0)
                    .Column(bottom =>
                    {
                        if (showFrontStrapline)
                        {
                            bottom.Item().Text(data.Options.Strapline)
                                .FontSize(15.5f)
                                .SemiBold()
                                .FontColor("#FFFFFF");
                        }
                    });
            });
        });
    }

    private static void ComposeDigitalInstitutionalOpening(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        byte[]? institutionalArtwork,
        BrochurePrintMatter matter,
        int pageNumber,
        int totalPages)
    {
        container.Page(page =>
        {
            ConfigureInnerPage(page, data.Options, fonts, sddLogo, "ABOUT SDD", pageNumber, totalPages);
            page.Content().PaddingTop(8).Column(column =>
            {
                column.Spacing(16);
                column.Item().Text("Simulator Development Division")
                    .FontFamily(fonts.DisplayFamily)
                    .FontSize(26)
                    .Bold()
                    .FontColor(Forest950);
                column.Item().Width(96).Height(3).Background(Gold);

                if (!string.IsNullOrWhiteSpace(matter.CentreStatement))
                {
                    column.Item()
                        .Background(Forest800)
                        .PaddingHorizontal(18)
                        .PaddingVertical(14)
                        .Text(matter.CentreStatement!)
                        .FontSize(14)
                        .SemiBold()
                        .LineHeight(1.18f)
                        .FontColor("#FFFFFF");
                }

                var showInstitutionalArtwork = data.Options.CoverStyle == BrochureCoverStyle.Contemporary
                    && institutionalArtwork is { Length: > 0 };
                column.Item().PaddingTop(6).Row(row =>
                {
                    row.Spacing(22);
                    row.RelativeItem(showInstitutionalArtwork ? 1.45f : 1.18f).Column(left =>
                    {
                        left.Spacing(8);
                        left.Item().Text("WHY SIMULATORS")
                            .FontSize(8)
                            .Bold()
                            .LetterSpacing(.8f)
                            .FontColor(Forest700);
                        if (!string.IsNullOrWhiteSpace(matter.OpeningNarrative))
                        {
                            left.Item().Text(matter.OpeningNarrative!)
                                .FontSize(10.6f)
                                .LineHeight(1.30f)
                                .FontColor(Ink);
                        }
                    });

                    row.RelativeItem(1f).Column(right =>
                    {
                        right.Spacing(10);
                        if (showInstitutionalArtwork)
                        {
                            right.Item().AlignCenter().Width(174).Height(139.2f)
                                .Border(1)
                                .BorderColor(Border)
                                .Image(institutionalArtwork!)
                                .FitArea();
                        }

                        right.Item().Text("FUTURE-READY CAPABILITY")
                            .FontSize(8)
                            .Bold()
                            .LetterSpacing(.72f)
                            .FontColor(Forest700);
                        if (!string.IsNullOrWhiteSpace(matter.FutureNarrative))
                        {
                            right.Item().Text(matter.FutureNarrative!)
                                .FontSize(9.9f)
                                .LineHeight(1.27f)
                                .FontColor(Ink);
                        }
                    });
                });
            });
        });
    }

    private static void ComposeAdditionalIntroductionPage(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        string text,
        int index,
        int totalIntroductionPages,
        int pageNumber,
        int totalPages)
    {
        container.Page(page =>
        {
            ConfigureInnerPage(page, data.Options, fonts, sddLogo, "ADDITIONAL INTRODUCTION", pageNumber, totalPages);
            page.Content().PaddingTop(12).Column(column =>
            {
                column.Spacing(18);
                var heading = index == 0 && !string.IsNullOrWhiteSpace(data.Options.IntroductionTitle)
                    ? data.Options.IntroductionTitle!
                    : totalIntroductionPages > 1
                        ? $"Introduction · {index + 1} of {totalIntroductionPages}"
                        : "Introduction";
                column.Item().Text(heading)
                    .FontFamily(fonts.DisplayFamily)
                    .FontSize(24)
                    .Bold()
                    .FontColor(Forest950);
                column.Item().Width(92).Height(3).Background(Gold);
                column.Item().Text(text)
                    .FontSize(11)
                    .LineHeight(1.34f)
                    .FontColor(Ink);
            });
        });
    }

    private static void ComposeDigitalInstitutionalClosing(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        BrochurePrintMatter matter,
        int pageNumber,
        int totalPages)
    {
        container.Page(page =>
        {
            ConfigureInnerPage(page, data.Options, fonts, sddLogo, "FUTURE CAPABILITY & ENGAGEMENT", pageNumber, totalPages);
            page.Content().PaddingTop(8).Column(column =>
            {
                column.Spacing(14);
                column.Item().Text("Future capability & engagement")
                    .FontFamily(fonts.DisplayFamily)
                    .FontSize(25)
                    .Bold()
                    .FontColor(Forest950);
                column.Item().Width(96).Height(3).Background(Gold);

                if (!string.IsNullOrWhiteSpace(matter.VisionaryHorizons))
                {
                    column.Item()
                        .Border(2)
                        .BorderColor("#264F78")
                        .Background("#FBF4D8")
                        .Padding(14)
                        .Column(panel =>
                        {
                            panel.Spacing(9);
                            panel.Item().AlignCenter()
                                .Background("#264F78")
                                .PaddingHorizontal(16)
                                .PaddingVertical(5)
                                .Text("Visionary Horizons & Strategic Objectives")
                                .FontSize(11.5f)
                                .SemiBold()
                                .FontColor("#FFFFFF");
                            panel.Item().Text(matter.VisionaryHorizons!)
                                .FontSize(10.2f)
                                .LineHeight(1.24f)
                                .FontColor(Ink);
                        });
                }

                column.Item().Row(row =>
                {
                    row.Spacing(18);
                    row.RelativeItem(1.35f).Column(left =>
                    {
                        left.Spacing(7);
                        if (!string.IsNullOrWhiteSpace(matter.ProcurementGuidance))
                        {
                            left.Item().Text("PROCUREMENT / ENGAGEMENT")
                                .FontSize(8)
                                .Bold()
                                .LetterSpacing(.75f)
                                .FontColor(Forest700);
                            left.Item().Text(matter.ProcurementGuidance!)
                                .FontSize(9.4f)
                                .LineHeight(1.23f)
                                .FontColor(Ink);
                        }
                    });

                    row.RelativeItem(1f).Column(right =>
                    {
                        right.Spacing(10);
                        if (!string.IsNullOrWhiteSpace(matter.DevelopingAgency))
                        {
                            right.Item().Text("DEVELOPING AGENCY")
                                .FontSize(7.6f)
                                .Bold()
                                .LetterSpacing(.65f)
                                .FontColor(Forest700);
                            right.Item().Text(matter.DevelopingAgency!)
                                .FontSize(8.7f)
                                .LineHeight(1.2f)
                                .FontColor(Ink);
                        }
                        if (!string.IsNullOrWhiteSpace(matter.ManufacturingAgency))
                        {
                            right.Item().Text("MANUFACTURING AGENCY")
                                .FontSize(7.6f)
                                .Bold()
                                .LetterSpacing(.65f)
                                .FontColor(Forest700);
                            right.Item().Text(matter.ManufacturingAgency!)
                                .FontSize(8.7f)
                                .LineHeight(1.2f)
                                .FontColor(Ink);
                        }
                    });
                });

                if (!string.IsNullOrWhiteSpace(matter.NewSimulatorsGuidance))
                {
                    column.Item()
                        .Background(Forest800)
                        .PaddingHorizontal(14)
                        .PaddingVertical(10)
                        .Text(text =>
                        {
                            text.DefaultTextStyle(style => style.FontSize(9.4f).LineHeight(1.18f));
                            text.Span("New Simulators. ").Bold().FontColor("#F0CD65");
                            text.Span(matter.NewSimulatorsGuidance!).FontColor("#FFFFFF");
                        });
                }
            });
        });
    }

    internal static IReadOnlyList<string> SplitIntroduction(string text, int maximumWords)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }
        maximumWords = Math.Max(80, maximumWords);
        if (BrochureLayoutPlanner.CountWords(text) <= maximumWords)
        {
            return new[] { text.Trim() };
        }

        var paragraphs = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pieces = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            if (BrochureLayoutPlanner.CountWords(paragraph) <= maximumWords)
            {
                pieces.Add(paragraph.Trim());
                continue;
            }

            var words = paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (var offset = 0; offset < words.Length; offset += maximumWords)
            {
                pieces.Add(string.Join(" ", words.Skip(offset).Take(maximumWords)));
            }
        }

        var pages = new List<string>();
        var current = new List<string>();
        var currentWords = 0;
        foreach (var piece in pieces)
        {
            var pieceWords = BrochureLayoutPlanner.CountWords(piece);
            if (current.Count > 0 && currentWords + pieceWords > maximumWords)
            {
                pages.Add(string.Join("\n\n", current));
                current.Clear();
                currentWords = 0;
            }
            current.Add(piece);
            currentWords += pieceWords;
        }
        if (current.Count > 0)
        {
            pages.Add(string.Join("\n\n", current));
        }
        return pages;
    }

    private static void ComposeProjectPage(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        BrochurePagePlan plan,
        int pageNumber,
        int totalPages,
        byte[]? sddLogo)
    {
        container.Page(page =>
        {
            ConfigureInnerPage(page, data.Options, fonts, sddLogo, "PROJECT CAPABILITIES", pageNumber, totalPages);

            page.Content().PaddingTop(6).Column(column =>
            {
                if (plan.Layout == BrochurePageLayoutKind.TwoFeature)
                {
                    const float featureGap = 18f;
                    const float moduleHeight = 349f;
                    for (var index = 0; index < plan.Items.Count; index++)
                    {
                        if (index > 0)
                        {
                            column.Item().Height(featureGap);
                        }

                        var fragment = plan.Items[index];
                        column.Item().Height(moduleHeight).Element(block =>
                            ComposeTwoFeatureBlock(block, fragment, data.Options.NarrativeAlignment, imageOnRight: index % 2 == 0));
                    }

                    return;
                }

                if (plan.Layout == BrochurePageLayoutKind.SingleFeature)
                {
                    var fragment = plan.Items[0];
                    column.Item().Element(block => ComposeSingleFeaturePage(block, fragment, data.Options.NarrativeAlignment));
                    return;
                }

                const float cardGap = 8f;
                const float totalHeight = 716f;
                var count = plan.Items.Count;
                var cardHeight = (totalHeight - (cardGap * Math.Max(0, count - 1))) / Math.Max(1, count);

                for (var index = 0; index < plan.Items.Count; index++)
                {
                    if (index > 0)
                    {
                        column.Item().Height(cardGap);
                    }

                    var fragment = plan.Items[index];
                    column.Item().Height(cardHeight).Element(card =>
                        ComposeProjectCard(card, fragment, plan.Layout, data.Options.NarrativeAlignment));
                }
            });

        });
    }

    private static void ConfigureInnerPage(
        PageDescriptor page,
        BrochureBuildOptions options,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        string sectionLabel,
        int pageNumber,
        int totalPages)
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(28);
        page.MarginTop(22);
        page.MarginBottom(20);
        page.PageColor(WarmWhite);
        page.DefaultTextStyle(style => style.FontFamily(fonts.PrimaryFamily).FontSize(9.75f).FontColor(Ink));

        page.Header().Height(34).Row(row =>
        {
            if (sddLogo is { Length: > 0 })
            {
                row.ConstantItem(26).Height(26).Image(sddLogo).FitArea();
                row.ConstantItem(9);
            }
            row.RelativeItem().AlignMiddle().Column(lockup =>
            {
                lockup.Item().Text(sectionLabel)
                    .FontSize(7.2f)
                    .Bold()
                    .LetterSpacing(.9f)
                    .FontColor(Forest700);
                lockup.Item().Text(options.Title)
                    .FontSize(8.2f)
                    .SemiBold()
                    .FontColor(Ink);
            });
            if (!string.IsNullOrWhiteSpace(options.HandlingMarking))
            {
                row.AutoItem().AlignMiddle().Text(options.HandlingMarking!.ToUpperInvariant())
                    .FontSize(7)
                    .Bold()
                    .LetterSpacing(.65f)
                    .FontColor("#8C6718");
            }
        });

        page.Footer().Height(22).Row(row =>
        {
            row.RelativeItem().AlignMiddle().Text(options.IssuerDisplayName.ToUpperInvariant())
                .FontSize(7.3f)
                .SemiBold()
                .LetterSpacing(.45f)
                .FontColor(Muted);
            row.AutoItem().AlignMiddle().Text($"{pageNumber} / {totalPages}")
                .FontSize(7.5f)
                .FontColor(Muted);
        });
    }

    private static void ComposeTwoFeatureBlock(
        IContainer container,
        BrochureProjectFragment fragment,
        BrochureNarrativeAlignment narrativeAlignment,
        bool imageOnRight)
    {
        var titleLength = fragment.Project.ProjectName.Length;
        var titleSize = titleLength switch
        {
            > 105 => 9.4f,
            > 72 => 10.2f,
            _ => 11f
        };
        var bodySize = fragment.NarrativeWordCount switch
        {
            > 175 => 9.5f,
            > 145 => 9.75f,
            _ => 10f
        };
        var (photoWidth, photoHeight, galleryHeight) = fragment.NarrativeWordCount switch
        {
            <= 125 => (225f, 145f, 112f),
            <= 155 => (215f, 132f, 108f),
            _ => (205f, 115f, 104f)
        };

        container.Background("#FFFFFF").Column(column =>
        {
            column.Item()
                .Height(titleLength > 105 ? 35 : 31)
                .Background(Forest800)
                .PaddingHorizontal(11)
                .Row(titleRow =>
                {
                    titleRow.RelativeItem().AlignMiddle().Text(fragment.Project.ProjectName.ToUpperInvariant())
                        .FontSize(titleSize)
                        .Bold()
                        .LineHeight(1.0f)
                        .FontColor("#FFFFFF");
                });

            column.Item()
                .PaddingVertical(12)
                .PaddingHorizontal(8)
                .Row(row =>
                {
                    var hasPrimary = fragment.Project.PrimaryPhoto is not null;
                    var useSecond = ShouldUseSecondImage(fragment.Project, BrochurePageLayoutKind.TwoFeature);

                    void AddText()
                        => row.RelativeItem().Element(text =>
                            ComposeNarrative(text, fragment.Narrative, bodySize, narrativeAlignment));

                    void AddImages()
                    {
                        if (!hasPrimary)
                        {
                            return;
                        }

                        row.ConstantItem(14);
                        row.ConstantItem(photoWidth).AlignMiddle().Column(images =>
                        {
                            if (!useSecond)
                            {
                                images.Item().Height(photoHeight)
                                    .Element(box => ComposeImageFrame(box, fragment.Project.PrimaryPhoto!.Content));
                                return;
                            }

                            images.Spacing(7);
                            images.Item().Height(galleryHeight)
                                .Element(box => ComposeImageFrame(box, fragment.Project.PrimaryPhoto!.Content));
                            images.Item().Height(galleryHeight)
                                .Element(box => ComposeImageFrame(box, fragment.Project.SecondaryPhoto!.Content));
                        });
                    }

                    if (!hasPrimary)
                    {
                        AddText();
                        return;
                    }

                    if (imageOnRight)
                    {
                        AddText();
                        AddImages();
                    }
                    else
                    {
                        // For the alternate block the visual leads the eye from the left.
                        row.ConstantItem(photoWidth).AlignMiddle().Column(images =>
                        {
                            if (!useSecond)
                            {
                                images.Item().Height(photoHeight)
                                    .Element(box => ComposeImageFrame(box, fragment.Project.PrimaryPhoto!.Content));
                                return;
                            }

                            images.Spacing(7);
                            images.Item().Height(galleryHeight)
                                .Element(box => ComposeImageFrame(box, fragment.Project.PrimaryPhoto!.Content));
                            images.Item().Height(galleryHeight)
                                .Element(box => ComposeImageFrame(box, fragment.Project.SecondaryPhoto!.Content));
                        });
                        row.ConstantItem(14);
                        AddText();
                    }
                });

            column.Item().Height(1).Background("#D7E2DE");
        });
    }

    private static void ComposeSingleFeaturePage(
        IContainer container,
        BrochureProjectFragment fragment,
        BrochureNarrativeAlignment narrativeAlignment)
    {
        var titleLength = fragment.Project.ProjectName.Length;
        var titleHeight = titleLength switch
        {
            <= 62 => 38f,
            <= 105 => 42f,
            _ => 48f
        };
        var titleSize = titleLength switch
        {
            > 105 => 10.4f,
            > 72 => 11.4f,
            _ => 12.4f
        };
        const float digitalBodyMinimum = 10.2f;
        var bodySize = fragment.NarrativeWordCount > 200 ? digitalBodyMinimum : 10.5f;
        var singleHeroWidth = fragment.NarrativeWordCount switch
        {
            <= 165 => 500f,
            <= 200 => 470f,
            _ => 445f
        };
        var singleHeroHeight = singleHeroWidth * 9f / 16f;
        const float galleryWidth = 500f;
        const float galleryGap = 10f;
        var galleryItemWidth = (galleryWidth - galleryGap) / 2f;
        var galleryHeight = galleryItemWidth * 9f / 16f;

        container.PaddingTop(3).Column(column =>
        {
            column.Item()
                .Height(titleHeight)
                .Background(Forest800)
                .PaddingHorizontal(12)
                .Row(titleRow =>
                {
                    titleRow.RelativeItem().AlignMiddle().Text(fragment.Project.ProjectName.ToUpperInvariant())
                        .FontSize(titleSize)
                        .Bold()
                        .LineHeight(1.0f)
                        .FontColor("#FFFFFF");
                    if (fragment.IsContinuation)
                    {
                        titleRow.AutoItem().AlignMiddle().Text($"CONTINUED {fragment.FragmentNumber}/{fragment.FragmentCount}")
                            .FontSize(6.8f)
                            .SemiBold()
                            .LetterSpacing(.45f)
                            .FontColor("#C8E0D8");
                    }
                });

            if (!fragment.IsContinuation && fragment.Project.PrimaryPhoto is not null)
            {
                column.Item().PaddingTop(16).AlignCenter().Element(imageArea =>
                {
                    if (ShouldUseSecondImage(fragment.Project, BrochurePageLayoutKind.SingleFeature))
                    {
                        imageArea.Width(galleryWidth).Height(galleryHeight).Row(row =>
                        {
                            row.Spacing(galleryGap);
                            row.RelativeItem().Element(box => ComposeImageFrame(box, fragment.Project.PrimaryPhoto.Content));
                            row.RelativeItem().Element(box => ComposeImageFrame(box, fragment.Project.SecondaryPhoto!.Content));
                        });
                    }
                    else
                    {
                        imageArea.Width(singleHeroWidth).Height(singleHeroHeight)
                            .Element(box => ComposeImageFrame(box, fragment.Project.PrimaryPhoto.Content));
                    }
                });
            }

            column.Item()
                .PaddingTop(fragment.IsContinuation || fragment.Project.PrimaryPhoto is null ? 18 : 20)
                .PaddingHorizontal(10)
                .Element(text => ComposeNarrative(text, fragment.Narrative, bodySize, narrativeAlignment));

            column.Item().PaddingTop(18).Width(92).Height(2).Background(Gold);
        });
    }

    private static void ComposeProjectCard(
        IContainer container,
        BrochureProjectFragment fragment,
        BrochurePageLayoutKind layout,
        BrochureNarrativeAlignment narrativeAlignment)
    {
        var titleLength = fragment.Project.ProjectName.Length;
        var titleHeight = titleLength switch
        {
            <= 62 => layout == BrochurePageLayoutKind.SingleFeature ? 34f : 28f,
            <= 105 => layout == BrochurePageLayoutKind.SingleFeature ? 38f : 34f,
            _ => 42f
        };
        var titleSize = layout switch
        {
            BrochurePageLayoutKind.FourCompact => titleLength > 105 ? 8.7f : titleLength > 72 ? 9.1f : 9.6f,
            BrochurePageLayoutKind.ThreeStandard => titleLength > 105 ? 8.9f : titleLength > 72 ? 9.6f : 10.2f,
            BrochurePageLayoutKind.TwoFeature => titleLength > 105 ? 9.3f : titleLength > 72 ? 10.1f : 11f,
            _ => titleLength > 105 ? 10f : titleLength > 72 ? 11f : 12f
        };
        var bodySize = layout switch
        {
            BrochurePageLayoutKind.FourCompact => 9.5f,
            BrochurePageLayoutKind.ThreeStandard => 9.75f,
            BrochurePageLayoutKind.TwoFeature => 10f,
            _ => 10.2f
        };

        container
            .Border(1)
            .BorderColor(Border)
            .Background("#FFFFFF")
            .Column(column =>
            {
                column.Item().Height(titleHeight).Background(Forest800).PaddingHorizontal(10).Row(titleRow =>
                {
                    titleRow.RelativeItem().AlignMiddle().Text(fragment.Project.ProjectName.ToUpperInvariant())
                        .FontSize(titleSize)
                        .Bold()
                        .LineHeight(1.0f)
                        .FontColor("#FFFFFF");
                    if (fragment.IsContinuation)
                    {
                        titleRow.AutoItem().AlignMiddle().Text($"CONTINUED {fragment.FragmentNumber}/{fragment.FragmentCount}")
                            .FontSize(6.6f)
                            .SemiBold()
                            .LetterSpacing(.45f)
                            .FontColor("#C8E0D8");
                    }
                });

                if (layout == BrochurePageLayoutKind.SingleFeature)
                {
                    column.Item().Element(body => ComposeSingleFeatureBody(body, fragment, bodySize, narrativeAlignment));
                }
                else
                {
                    column.Item().Element(body => ComposeMultiCardBody(body, fragment, bodySize, layout, narrativeAlignment));
                }
            });
    }

    private static void ComposeMultiCardBody(
        IContainer container,
        BrochureProjectFragment fragment,
        float fontSize,
        BrochurePageLayoutKind layout,
        BrochureNarrativeAlignment narrativeAlignment)
    {
        container.Padding(9).Row(row =>
        {
            var hasPrimary = fragment.Project.PrimaryPhoto is not null;
            var useSecond = ShouldUseSecondImage(fragment.Project, layout);
            var textWeight = hasPrimary ? 1.55f : 1f;
            row.RelativeItem(textWeight).Element(textBox => ComposeNarrative(textBox, fragment.Narrative, fontSize, narrativeAlignment));

            if (!hasPrimary)
            {
                return;
            }

            row.ConstantItem(10);
            var photoWidth = layout == BrochurePageLayoutKind.FourCompact ? 150f : 164f;
            if (!useSecond)
            {
                row.ConstantItem(photoWidth)
                    .AlignMiddle()
                    .Height(photoWidth * 9f / 16f)
                    .Element(box => ComposeImageFrame(box, fragment.Project.PrimaryPhoto!.Content));
                return;
            }

            row.ConstantItem(photoWidth).AlignMiddle().Column(gallery =>
            {
                gallery.Spacing(6);
                gallery.Item().Height(photoWidth * 9f / 16f)
                    .Element(box => ComposeImageFrame(box, fragment.Project.PrimaryPhoto!.Content));
                gallery.Item().Height(photoWidth * 9f / 16f)
                    .Element(box => ComposeImageFrame(box, fragment.Project.SecondaryPhoto!.Content));
            });
        });
    }

    private static void ComposeSingleFeatureBody(
        IContainer container,
        BrochureProjectFragment fragment,
        float fontSize,
        BrochureNarrativeAlignment narrativeAlignment)
    {
        container.Padding(11).Column(column =>
        {
            column.Spacing(10);
            if (!fragment.IsContinuation && fragment.Project.PrimaryPhoto is not null)
            {
                if (ShouldUseSecondImage(fragment.Project, BrochurePageLayoutKind.SingleFeature))
                {
                    column.Item().Height(150).Row(row =>
                    {
                        row.Spacing(9);
                        row.RelativeItem().Element(box => ComposeImageFrame(box, fragment.Project.PrimaryPhoto.Content));
                        row.RelativeItem().Element(box => ComposeImageFrame(box, fragment.Project.SecondaryPhoto!.Content));
                    });
                }
                else
                {
                    column.Item()
                        .AlignCenter()
                        .Width(382)
                        .Height(215)
                        .Element(box => ComposeImageFrame(box, fragment.Project.PrimaryPhoto.Content));
                }
            }

            column.Item().Element(textBox => ComposeNarrative(textBox, fragment.Narrative, fontSize, narrativeAlignment));
        });
    }

    private static bool ShouldUseSecondImage(
        BrochurePublicationProject project,
        BrochurePageLayoutKind layout)
    {
        if (project.SecondaryPhoto is null || project.ImageMode == BrochureImageMode.Single)
        {
            return false;
        }

        if (project.ImageMode == BrochureImageMode.GalleryTwo)
        {
            return layout is BrochurePageLayoutKind.TwoFeature or BrochurePageLayoutKind.SingleFeature;
        }

        return project.ImageMode == BrochureImageMode.Automatic
               && layout is BrochurePageLayoutKind.TwoFeature or BrochurePageLayoutKind.SingleFeature;
    }

    private static void ComposeImageFrame(IContainer container, byte[] image)
    {
        container
            .Border(1)
            .BorderColor("#B5C9C2")
            .Background(Forest50)
            .Image(image)
            .FitArea();
    }

    private static void ComposeNarrative(
        IContainer container,
        string narrative,
        float fontSize,
        BrochureNarrativeAlignment narrativeAlignment)
    {
        if (BrochureNarrativeTypographyPolicy.ShouldJustify(
                narrativeAlignment, BrochureNarrativeSegment.FullWidth))
        {
            container.Text(narrative)
                .FontSize(fontSize)
                .LineHeight(1.18f)
                .Justify()
                .FontColor(Ink);
            return;
        }

        container.Text(narrative)
            .FontSize(fontSize)
            .LineHeight(1.18f)
            .FontColor(Ink);
    }

    private static void ComposeCoverPhotoMontage(IContainer container, IReadOnlyList<byte[]> photos)
    {
        if (photos.Count == 0)
        {
            ComposeGraphicPlaceholder(container);
            return;
        }

        if (photos.Count == 1)
        {
            container.AlignCenter().Width(320).Height(180)
                .Element(box => ComposeImageFrame(box, photos[0]));
            return;
        }

        // All montage frames remain wide. This prevents a 16:9 publication crop being
        // forced into a tall portrait box and keeps Cover A deterministic.
        container.Column(column =>
        {
            column.Spacing(8);
            column.Item().Height(118).Element(box => ComposeImageFrame(box, photos[0]));
            column.Item().Height(76).Row(row =>
            {
                row.Spacing(8);
                row.RelativeItem().Element(box => ComposeImageFrame(box, photos[1]));
                if (photos.Count > 2)
                {
                    row.RelativeItem().Element(box => ComposeImageFrame(box, photos[2]));
                }
                else
                {
                    // A cover fallback must never inject system-owned copy. Keep the
                    // empty montage cell purely decorative so every visible cover line
                    // remains user-editable/suppressible.
                    row.RelativeItem().Background(Forest900).Padding(12).Column(fallback =>
                    {
                        fallback.Item().Height(4).Width(72).Background(Gold);
                        fallback.Item().PaddingTop(10).Height(18).Background("#184C42");
                        fallback.Item().PaddingTop(6).Height(8).Width(96).Background("#2C6A5B");
                    });
                }
            });
        });
    }

    private static void ComposeGraphicPlaceholder(IContainer container)
    {
        // Deliberately graphic-only. This helper is used by cover fallbacks, so it must
        // never introduce copy the user cannot edit or suppress in the builder.
        container.Background(Forest900).Padding(24).Column(column =>
        {
            column.Spacing(12);
            column.Item().Height(5).Width(165).Background(Gold);
            column.Item().Height(42).Row(row =>
            {
                row.RelativeItem(1.15f).Background("#184C42");
                row.ConstantItem(10);
                row.RelativeItem(.85f).Background("#2C6A5B");
            });
            column.Item().Height(18).Width(210).Background("#123D35");
            column.Item().Height(8).Width(126).Background("#5B8C7E");
        });
    }

    private static void ComposeBackCover(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        byte[]? artracLogo)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => style.FontFamily(fonts.PrimaryFamily).FontColor("#FFFFFF"));

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Background(Forest950);
                layers.Layer().AlignTop().Height(8).Background(Gold);
                layers.Layer().PaddingHorizontal(56).PaddingTop(120).Column(column =>
                {
                    column.Spacing(18);
                    column.Item().Row(logos =>
                    {
                        if (artracLogo is { Length: > 0 })
                        {
                            logos.ConstantItem(50).Height(50).Image(artracLogo).FitArea();
                            logos.ConstantItem(12);
                        }
                        if (sddLogo is { Length: > 0 })
                        {
                            logos.ConstantItem(50).Height(50).Image(sddLogo).FitArea();
                        }
                    });
                    if (data.Options.ShowBackCoverKicker && !string.IsNullOrWhiteSpace(data.Options.BackCoverKicker))
                    {
                        column.Item().PaddingTop(36).Text(data.Options.BackCoverKicker!)
                            .FontSize(12)
                            .Bold()
                            .LetterSpacing(.9f)
                            .FontColor("#CFE2DC");
                    }
                    if (data.Options.ShowBackCoverStrapline && !string.IsNullOrWhiteSpace(data.Options.BackCoverStrapline))
                    {
                        column.Item().Text(data.Options.BackCoverStrapline!)
                            .FontSize(25)
                            .SemiBold()
                            .LineHeight(1.13f)
                            .FontColor("#FFFFFF");
                        column.Item().Width(110).Height(3).Background(Gold);
                    }
                    if (data.Options.ShowBackCoverEdition && !string.IsNullOrWhiteSpace(data.Options.BackCoverEdition))
                    {
                        column.Item().Text(data.Options.BackCoverEdition!)
                            .FontSize(10)
                            .LetterSpacing(.35f)
                            .FontColor("#9EC0B6");
                    }
                });

                if (!string.IsNullOrWhiteSpace(data.Options.HandlingMarking))
                {
                    layers.Layer().AlignBottom().PaddingBottom(42).AlignCenter()
                        .Text(data.Options.HandlingMarking!.ToUpperInvariant())
                        .FontSize(8)
                        .Bold()
                        .LetterSpacing(1.1f)
                        .FontColor("#F5D978");
                }
            });
        });
    }

    private byte[]? TryLoadInstitutionalArtwork(BrochureInstitutionalCoverArtwork artwork)
        => TryLoadFirstAsset(
            BrochureInstitutionalCoverArtworkCatalog.RelativePath(artwork),
            BrochureInstitutionalCoverArtworkCatalog.RelativePath(BrochureInstitutionalCoverArtwork.ReferenceOriginal),
            "img/publications/cover-a-institutional.jpg",
            "img/publications/cover-a-institutional.png",
            "img/publications/cover-a-institutional.webp");

    private byte[]? TryLoadFirstAsset(params string[] relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var asset = TryLoadAsset(relativePath);
            if (asset is { Length: > 0 })
            {
                return asset;
            }
        }

        return null;
    }

    private byte[]? TryLoadAsset(string relativePath)
    {
        var root = _environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var path = Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
