using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ProjectManagement.Services.Publications;

namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Publication-grade A4 brochure composer. The layout is deterministic and adaptive:
/// short narratives can share a four-project page, while longer projects automatically
/// receive progressively more page area rather than forcing smaller body typography.
/// </summary>
public sealed class BrochurePdfReportBuilder
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

    static BrochurePdfReportBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public BrochurePdfReportBuilder(IWebHostEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public byte[] Build(BrochurePublicationData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Projects.Count == 0)
        {
            throw new InvalidOperationException("A brochure requires at least one project.");
        }

        var fontStatus = PublicationFontRegistry.EnsureRegistered(_environment.WebRootPath);
        var pagePlans = BrochureLayoutPlanner.Plan(data.Projects);
        var sddLogo = TryLoadAsset("img/logos/sdd.png");
        var artracLogo = TryLoadAsset("img/logos/artrac.png");

        var document = Document.Create(container =>
        {
            if (data.Options.CoverStyle == BrochureCoverStyle.Institutional)
            {
                ComposeInstitutionalCover(container, data, fontStatus, sddLogo, artracLogo);
            }
            else
            {
                ComposeContemporaryCover(container, data, fontStatus, sddLogo, artracLogo);
            }

            if (!string.IsNullOrWhiteSpace(data.Options.IntroductionText))
            {
                ComposeIntroductionPage(container, data, fontStatus, sddLogo);
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
        })
        .WithMetadata(new DocumentMetadata
        {
            Title = data.Options.Title,
            Author = data.Options.IssuerDisplayName,
            Subject = $"Capability brochure containing {data.Projects.Count} project(s).",
            Keywords = "SDD, PRISM ERP, capability brochure, simulators, projects",
            Creator = "PRISM ERP",
            Producer = "PRISM ERP / QuestPDF",
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
        byte[]? artracLogo)
    {
        var coverPhotos = data.Projects
            .Where(project => project.Photo is { Length: > 0 })
            .Take(3)
            .Select(project => project.Photo!)
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

                    var coverTitle = column.Item().PaddingTop(48).Text(data.Options.Title)
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

                    column.Item().PaddingTop(28).BorderLeft(3).BorderColor(Gold).PaddingLeft(16)
                        .Text(data.Options.Strapline)
                        .FontSize(18)
                        .SemiBold()
                        .LineHeight(1.18f)
                        .FontColor("#F7F3E5");

                    column.Item().PaddingTop(38).Height(178).Element(box =>
                        ComposeCoverPhotoMontage(box, coverPhotos));

                    column.Item().PaddingTop(18).Row(bottom =>
                    {
                        bottom.RelativeItem().Text($"{data.Projects.Count} selected project{(data.Projects.Count == 1 ? string.Empty : "s")}")
                            .FontSize(9)
                            .FontColor("#9EC0B6");
                        bottom.AutoItem().Text("Generated from PRISM ERP")
                            .FontSize(8.5f)
                            .FontColor("#9EC0B6");
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
        var hero = data.Projects.FirstOrDefault(project => project.Photo is { Length: > 0 })?.Photo;

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(WarmWhite);
            page.DefaultTextStyle(style => style.FontFamily(fonts.PrimaryFamily).FontColor(Ink));

            page.Content().Column(column =>
            {
                column.Item().Height(8).Background(Forest800);
                column.Item().PaddingHorizontal(38).PaddingTop(30).Row(row =>
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

                column.Item().PaddingHorizontal(38).PaddingTop(54).Text(data.Options.Title)
                    .FontSize(40)
                    .Bold()
                    .LineHeight(1.0f)
                    .FontColor(Forest950);
                column.Item().PaddingHorizontal(38).PaddingTop(10).Text(data.Options.Subtitle)
                    .FontSize(15)
                    .SemiBold()
                    .FontColor(Forest700);
                column.Item().PaddingHorizontal(38).PaddingTop(5).Text(data.Options.Edition)
                    .FontSize(10)
                    .FontColor(Muted);

                if (!string.IsNullOrWhiteSpace(data.Options.HandlingMarking))
                {
                    column.Item().PaddingHorizontal(38).PaddingTop(14).Text(data.Options.HandlingMarking!.ToUpperInvariant())
                        .FontSize(8)
                        .Bold()
                        .LetterSpacing(1.0f)
                        .FontColor("#986D14");
                }

                column.Item().PaddingTop(34).Height(340).Element(heroBox =>
                {
                    if (hero is { Length: > 0 })
                    {
                        heroBox.Image(hero).FitArea();
                    }
                    else
                    {
                        ComposeGraphicPlaceholder(heroBox);
                    }
                });

                column.Item().Background(Forest950).PaddingHorizontal(38).PaddingVertical(22).Column(bottom =>
                {
                    bottom.Spacing(7);
                    bottom.Item().Text(data.Options.Strapline)
                        .FontSize(16)
                        .SemiBold()
                        .FontColor("#FFFFFF");
                    bottom.Item().Text($"{data.Projects.Count} projects · Generated from authoritative PRISM records")
                        .FontSize(8.5f)
                        .FontColor("#9EC0B6");
                });
            });
        });
    }

    private static void ComposeIntroductionPage(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo)
    {
        container.Page(page =>
        {
            ConfigureInnerPage(page, data.Options, fonts, sddLogo, "ABOUT SDD");
            page.Content().PaddingTop(12).Column(column =>
            {
                column.Spacing(18);
                column.Item().Text(string.IsNullOrWhiteSpace(data.Options.IntroductionTitle)
                        ? "Simulator Development Division"
                        : data.Options.IntroductionTitle!)
                    .FontSize(25)
                    .Bold()
                    .FontColor(Forest950);
                column.Item().Width(92).Height(3).Background(Gold);
                column.Item().Text(data.Options.IntroductionText!)
                    .FontSize(11)
                    .LineHeight(1.34f)
                    .FontColor(Ink);

                var photos = data.Projects
                    .Where(project => project.Photo is { Length: > 0 })
                    .Take(2)
                    .Select(project => project.Photo!)
                    .ToArray();
                if (photos.Length > 0)
                {
                    column.Item().PaddingTop(12).Height(210).Row(row =>
                    {
                        row.Spacing(10);
                        foreach (var photo in photos)
                        {
                            row.RelativeItem().Border(1).BorderColor(Border).Background(Forest50)
                                .Image(photo).FitArea();
                        }
                    });
                }
            });
        });
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
                var gap = 8f;
                var totalHeight = 716f;
                var count = plan.Items.Count;
                var cardHeight = (totalHeight - (gap * Math.Max(0, count - 1))) / Math.Max(1, count);

                for (var index = 0; index < plan.Items.Count; index++)
                {
                    if (index > 0)
                    {
                        column.Item().Height(gap);
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
            row.RelativeItem(1.55f).Element(textBox => ComposeNarrative(textBox, fragment.Narrative, fontSize));

            if (fragment.Project.Photo is { Length: > 0 })
            {
                row.ConstantItem(10);
                var photoWidth = layout == BrochurePageLayoutKind.FourCompact ? 150f : 164f;
                var photoHeight = photoWidth * 9f / 16f;
                row.ConstantItem(photoWidth)
                    .AlignMiddle()
                    .Height(photoHeight)
                    .Border(1)
                    .BorderColor("#B5C9C2")
                    .Background(Forest50)
                    .Image(fragment.Project.Photo)
                    .FitArea();
            }
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
            if (!fragment.IsContinuation && fragment.Project.Photo is { Length: > 0 })
            {
                column.Item()
                    .AlignCenter()
                    .Width(382)
                    .Height(215)
                    .Border(1)
                    .BorderColor("#B5C9C2")
                    .Background(Forest50)
                    .Image(fragment.Project.Photo)
                    .FitArea();
            }

            column.Item().Element(textBox => ComposeNarrative(textBox, fragment.Narrative, fontSize));
        });
    }

    private static void ComposeNarrative(IContainer container, string narrative, float fontSize)
    {
        var isMissing = narrative.EndsWith("not recorded.", StringComparison.OrdinalIgnoreCase);
        var text = container.Text(narrative)
            .FontSize(fontSize)
            .LineHeight(1.18f)
            .FontColor(isMissing ? "#8A6B30" : Ink);
        if (isMissing)
        {
            text.Italic();
        }
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
            container.AlignCenter().Width(300).Height(169)
                .Border(1)
                .BorderColor("#5E887C")
                .Background(Forest900)
                .Image(photos[0])
                .FitArea();
            return;
        }

        container.Row(row =>
        {
            row.RelativeItem(1.65f)
                .Border(1)
                .BorderColor("#5E887C")
                .Background(Forest900)
                .Image(photos[0])
                .FitArea();

            row.ConstantItem(8);
            row.RelativeItem().Column(right =>
            {
                right.Spacing(8);
                right.Item().Height(85)
                    .Border(1)
                    .BorderColor("#5E887C")
                    .Background(Forest900)
                    .Image(photos[1])
                    .FitArea();

                if (photos.Count > 2)
                {
                    right.Item().Height(85)
                        .Border(1)
                        .BorderColor("#5E887C")
                        .Background(Forest900)
                        .Image(photos[2])
                        .FitArea();
                }
                else
                {
                    right.Item().Height(85)
                        .Background(Forest900)
                        .Padding(12)
                        .AlignMiddle()
                        .Text("SDD · PRISM")
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

