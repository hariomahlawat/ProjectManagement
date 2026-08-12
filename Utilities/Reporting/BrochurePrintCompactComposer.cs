using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ProjectManagement.Services.Publications;

namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Original-format hard-copy brochure compositor. Phase 14 consumes adaptive measured geometry:
/// imagery is anchored upper-right, copy wraps beside it and returns to full width below, image
/// frames use the same 16:9 geometry as the publication crop pipeline, and Cover A contact headings
/// reserve an explicit centre lane for the CONTACTS identifier.
/// </summary>
internal static class BrochurePrintCompactComposer
{
    internal const float ReferenceWidthPoints = BrochurePrintLayoutMetrics.ReferenceWidthPoints;
    internal const float ReferenceHeightPoints = BrochurePrintLayoutMetrics.ReferenceHeightPoints;

    private const string Forest950 = "#0B2F2A";
    private const string Forest900 = "#103D35";
    private const string Forest800 = "#156656";
    private const string Forest700 = "#156656";
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
        byte[]? institutionalArtwork,
        BrochurePrintCompactPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        ComposeFrontPage(
            container,
            data,
            fonts,
            sddLogo,
            artracLogo,
            institutionalArtwork,
            plan.FrontPage);
        ComposePlannedProjectPages(container, data, fonts, sddLogo, plan);
    }

    private static void ComposeFrontPage(
        IDocumentContainer container,
        BrochurePublicationData data,
        PublicationFontStatus fonts,
        byte[]? sddLogo,
        byte[]? artracLogo,
        byte[]? institutionalArtwork,
        BrochurePrintFrontPagePlan frontPlan)
    {
        container.Page(page =>
        {
            page.Size(ReferenceWidthPoints, ReferenceHeightPoints);
            page.Margin(0);
            page.PageColor(data.Options.CoverStyle == BrochureCoverStyle.Institutional ? Forest800 : Paper);
            page.DefaultTextStyle(style => style.FontFamily(fonts.PrimaryFamily));

            page.Content().Column(column =>
            {
                column.Spacing(0);
                column.Item().Height(frontPlan.HeroHeightPoints).Element(hero =>
                    ComposeFrontHero(
                        hero,
                        data,
                        sddLogo,
                        artracLogo,
                        institutionalArtwork));

                if (frontPlan.CentreBlockHeightPoints > .5f)
                {
                    column.Item().Height(frontPlan.CentreBlockHeightPoints)
                        .Background(Forest950)
                        .PaddingHorizontal(12)
                        .PaddingVertical(7)
                        .AlignCenter()
                        .AlignMiddle()
                        .Text(data.Options.PrintCentreStatement ?? string.Empty)
                        .FontSize(frontPlan.CentreFontSize)
                        .Bold()
                        .LineHeight(BrochurePrintLayoutMetrics.FrontCentreLineHeight)
                        .AlignCenter()
                        .FontColor("#FFFFFF");
                }

                column.Item().Height(frontPlan.BodyBlockHeightPoints)
                    .Background(data.Options.CoverStyle == BrochureCoverStyle.Institutional ? Forest800 : "#F8F8F4")
                    .PaddingHorizontal(10)
                    .PaddingTop(7)
                    .PaddingBottom(6)
                    .Column(body =>
                    {
                        body.Spacing(frontPlan.BodySpacingPoints);

                        if (data.Options.CoverStyle == BrochureCoverStyle.Contemporary)
                        {
                            body.Item().Background("#EFF4F1").PaddingHorizontal(5).PaddingVertical(2)
                                .Text(data.Options.PrintIntroText ?? string.Empty)
                                .FontSize(frontPlan.BodyFontSize)
                                .LineHeight(frontPlan.BodyLineHeight)
                                .Justify()
                                .FontColor(Ink);
                        }
                        else
                        {
                            body.Item().Text(data.Options.PrintIntroText ?? string.Empty)
                                .FontSize(frontPlan.BodyFontSize)
                                .LineHeight(frontPlan.BodyLineHeight)
                                .Justify()
                                .FontColor("#FFFFFF");
                        }

                        body.Item().Text(data.Options.PrintFutureText ?? string.Empty)
                            .FontSize(frontPlan.BodyFontSize)
                            .LineHeight(frontPlan.BodyLineHeight)
                            .Justify()
                            .FontColor(data.Options.CoverStyle == BrochureCoverStyle.Institutional ? "#FFFFFF" : Ink);

                        body.Item().Text(text =>
                        {
                            text.DefaultTextStyle(style => style
                                .FontSize(Math.Max(BrochurePrintLayoutMetrics.FrontBodyMinimumFontSize, frontPlan.BodyFontSize - .1f))
                                .LineHeight(1.06f)
                                .FontColor(data.Options.CoverStyle == BrochureCoverStyle.Institutional ? "#FFFFFF" : Ink));
                            text.Justify();
                            text.Span("Procurement: ")
                                .Bold()
                                .FontColor(data.Options.CoverStyle == BrochureCoverStyle.Institutional ? "#F1D35D" : "#7A6516");
                            text.Span(data.Options.PrintProcurementText ?? string.Empty);
                        });
                    });

                column.Item().Height(frontPlan.ContactBlockHeightPoints)
                    .Element(contact => ComposeContactBlock(contact, data, frontPlan.ContactFontSize));

                column.Item().Height(frontPlan.StraplineHeightPoints)
                    .Background(Forest800)
                    .AlignCenter()
                    .AlignMiddle()
                    .Text(data.Options.Strapline)
                    .FontSize(8.5f)
                    .SemiBold()
                    .Italic()
                    .FontColor("#F4D66E");
            });
        });
    }

    private static void ComposeFrontHero(
        IContainer container,
        BrochurePublicationData data,
        byte[]? sddLogo,
        byte[]? artracLogo,
        byte[]? institutionalArtwork)
    {
        container.Layers(layers =>
        {
            layers.PrimaryLayer().Element(area =>
            {
                if (data.Options.CoverStyle == BrochureCoverStyle.Institutional)
                {
                    if (institutionalArtwork is { Length: > 0 })
                    {
                        area.Background(Forest900).Image(institutionalArtwork).FitArea();
                    }
                    else
                    {
                        ComposeInstitutionalFallbackArtwork(area);
                    }
                    return;
                }

                if (data.CoverHeroImage?.Content is { Length: > 0 } hero)
                {
                    area.Background(Forest900).Image(hero).FitArea();
                }
                else
                {
                    area.Background(Forest800);
                }
            });

            if (data.Options.CoverStyle == BrochureCoverStyle.Institutional)
            {
                ComposeInstitutionalCentreOverlay(layers.Layer(), data.Options.PrintCentreStatement);
                if (institutionalArtwork is not { Length: > 0 })
                {
                    ComposeFrontLockup(layers.Layer(), data, sddLogo, artracLogo);
                }
            }
            else
            {
                ComposeFrontLockup(layers.Layer(), data, sddLogo, artracLogo);
            }
            ComposeHandlingMarking(layers.Layer(), data.Options.HandlingMarking);
        });
    }

    private static void ComposeInstitutionalCentreOverlay(IContainer layer, string? statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return;
        }

        layer.AlignBottom()
            .PaddingBottom(8)
            .PaddingHorizontal(34)
            .Background("#741A16")
            .BorderTop(1.2f)
            .BorderColor(Gold)
            .PaddingHorizontal(12)
            .PaddingVertical(6)
            .AlignCenter()
            .Text(statement)
            .FontSize(10.2f)
            .Bold()
            .LineHeight(1.03f)
            .AlignCenter()
            .FontColor("#FFFFFF");
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
            layers.Layer().AlignRight().Width(140).Background(Forest800);
            layers.Layer().AlignBottom().Height(9).Background(Gold);
            layers.Layer().PaddingTop(72).PaddingHorizontal(32).Column(column =>
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

                // The artwork asset remains authoritative. This fallback is deliberately more
                // content-bearing than the Phase 9 empty field so a missing optional asset still
                // produces a credible institutional cover rather than a large visual void.
                column.Item().PaddingTop(27).AlignCenter().Text("SDD")
                    .FontSize(31f)
                    .Bold()
                    .LetterSpacing(2.2f)
                    .FontColor("#DDEBE6");
                column.Item().PaddingTop(6).Row(row =>
                {
                    var labels = new[] { "SIM", "AR/VR", "AI", "DRONES", "ROBOTICS" };
                    for (var index = 0; index < labels.Length; index++)
                    {
                        if (index > 0)
                        {
                            row.ConstantItem(4);
                        }

                        row.RelativeItem().Border(.7f).BorderColor("#789E90")
                            .PaddingVertical(4).AlignCenter().Text(labels[index])
                            .FontSize(6.6f).SemiBold().LetterSpacing(.2f).FontColor("#F4E07B");
                    }
                });
            });
        });
    }

    private static void ComposeContactBlock(
        IContainer container,
        BrochurePublicationData data,
        float contactFontSize)
    {
        container.Background(Contact)
            .PaddingHorizontal(8)
            .PaddingVertical(6)
            .Column(column =>
            {
                column.Spacing(2);

                column.Item().Height(BrochurePrintLayoutMetrics.FrontContactBadgeHeightPoints)
                    .AlignCenter()
                    .AlignMiddle()
                    .Background("#E0182D")
                    .PaddingHorizontal(18)
                    .PaddingVertical(3)
                    .Text("CONTACTS")
                    .FontSize(7.6f)
                    .Bold()
                    .AlignCenter()
                    .FontColor("#F8E34F");

                column.Item().Height(BrochurePrintLayoutMetrics.FrontContactAgencyHeadingHeightPoints)
                    .Row(header =>
                    {
                        header.RelativeItem(BrochurePrintLayoutMetrics.FrontContactDevelopingFraction)
                            .AlignMiddle()
                            .Text("Developing Agency")
                            .FontSize(contactFontSize + .45f)
                            .Bold().Underline().FontColor("#FFF5DB");
                        header.ConstantItem(12);
                        header.RelativeItem(BrochurePrintLayoutMetrics.FrontContactManufacturingFraction)
                            .AlignMiddle()
                            .Text("Manufacturing Agency")
                            .FontSize(contactFontSize + .45f)
                            .Bold().Underline().FontColor("#FFF5DB");
                    });

                column.Item().Row(row =>
                {
                    row.RelativeItem(BrochurePrintLayoutMetrics.FrontContactDevelopingFraction)
                        .Text(data.Options.PrintDevelopingAgencyText ?? string.Empty)
                        .FontSize(contactFontSize).SemiBold()
                        .LineHeight(BrochurePrintLayoutMetrics.FrontContactLineHeight)
                        .FontColor("#FFFFFF");
                    row.ConstantItem(12);
                    row.RelativeItem(BrochurePrintLayoutMetrics.FrontContactManufacturingFraction)
                        .Text(data.Options.PrintManufacturingAgencyText ?? string.Empty)
                        .FontSize(contactFontSize).SemiBold()
                        .LineHeight(BrochurePrintLayoutMetrics.FrontContactLineHeight)
                        .FontColor("#FFFFFF");
                });
            });
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
        byte[]? sddLogo,
        BrochurePrintCompactPlan plan)
    {
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
            page.MarginHorizontal(BrochurePrintLayoutMetrics.ProjectMarginHorizontalPoints);
            page.MarginTop(string.IsNullOrWhiteSpace(data.Options.HandlingMarking)
                ? BrochurePrintLayoutMetrics.ProjectMarginTopPoints
                : BrochurePrintLayoutMetrics.ProjectMarginTopWithHandlingPoints);
            page.MarginBottom(BrochurePrintLayoutMetrics.ProjectMarginBottomPoints);
            page.PageColor(Paper);
            page.DefaultTextStyle(style => style.FontFamily(fonts.PrimaryFamily).FontColor(Ink));

            if (!string.IsNullOrWhiteSpace(data.Options.HandlingMarking))
            {
                page.Header().Height(BrochurePrintLayoutMetrics.HandlingHeaderHeightPoints)
                    .AlignRight()
                    .Text(data.Options.HandlingMarking!.ToUpperInvariant())
                    .FontSize(6.1f)
                    .Bold()
                    .LetterSpacing(.5f)
                    .FontColor("#8A6817");
            }

            page.Content().Column(column =>
            {
                // Do not use Column.Spacing here. A global spacing rule also inserts hidden gaps
                // around the closing block, making the physical PDF taller than the planner model.
                // Every spacer is therefore explicit and shares the exact same metric as planning.
                for (var projectOffset = 0; projectOffset < sheet.Projects.Count; projectOffset++)
                {
                    if (projectOffset > 0)
                    {
                        column.Item().Height(
                            BrochurePrintLayoutMetrics.InterModuleSpacingPoints
                            + sheet.ExtraInterModuleSpacingPoints);
                    }

                    var plannedProject = sheet.Projects[projectOffset];
                    var project = data.Projects[plannedProject.ProjectIndex];
                    // ShowEntire must wrap the complete fixed-height card. Keeping it outside the
                    // height constraint makes a measurement mismatch fail as one atomic module
                    // instead of allowing a titleless continuation to leak onto another page.
                    column.Item()
                        .ShowEntire()
                        .Height(plannedProject.Measurement.TotalHeightPoints + sheet.ExtraModuleVerticalPaddingPoints)
                        .Element(module => ComposeProjectModule(
                            module,
                            project,
                            plannedProject.Measurement,
                            sheet.ExtraModuleVerticalPaddingPoints));
                }

                if (sheet.IncludesClosingMatter && sheet.ClosingHeightPoints > .5f)
                {
                    if (sheet.Projects.Count > 0)
                    {
                        column.Item().Height(BrochurePrintLayoutMetrics.ClosingGapPoints);
                    }

                    column.Item()
                        .ShowEntire()
                        .Height(sheet.ClosingHeightPoints)
                        .Element(block => ComposeClosingMatter(block, data));
                }
            });
        });
    }

    private static void ComposeProjectModule(
        IContainer container,
        BrochurePublicationProject project,
        BrochurePrintProjectMeasurement layout,
        float extraVerticalPaddingPoints)
    {
        container.Border(BrochurePrintLayoutMetrics.ModuleBorderPoints)
            .BorderColor(Forest700)
            .Background("#FBFBF8")
            .Column(column =>
            {
                column.Item().Height(layout.TitleHeightPoints)
                    .Background(Forest800)
                    .PaddingHorizontal(BrochurePrintLayoutMetrics.ModuleHorizontalPaddingPoints)
                    .AlignMiddle()
                    .Text(project.ProjectName.ToUpperInvariant())
                    .FontSize(layout.TitleFontSize)
                    .Bold()
                    .LineHeight(BrochurePrintLayoutMetrics.ProjectTitleLineHeight)
                    .AlignCenter()
                    .FontColor("#FFFFFF");

                column.Item()
                    .PaddingHorizontal(layout.BodyPaddingPoints)
                    .PaddingVertical(layout.BodyPaddingPoints + (extraVerticalPaddingPoints / 2f))
                    .Column(body =>
                {
                    var hasPrimary = project.PrimaryPhoto is not null;
                    var useSecond = layout.UsesSecondaryImage
                                    && project.SecondaryPhoto is not null;

                    if (!hasPrimary || !layout.UsesFloatLayout)
                    {
                        body.Item().Element(text => ComposeNarrativeText(
                            text,
                            project.Narrative,
                            layout.BodyFontSize,
                            layout.BodyLineHeight,
                            layout.ParagraphSpacingPoints,
                            justify: true));
                        return;
                    }

                    // Reference-style float: the image stack always occupies the upper-right. Only
                    // the leading narrative sits beside it; the remainder returns to full width.
                    body.Item().Row(row =>
                    {
                        row.RelativeItem().Element(text => ComposeNarrativeText(
                            text,
                            layout.LeadingNarrative,
                            layout.BodyFontSize,
                            layout.BodyLineHeight,
                            layout.ParagraphSpacingPoints,
                            justify: false));

                        row.ConstantItem(BrochurePrintLayoutMetrics.TextImageGapPoints);
                        row.ConstantItem(layout.ImageWidthPoints).AlignTop().Column(images =>
                        {
                            images.Item().Height(layout.PrimaryImageHeightPoints)
                                .Element(box => ComposeImage(box, project.PrimaryPhoto!.Content));

                            if (useSecond)
                            {
                                images.Item().Height(BrochurePrintLayoutMetrics.GalleryImageGapPoints);
                                images.Item().Height(layout.SecondaryImageHeightPoints)
                                    .Element(box => ComposeImage(box, project.SecondaryPhoto!.Content));
                            }
                        });
                    });

                    var hasContinuation = !string.IsNullOrWhiteSpace(layout.ContinuationNarrative);
                    if (hasContinuation)
                    {
                        body.Item().PaddingTop(layout.RemainderGapPoints)
                            .Element(text => ComposeNarrativeText(
                                text,
                                layout.ContinuationNarrative,
                                layout.BodyFontSize,
                                layout.BodyLineHeight,
                                layout.ParagraphSpacingPoints,
                                justify: false));
                    }

                    if (!string.IsNullOrWhiteSpace(layout.TrailingNarrative))
                    {
                        var trailing = body.Item();
                        if (!hasContinuation)
                        {
                            trailing = trailing.PaddingTop(layout.RemainderGapPoints);
                        }
                        trailing.Element(text => ComposeNarrativeText(
                            text,
                            layout.TrailingNarrative,
                            layout.BodyFontSize,
                            layout.BodyLineHeight,
                            layout.ParagraphSpacingPoints,
                            justify: true));
                    }
                });
            });
    }

    private static void ComposeNarrativeText(
        IContainer container,
        string? narrative,
        float fontSize,
        float lineHeight,
        float paragraphSpacingPoints,
        bool justify,
        bool italic = false)
    {
        var paragraphs = (narrative ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(paragraph => paragraph.Trim())
            .Where(paragraph => paragraph.Length > 0)
            .ToArray();

        if (paragraphs.Length == 0)
        {
            return;
        }

        container.Column(column =>
        {
            column.Spacing(Math.Max(0f, paragraphSpacingPoints));
            foreach (var paragraph in paragraphs)
            {
                if (justify && italic)
                {
                    column.Item().Text(paragraph)
                        .FontSize(fontSize)
                        .LineHeight(lineHeight)
                        .Justify()
                        .Italic()
                        .FontColor(Ink);
                }
                else if (justify)
                {
                    column.Item().Text(paragraph)
                        .FontSize(fontSize)
                        .LineHeight(lineHeight)
                        .Justify()
                        .FontColor(Ink);
                }
                else if (italic)
                {
                    column.Item().Text(paragraph)
                        .FontSize(fontSize)
                        .LineHeight(lineHeight)
                        .Italic()
                        .FontColor(Ink);
                }
                else
                {
                    column.Item().Text(paragraph)
                        .FontSize(fontSize)
                        .LineHeight(lineHeight)
                        .FontColor(Ink);
                }
            }
        });
    }

    private static void ComposeClosingMatter(IContainer container, BrochurePublicationData data)
    {
        container.PaddingTop(1).PaddingBottom(2).Column(column =>
        {
            column.Spacing(5);

            column.Item().Border(4.2f).BorderColor(VisionBlue).Background(VisionPaper).PaddingHorizontal(9).PaddingVertical(7).Column(vision =>
            {
                vision.Spacing(4);
                vision.Item().AlignCenter().Background(VisionBlue).PaddingHorizontal(8).PaddingVertical(2)
                    .Text("Visionary Horizons & Strategic Objectives")
                    .FontSize(BrochurePrintLayoutMetrics.ClosingVisionHeadingFontSize)
                    .Bold()
                    .Italic()
                    .AlignCenter()
                    .FontColor("#FFFFFF");
                vision.Item().Element(text => ComposeNarrativeText(
                    text,
                    data.Options.PrintVisionaryText,
                    BrochurePrintLayoutMetrics.ClosingVisionBodyFontSize,
                    BrochurePrintLayoutMetrics.ClosingVisionBodyLineHeight,
                    BrochurePrintLayoutMetrics.ClosingVisionParagraphSpacingPoints,
                    justify: true,
                    italic: true));
            });

            column.Item().Background(Forest800).PaddingHorizontal(8).PaddingVertical(7).Text(text =>
            {
                text.DefaultTextStyle(style => style
                    .FontSize(BrochurePrintLayoutMetrics.ClosingNewSimulatorsFontSize)
                    .LineHeight(BrochurePrintLayoutMetrics.ClosingNewSimulatorsLineHeight));
                text.Justify();
                text.Span("New Simulators. ").Bold().Italic().FontColor("#F4D66E");
                text.Span(data.Options.PrintNewSimulatorsText ?? string.Empty).SemiBold().FontColor("#FFFFFF");
            });
        });
    }

    private static void ComposeImage(IContainer container, byte[] image)
        => container
            .Border(.6f)
            .BorderColor("#71817B")
            .Background("#FFFFFF")
            .Image(image)
            .FitArea();
}
