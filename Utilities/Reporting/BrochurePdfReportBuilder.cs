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
        var pagePlans = BrochureLayoutPlanner.Plan(data.Projects);
        var sddLogo = TryLoadAsset("img/logos/sdd.png");
        var artracLogo = TryLoadAsset("img/logos/artrac.png");
        var institutionalArtwork = TryLoadInstitutionalArtwork(data.Options.InstitutionalCoverArtwork);

        var document = Document.Create(container =>
        {
            if (data.Options.PublicationProfile == BrochurePublicationProfile.PrintCompact)
            {
                var printPlan = _printPagePlanner.Plan(
                    data.Projects,
                    BrochurePrintPublicationPolicy.FromOptions(data.Options),
                    data.Options.CoverStyle,
                    data.Options.Strapline,
                    !string.IsNullOrWhiteSpace(data.Options.HandlingMarking));

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

            if (!string.IsNullOrWhiteSpace(data.Options.IntroductionText))
            {
                ComposeIntroductionPages(container, data, fontStatus, sddLogo);
            }

            for (var index = 0; index < pagePlans.Count; index++)
            {
                ComposeProjectPage(
                    container,
                    data,
                    fontStatus,
                    pagePlans[index],
                    index + 1,
                    pagePlans.Count,
                    sddLogo);
            }

            if (data.Options.IncludeBackCover)
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

        return document.GeneratePdf();
    }

    private static void ComposeInstitutionalCover(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        byte[]? artracLogo,
        byte[]? institutionalArtwork)
    {
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
                layers.Layer()
                    .AlignRight()
                    .Width(185)
                    .Height(842)
                    .Background(Forest900);
                layers.Layer()
                    .AlignRight()
                    .AlignTop()
                    .Width(330)
                    .Height(8)
                    .Background(Gold);

                layers.PrimaryLayer().Padding(36).Column(column =>
                {
                    column.Spacing(16);
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(lockup =>
                        {
                            lockup.Item().Text(data.Options.IssuerDisplayName.ToUpperInvariant())
                                .FontSize(11)
                                .SemiBold()
                                .LetterSpacing(.7f)
                                .FontColor("#DCE9E5");
                            lockup.Item().PaddingTop(3).Text("OFFICIAL CAPABILITY PUBLICATION")
                                .FontSize(7.5f)
                                .LetterSpacing(1.1f)
                                .FontColor("#90B6AA");
                        });
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
                    });

                    if (!string.IsNullOrWhiteSpace(data.Options.HandlingMarking))
                    {
                        column.Item().AlignLeft().Text(data.Options.HandlingMarking!.ToUpperInvariant())
                            .FontSize(8)
                            .Bold()
                            .LetterSpacing(1.2f)
                            .FontColor("#F5D978");
                    }

                    var coverTitle = column.Item().PaddingTop(42).Text(data.Options.Title)
                        .FontFamily(fonts.DisplayFamily)
                        .FontSize(35)
                        .LineHeight(1.02f)
                        .FontColor("#FFFFFF");
                    if (!fonts.AlatsiAvailable)
                    {
                        coverTitle.Bold();
                    }

                    column.Item().Width(120).Height(3).Background(Gold);
                    column.Item().Text(data.Options.Subtitle)
                        .FontSize(15)
                        .SemiBold()
                        .FontColor("#D6E8E2");
                    column.Item().Text(data.Options.Edition)
                        .FontSize(10.5f)
                        .FontColor("#91B7AB");

                    column.Item().PaddingTop(24).BorderLeft(3).BorderColor(Gold).PaddingLeft(16)
                        .Text(data.Options.Strapline)
                        .FontSize(17)
                        .SemiBold()
                        .LineHeight(1.18f)
                        .FontColor("#F7F3E5");

                    column.Item().PaddingTop(28).Height(210).Element(box =>
                    {
                        if (institutionalArtwork is { Length: > 0 })
                        {
                            box.Border(1)
                                .BorderColor("#5E887C")
                                .Background(Forest900)
                                .Image(institutionalArtwork)
                                .FitArea();
                        }
                        else
                        {
                            ComposeCoverPhotoMontage(box, coverPhotos);
                        }
                    });

                    column.Item().PaddingTop(14).Text(data.Options.Edition)
                        .FontSize(8.5f)
                        .LetterSpacing(.35f)
                        .FontColor("#9EC0B6");
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
            > 78 => 30f,
            > 52 => 34f,
            _ => 40f
        };

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(WarmWhite);
            page.DefaultTextStyle(style => style.FontFamily(fonts.PrimaryFamily).FontColor(Ink));

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Background(WarmWhite);

                layers.Layer().AlignTop().Height(8).Background(Forest800);

                layers.Layer().PaddingHorizontal(38).PaddingTop(30).Row(row =>
                {
                    row.RelativeItem().Column(lockup =>
                    {
                        lockup.Item().Text("SIMULATOR DEVELOPMENT DIVISION")
                            .FontSize(9.5f)
                            .Bold()
                            .LetterSpacing(.8f)
                            .FontColor(Forest800);
                        lockup.Item().PaddingTop(2).Text("CAPABILITY PUBLICATION")
                            .FontSize(7.5f)
                            .LetterSpacing(1.2f)
                            .FontColor(Muted);
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

                layers.Layer().PaddingHorizontal(38).PaddingTop(130).Column(title =>
                {
                    title.Spacing(7);
                    title.Item().Text(data.Options.Title)
                        .FontSize(titleSize)
                        .Bold()
                        .LineHeight(1.0f)
                        .FontColor(Forest950);
                    title.Item().Text(data.Options.Subtitle)
                        .FontSize(15)
                        .SemiBold()
                        .FontColor(Forest700);
                    title.Item().Text(data.Options.Edition)
                        .FontSize(10)
                        .FontColor(Muted);

                    if (!string.IsNullOrWhiteSpace(data.Options.HandlingMarking))
                    {
                        title.Item().PaddingTop(5).Text(data.Options.HandlingMarking!.ToUpperInvariant())
                            .FontSize(8)
                            .Bold()
                            .LetterSpacing(1.0f)
                            .FontColor("#986D14");
                    }
                });

                // The hero and closing band are bottom-anchored. This prevents the
                // unexplained white tail that appeared when a fixed-height vertical
                // column finished before the A4 page ended.
                layers.Layer()
                    .AlignBottom()
                    .PaddingBottom(92)
                    .Height(364)
                    .Element(heroBox =>
                    {
                        if (hero is { Length: > 0 })
                        {
                            heroBox.Background(Forest50).Image(hero).FitArea();
                        }
                        else
                        {
                            ComposeGraphicPlaceholder(heroBox);
                        }
                    });

                layers.Layer()
                    .AlignBottom()
                    .Height(92)
                    .Background(Forest950)
                    .PaddingHorizontal(38)
                    .PaddingVertical(20)
                    .Column(bottom =>
                    {
                        bottom.Spacing(6);
                        bottom.Item().Text(data.Options.Strapline)
                            .FontSize(16)
                            .SemiBold()
                            .FontColor("#FFFFFF");
                        bottom.Item().Text(data.Options.Edition)
                            .FontSize(8.2f)
                            .LetterSpacing(.25f)
                            .FontColor("#9EC0B6");
                    });
            });
        });
    }

    private static void ComposeIntroductionPages(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo)
    {
        var chunks = SplitIntroduction(data.Options.IntroductionText!, 330);
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            var wordCount = BrochureLayoutPlanner.CountWords(chunk);
            var firstPage = index == 0;
            var photoCount = firstPage
                ? wordCount <= 170 ? 2 : wordCount <= 250 ? 1 : 0
                : 0;

            container.Page(page =>
            {
                ConfigureInnerPage(page, data.Options, fonts, sddLogo, "ABOUT SDD");
                page.Content().PaddingTop(12).Column(column =>
                {
                    column.Spacing(18);
                    column.Item().Text(firstPage
                            ? string.IsNullOrWhiteSpace(data.Options.IntroductionTitle)
                                ? "Simulator Development Division"
                                : data.Options.IntroductionTitle!
                            : "Introduction · continued")
                        .FontSize(firstPage ? 25 : 19)
                        .Bold()
                        .FontColor(Forest950);
                    column.Item().Width(92).Height(3).Background(Gold);
                    column.Item().Text(chunk)
                        .FontSize(11)
                        .LineHeight(1.34f)
                        .FontColor(Ink);

                    if (photoCount > 0)
                    {
                        var photos = data.Projects
                            .Where(project => project.PrimaryPhoto is not null)
                            .Take(photoCount)
                            .Select(project => project.PrimaryPhoto!.Content)
                            .ToArray();
                        if (photos.Length > 0)
                        {
                            column.Item().PaddingTop(10).Height(photoCount == 1 ? 205 : 190).Row(row =>
                            {
                                row.Spacing(10);
                                foreach (var photo in photos)
                                {
                                    row.RelativeItem()
                                        .Border(1)
                                        .BorderColor(Border)
                                        .Background(Forest50)
                                        .Image(photo)
                                        .FitArea();
                                }
                            });
                        }
                    }
                });
            });
        }
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
        int sequence,
        int totalProjectPages,
        byte[]? sddLogo)
    {
        container.Page(page =>
        {
            ConfigureInnerPage(page, data.Options, fonts, sddLogo, "PROJECT CAPABILITIES");

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
                            ComposeTwoFeatureBlock(block, fragment, imageOnRight: index % 2 == 0));
                    }

                    return;
                }

                if (plan.Layout == BrochurePageLayoutKind.SingleFeature)
                {
                    var fragment = plan.Items[0];
                    column.Item().Element(block => ComposeSingleFeaturePage(block, fragment));
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
                        ComposeProjectCard(card, fragment, plan.Layout));
                }
            });

            page.Footer().Height(22).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text(data.Options.IssuerDisplayName.ToUpperInvariant())
                    .FontSize(7.3f)
                    .SemiBold()
                    .LetterSpacing(.45f)
                    .FontColor(Muted);
                row.AutoItem().AlignMiddle().Text($"{sequence} / {totalProjectPages}")
                    .FontSize(7.5f)
                    .FontColor(Muted);
            });
        });
    }

    private static void ConfigureInnerPage(
        PageDescriptor page,
        BrochureBuildOptions options,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        string sectionLabel)
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
    }

    private static void ComposeTwoFeatureBlock(
        IContainer container,
        BrochureProjectFragment fragment,
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
                            ComposeNarrative(text, fragment.Narrative, bodySize));

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
        BrochureProjectFragment fragment)
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
        var bodySize = fragment.NarrativeWordCount > 200 ? 10.2f : 10.5f;

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
                        imageArea.Width(500).Height(205).Row(row =>
                        {
                            row.Spacing(10);
                            row.RelativeItem().Element(box => ComposeImageFrame(box, fragment.Project.PrimaryPhoto.Content));
                            row.RelativeItem().Element(box => ComposeImageFrame(box, fragment.Project.SecondaryPhoto!.Content));
                        });
                    }
                    else
                    {
                        imageArea.Width(445).Height(250)
                            .Element(box => ComposeImageFrame(box, fragment.Project.PrimaryPhoto.Content));
                    }
                });
            }

            column.Item()
                .PaddingTop(fragment.IsContinuation || fragment.Project.PrimaryPhoto is null ? 18 : 20)
                .PaddingHorizontal(10)
                .Element(text => ComposeNarrative(text, fragment.Narrative, bodySize));

            column.Item().PaddingTop(18).Width(92).Height(2).Background(Gold);
        });
    }

    private static void ComposeProjectCard(
        IContainer container,
        BrochureProjectFragment fragment,
        BrochurePageLayoutKind layout)
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
                    column.Item().Element(body => ComposeSingleFeatureBody(body, fragment, bodySize));
                }
                else
                {
                    column.Item().Element(body => ComposeMultiCardBody(body, fragment, bodySize, layout));
                }
            });
    }

    private static void ComposeMultiCardBody(
        IContainer container,
        BrochureProjectFragment fragment,
        float fontSize,
        BrochurePageLayoutKind layout)
    {
        container.Padding(9).Row(row =>
        {
            var hasPrimary = fragment.Project.PrimaryPhoto is not null;
            var useSecond = ShouldUseSecondImage(fragment.Project, layout);
            var textWeight = hasPrimary ? 1.55f : 1f;
            row.RelativeItem(textWeight).Element(textBox => ComposeNarrative(textBox, fragment.Narrative, fontSize));

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
        float fontSize)
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

            column.Item().Element(textBox => ComposeNarrative(textBox, fragment.Narrative, fontSize));
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

    private static void ComposeNarrative(IContainer container, string narrative, float fontSize)
    {
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
                    row.RelativeItem().Background(Forest900).Padding(12).AlignMiddle().Text("SDD · PRISM")
                        .FontSize(10)
                        .SemiBold()
                        .FontColor("#9EC0B6");
                }
            });
        });
    }

    private static void ComposeGraphicPlaceholder(IContainer container)
    {
        container.Background(Forest900).Padding(24).Column(column =>
        {
            column.Spacing(10);
            column.Item().Height(5).Width(165).Background(Gold);
            column.Item().Text("SIMULATORS · AI · AR/VR · ROBOTICS · DRONES")
                .FontSize(12)
                .SemiBold()
                .LetterSpacing(.6f)
                .FontColor("#DCE9E5");
            column.Item().Text("Capability imagery is drawn from the selected PRISM project photographs when available.")
                .FontSize(9)
                .LineHeight(1.25f)
                .FontColor("#9EC0B6");
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
                    column.Item().PaddingTop(36).Text("SIMULATOR DEVELOPMENT DIVISION")
                        .FontSize(12)
                        .Bold()
                        .LetterSpacing(.9f)
                        .FontColor("#CFE2DC");
                    column.Item().Text(data.Options.Strapline)
                        .FontSize(25)
                        .SemiBold()
                        .LineHeight(1.13f)
                        .FontColor("#FFFFFF");
                    column.Item().Width(110).Height(3).Background(Gold);
                    column.Item().Text(data.Options.Edition)
                        .FontSize(10)
                        .LetterSpacing(.35f)
                        .FontColor("#9EC0B6");
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
    {
        var selected = artwork switch
        {
            BrochureInstitutionalCoverArtwork.PremiumGreenGold => "img/publications/covers/cover-a-premium-green-gold.jpg",
            BrochureInstitutionalCoverArtwork.CinematicCyber => "img/publications/covers/cover-a-cinematic-cyber.jpg",
            BrochureInstitutionalCoverArtwork.ExecutiveTeal => "img/publications/covers/cover-a-executive-teal.jpg",
            BrochureInstitutionalCoverArtwork.LuminousHalo => "img/publications/covers/cover-a-luminous-halo.jpg",
            _ => "img/publications/covers/cover-a-reference-original.jpg"
        };

        return TryLoadFirstAsset(
            selected,
            "img/publications/covers/cover-a-reference-original.jpg",
            "img/publications/cover-a-institutional.jpg",
            "img/publications/cover-a-institutional.png",
            "img/publications/cover-a-institutional.webp");
    }

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
