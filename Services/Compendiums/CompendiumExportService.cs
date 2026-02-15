using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Services.Compendiums;

// SECTION: PDF export orchestration for the Proliferation Compendium.
public sealed class CompendiumExportService : ICompendiumExportService
{
    private readonly ICompendiumReadService _readService;
    private readonly IProjectPhotoService _projectPhotoService;
    private readonly ICompendiumPdfReportBuilder _pdfBuilder;
    private readonly CompendiumPdfOptions _options;

    public CompendiumExportService(
        ICompendiumReadService readService,
        IProjectPhotoService projectPhotoService,
        ICompendiumPdfReportBuilder pdfBuilder,
        IOptions<CompendiumPdfOptions> options)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _projectPhotoService = projectPhotoService ?? throw new ArgumentNullException(nameof(projectPhotoService));
        _pdfBuilder = pdfBuilder ?? throw new ArgumentNullException(nameof(pdfBuilder));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<CompendiumExportResult> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var data = await _readService.GetProliferationCompendiumAsync(cancellationToken);

        // SECTION: Attach photos in a fail-safe way.
        var categories = new List<CompendiumPdfCategorySection>(data.Groups.Count);

        foreach (var group in data.Groups)
        {
            var projects = new List<CompendiumPdfProjectSection>(group.Projects.Count);

            foreach (var p in group.Projects)
            {
                var photoBytes = await TryLoadCoverPhotoAsync(p.ProjectId, p.CoverPhotoId, cancellationToken);

                projects.Add(new CompendiumPdfProjectSection(
                    ProjectId: p.ProjectId,
                    ProjectName: p.ProjectName,
                    CategoryName: group.TechnicalCategoryName,
                    CompletionYearDisplay: p.CompletionYearDisplay,
                    ArmServiceDisplay: p.ArmServiceDisplay,
                    ProliferationCostDisplay: p.ProliferationCostLakhs.HasValue
                        ? p.ProliferationCostLakhs.Value.ToString("0.##", CultureInfo.InvariantCulture)
                        : "Not recorded",
                    DescriptionMarkdown: p.DescriptionMarkdown,
                    CoverPhoto: photoBytes));
            }

            categories.Add(new CompendiumPdfCategorySection(
                CategoryName: group.TechnicalCategoryName,
                Projects: projects));
        }

        var context = new CompendiumPdfReportContext(
            Title: data.Title,
            UnitDisplayName: data.UnitDisplayName,
            GeneratedAtUtc: data.GeneratedAtUtc,
            Categories: categories);

        var bytes = _pdfBuilder.Build(context);

        var generatedAtIst = TimeZoneInfo.ConvertTime(data.GeneratedAtUtc, TimeZoneHelper.GetIst());
        var safeDate = generatedAtIst.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var fileName = $"Proliferation_Compendium_{safeDate}.pdf";

        return new CompendiumExportResult(bytes, fileName);
    }

    private async Task<byte[]?> TryLoadCoverPhotoAsync(int projectId, int? photoId, CancellationToken cancellationToken)
    {
        if (!photoId.HasValue)
        {
            return null;
        }

        try
        {
            var derivativeKey = string.IsNullOrWhiteSpace(_options.CoverPhotoDerivativeKey)
                ? "md"
                : _options.CoverPhotoDerivativeKey.Trim();

            var opened = await _projectPhotoService.OpenDerivativeAsync(
                projectId,
                photoId.Value,
                derivativeKey,
                preferWebp: _options.PreferWebp,
                cancellationToken);

            if (opened is null)
            {
                return null;
            }

            await using var stream = opened.Value.Stream;
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            return ms.ToArray();
        }
        catch
        {
            // SECTION: URD requirement: cover photo failures must never fail PDF generation.
            return null;
        }
    }
}
