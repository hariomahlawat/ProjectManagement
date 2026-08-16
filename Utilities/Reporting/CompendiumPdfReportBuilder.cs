using System.Globalization;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services.Compendiums;
using ProjectManagement.Utilities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProjectManagement.Utilities.Reporting;

public interface ICompendiumPdfReportBuilder
{
    byte[] Build(CompendiumPdfReportContext context);
}

public sealed record CompendiumPdfReportContext(
    string Title,
    string Subtitle,
    string UnitDisplayName,
    string IssuerDisplayName,
    string? HandlingMarking,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<CompendiumPdfCategorySection> Categories,
    bool ShowMissingPhotoPlaceholder)
{
    public string Edition { get; init; } = string.Empty;
    public byte[]? CoverHero { get; init; }
    public CompendiumPdfCoverDesign? CoverDesign { get; init; }
    public CompendiumPagePlan? Plan { get; init; }
}

public sealed record CompendiumPdfCoverImage(
    CompendiumCoverSurface Surface,
    string SlotKey,
    byte[]? Content,
    CompendiumImageFitMode FitMode,
    int? ProjectId = null,
    int? PhotoId = null);

public sealed record CompendiumPdfCoverDesign(
    CompendiumFrontCoverTemplate FrontTemplate,
    CompendiumBackCoverTemplate BackTemplate,
    IReadOnlyList<CompendiumPdfCoverImage> Images)
{
    public string? FrontTitle { get; init; }
    public string? FrontSubtitle { get; init; }
    public string? FrontEdition { get; init; }
    public string? FrontEyebrow { get; init; }
    public string? BackTitle { get; init; }
    public string? BackSubtitle { get; init; }
    public string? BackEdition { get; init; }
    public string? BackEyebrow { get; init; }
    public bool ShowFrontTitle { get; init; } = true;
    public bool ShowFrontSubtitle { get; init; } = true;
    public bool ShowFrontEdition { get; init; } = true;
    public bool ShowFrontLeftLogo { get; init; } = true;
    public bool ShowFrontRightLogo { get; init; } = true;
    public CompendiumCoverLogoPlacement FrontLogoPlacement { get; init; } = CompendiumCoverLogoPlacement.TopCorners;
    public bool ShowBackTitle { get; init; } = true;
    public bool ShowBackSubtitle { get; init; } = true;
    public bool ShowBackEdition { get; init; } = true;
    public bool ShowBackLeftLogo { get; init; } = true;
    public bool ShowBackRightLogo { get; init; } = true;
    public CompendiumCoverLogoPlacement BackLogoPlacement { get; init; } = CompendiumCoverLogoPlacement.TopCorners;
}

public sealed record CompendiumPdfCategorySection(
    string CategoryName,
    IReadOnlyList<CompendiumPdfProjectSection> Projects);

public sealed record CompendiumPdfProjectImage(
    CompendiumDossierImageRole Role,
    byte[]? Content,
    CompendiumImageFitMode FitMode,
    int? PhotoId);

