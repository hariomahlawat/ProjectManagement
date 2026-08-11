using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ProjectManagement.Services.Publications;

namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Hard-copy brochure compositor modelled on the effective CropBox of the approved
/// Canva reference (423.23 x 846.755 points, approximately 149.3 x 298.7 mm).
/// The print profile intentionally favours dense, content-bearing pages and allows
/// QuestPDF to measure/pack project modules naturally rather than forcing A4 feature cards.
/// </summary>
internal static class BrochurePrintCompactComposer
{
    internal const float ReferenceWidthPoints = 423.23f;
    internal const float ReferenceHeightPoints = 846.755f;

    private const string Forest950 = "#0B2F2A";
    private const string Forest900 = "#103D35";
    private const string Forest800 = "#155447";
    private const string Forest700 = "#1B6B59";
    private const string Gold = "#D6B64B";
    private const string Ink = "#15231F";
    private const string Muted = "#5E6E68";
    private const string Paper = "#F5F4EF";
    private const string Contact = "#741A16";
    private const string VisionBlue = "#1C5C77";
    private const string VisionPaper = "#F7E6A3";

    internal static void Compose(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        byte[]? artracLogo,
        byte[]? institutionalArtwork)
    {
        ComposeFrontPage(container, data, fonts, sddLogo, artracLogo, institutionalArtwork);
        ComposeProjectAndClosingPages(container, data, fonts, sddLogo);
    }

