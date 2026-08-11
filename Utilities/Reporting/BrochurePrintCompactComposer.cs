using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ProjectManagement.Services.Publications;

namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Original-format hard-copy brochure compositor. The reference sheet is 423.23 x 846.755 pt
/// (approximately 149.3 x 298.7 mm). Phase 8 adds deterministic sheet planning so the final
/// institutional matter normally shares the last page with projects and interior pages are balanced.
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
        ComposePlannedProjectPages(container, data, fonts, sddLogo);
    }

    private static void ComposeFrontPage(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        byte[]? artracLogo,
        byte[]? institutionalArtwork)
    {
        if (data.Options.CoverStyle == BrochureCoverStyle.Institutional)
        {
            ComposeInstitutionalFrontPage(
                container,
                data,
                fonts,
                sddLogo,
                artracLogo,
                institutionalArtwork);
            return;
        }

        ComposeContemporaryFrontPage(container, data, fonts, sddLogo, artracLogo);
    }

    /// <summary>
    /// Official-style hard-copy treatment. The institutional artwork is authoritative when supplied.
    /// When it is absent, PRISM deliberately renders a restrained institutional fallback rather than
    /// substituting an unrelated first-project photograph.
    /// </summary>
    private static void ComposeInstitutionalFrontPage(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        byte[]? artracLogo,
        byte[]? institutionalArtwork)
    {
        container.Page(page =>
        {
            page.Size(ReferenceWidthPoints, ReferenceHeightPoints);
            page.Margin(0);
            page.PageColor(Forest800);
            page.DefaultTextStyle(style => style.FontFamily(fonts.PrimaryFamily).FontColor("#FFFFFF"));

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Background(Forest800);

                layers.Layer().AlignTop().Height(326).Element(area =>
                {
                    if (institutionalArtwork is { Length: > 0 })
                    {
                        area.Image(institutionalArtwork).FitArea();
                    }
                    else
                    {
                        ComposeInstitutionalFallbackArtwork(area);
                    }
                });

                ComposeFrontLockup(layers.Layer(), data, sddLogo, artracLogo);

                layers.Layer()
                    .AlignTop()
                    .PaddingTop(232)
                    .PaddingHorizontal(15)
                    .MinHeight(58)
                    .Background(Forest950)
                    .PaddingHorizontal(10)
                    .PaddingVertical(8)
                    .AlignCenter()
                    .AlignMiddle()
                    .Text(data.Options.PrintCentreStatement ?? string.Empty)
                    .FontSize(8.2f)
                    .Bold()
                    .LineHeight(1.04f)
                    .AlignCenter()
                    .FontColor("#FFFFFF");

                layers.Layer()
                    .PaddingTop(326)
                    .PaddingBottom(120)
                    .Background(Forest800)
                    .PaddingHorizontal(10)
                    .PaddingVertical(7)
                    .Column(column =>
                    {
                        column.Spacing(3.2f);
                        column.Item().Text(data.Options.PrintIntroText ?? string.Empty)
                            .FontSize(7.05f)
                            .LineHeight(1.06f)
                            .Justify()
                            .FontColor("#FFFFFF");
                        column.Item().Text(data.Options.PrintFutureText ?? string.Empty)
                            .FontSize(7.0f)
                            .LineHeight(1.06f)
                            .Justify()
                            .FontColor("#FFFFFF");
                        column.Item().Text(text =>
                        {
                            text.DefaultTextStyle(style => style
                                .FontSize(6.95f)
                                .LineHeight(1.05f)
                                .FontColor("#FFFFFF"));
                            text.Justify();
                            text.Span("Procurement: ").Bold().FontColor("#F1D35D");
                            text.Span(data.Options.PrintProcurementText ?? string.Empty);
                        });
                    });

                ComposeContactAndStrapline(layers.Layer(), layers.Layer(), data);
                ComposeHandlingMarking(layers.Layer(), data.Options.HandlingMarking);
            });
        });
    }

    /// <summary>
    /// Contemporary image-led alternative. It retains the same authoritative institutional copy
    /// but deliberately uses a lighter editorial field so Cover B remains visually distinct.
    /// </summary>
    private static void ComposeContemporaryFrontPage(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        byte[]? artracLogo)
    {
        var hero = data.CoverHeroImage?.Content;

        container.Page(page =>
        {
            page.Size(ReferenceWidthPoints, ReferenceHeightPoints);
            page.Margin(0);
            page.PageColor(Paper);
            page.DefaultTextStyle(style => style.FontFamily(fonts.PrimaryFamily).FontColor(Ink));

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Background(Paper);

                layers.Layer().AlignTop().Height(278).Background(Forest900).Element(area =>
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

                ComposeFrontLockup(layers.Layer(), data, sddLogo, artracLogo);

                layers.Layer().AlignTop().PaddingTop(215).PaddingHorizontal(16).MinHeight(58)
                    .Background(Forest950)
                    .PaddingHorizontal(10)
                    .PaddingVertical(8)
                    .AlignCenter()
                    .AlignMiddle()
                    .Text(data.Options.PrintCentreStatement ?? string.Empty)
                    .FontSize(8.15f)
                    .Bold()
                    .LineHeight(1.04f)
                    .AlignCenter()
                    .FontColor("#FFFFFF");

                layers.Layer()
                    .PaddingTop(282)
                    .PaddingBottom(120)
                    .PaddingHorizontal(10)
                    .Background("#F8F8F4")
                    .PaddingVertical(7)
                    .Column(column =>
                    {
                        column.Spacing(4);
                        column.Item().Background("#EFF4F1").PaddingHorizontal(6).PaddingVertical(3)
                            .Text(data.Options.PrintIntroText ?? string.Empty)
                            .FontSize(7.15f)
                            .LineHeight(1.07f)
                            .Justify()
                            .FontColor(Ink);
                        column.Item().Text(data.Options.PrintFutureText ?? string.Empty)
                            .FontSize(7.05f)
                            .LineHeight(1.07f)
                            .Justify()
                            .FontColor(Ink);
                        column.Item().Text(text =>
                        {
                            text.DefaultTextStyle(style => style
                                .FontSize(7.0f)
                                .LineHeight(1.05f)
                                .FontColor(Ink));
                            text.Justify();
                            text.Span("Procurement: ").Bold().FontColor("#7A6516");
                            text.Span(data.Options.PrintProcurementText ?? string.Empty);
                        });
                    });

                ComposeContactAndStrapline(layers.Layer(), layers.Layer(), data);
                ComposeHandlingMarking(layers.Layer(), data.Options.HandlingMarking);
            });
        });
    }

    private static void ComposeFrontLockup(
        IContainer layer,
        BrochurePublicationData data,
        byte[]? sddLogo,
        byte[]? artracLogo)
    {
        layer.AlignTop().PaddingTop(8).PaddingHorizontal(10).Row(row =>
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
    }

    private static void ComposeInstitutionalFallbackArtwork(IContainer container)
    {
        container.Background(Forest900).Layers(layers =>
        {
            layers.PrimaryLayer().Background(Forest900);
            layers.Layer().AlignRight().Width(140).Height(326).Background(Forest800);
            layers.Layer().AlignBottom().Height(9).Background(Gold);
            layers.Layer().PaddingTop(78).PaddingHorizontal(32).Column(column =>
            {
                column.Spacing(8);
                column.Item().AlignCenter().Text("SIMULATORS · AR/VR · AI")
                    .FontSize(15f)
                    .Bold()
                    .LetterSpacing(.25f)
                    .FontColor("#FFFFFF");
                column.Item().AlignCenter().Text("DRONES · ROBOTICS · NICHE TECHNOLOGIES")
                    .FontSize(7.4f)
                    .SemiBold()
                    .LetterSpacing(.35f)
                    .FontColor("#D9C673");
                column.Item().PaddingTop(10).AlignCenter().Width(190).Height(1.2f).Background("#A9C6BB");
                column.Item().PaddingTop(9).AlignCenter().Text("SIMULATOR DEVELOPMENT DIVISION")
                    .FontSize(9.2f)
                    .Bold()
                    .LetterSpacing(.4f)
                    .FontColor("#E8F1EE");
            });
        });
    }

    private static void ComposeContactAndStrapline(
        IContainer contactLayer,
        IContainer straplineLayer,
        BrochurePublicationData data)
    {
        contactLayer.AlignBottom().PaddingBottom(22).Height(98).Background(Contact).Padding(8).Row(row =>
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

        straplineLayer.AlignBottom().Height(22).Background(Forest800).AlignCenter().AlignMiddle()
            .Text(data.Options.Strapline)
            .FontSize(8.3f)
            .SemiBold()
            .Italic()
            .FontColor("#F4D66E");
    }

    private static void ComposeHandlingMarking(IContainer layer, string? handlingMarking)
    {
        if (string.IsNullOrWhiteSpace(handlingMarking))
        {
            return;
        }

        layer.AlignTop().PaddingTop(48).AlignCenter()
            .Background(Contact)
            .PaddingHorizontal(8)
            .PaddingVertical(2)
            .Text(handlingMarking.ToUpperInvariant())
            .FontSize(6.4f)
            .Bold()
            .LetterSpacing(.6f)
            .FontColor("#FFFFFF");
    }

    private static void ComposePlannedProjectPages(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo)
    {
        var plan = BrochurePrintCompactPlanner.Plan(
            data.Projects,
            data.Options.PrintVisionaryText,
            data.Options.PrintNewSimulatorsText);

        foreach (var sheet in plan.Pages)
        {
            ComposeProjectSheet(container, data, fonts, sddLogo, sheet);
        }
    }

    private static void ComposeProjectSheet(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        BrochurePrintCompactPage sheet)
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
                column.Spacing(BrochurePrintCompactPlanner.InterModuleSpacingPoints);

                foreach (var projectIndex in sheet.ProjectIndexes)
                {
                    var project = data.Projects[projectIndex];
                    var planningItem = new BrochurePrintPlanningItem(
                        project.ProjectId,
                        project.ProjectName,
                        project.NarrativeWordCount,
                        project.ImageMode,
                        project.PrimaryPhoto is not null,
                        project.SecondaryPhoto is not null);
                    var minimumHeight = BrochurePrintCompactPlanner.EstimateProjectHeight(planningItem)
                                        + sheet.ModuleExpansionPoints;

                    column.Item()
                        .MinHeight(minimumHeight)
                        .ShowEntire()
                        .Element(module => ComposeProjectModule(
                            module,
                            project,
                            imageOnRight: projectIndex % 2 == 0,
                            moduleExpansionPoints: sheet.ModuleExpansionPoints));
                }

                if (sheet.IncludesClosingMatter)
                {
                    if (sheet.ProjectIndexes.Count > 0)
                    {
                        column.Item().Height(BrochurePrintCompactPlanner.ClosingGapPoints);
                    }
                    column.Item().ShowEntire().Element(block => ComposeClosingMatter(block, data));
                }
            });
        });
    }

    private static void ComposeProjectModule(
        IContainer container,
        BrochurePublicationProject project,
        bool imageOnRight,
        float moduleExpansionPoints)
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
        imageWidth += Math.Min(7f, moduleExpansionPoints * .45f);
        var bodyPadding = 5f + Math.Min(1.5f, moduleExpansionPoints * .08f);

        container.Border(1.05f).BorderColor(Forest700).Background("#FBFBF8").Column(column =>
        {
            column.Item().Height(titleHeight).Background(Forest800).PaddingHorizontal(6).AlignMiddle()
                .Text(project.ProjectName.ToUpperInvariant())
                .FontSize(titleSize)
                .Bold()
                .LineHeight(1.0f)
                .AlignCenter()
                .FontColor("#FFFFFF");

            column.Item().Padding(bodyPadding).Row(row =>
            {
                var hasPrimary = project.PrimaryPhoto is not null;
                var useSecond = project.SecondaryPhoto is not null
                                && project.ImageMode != BrochureImageMode.Single;

                void AddText()
                    => row.RelativeItem().Text(project.Narrative)
                        .FontSize(bodySize)
                        .LineHeight(1.05f)
                        .Justify()
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
        container.PaddingTop(1).Column(column =>
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
                    .AlignCenter()
                    .FontColor("#FFFFFF");
                vision.Item().Text(data.Options.PrintVisionaryText ?? string.Empty)
                    .FontSize(7.6f)
                    .Italic()
                    .LineHeight(1.08f)
                    .Justify()
                    .FontColor("#27302B");
            });

            column.Item().Background(Forest800).Padding(7).Text(text =>
            {
                text.DefaultTextStyle(style => style
                    .FontSize(7.25f)
                    .LineHeight(1.05f));
                text.Justify();
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