public sealed record CompendiumPdfProjectSection(
    int ProjectId,
    string ProjectName,
    string? CaseFileNumber,
    string CategoryName,
    string CompletionYearDisplay,
    string SponsoringLineDirectorateDisplay,
    string ProliferationCostDisplay,
    string? ProliferationCostRemarks,
    string DescriptionMarkdown,
    byte[]? CoverPhoto,
    bool PhotoWasSelected)
{
    public string LifecycleDisplay { get; init; } = "Completed";
    public string? ProjectCategoryDisplay { get; init; }
    /// <summary>Authoritative project technical category. CategoryName is the publication section/group heading.</summary>
    public string TechnicalCategoryDisplay { get; init; } = string.Empty;
    public string NarrativeLabel { get; init; } = "Project Brief";
    public bool IsAvailableForProliferation { get; init; }
    public bool? ProliferationAvailability { get; init; }
    public CompendiumImageFitMode ImageFitMode { get; init; } = CompendiumImageFitMode.Fill;
    public CompendiumDossierLayout DossierLayoutRequested { get; init; } = CompendiumDossierLayout.Automatic;
    public CompendiumDossierLayout DossierLayout { get; init; } = CompendiumDossierLayout.Balanced;
    public string DossierLayoutReason { get; init; } = string.Empty;
    public float DossierPrimaryImageHeightPoints { get; init; } = 246f;
    public float DossierNarrativeFontScale { get; init; } = 1f;
    public int DossierFirstPageNarrativeBudget { get; init; } = 2200;
    public float DossierFirstPageNarrativeHeightPoints { get; init; } = 610f;
    public int DossierFirstPageSpecificationCount { get; init; } = 6;
    public int DossierSpecificationColumns { get; init; } = 1;
    public int DossierProgrammeColumns { get; init; } = 1;
    public CompendiumProjectParticularsStyle ProjectParticularsStyle { get; init; } = CompendiumProjectParticularsStyle.Panel;
    public CompendiumBalancedTextFlowMode BalancedTextFlowMode { get; init; } = CompendiumBalancedTextFlowMode.FlowBelowImage;
    public CompendiumNarrativeAlignment NarrativeAlignment { get; init; } = CompendiumNarrativeAlignment.Left;
    public CompendiumDossierNarrativeFlowPlan NarrativeFlow { get; init; } = CompendiumDossierNarrativeFlowPlan.Empty;
    public int EstimatedDossierPageCount { get; init; } = 1;
    public string DossierPaginationNote { get; init; } = "1 dossier page";
    public string DossierPaginationReason { get; init; } = string.Empty;
    public IReadOnlyList<CompendiumPdfProjectImage> Images { get; init; } = Array.Empty<CompendiumPdfProjectImage>();
    public IReadOnlyList<CompendiumProgrammeModuleDto> ProgrammeModules { get; init; } = Array.Empty<CompendiumProgrammeModuleDto>();
    public IReadOnlyList<CompendiumIprCredentialDto> IprCredentials { get; init; } = Array.Empty<CompendiumIprCredentialDto>();
    public CompendiumTechnologyTransferDto? TechnologyTransfer { get; init; }
    public string? AdditionalNote { get; init; }
    public IReadOnlyList<string> TechnicalSpecifications { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Phase 24 A4 portrait compositor. Page membership is decided by CompendiumPagePlanner before
/// drawing; this class renders that plan without changing project membership or pagination policy.
/// </summary>
public sealed class CompendiumPdfReportBuilder : ICompendiumPdfReportBuilder
{
    private const string Forest950 = "#102A23";
    private const string Forest900 = "#17382F";
    private const string Forest800 = "#205244";
    private const string Forest100 = "#EAF3EF";
    private const string Forest50 = "#F5F9F7";
    private const string Gold = "#C9A646";
    private const string GoldSoft = "#E9D9A7";
    private const string Ink = "#14221D";
    private const string Slate700 = "#334155";
    private const string Slate600 = "#475569";
    private const string Slate500 = "#64748B";
    private const string Slate300 = "#CBD5E1";
    private const string Slate200 = "#E2E8F0";
    private const string Slate100 = "#F1F5F9";
    private const string Slate50 = "#F8FAFC";
    private const string White = "#FFFFFF";
    private const float ProgrammeTopRuleHeight = 2.25f;

    private readonly IWebHostEnvironment _environment;
    private readonly IPublicationFontService _fontService;
    private readonly ILogger<CompendiumPdfReportBuilder> _logger;
    private static string s_primaryFontFamily = PublicationFontService.FallbackFamilyName;

    static CompendiumPdfReportBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public CompendiumPdfReportBuilder(
        IWebHostEnvironment environment,
        IPublicationFontService fontService,
        ILogger<CompendiumPdfReportBuilder> logger)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public byte[] Build(CompendiumPdfReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fontStatus = _fontService.EnsureRegistered();
        // The compositor is otherwise stateless/static. The publication font result is process-wide
        // and deterministic, so every page - cover, index, dossier and back cover - shares it.
        Volatile.Write(ref s_primaryFontFamily, fontStatus.PrimaryFamily);

        var title = NormalizeOptional(context.Title)
                    ?? throw new InvalidOperationException("A Compendium publication title is required before PDF generation.");
        var subtitle = NormalizeOptional(context.Subtitle) ?? string.Empty;
        var issuer = Normalize(context.IssuerDisplayName, "Simulator Development Division");
        var marking = NormalizeOptional(context.HandlingMarking)?.ToUpperInvariant();
        var edition = NormalizeOptional(context.Edition) ?? string.Empty;
        var crest = TryLoadAsset("img/logos/artrac.png");
        var sddMark = TryLoadAsset("img/logos/sdd.png");
        var footerLogo = sddMark;
        var programmeIcons = LoadProgrammeIcons();
        var plan = context.Plan ?? new CompendiumPagePlanner().Plan(context);

        var document = Document
            .Create(container =>
            {
                foreach (var planned in plan.Pages)
                {
                    switch (planned.Kind)
                    {
                        case CompendiumPageKind.Cover:
                            ComposeCover(container, title, subtitle, edition, marking, context.CoverDesign, context.CoverHero, crest, sddMark);
                            break;
                        case CompendiumPageKind.Index:
                            ComposeIndexPage(container, planned, title, edition, issuer, marking, footerLogo, plan.IndexPageCount);
                            break;
                        case CompendiumPageKind.Project:
                            ComposeProjectPage(container, planned, title, edition, issuer, marking, footerLogo, programmeIcons);
                            break;
                        case CompendiumPageKind.ProjectContinuation:
                            ComposeProjectContinuationPage(container, planned, title, edition, issuer, marking, footerLogo);
                            break;
                        case CompendiumPageKind.BackCover:
                            ComposeBackCover(container, title, subtitle, edition, marking, context.CoverDesign, crest, sddMark);
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported Compendium page kind: {planned.Kind}.");
                    }
                }
            })
            .WithMetadata(new DocumentMetadata
            {
                Title = $"{title} — {subtitle}",
                Author = issuer,
                Subject = "Detailed project reference generated from selected PRISM project records.",
                Keywords = "projects, simulators, capabilities, SDD, PRISM ERP",
                Creator = "PRISM ERP",
                Producer = "PRISM ERP / QuestPDF",
                CreationDate = context.GeneratedAtUtc,
                ModifiedDate = context.GeneratedAtUtc
            });

        return document.GeneratePdf();
    }

    private static void ComposeCover(
        IDocumentContainer container,
        string title,
        string subtitle,
        string edition,
        string? marking,
        CompendiumPdfCoverDesign? design,
        byte[]? legacyHero,
        byte[]? crest,
        byte[]? sddMark)
    {
        design ??= new CompendiumPdfCoverDesign(
            CompendiumFrontCoverTemplate.InstitutionalHero,
            CompendiumBackCoverTemplate.MinimalInstitutional,
            legacyHero is { Length: > 0 }
                ? new[] { new CompendiumPdfCoverImage(CompendiumCoverSurface.Front, "Hero", legacyHero, CompendiumImageFitMode.Fill) }
                : Array.Empty<CompendiumPdfCoverImage>());

        var frontImages = design.Images
            .Where(image => image.Surface == CompendiumCoverSurface.Front && image.Content is { Length: > 0 })
            .ToDictionary(image => image.SlotKey, StringComparer.OrdinalIgnoreCase);
        var hero = frontImages.GetValueOrDefault("Hero")?.Content ?? legacyHero;
        var secondary1 = frontImages.GetValueOrDefault("Secondary1")?.Content;
        var secondary2 = frontImages.GetValueOrDefault("Secondary2")?.Content;
        var secondary3 = frontImages.GetValueOrDefault("Secondary3")?.Content;

        var displayTitle = design.ShowFrontTitle ? NormalizeOptional(design.FrontTitle) ?? title : null;
        var displaySubtitle = design.ShowFrontSubtitle ? NormalizeOptional(design.FrontSubtitle) ?? subtitle : null;
        var displayEdition = design.ShowFrontEdition ? NormalizeOptional(design.FrontEdition) ?? edition : null;
        var eyebrow = NormalizeOptional(design.FrontEyebrow);

        switch (design.FrontTemplate)
        {
            case CompendiumFrontCoverTemplate.FullBleedHero:
                ComposeFullBleedCover(container, displayTitle, displaySubtitle, displayEdition, eyebrow, marking, hero, crest, sddMark,
                    design.FrontLogoPlacement, design.ShowFrontLeftLogo, design.ShowFrontRightLogo);
                break;
            case CompendiumFrontCoverTemplate.EditorialSplit:
                ComposeSplitCover(container, displayTitle, displaySubtitle, displayEdition, eyebrow, marking, hero, secondary1, crest, sddMark,
                    design.FrontLogoPlacement, design.ShowFrontLeftLogo, design.ShowFrontRightLogo);
                break;
            case CompendiumFrontCoverTemplate.Triptych:
                ComposeTriptychCover(container, displayTitle, displaySubtitle, displayEdition, eyebrow, marking, hero, secondary1, secondary2, crest, sddMark,
                    design.FrontLogoPlacement, design.ShowFrontLeftLogo, design.ShowFrontRightLogo);
                break;
            case CompendiumFrontCoverTemplate.PortfolioQuartet:
                ComposePortfolioQuartetCover(container, displayTitle, displaySubtitle, displayEdition, eyebrow, marking, hero, secondary1, secondary2, secondary3, crest, sddMark,
                    design.FrontLogoPlacement, design.ShowFrontLeftLogo, design.ShowFrontRightLogo);
                break;
            case CompendiumFrontCoverTemplate.Minimal:
                ComposeInstitutionalCover(container, displayTitle, displaySubtitle, displayEdition, eyebrow, marking, null, crest, sddMark,
                    design.FrontLogoPlacement, design.ShowFrontLeftLogo, design.ShowFrontRightLogo, showHeroFrame: false);
                break;
            default:
                ComposeInstitutionalCover(container, displayTitle, displaySubtitle, displayEdition, eyebrow, marking, hero, crest, sddMark,
                    design.FrontLogoPlacement, design.ShowFrontLeftLogo, design.ShowFrontRightLogo, showHeroFrame: true);
                break;
        }
    }

    private static void ComposeInstitutionalCover(
        IDocumentContainer container,
        string? title,
        string? subtitle,
        string? edition,
        string? eyebrow,
        string? marking,
        byte[]? hero,
        byte[]? crest,
        byte[]? sddMark,
        CompendiumCoverLogoPlacement logoPlacement,
        bool showLeftLogo,
        bool showRightLogo,
        bool showHeroFrame)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => BaseStyle(style).FontColor(White));
            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Background(Forest950).PaddingHorizontal(52).PaddingVertical(48).Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Element(row => ComposeCoverLogos(row, crest, sddMark, logoPlacement, showLeftLogo, showRightLogo));
                    if (!string.IsNullOrWhiteSpace(marking))
                    {
                        column.Item().PaddingTop(2).AlignCenter().Text(marking).FontSize(8.5f).SemiBold().LetterSpacing(.35f).FontColor(GoldSoft);
                    }
                    column.Item().PaddingTop(showHeroFrame ? 42 : 120).Element(identity => ComposeCoverIdentity(identity, eyebrow, title, subtitle, edition, 34));
                    if (showHeroFrame)
                    {
                        column.Item().PaddingTop(22).Element(frame => ComposeCoverHero(frame, hero));
                    }
                });
                layers.Layer().AlignTop().Height(6).Background(Gold);
                layers.Layer().AlignBottom().Height(16).Background(Forest900);
            });
        });
    }

    private static void ComposeFullBleedCover(
        IDocumentContainer container,
        string? title,
        string? subtitle,
        string? edition,
        string? eyebrow,
        string? marking,
        byte[]? hero,
        byte[]? crest,
        byte[]? sddMark,
        CompendiumCoverLogoPlacement logoPlacement,
        bool showLeftLogo,
        bool showRightLogo)
    {
        if (hero is not { Length: > 0 })
        {
            ComposeInstitutionalCover(container, title, subtitle, edition, eyebrow, marking, null, crest, sddMark, logoPlacement, showLeftLogo, showRightLogo, false);
            return;
        }

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => BaseStyle(style).FontColor(White));
            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Image(hero).FitArea();
                layers.Layer().AlignTop().PaddingHorizontal(44).PaddingTop(40).Element(row => ComposeCoverLogos(row, crest, sddMark, logoPlacement, showLeftLogo, showRightLogo));
                layers.Layer().AlignBottom().Height(315).Background(Forest950).PaddingHorizontal(52).PaddingTop(38).Column(column =>
                {
                    column.Item().Element(identity => ComposeCoverIdentity(identity, eyebrow, title, subtitle, edition, 33));
                    if (!string.IsNullOrWhiteSpace(marking))
                    {
                        column.Item().PaddingTop(18).Text(marking).FontSize(8.5f).SemiBold().LetterSpacing(.35f).FontColor(GoldSoft);
                    }
                });
                layers.Layer().AlignTop().Height(6).Background(Gold);
                layers.Layer().AlignBottom().Height(16).Background(Forest900);
            });
        });
    }

    private static void ComposeSplitCover(
        IDocumentContainer container,
        string? title,
        string? subtitle,
        string? edition,
        string? eyebrow,
        string? marking,
        byte[]? hero,
        byte[]? secondary,
        byte[]? crest,
        byte[]? sddMark,
        CompendiumCoverLogoPlacement logoPlacement,
        bool showLeftLogo,
        bool showRightLogo)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => BaseStyle(style).FontColor(White));
            page.Content().Column(column =>
            {
                column.Item().Height(355).Background(Forest950).PaddingHorizontal(52).PaddingTop(42).Column(top =>
                {
                    top.Item().Element(row => ComposeCoverLogos(row, crest, sddMark, logoPlacement, showLeftLogo, showRightLogo));
                    top.Item().PaddingTop(50).Element(identity => ComposeCoverIdentity(identity, eyebrow, title, subtitle, edition, 31));
                    if (!string.IsNullOrWhiteSpace(marking)) top.Item().PaddingTop(14).Text(marking).FontSize(8.2f).SemiBold().FontColor(GoldSoft);
                });
                column.Item().Height(471).Padding(0).Element(images =>
                {
                    var available = new[] { hero, secondary }.Where(image => image is { Length: > 0 }).ToArray();
                    if (available.Length <= 1)
                    {
                        ComposeCoverTile(images, available.FirstOrDefault());
                        return;
                    }
                    images.Row(row =>
                    {
                        row.RelativeItem(2).Element(cell => ComposeCoverTile(cell, available[0]));
                        row.ConstantItem(4).Background(Forest950);
                        row.RelativeItem(1).Element(cell => ComposeCoverTile(cell, available[1]));
                    });
                });
                column.Item().Height(16).Background(Forest900);
            });
        });
    }

    private static void ComposeTriptychCover(
        IDocumentContainer container,
        string? title,
        string? subtitle,
        string? edition,
        string? eyebrow,
        string? marking,
        byte[]? hero,
        byte[]? secondary1,
        byte[]? secondary2,
        byte[]? crest,
        byte[]? sddMark,
        CompendiumCoverLogoPlacement logoPlacement,
        bool showLeftLogo,
        bool showRightLogo)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => BaseStyle(style).FontColor(White));
            page.Content().Column(column =>
            {
                column.Item().Height(395).Background(Forest950).PaddingHorizontal(52).PaddingTop(42).Column(top =>
                {
                    top.Item().Element(row => ComposeCoverLogos(row, crest, sddMark, logoPlacement, showLeftLogo, showRightLogo));
                    top.Item().PaddingTop(48).Element(identity => ComposeCoverIdentity(identity, eyebrow, title, subtitle, edition, 29));
                    if (!string.IsNullOrWhiteSpace(marking)) top.Item().PaddingTop(12).Text(marking).FontSize(8.2f).SemiBold().FontColor(GoldSoft);
                });
                column.Item().Height(431).Element(images =>
                {
                    var available = new[] { hero, secondary1, secondary2 }.Where(image => image is { Length: > 0 }).ToArray();
                    if (available.Length <= 1)
                    {
                        ComposeCoverTile(images, available.FirstOrDefault());
                        return;
                    }
                    images.Row(row =>
                    {
                        for (var index = 0; index < available.Length; index++)
                        {
                            if (index > 0) row.ConstantItem(3).Background(Forest950);
                            row.RelativeItem().Element(cell => ComposeCoverTile(cell, available[index]));
                        }
                    });
                });
                column.Item().Height(16).Background(Forest900);
            });
        });
    }

    private static void ComposePortfolioQuartetCover(
        IDocumentContainer container,
        string? title, string? subtitle, string? edition, string? eyebrow, string? marking,
        byte[]? hero, byte[]? secondary1, byte[]? secondary2, byte[]? secondary3,
        byte[]? crest, byte[]? sddMark, CompendiumCoverLogoPlacement logoPlacement, bool showLeftLogo, bool showRightLogo)
    {
        var images = new[] { hero, secondary1, secondary2, secondary3 };
        if (images.Any(image => image is not { Length: > 0 }))
            throw new InvalidOperationException("Portfolio Quartet requires four rendered cover photographs.");

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => BaseStyle(style).FontColor(White));
            page.Content().Column(column =>
            {
                column.Item().Height(338).Background(Forest950).PaddingHorizontal(52).PaddingTop(42).Column(top =>
                {
                    top.Item().Element(row => ComposeCoverLogos(row, crest, sddMark, logoPlacement, showLeftLogo, showRightLogo));
                    top.Item().PaddingTop(38).Element(identity => ComposeCoverIdentity(identity, eyebrow, title, subtitle, edition, 28));
                    if (!string.IsNullOrWhiteSpace(marking)) top.Item().PaddingTop(10).Text(marking).FontSize(8.2f).SemiBold().FontColor(GoldSoft);
                });
                column.Item().Height(488).Row(row =>
                {
                    row.RelativeItem(2).Element(cell => ComposeCoverTile(cell, hero));
                    row.ConstantItem(4).Background(Forest950);
                    row.RelativeItem(1).Column(stack =>
                    {
                        stack.Item().Height(160).Element(cell => ComposeCoverTile(cell, secondary1));
                        stack.Item().Height(4).Background(Forest950);
                        stack.Item().Height(160).Element(cell => ComposeCoverTile(cell, secondary2));
                        stack.Item().Height(4).Background(Forest950);
                        stack.Item().Height(160).Element(cell => ComposeCoverTile(cell, secondary3));
                    });
                });
                column.Item().Height(16).Background(Forest900);
            });
        });
    }

    private static void ComposeCoverLogos(
        IContainer container,
        byte[]? crest,
        byte[]? sddMark,
        CompendiumCoverLogoPlacement placement,
        bool showLeft,
        bool showRight)
    {
        container.Row(row =>
        {
            if (placement == CompendiumCoverLogoPlacement.TopCenter)
            {
                row.RelativeItem();
                if (showLeft && crest is { Length: > 0 }) row.ConstantItem(44).Height(44).AlignMiddle().Image(crest).FitArea();
                if (showLeft && showRight && crest is { Length: > 0 } && sddMark is { Length: > 0 }) row.ConstantItem(20);
                if (showRight && sddMark is { Length: > 0 }) row.ConstantItem(48).Height(48).AlignMiddle().Image(sddMark).FitArea();
                row.RelativeItem();
                return;
            }

            if (showLeft && crest is { Length: > 0 }) row.ConstantItem(44).Height(44).AlignMiddle().Image(crest).FitArea();
            row.RelativeItem();
            if (showRight && sddMark is { Length: > 0 }) row.ConstantItem(48).Height(48).AlignMiddle().Image(sddMark).FitArea();
        });
    }

    private static void ComposeCoverIdentity(IContainer container, string? eyebrow, string? title, string? subtitle, string? edition, float titleSize)
    {
        container.Column(column =>
        {
            column.Spacing(10);
            if (!string.IsNullOrWhiteSpace(eyebrow))
            {
                column.Item().Text(eyebrow!.ToUpperInvariant()).FontSize(8).SemiBold().LetterSpacing(.26f).FontColor(GoldSoft);
            }
            if (!string.IsNullOrWhiteSpace(title))
            {
                column.Item().Text(title!).FontSize(titleSize).SemiBold().LineHeight(1.04f).FontColor(White);
                column.Item().Width(128).Height(3).Background(Gold);
            }
            if (!string.IsNullOrWhiteSpace(subtitle)) column.Item().Text(subtitle!).FontSize(14).LineHeight(1.18f).FontColor("#D7E3DE");
            if (!string.IsNullOrWhiteSpace(edition)) column.Item().Text(edition!).FontSize(10.5f).SemiBold().FontColor(GoldSoft);
        });
    }

    private static void ComposeCoverTile(IContainer container, byte[]? image)
    {
        if (image is { Length: > 0 })
        {
            container.Background(Forest900).Image(image).FitArea();
            return;
        }
        container.Background(Forest900).Layers(layers =>
        {
            layers.PrimaryLayer().Background(Forest900);
            layers.Layer().AlignBottom().Height(72).Background(Forest800);
        });
    }

    private static void ComposeCoverHero(IContainer container, byte[]? hero)
    {
        if (hero is { Length: > 0 })
        {
            container.Height((float)CompendiumCoverImagePolicy.FrameHeightPoints)
                .Background(Forest900)
                .Image(hero)
                .FitArea();
            return;
        }

        container.Height((float)CompendiumCoverImagePolicy.FrameHeightPoints).Layers(layers =>
        {
            layers.PrimaryLayer().Background(Forest900);
            layers.Layer().AlignTop().Height(3).Background(Gold);
            layers.Layer().AlignBottom().Height(86).Background(Forest800);
        });
    }

    private static void ComposeIndexPage(
        IDocumentContainer container,
        CompendiumPagePlanItem planned,
        string title,
        string edition,
        string issuer,
        string? marking,
        byte[]? footerLogo,
        int totalIndexPages)
    {
        container.Page(page =>
        {
            ConfigureStandardPage(page);
            page.Header().Element(header => ComposeRunningHeader(header, "SDD SIMULATORS COMPENDIUM", edition, marking));
            page.Content().PaddingTop(12).Column(content =>
            {
                content.Spacing(12);
                content.Item().Row(row =>
                {
                    row.RelativeItem().Column(copy =>
                    {
                        copy.Item().Text("Compendium Index")
                            .FontSize(22)
                            .SemiBold()
                            .FontColor(Ink);
                        copy.Item().Text(title)
                            .FontSize(9.5f)
                            .FontColor(Slate500);
                    });
                    if (totalIndexPages > 1)
                    {
                        row.AutoItem().AlignBottom().Text($"Index page {planned.PhysicalPageNumber - 1} of {totalIndexPages}")
                            .FontSize(8)
                            .FontColor(Slate500);
                    }
                });

                content.Item().Height(2).Background(Gold);

                var showGroupHeadings = !(planned.IndexGroups.Count == 1
                    && string.Equals(planned.IndexGroups[0].CategoryName, "Projects", StringComparison.OrdinalIgnoreCase));
                foreach (var group in planned.IndexGroups)
                {
                    content.Item().Element(element => ComposeIndexGroup(element, group, showGroupHeadings));
                }
            });
            page.Footer().Element(footer => ComposeFooter(footer, issuer, marking, footerLogo));
        });
    }

    private static void ComposeIndexGroup(IContainer container, CompendiumIndexGroupPlan group, bool showHeading = true)
    {
        container.Column(column =>
        {
            column.Spacing(0);
            if (showHeading)
            {
                column.Item().Background(Forest100).BorderLeft(4).BorderColor(Forest800).PaddingHorizontal(10).PaddingVertical(7)
                    .Row(row =>
                    {
                        row.RelativeItem().Text(group.CategoryName)
                            .FontSize(11.5f)
                            .SemiBold()
                            .FontColor(Forest900);
                        row.AutoItem().Text($"{group.Projects.Count} project{(group.Projects.Count == 1 ? string.Empty : "s")}")
                            .FontSize(8)
                            .FontColor(Slate500);
                    });
            }

            foreach (var project in group.Projects)
            {
                column.Item().BorderBottom(1).BorderColor(Slate200).PaddingHorizontal(10).PaddingVertical(6).Row(row =>
                {
                    row.RelativeItem().SectionLink(ProjectAnchorId(project.ProjectId)).Text(project.ProjectName)
                        .FontSize(9.3f)
                        .FontColor(Ink);
                    row.ConstantItem(76).AlignRight().Text(
                            string.Equals(project.LifecycleDisplay, "Completed", StringComparison.OrdinalIgnoreCase)
                                ? project.CompletionDisplay
                                : project.LifecycleDisplay)
                        .FontSize(8.5f)
                        .FontColor(Slate500);
                    row.ConstantItem(34).AlignRight().Text(project.ProjectPageNumber.ToString(CultureInfo.InvariantCulture))
                        .FontSize(9)
                        .SemiBold()
                        .FontColor(Forest800);
                });
            }
        });
    }

    private static void ComposeProjectPage(
        IDocumentContainer container,
        CompendiumPagePlanItem planned,
        string publicationTitle,
        string edition,
        string issuer,
        string? marking,
        byte[]? footerLogo,
        IReadOnlyDictionary<string, string> programmeIcons)
    {
        var project = planned.Project ?? throw new InvalidOperationException("Project page is missing its project payload.");
        var narrativeLabel = NormalizeNarrativeLabel(project.NarrativeLabel);
        var publicationKicker = ResolveProjectKicker(project);

        container.Page(page =>
        {
            ConfigureStandardPage(page);
            page.Header().Element(header => ComposeRunningHeader(header, publicationTitle.ToUpperInvariant(), edition, marking));
            page.Content().PaddingTop(8).Section(ProjectAnchorId(project.ProjectId)).Column(column =>
            {
                column.Spacing(9);
                column.Item().Text(publicationKicker.ToUpperInvariant())
                    .FontSize(7.3f).SemiBold().LetterSpacing(.5f).FontColor(Forest800);
                column.Item().Height(2).Width(58).Background(Gold);
                column.Item().Text(project.ProjectName)
                    .FontSize(ResolveProjectTitleFontSize(project.ProjectName)).SemiBold().LineHeight(1.08f).FontColor(Ink);


                column.Item().Element(main => ComposeAdaptiveDossierMain(main, project, planned.DescriptionMarkdown, narrativeLabel));
                column.Item().Element(programme => ComposeProgrammeInformation(programme, project, programmeIcons));

                if (planned.TechnicalSpecifications.Count > 0)
                {
                    column.Item().Element(specs => ComposeTechnicalSpecifications(specs, planned.TechnicalSpecifications, project.DossierSpecificationColumns));
                }

                if (!string.IsNullOrWhiteSpace(planned.AdditionalNoteMarkdown))
                {
                    column.Item().Element(note => ComposeAdditionalNote(note, planned.AdditionalNoteMarkdown, project.NarrativeAlignment, project.DossierNarrativeFontScale));
                }
            });
            page.Footer().Element(footer => ComposeFooter(footer, issuer, marking, footerLogo));
        });
    }

    private static void ComposeProjectContinuationPage(
        IDocumentContainer container,
        CompendiumPagePlanItem planned,
        string publicationTitle,
        string edition,
        string issuer,
        string? marking,
        byte[]? footerLogo)
    {
        var project = planned.Project ?? throw new InvalidOperationException("Continuation page is missing its project payload.");
        var narrativeLabel = NormalizeNarrativeLabel(project.NarrativeLabel);
        container.Page(page =>
        {
            ConfigureStandardPage(page);
            page.Header().Element(header => ComposeRunningHeader(header, publicationTitle.ToUpperInvariant(), edition, marking));
            page.Content().PaddingTop(12).Column(column =>
            {
                column.Spacing(10);
                column.Item().Text(project.ProjectName)
                    .FontSize(17).SemiBold().LineHeight(1.06f).FontColor(Ink);
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text(planned.IsAdditionalNoteContinuation
                            ? "ADDITIONAL NOTE"
                            : planned.IsTechnicalContinuation ? "TECHNICAL REFERENCE" : narrativeLabel.ToUpperInvariant())
                        .FontSize(8.4f).SemiBold().LetterSpacing(.28f).FontColor(Forest800);
                    row.AutoItem().Text($"CONTINUED · PART {planned.ContinuationPart + 1}")
                        .FontSize(7.2f).SemiBold().LetterSpacing(.26f).FontColor(Slate500);
                });
                column.Item().Height(2).Background(Gold);

                if (!string.IsNullOrWhiteSpace(planned.DescriptionMarkdown))
                {
                    column.Item().Element(description => ComposeDescription(
                        description,
                        planned.DescriptionMarkdown,
                        narrativeLabel,
                        continuation: true,
                        narrativeAlignment: project.NarrativeAlignment));
                }

                if (planned.TechnicalSpecifications.Count > 0)
                {
                    column.Item().Element(specs => ComposeTechnicalSpecifications(specs, planned.TechnicalSpecifications, project.DossierSpecificationColumns));
                }

                if (!string.IsNullOrWhiteSpace(planned.AdditionalNoteMarkdown))
                {
                    column.Item().Element(note => ComposeAdditionalNote(note, planned.AdditionalNoteMarkdown, project.NarrativeAlignment, project.DossierNarrativeFontScale, showHeading: !planned.IsAdditionalNoteContinuation));
                }
            });
            page.Footer().Element(footer => ComposeFooter(footer, issuer, marking, footerLogo));
        });
    }

    private static float ResolveProjectTitleFontSize(string? title)
    {
        var length = title?.Trim().Length ?? 0;
        return length switch
        {
            > 105 => 17.5f,
            > 76 => 19f,
            > 54 => 20.5f,
            _ => 22f
        };
    }

    private static void ComposeAdaptiveDossierMain(
        IContainer container,
        CompendiumPdfProjectSection project,
        string narrative,
        string narrativeLabel)
    {
        var images = project.Images.Where(image => image.Content is { Length: > 0 }).ToArray();
        var primaryImage = images.FirstOrDefault(image => image.Role == CompendiumDossierImageRole.Primary);
        var primaryBytes = primaryImage?.Content ?? project.CoverPhoto;
        var primaryFit = primaryImage?.FitMode ?? project.ImageFitMode;
        var imageHeight = Math.Max(1f, project.DossierPrimaryImageHeightPoints);

        switch (project.DossierLayout)
        {
            case CompendiumDossierLayout.VisualHero:
                container.Column(column =>
                {
                    column.Spacing(9);
                    if (primaryBytes is { Length: > 0 })
                        column.Item().Element(frame => ComposeDossierImage(frame, primaryBytes, imageHeight, primaryFit));
                    column.Item().Element(text => ComposeDescription(text, narrative, narrativeLabel, false, project.DossierNarrativeFontScale, narrativeAlignment: project.NarrativeAlignment));
                });
                return;

            case CompendiumDossierLayout.MultiImageEditorial when images.Length >= 2:
                container.Column(column =>
                {
                    column.Spacing(9);
                    column.Item().Element(mosaic => ComposeDossierMosaic(mosaic, images, imageHeight));
                    column.Item().Element(text => ComposeDescription(text, narrative, narrativeLabel, false, project.DossierNarrativeFontScale, narrativeAlignment: project.NarrativeAlignment));
                });
                return;

            case CompendiumDossierLayout.Technical:
                container.Column(column =>
                {
                    column.Spacing(8);
                    if (primaryBytes is { Length: > 0 })
                        column.Item().Element(frame => ComposeDossierImage(frame, primaryBytes, imageHeight, primaryFit));
                    column.Item().Element(text => ComposeDescription(text, narrative, narrativeLabel, false, project.DossierNarrativeFontScale, narrativeAlignment: project.NarrativeAlignment));
                });
                return;
        }

        if (primaryBytes is not { Length: > 0 })
        {
            container.Element(text => ComposeDescription(text, narrative, narrativeLabel, false, project.DossierNarrativeFontScale, narrativeAlignment: project.NarrativeAlignment));
            return;
        }

        var flow = project.NarrativeFlow;
        if (project.BalancedTextFlowMode == CompendiumBalancedTextFlowMode.FlowBelowImage)
        {
            container.Column(column =>
            {
                column.Spacing(8);
                column.Item().Row(row =>
                {
                    row.RelativeItem(1.12f).Element(frame => ComposeDossierImage(frame, primaryBytes, imageHeight, primaryFit));
                    row.ConstantItem(13);
                    row.RelativeItem(.88f).Element(text => ComposeDescription(
                        text, flow.SideSegment, narrativeLabel, false, project.DossierNarrativeFontScale,
                        narrativeAlignment: flow.SideAlignment));
                });
                if (!string.IsNullOrWhiteSpace(flow.BelowImageSegment))
                    column.Item().Element(text => ComposeDescription(
                        text, flow.BelowImageSegment, narrativeLabel, false, project.DossierNarrativeFontScale,
                        showHeading: false, narrativeAlignment: flow.BelowAlignment));
            });
            return;
        }

        container.Row(row =>
        {
            row.RelativeItem(1.12f).Element(frame => ComposeDossierImage(frame, primaryBytes, imageHeight, primaryFit));
            row.ConstantItem(13);
            row.RelativeItem(.88f).Element(text => ComposeDescription(
                text, narrative, narrativeLabel, false, project.DossierNarrativeFontScale,
                narrativeAlignment: flow.SideAlignment));
        });
    }

    private static void ComposeDossierImage(IContainer container, byte[] image, float height, CompendiumImageFitMode fitMode)
    {
        if (fitMode == CompendiumImageFitMode.Fit)
        {
            container.Height(height).AlignCenter().AlignMiddle().Image(image).FitArea();
            return;
        }

        container.Height(height).Layers(layers =>
        {
            layers.PrimaryLayer().Image(image).FitArea();
            layers.Layer().AlignBottom().Height(2).Background(Gold);
        });
    }

    private static void ComposeDossierMosaic(
        IContainer container,
        IReadOnlyList<CompendiumPdfProjectImage> images,
        float height)
    {
        var available = images.Where(image => image.Content is { Length: > 0 }).Take(3).ToArray();
        if (available.Length == 0) return;
        height = Math.Max(120f, height);
        if (available.Length == 1)
        {
            ComposeDossierImage(container, available[0].Content!, height, available[0].FitMode);
            return;
        }

        container.Height(height).Row(row =>
        {
            row.RelativeItem(1.55f).Element(frame => ComposeDossierImage(frame, available[0].Content!, height, available[0].FitMode));
            row.ConstantItem(7);
            row.RelativeItem(1f).Column(column =>
            {
                column.Spacing(7);
                var secondaryHeight = available.Length >= 3 ? Math.Max(52f, (height - 7f) / 2f) : height;
                column.Item().Element(frame => ComposeDossierImage(frame, available[1].Content!, secondaryHeight, available[1].FitMode));
                if (available.Length >= 3)
                    column.Item().Element(frame => ComposeDossierImage(frame, available[2].Content!, secondaryHeight, available[2].FitMode));
            });
        });
    }

    private static void ComposeProgrammeInformation(
        IContainer container,
        CompendiumPdfProjectSection project,
        IReadOnlyDictionary<string, string> programmeIcons)
    {
        var modules = project.ProgrammeModules.Count > 0
            ? project.ProgrammeModules
            : CompendiumProgrammeInformation.Resolve(
                project.SponsoringLineDirectorateDisplay,
                project.ProliferationCostDisplay,
                project.IprCredentials,
                project.TechnologyTransfer);
        if (modules.Count == 0) return;

        if (CompendiumProjectParticularsLayoutPolicy.Normalize(project.ProjectParticularsStyle)
            == CompendiumProjectParticularsStyle.Minimal)
        {
            ComposeProjectParticularsMinimal(container, project, modules, programmeIcons);
            return;
        }

        ComposeProjectParticularsPanel(container, project, modules, programmeIcons);
    }

    private static void ComposeProjectParticularsPanel(
        IContainer container,
        CompendiumPdfProjectSection project,
        IReadOnlyList<CompendiumProgrammeModuleDto> modules,
        IReadOnlyDictionary<string, string> programmeIcons)
    {
        var programmeColumns = Math.Clamp(project.DossierProgrammeColumns, 1, 3);
        var labelFontSize = programmeColumns switch
        {
            >= 3 => 6.05f,
            2 => 6.3f,
            _ => 6.5f
        };
        var labelLetterSpacing = programmeColumns switch
        {
            >= 3 => .08f,
            2 => .12f,
            _ => .16f
        };

        var useHalfWidthSingleModule = modules.Count == 1
                                       && IsCompactSingleProgrammeModule(modules[0]);
        var panelPaddingVertical = modules.Count switch
        {
            1 => 5f,
            2 or 3 => 6f,
            _ => 7f
        };
        var panelSpacing = modules.Count == 1 ? 4f : 6f;

        container.Background(Forest50).Border(1).BorderColor("#D8E5DF").Padding(0).Column(column =>
        {
            column.Item().Height(ProgrammeTopRuleHeight).Background(Forest800);
            column.Item().PaddingHorizontal(10).PaddingVertical(panelPaddingVertical).Column(content =>
            {
                content.Spacing(panelSpacing);
                content.Item().Text("PROJECT PARTICULARS")
                    .FontSize(7.2f).SemiBold().LetterSpacing(.32f).FontColor(Forest800);

                foreach (var rowModules in modules.Chunk(programmeColumns))
                {
                    content.Item().Row(row =>
                    {
                        foreach (var module in rowModules)
                        {
                            row.RelativeItem().PaddingRight(8).Row(cell =>
                            {
                                cell.ConstantItem(22).Height(22).AlignMiddle().Element(iconTile =>
                                    ComposeProgrammeIcon(iconTile, module, programmeIcons));
                                cell.ConstantItem(7);

                                cell.RelativeItem().Column(text =>
                                {
                                    text.Item().Text(module.Label.ToUpperInvariant())
                                        .FontSize(labelFontSize)
                                        .SemiBold()
                                        .LetterSpacing(labelLetterSpacing)
                                        .FontColor(Slate500);
                                    text.Item().PaddingTop(2).Text(module.Value)
                                        .FontSize(9.1f)
                                        .SemiBold()
                                        .FontColor(Ink)
                                        .LineHeight(1.08f);
                                });
                            });
                        }

                        if (useHalfWidthSingleModule)
                        {
                            row.RelativeItem();
                        }
                    });
                }
            });
        });
    }

    private static void ComposeProjectParticularsMinimal(
        IContainer container,
        CompendiumPdfProjectSection project,
        IReadOnlyList<CompendiumProgrammeModuleDto> modules,
        IReadOnlyDictionary<string, string> programmeIcons)
    {
        var programmeColumns = Math.Clamp(project.DossierProgrammeColumns, 1, Math.Min(4, modules.Count));
        var labelFontSize = programmeColumns switch
        {
            >= 4 => 5.55f,
            3 => 5.75f,
            _ => 6f
        };
        var valueFontSize = programmeColumns >= 4 ? 8.4f : 8.8f;
        var useHalfWidthSingleModule = modules.Count == 1 && IsCompactSingleProgrammeModule(modules[0]);

        container.Column(column =>
        {
            column.Spacing(7f);
            column.Item().Row(header =>
            {
                header.AutoItem().Text("PROJECT PARTICULARS")
                    .FontSize(7.2f).SemiBold().LetterSpacing(.32f).FontColor(Forest800);
                header.ConstantItem(9f);
                header.RelativeItem().PaddingTop(4.4f).Height(1f).Background(GoldSoft);
            });

            foreach (var rowModules in modules.Chunk(programmeColumns))
            {
                column.Item().Row(row =>
                {
                    foreach (var module in rowModules)
                    {
                        row.RelativeItem().PaddingRight(programmeColumns >= 4 ? 5f : 10f).Row(cell =>
                        {
                            cell.ConstantItem(19).Height(19).AlignMiddle().Element(iconTile =>
                                ComposeProgrammeIcon(iconTile, module, programmeIcons));
                            cell.ConstantItem(6);
                            cell.RelativeItem().Column(text =>
                            {
                                text.Item().Text(module.Label.ToUpperInvariant())
                                    .FontSize(labelFontSize)
                                    .SemiBold()
                                    .LetterSpacing(programmeColumns >= 4 ? .04f : .1f)
                                    .FontColor(Slate500);
                                text.Item().PaddingTop(1.6f).Text(module.Value)
                                    .FontSize(valueFontSize)
                                    .SemiBold()
                                    .FontColor(Ink)
                                    .LineHeight(1.08f);
                            });
                        });
                    }

                    var missing = programmeColumns - rowModules.Length;
                    for (var i = 0; i < missing; i++) row.RelativeItem();
                    if (useHalfWidthSingleModule) row.RelativeItem();
                });
            }
        });
    }

    private static bool IsCompactSingleProgrammeModule(CompendiumProgrammeModuleDto module)
        => !module.Value.Contains('\n') && module.Value.Trim().Length <= 48;

    private static void ComposeProgrammeIcon(
        IContainer container,
        CompendiumProgrammeModuleDto module,
        IReadOnlyDictionary<string, string> programmeIcons)
    {
        var fallbackColor = module.Tone.ToLowerInvariant() switch
        {
            "maroon" => "#8B3A3A",
            "green" => "#27825B",
            "blue" => "#3275C7",
            _ => "#A97712"
        };

        // The programme panel already supplies the visual container. Keeping the icon column
        // unboxed avoids nested card furniture and gives every coloured symbol a clean 18-point field.
        container.Padding(2).Element(icon =>
        {
            if (programmeIcons.TryGetValue(module.IconKey, out var svg) && !string.IsNullOrWhiteSpace(svg))
            {
                icon.Svg(svg).FitArea();
                return;
            }

            icon.AlignCenter().AlignMiddle().Text(module.Kind switch
                {
                    CompendiumProgrammeModuleKind.ArmsServices => "A/S",
                    CompendiumProgrammeModuleKind.ProliferationCost => "₹",
                    CompendiumProgrammeModuleKind.TechnologyTransfer => "↔",
                    _ => "IPR"
                })
                .FontSize(6.2f)
                .SemiBold()
                .FontColor(fallbackColor);
        });
    }

    private static void ComposeTechnicalSpecifications(IContainer container, IReadOnlyList<string> specifications, int plannedColumns)
    {
        var items = specifications.Where(item => !string.IsNullOrWhiteSpace(item)).Take(6).ToArray();
        if (items.Length == 0) return;
        var columns = Math.Clamp(plannedColumns, 1, 3);

        container.Background(White).Column(column =>
        {
            column.Spacing(6);
            column.Item().Row(header =>
            {
                header.AutoItem().Text("HARDWARE / TECHNICAL SPECIFICATION")
                    .FontSize(7.7f).SemiBold().LetterSpacing(.14f).FontColor(Forest800);
                header.ConstantItem(10f);
                header.RelativeItem().PaddingTop(4.6f).Height(1f).Background(GoldSoft);
            });

            if (columns == 1)
            {
                foreach (var item in items)
                    column.Item().Element(cell => ComposeSpecificationBullet(cell, item));
            }
            else
            {
                foreach (var group in items.Chunk(columns))
                {
                    column.Item().Row(row =>
                    {
                        foreach (var item in group)
                        {
                            row.RelativeItem().PaddingRight(10).Element(cell => ComposeSpecificationBullet(cell, item));
                        }
                    });
                }
            }
        });
    }

    private static void ComposeSpecificationBullet(IContainer container, string text)
        => container.Row(row =>
        {
            row.ConstantItem(13).Text("•").FontSize(10.2f).FontColor(Gold);
            row.RelativeItem().Text(text).FontSize(8.75f).FontColor(Slate700).LineHeight(1.22f);
        });

    private static void ComposeAdditionalNote(
        IContainer container,
        string noteMarkdown,
        CompendiumNarrativeAlignment alignment,
        float narrativeFontScale,
        bool showHeading = true)
    {
        if (string.IsNullOrWhiteSpace(noteMarkdown)) return;
        container.Column(column =>
        {
            column.Spacing(5);
            if (showHeading)
            {
                column.Item().Row(row =>
                {
                    row.AutoItem().Text("ADDITIONAL NOTE")
                        .FontSize(7.4f).SemiBold().LetterSpacing(.34f).FontColor(Forest800);
                    row.ConstantItem(10f);
                    row.RelativeItem().PaddingTop(4.4f).Height(1f).Background(GoldSoft);
                });
            }
            column.Item().Element(text => ComposeDescription(
                text,
                noteMarkdown,
                "Additional Note",
                continuation: false,
                narrativeFontScale: narrativeFontScale,
                showHeading: false,
                narrativeAlignment: alignment,
                allowMinorHeadings: false));
        });
    }

    private static void ComposeProjectMetadata(IContainer container, CompendiumPdfProjectSection project)
    {
        var items = new List<(string Key, string Value, bool Emphasize)>();
        if (!string.IsNullOrWhiteSpace(project.ProjectCategoryDisplay))
        {
            items.Add(("Project category", project.ProjectCategoryDisplay!, false));
        }

        var technicalCategory = string.IsNullOrWhiteSpace(project.TechnicalCategoryDisplay)
            ? project.CategoryName
            : project.TechnicalCategoryDisplay;
        items.Add(("Technical category", technicalCategory, false));

        if (string.Equals(project.LifecycleDisplay, "Completed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(project.CompletionYearDisplay)
            && !string.Equals(project.CompletionYearDisplay, "Not recorded", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(("Completed", project.CompletionYearDisplay, true));
        }
        if (!string.Equals(project.SponsoringLineDirectorateDisplay, "Not recorded", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(("Arms / Services", project.SponsoringLineDirectorateDisplay, false));
        }
        if (project.ProliferationAvailability.HasValue)
        {
            items.Add(("Proliferation", project.ProliferationAvailability.Value ? "Available" : "Not available", false));
        }
        if (project.ProliferationAvailability == true
            && !string.IsNullOrWhiteSpace(project.ProliferationCostDisplay)
            && !string.Equals(project.ProliferationCostDisplay, "Not recorded", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(("Indicative cost", project.ProliferationCostDisplay, true));
        }

        container.Background(Forest50).Border(1).BorderColor("#D8E5DF").Padding(0).Column(column =>
        {
            column.Item().Height(3).Background(Forest800);
            column.Item().PaddingHorizontal(11).PaddingVertical(9).Column(grid =>
            {
                grid.Spacing(8);
                foreach (var rowItems in BuildMetadataRows(items))
                {
                    grid.Item().Row(row =>
                    {
                        for (var index = 0; index < rowItems.Count; index++)
                        {
                            if (index > 0) row.ConstantItem(14);
                            var item = rowItems[index];
                            row.RelativeItem().Element(cell => ComposeMetadataCell(cell, item.Key, item.Value, item.Emphasize));
                        }
                    });
                }

                if (!string.IsNullOrWhiteSpace(project.ProliferationCostRemarks))
                {
                    grid.Item().BorderTop(1).BorderColor("#D8E5DF").PaddingTop(6)
                        .Text(project.ProliferationCostRemarks!)
                        .FontSize(8.2f)
                        .FontColor(Slate600)
                        .LineHeight(1.18f);
                }
            });
        });
    }

    private static IReadOnlyList<IReadOnlyList<(string Key, string Value, bool Emphasize)>> BuildMetadataRows(
        IReadOnlyList<(string Key, string Value, bool Emphasize)> items)
    {
        if (items.Count == 0)
        {
            return Array.Empty<IReadOnlyList<(string Key, string Value, bool Emphasize)>>();
        }

        var rowSizes = items.Count switch
        {
            1 => new[] { 1 },
            2 => new[] { 2 },
            3 => new[] { 3 },
            4 => new[] { 2, 2 },
            5 => new[] { 3, 2 },
            _ => Enumerable.Repeat(3, (items.Count + 2) / 3).ToArray()
        };

        var rows = new List<IReadOnlyList<(string Key, string Value, bool Emphasize)>>();
        var offset = 0;
        foreach (var requestedSize in rowSizes)
        {
            if (offset >= items.Count) break;
            var size = Math.Min(requestedSize, items.Count - offset);
            rows.Add(items.Skip(offset).Take(size).ToArray());
            offset += size;
        }
        return rows;
    }

    private static void ComposeMetadataCell(IContainer container, string key, string value, bool emphasize)
    {
        container.MinHeight(27).Column(column =>
        {
            column.Item().Text(key.ToUpperInvariant())
                .FontSize(6.7f)
                .SemiBold()
                .LetterSpacing(.18f)
                .FontColor(Slate500);
            if (emphasize)
            {
                column.Item().Text(value).FontSize(10f).SemiBold().FontColor(Forest900);
            }
            else
            {
                column.Item().Text(value).FontSize(8.9f).FontColor(Ink);
            }
        });
    }

    private static void ComposeProjectImage(IContainer container, byte[] photo, CompendiumProjectLayoutVariant layout)
    {
        var height = CompendiumLayoutMetrics.ProjectImageHeightPoints(layout);
        container.Border(1).BorderColor(Slate200).Background(Slate50).Padding(3).Layers(layers =>
        {
            layers.PrimaryLayer().Height(height).Image(photo).FitArea();
            layers.Layer().AlignBottom().Height(3).Background(Gold);
        });
    }

    private static void ComposeNoPhotoTreatment(IContainer container, CompendiumPdfProjectSection project)
    {
        var technicalCategory = string.IsNullOrWhiteSpace(project.TechnicalCategoryDisplay)
            ? project.CategoryName
            : project.TechnicalCategoryDisplay;

        // A missing photograph is treated as an intentional text-led dossier. No authoring or
        // diagnostic language is printed into the issued publication.
        container.Height(94).Background(Forest100).Border(1).BorderColor("#D7E7DF").Padding(0).Row(row =>
        {
            row.ConstantItem(7).Background(Gold);
            row.RelativeItem().PaddingHorizontal(17).PaddingVertical(13).Column(column =>
            {
                column.Item().Text("CAPABILITY DOSSIER")
                    .FontSize(7)
                    .SemiBold()
                    .LetterSpacing(.45f)
                    .FontColor(Slate500);
                column.Item().PaddingTop(5).Text(technicalCategory.ToUpperInvariant())
                    .FontSize(16)
                    .SemiBold()
                    .FontColor(Forest950);
                column.Item().PaddingTop(5).Text(string.IsNullOrWhiteSpace(project.CaseFileNumber)
                        ? "PRISM · DETAILED PROJECT REFERENCE"
                        : $"PROJECT REFERENCE · {project.CaseFileNumber}")
                    .FontSize(7.4f)
                    .SemiBold()
                    .LetterSpacing(.14f)
                    .FontColor(Slate500);
            });
        });
    }

    private static void ComposeDescription(
        IContainer container,
        string markdown,
        string narrativeLabel,
        bool continuation,
        float narrativeFontScale = 1f,
        bool showHeading = true,
        CompendiumNarrativeAlignment narrativeAlignment = CompendiumNarrativeAlignment.Left,
        bool allowMinorHeadings = true)
    {
        narrativeLabel = NormalizeNarrativeLabel(narrativeLabel);
        narrativeFontScale = continuation ? 1f : CompendiumNarrativeTypographyPolicy.NormalizeScale(narrativeFontScale);
        container.Column(column =>
        {
            column.Spacing(7);
            if (!continuation && showHeading)
            {
                column.Item().Row(row =>
                {
                    row.AutoItem().Text(narrativeLabel.ToUpperInvariant())
                        .FontSize(8.6f)
                        .SemiBold()
                        .LetterSpacing(.14f)
                        .FontColor(Forest900);
                    row.RelativeItem().PaddingLeft(10).AlignMiddle().Height(1).Background(GoldSoft);
                });
            }

            if (string.IsNullOrWhiteSpace(markdown))
            {
                column.Item().Text($"{narrativeLabel} not recorded.")
                    .FontSize(CompendiumLayoutMetrics.ProjectBodyFontSize * narrativeFontScale)
                    .Italic()
                    .FontColor(Slate500);
                return;
            }

            var bodyFontSize = continuation
                ? CompendiumLayoutMetrics.ContinuationBodyFontSize
                : CompendiumLayoutMetrics.ProjectBodyFontSize * narrativeFontScale;
            var typography = new MarkdownPdfTypography(
                BodyFontSize: bodyFontSize,
                BodyLineHeight: CompendiumNarrativeTypographyPolicy.BodyLineHeightMultiplier,
                BlockSpacing: CompendiumNarrativeTypographyPolicy.ParagraphSpacingPoints,
                HeadingScale: continuation ? 1f : narrativeFontScale,
                BodyFontColor: Slate700);

            column.Item()
                .DefaultTextStyle(style => BaseStyle(style)
                    .FontSize(bodyFontSize)
                    .FontColor(Slate700)
                    .LineHeight(CompendiumNarrativeTypographyPolicy.BodyLineHeightMultiplier))
                .Element(element => CompendiumNarrativePdfRenderer.Render(
                    element,
                    markdown,
                    justifyParagraphs: narrativeAlignment == CompendiumNarrativeAlignment.Justified,
                    typography: typography,
                    allowMinorHeadings: allowMinorHeadings));
        });
    }

    private static void ComposeBackCover(
        IDocumentContainer container,
        string title,
        string subtitle,
        string edition,
        string? marking,
        CompendiumPdfCoverDesign? design,
        byte[]? crest,
        byte[]? sddMark)
    {
        design ??= new CompendiumPdfCoverDesign(
            CompendiumFrontCoverTemplate.InstitutionalHero,
            CompendiumBackCoverTemplate.MinimalInstitutional,
            Array.Empty<CompendiumPdfCoverImage>());
        var images = design.Images
            .Where(image => image.Surface == CompendiumCoverSurface.Back && image.Content is { Length: > 0 })
            .ToDictionary(image => image.SlotKey, StringComparer.OrdinalIgnoreCase);
        var hero = images.GetValueOrDefault("Hero")?.Content;
        var secondary1 = images.GetValueOrDefault("Secondary1")?.Content;
        var secondary2 = images.GetValueOrDefault("Secondary2")?.Content;
        var displayTitle = design.ShowBackTitle ? NormalizeOptional(design.BackTitle) ?? title : null;
        var displaySubtitle = design.ShowBackSubtitle ? NormalizeOptional(design.BackSubtitle) ?? subtitle : null;
        var displayEdition = design.ShowBackEdition ? NormalizeOptional(design.BackEdition) ?? edition : null;
        var eyebrow = NormalizeOptional(design.BackEyebrow);

        switch (design.BackTemplate)
        {
            case CompendiumBackCoverTemplate.ImageEcho:
                ComposeImageEchoBackCover(container, displayTitle, displaySubtitle, displayEdition, eyebrow, marking, hero,
                    crest, sddMark, design.BackLogoPlacement, design.ShowBackLeftLogo, design.ShowBackRightLogo);
                break;
            case CompendiumBackCoverTemplate.PortfolioStrip:
                ComposePortfolioBackCover(container, displayTitle, displaySubtitle, displayEdition, eyebrow, marking,
                    hero, secondary1, secondary2, crest, sddMark, design.BackLogoPlacement, design.ShowBackLeftLogo, design.ShowBackRightLogo);
                break;
            case CompendiumBackCoverTemplate.TypographyOnly:
                ComposeTypographyBackCover(container, displayTitle, displaySubtitle, displayEdition, eyebrow, marking,
                    crest, sddMark, design.BackLogoPlacement, design.ShowBackLeftLogo, design.ShowBackRightLogo, clean: false);
                break;
            case CompendiumBackCoverTemplate.Clean:
                ComposeTypographyBackCover(container, displayTitle, displaySubtitle, displayEdition, eyebrow, marking,
                    crest, sddMark, design.BackLogoPlacement, design.ShowBackLeftLogo, design.ShowBackRightLogo, clean: true);
                break;
            default:
                ComposeMinimalBackCover(container, displayTitle, displaySubtitle, displayEdition, eyebrow, marking,
                    crest, sddMark, design.BackLogoPlacement, design.ShowBackLeftLogo, design.ShowBackRightLogo);
                break;
        }
    }

    private static void ComposeMinimalBackCover(
        IDocumentContainer container,
        string? title,
        string? subtitle,
        string? edition,
        string? eyebrow,
        string? marking,
        byte[]? crest,
        byte[]? sddMark,
        CompendiumCoverLogoPlacement logoPlacement,
        bool showLeftLogo,
        bool showRightLogo)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => BaseStyle(style).FontColor(White));
            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Background(Forest950).PaddingHorizontal(58).PaddingVertical(54).Column(column =>
                {
                    column.Item().Element(row => ComposeCoverLogos(row, crest, sddMark, logoPlacement, showLeftLogo, showRightLogo));
                    column.Item().PaddingTop(190).Element(identity => ComposeCoverIdentity(identity, eyebrow, title, subtitle, edition, 24));
                    if (!string.IsNullOrWhiteSpace(marking))
                    {
                        column.Item().PaddingTop(210).Text(marking).FontSize(8.5f).SemiBold().LetterSpacing(.35f).FontColor(GoldSoft);
                    }
                });
                layers.Layer().AlignTop().Height(6).Background(Gold);
                layers.Layer().AlignBottom().Height(16).Background(Forest900);
            });
        });
    }

    private static void ComposeImageEchoBackCover(
        IDocumentContainer container,
        string? title,
        string? subtitle,
        string? edition,
        string? eyebrow,
        string? marking,
        byte[]? hero,
        byte[]? crest,
        byte[]? sddMark,
        CompendiumCoverLogoPlacement logoPlacement,
        bool showLeftLogo,
        bool showRightLogo)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => BaseStyle(style).FontColor(White));
            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Column(column =>
                {
                    column.Item().Height(300).Background(Forest950).PaddingHorizontal(54).PaddingTop(44).Column(top =>
                    {
                        top.Item().Element(row => ComposeCoverLogos(row, crest, sddMark, logoPlacement, showLeftLogo, showRightLogo));
                        top.Item().PaddingTop(48).Element(identity => ComposeCoverIdentity(identity, eyebrow, title, subtitle, edition, 25));
                    });
                    column.Item().Height(526).Element(tile => ComposeCoverTile(tile, hero));
                    column.Item().Height(16).Background(Forest900);
                });
                if (!string.IsNullOrWhiteSpace(marking))
                {
                    layers.Layer().AlignBottom().PaddingBottom(30).PaddingHorizontal(54).Text(marking)
                        .FontSize(8.2f).SemiBold().FontColor(GoldSoft);
                }
            });
        });
    }

    private static void ComposePortfolioBackCover(
        IDocumentContainer container,
        string? title,
        string? subtitle,
        string? edition,
        string? eyebrow,
        string? marking,
        byte[]? hero,
        byte[]? secondary1,
        byte[]? secondary2,
        byte[]? crest,
        byte[]? sddMark,
        CompendiumCoverLogoPlacement logoPlacement,
        bool showLeftLogo,
        bool showRightLogo)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => BaseStyle(style).FontColor(White));
            page.Content().Column(column =>
            {
                column.Item().Height(510).Background(Forest950).PaddingHorizontal(56).PaddingTop(50).Column(top =>
                {
                    top.Item().Element(row => ComposeCoverLogos(row, crest, sddMark, logoPlacement, showLeftLogo, showRightLogo));
                    top.Item().PaddingTop(145).Element(identity => ComposeCoverIdentity(identity, eyebrow, title, subtitle, edition, 25));
                    if (!string.IsNullOrWhiteSpace(marking)) top.Item().PaddingTop(18).Text(marking).FontSize(8.2f).SemiBold().FontColor(GoldSoft);
                });
                column.Item().Height(316).Row(row =>
                {
                    row.RelativeItem().Element(cell => ComposeCoverTile(cell, hero));
                    row.ConstantItem(3).Background(Forest950);
                    row.RelativeItem().Element(cell => ComposeCoverTile(cell, secondary1));
                    row.ConstantItem(3).Background(Forest950);
                    row.RelativeItem().Element(cell => ComposeCoverTile(cell, secondary2));
                });
                column.Item().Height(16).Background(Forest900);
            });
        });
    }

    private static void ComposeTypographyBackCover(
        IDocumentContainer container,
        string? title,
        string? subtitle,
        string? edition,
        string? eyebrow,
        string? marking,
        byte[]? crest,
        byte[]? sddMark,
        CompendiumCoverLogoPlacement logoPlacement,
        bool showLeftLogo,
        bool showRightLogo,
        bool clean)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Forest950);
            page.DefaultTextStyle(style => BaseStyle(style).FontColor(White));
            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Background(Forest950).PaddingHorizontal(66).PaddingVertical(58).Column(column =>
                {
                    column.Item().Element(row => ComposeCoverLogos(row, crest, sddMark, logoPlacement, showLeftLogo, showRightLogo));
                    column.Item().PaddingTop(clean ? 270 : 220).Element(identity => ComposeCoverIdentity(identity, eyebrow, title, subtitle, edition, clean ? 27 : 25));
                    if (!string.IsNullOrWhiteSpace(marking)) column.Item().PaddingTop(180).Text(marking).FontSize(8.2f).SemiBold().FontColor(GoldSoft);
                });
                layers.Layer().AlignTop().Height(6).Background(Gold);
                layers.Layer().AlignBottom().Height(16).Background(Forest900);
            });
        });
    }

    private static void ConfigureStandardPage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginTop(CompendiumLayoutMetrics.TopMarginPoints);
        page.MarginLeft(CompendiumLayoutMetrics.HorizontalMarginPoints);
        page.MarginRight(CompendiumLayoutMetrics.HorizontalMarginPoints);
        page.MarginBottom(0);
        page.PageColor(White);
        page.DefaultTextStyle(style => BaseStyle(style).FontSize(10f).FontColor(Ink));
    }

    private static void ComposeRunningHeader(IContainer container, string left, string right, string? marking)
    {
        container.PaddingBottom(7).BorderBottom(1).BorderColor(Slate200).Row(row =>
        {
            row.RelativeItem().Text(left)
                .FontSize(7.6f)
                .SemiBold()
                .LetterSpacing(.24f)
                .FontColor(Forest800);
            row.AutoItem().Text(right)
                .FontSize(7.5f)
                .FontColor(Slate500);
            if (!string.IsNullOrWhiteSpace(marking))
            {
                row.ConstantItem(12);
                row.AutoItem().Text(marking)
                    .FontSize(7.3f)
                    .SemiBold()
                    .FontColor("#8B6B18");
            }
        });
    }

    private static void ComposeFooter(IContainer container, string issuer, string? marking, byte[]? logo)
    {
        container.Height(CompendiumLayoutMetrics.FooterHeightPoints).PaddingTop(5).BorderTop(1).BorderColor(Slate200).Row(row =>
        {
            row.RelativeItem().Row(left =>
            {
                if (logo is { Length: > 0 })
                {
                    left.ConstantItem(18).Height(18).AlignMiddle().Image(logo).FitArea();
                    left.ConstantItem(6);
                }
                left.RelativeItem().AlignMiddle().Text(issuer)
                    .FontSize(7.8f)
                    .SemiBold()
                    .FontColor(Slate600);
            });

            if (!string.IsNullOrWhiteSpace(marking))
            {
                row.AutoItem().PaddingHorizontal(10).AlignMiddle().Text(marking)
                    .FontSize(7)
                    .SemiBold()
                    .FontColor("#8B6B18");
            }

            row.RelativeItem().AlignRight().AlignMiddle().Text(text =>
            {
                text.DefaultTextStyle(BaseStyle(TextStyle.Default).FontSize(7.8f).FontColor(Slate500));
                text.Span("Page ");
                text.CurrentPageNumber().SemiBold();
                text.Span(" / ");
                text.TotalPages().SemiBold();
            });
        });
    }

    private static TextStyle BaseStyle(TextStyle style)
        => style
            .FontFamily(Volatile.Read(ref s_primaryFontFamily))
            .DisableFontFeature(FontFeatures.StandardLigatures);

    private static string ProjectAnchorId(int projectId)
        => $"compendium-project-{projectId.ToString(CultureInfo.InvariantCulture)}";

    private byte[]? TryLoadAsset(string relativeUnderWwwRoot)
    {
        try
        {
            var relative = relativeUnderWwwRoot.Trim().Replace('\\', '/');
            var path = Path.Combine(
                _environment.WebRootPath,
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                _logger.LogWarning("Compendium PDF asset was not found at {AssetPath}.", path);
                return null;
            }
            return File.ReadAllBytes(path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to load compendium PDF asset {RelativeAssetPath}.", relativeUnderWwwRoot);
            return null;
        }
    }

    private IReadOnlyDictionary<string, string> LoadProgrammeIcons()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[]
                 {
                     "arms-services",
                     "proliferation-cost",
                     "ipr-filed",
                     "ipr-granted",
                     "ipr-mixed",
                     "technology-transfer"
                 })
        {
            var svg = TryLoadTextAsset($"images/publications/compendium-icons/{key}.svg");
            if (!string.IsNullOrWhiteSpace(svg))
            {
                result[key] = svg;
            }
        }

        return result;
    }

    private string? TryLoadTextAsset(string relativeUnderWwwRoot)
    {
        try
        {
            var relative = relativeUnderWwwRoot.Trim().Replace('\\', '/');
            var path = Path.Combine(
                _environment.WebRootPath,
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                _logger.LogWarning("Compendium PDF asset was not found at {AssetPath}.", path);
                return null;
            }

            return File.ReadAllText(path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to load compendium PDF asset {RelativeAssetPath}.", relativeUnderWwwRoot);
            return null;
        }
    }

    private static string ResolveProjectKicker(CompendiumPdfProjectSection project)
    {
        var publicationSection = Normalize(project.CategoryName, "Projects");
        if (!string.Equals(publicationSection, "Projects", StringComparison.OrdinalIgnoreCase))
        {
            return publicationSection;
        }

        return Normalize(project.TechnicalCategoryDisplay, "Project dossier");
    }

    private static string NormalizeNarrativeLabel(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Project Brief" : value.Trim();

    private static string ResolveStatusBackground(string? lifecycle)
        => string.Equals(lifecycle, "Completed", StringComparison.OrdinalIgnoreCase) ? "#EAF4EF" : "#EEF4FF";

    private static string ResolveStatusBorder(string? lifecycle)
        => string.Equals(lifecycle, "Completed", StringComparison.OrdinalIgnoreCase) ? "#BCD8CA" : "#C9D8F4";

    private static string ResolveStatusText(string? lifecycle)
        => string.Equals(lifecycle, "Completed", StringComparison.OrdinalIgnoreCase) ? Forest800 : "#315E9A";

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