    private static void ComposeFrontPage(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        byte[]? artracLogo,
        byte[]? institutionalArtwork)
    {
        var hero = data.Options.CoverStyle == BrochureCoverStyle.Contemporary
            ? data.CoverHeroImage?.Content
            : institutionalArtwork;

        if (hero is null && data.Options.CoverStyle == BrochureCoverStyle.Institutional)
        {
            hero = data.Projects.FirstOrDefault(project => project.PrimaryPhoto is not null)?.PrimaryPhoto?.Content;
        }

        container.Page(page =>
        {
            page.Size(ReferenceWidthPoints, ReferenceHeightPoints);
            page.Margin(0);
            page.PageColor(Paper);
            page.DefaultTextStyle(style => style.FontFamily(fonts.PrimaryFamily).FontColor(Ink));

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Background(Paper);

                layers.Layer().AlignTop().Height(248).Background(Forest900).Element(area =>
                {
                    if (hero is { Length: > 0 })
                    {
                        area.Image(hero).FitArea();
                    }
                    else
                    {
                        area.Background(Forest800);
                    }
                });

                layers.Layer().AlignTop().PaddingTop(8).PaddingHorizontal(10).Row(row =>
                {
                    if (artracLogo is { Length: > 0 })
                    {
                        row.ConstantItem(34).Height(34).Image(artracLogo).FitArea();
                    }

                    row.RelativeItem().AlignMiddle().AlignCenter().Column(lockup =>
                    {
                        lockup.Item().AlignCenter().Text(data.Options.Title.ToUpperInvariant())
                            .FontSize(8.7f)
                            .Bold()
                            .LetterSpacing(.55f)
                            .FontColor("#FFFFFF");
                        lockup.Item().AlignCenter().Text(data.Options.Edition)
                            .FontSize(6.3f)
                            .SemiBold()
                            .FontColor("#D6E8E2");
                    });

                    if (sddLogo is { Length: > 0 })
                    {
                        row.ConstantItem(34).Height(34).Image(sddLogo).FitArea();
                    }
                });

                layers.Layer().AlignTop().PaddingTop(192).PaddingHorizontal(16).Height(54)
                    .Background(Forest950)
                    .Padding(8)
                    .AlignCenter()
                    .AlignMiddle()
                    .Text(data.Options.PrintCentreStatement ?? string.Empty)
                    .FontSize(8.2f)
                    .Bold()
                    .LineHeight(1.05f)
                    .FontColor("#FFFFFF");

                layers.Layer()
                    .PaddingTop(253)
                    .PaddingBottom(121)
                    .PaddingHorizontal(10)
                    .Column(column =>
                    {
                        column.Spacing(4);
                        column.Item().Text(data.Options.PrintIntroText ?? string.Empty)
                            .FontSize(7.05f)
                            .LineHeight(1.06f)
                            .FontColor(Ink);
                        column.Item().Text(data.Options.PrintFutureText ?? string.Empty)
                            .FontSize(7.05f)
                            .LineHeight(1.06f)
                            .FontColor(Ink);
                        column.Item().Text(text =>
                        {
                            text.DefaultTextStyle(style => style
                                .FontSize(7.0f)
                                .LineHeight(1.05f)
                                .FontColor(Ink));
                            text.Span("Procurement: ").Bold().FontColor("#7A6516");
                            text.Span(data.Options.PrintProcurementText ?? string.Empty);
                        });
                    });

                layers.Layer().AlignBottom().PaddingBottom(22).Height(98).Background(Contact).Padding(8).Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text("Developing Agency")
                            .FontSize(7.0f)
                            .Bold()
                            .Underline()
                            .FontColor("#FFF5DB");
                        left.Item().PaddingTop(2).Text(data.Options.PrintDevelopingAgencyText ?? string.Empty)
                            .FontSize(6.4f)
                            .SemiBold()
                            .LineHeight(1.05f)
                            .FontColor("#FFFFFF");
                    });

                    row.ConstantItem(10);
                    row.RelativeItem().Column(right =>
                    {
                        right.Item().Text("Manufacturing Agency")
                            .FontSize(7.0f)
                            .Bold()
                            .Underline()
                            .FontColor("#FFF5DB");
                        right.Item().PaddingTop(2).Text(data.Options.PrintManufacturingAgencyText ?? string.Empty)
                            .FontSize(6.4f)
                            .SemiBold()
                            .LineHeight(1.05f)
                            .FontColor("#FFFFFF");
                    });
                });

                layers.Layer().AlignBottom().Height(22).Background(Forest800).AlignCenter().AlignMiddle()
                    .Text(data.Options.Strapline)
                    .FontSize(8.3f)
                    .SemiBold()
                    .Italic()
                    .FontColor("#F4D66E");

                if (!string.IsNullOrWhiteSpace(data.Options.HandlingMarking))
                {
                    layers.Layer().AlignTop().PaddingTop(48).AlignCenter()
                        .Background(Contact)
                        .PaddingHorizontal(8)
                        .PaddingVertical(2)
                        .Text(data.Options.HandlingMarking!.ToUpperInvariant())
                        .FontSize(6.4f)
                        .Bold()
                        .LetterSpacing(.6f)
                        .FontColor("#FFFFFF");
                }
            });
        });
    }

    private static void ComposeProjectAndClosingPages(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo)
    {
        container.Page(page =>
        {
            page.Size(ReferenceWidthPoints, ReferenceHeightPoints);
            page.MarginHorizontal(5);
            page.MarginTop(string.IsNullOrWhiteSpace(data.Options.HandlingMarking) ? 5 : 15);
            page.MarginBottom(5);
            page.PageColor(Paper);
            page.DefaultTextStyle(style => style.FontFamily(fonts.PrimaryFamily).FontColor(Ink));

            if (!string.IsNullOrWhiteSpace(data.Options.HandlingMarking))
            {
                page.Header().Height(10).AlignRight().Text(data.Options.HandlingMarking!.ToUpperInvariant())
                    .FontSize(6.1f)
                    .Bold()
                    .LetterSpacing(.5f)
                    .FontColor("#8A6817");
            }

            page.Content().Column(column =>
            {
                column.Spacing(4);

                for (var index = 0; index < data.Projects.Count; index++)
                {
                    var project = data.Projects[index];
                    column.Item()
                        .ShowEntire()
                        .Element(module => ComposeProjectModule(module, project, imageOnRight: index % 2 == 0));
                }

                // The reference hard-copy brochure uses its final page for both the last
                // project modules and institutional closing matter. ShowEntire keeps the
                // institutional closing block intact while natural column flow places it
                // immediately after the final project whenever sufficient space remains.
                column.Item().ShowEntire().Element(block => ComposeClosingMatter(block, data));
            });
        });
    }

    private static void ComposeProjectModule(
        IContainer container,
        BrochurePublicationProject project,
        bool imageOnRight)
    {
        var titleLength = project.ProjectName.Length;
        var titleSize = titleLength switch
        {
            > 115 => 7.4f,
            > 82 => 7.9f,
            _ => 8.5f
        };
        var titleHeight = titleLength switch
        {
            > 115 => 30f,
            > 82 => 26f,
            _ => 22f
        };
        var bodySize = project.NarrativeWordCount switch
        {
            > 190 => 7.65f,
            > 155 => 7.85f,
            > 120 => 8.0f,
            _ => 8.15f
        };
        var imageWidth = project.NarrativeWordCount switch
        {
            > 180 => 112f,
            > 145 => 120f,
            > 110 => 128f,
            _ => 136f
        };

        container.Border(1.05f).BorderColor(Forest700).Background("#FBFBF8").Column(column =>
        {
            column.Item().Height(titleHeight).Background(Forest800).PaddingHorizontal(6).AlignMiddle()
                .Text(project.ProjectName.ToUpperInvariant())
                .FontSize(titleSize)
                .Bold()
                .LineHeight(1.0f)
                .FontColor("#FFFFFF");

            column.Item().Padding(5).Row(row =>
            {
                var hasPrimary = project.PrimaryPhoto is not null;
                var useSecond = project.SecondaryPhoto is not null
                                && project.ImageMode != BrochureImageMode.Single;

                void AddText()
                    => row.RelativeItem().Text(project.Narrative)
                        .FontSize(bodySize)
                        .LineHeight(1.05f)
                        .FontColor(Ink);

                void AddImageColumn()
                {
                    if (!hasPrimary)
                    {
                        return;
                    }

                    row.ConstantItem(imageWidth).AlignMiddle().Column(images =>
                    {
                        if (!useSecond)
                        {
                            images.Item().Height(imageWidth * 9f / 16f)
                                .Element(box => ComposeImage(box, project.PrimaryPhoto!.Content));
                            return;
                        }

                        var galleryHeight = imageWidth * 9f / 16f;
                        images.Spacing(4);
                        images.Item().Height(galleryHeight)
                            .Element(box => ComposeImage(box, project.PrimaryPhoto!.Content));
                        images.Item().Height(galleryHeight)
                            .Element(box => ComposeImage(box, project.SecondaryPhoto!.Content));
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
                    row.ConstantItem(6);
                    AddImageColumn();
                }
                else
                {
                    AddImageColumn();
                    row.ConstantItem(6);
                    AddText();
                }
            });
        });
    }

    private static void ComposeClosingMatter(IContainer container, BrochurePublicationData data)
    {
        container.PaddingTop(3).Column(column =>
        {
            column.Spacing(5);

            column.Item().Border(4).BorderColor(VisionBlue).Background(VisionPaper).Padding(7).Column(vision =>
            {
                vision.Spacing(4);
                vision.Item().AlignCenter().Background(VisionBlue).PaddingHorizontal(8).PaddingVertical(2)
                    .Text("Visionary Horizons & Strategic Objectives")
                    .FontSize(8.8f)
                    .Bold()
                    .Italic()
                    .FontColor("#FFFFFF");
                vision.Item().Text(data.Options.PrintVisionaryText ?? string.Empty)
                    .FontSize(7.6f)
                    .Italic()
                    .LineHeight(1.08f)
                    .FontColor("#27302B");
            });

            column.Item().Background(Forest800).Padding(7).Text(text =>
            {
                text.DefaultTextStyle(style => style
                    .FontSize(7.25f)
                    .LineHeight(1.05f));
                text.Span("New Simulators. ").Bold().Italic().FontColor("#F4D66E");
                text.Span(data.Options.PrintNewSimulatorsText ?? string.Empty).SemiBold().FontColor("#FFFFFF");
            });

            column.Item().AlignCenter().Text(data.Options.Strapline)
                .FontSize(6.7f)
                .SemiBold()
                .Italic()
                .FontColor(Forest700);
        });
    }

    private static void ComposeImage(IContainer container, byte[] image)
        => container
            .Border(.6f)
            .BorderColor("#71817B")
            .Background("#FFFFFF")
            .Padding(1)
            .Image(image)
            .FitArea();
}
