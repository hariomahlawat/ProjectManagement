using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.Publications;

/// <summary>
/// Read/composition service for project brochures. It intentionally reads the same
/// authoritative Project, capability-statement and ProjectPhoto records already used
/// by PRISM briefing/export workflows; no brochure copy is persisted separately.
/// </summary>
public sealed partial class BrochurePublicationService
{
    private readonly ApplicationDbContext _db;
    private readonly IProjectBriefingPhotoLoader _photoLoader;

    public BrochurePublicationService(
        ApplicationDbContext db,
        IProjectBriefingPhotoLoader photoLoader)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _photoLoader = photoLoader ?? throw new ArgumentNullException(nameof(photoLoader));
    }

    public async Task<IReadOnlyList<BrochureProjectListItemVm>> GetProjectOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted
                              && !project.IsArchived
                              && (project.LifecycleStatus == ProjectLifecycleStatus.Active
                                  || project.LifecycleStatus == ProjectLifecycleStatus.Completed))
            .OrderBy(project => project.Name)
            .Select(project => new ProjectOptionRow(
                project.Id,
                project.Name,
                project.LifecycleStatus,
                project.Category != null ? project.Category.Name : null,
                project.TechnicalCategory != null ? project.TechnicalCategory.Name : null,
                project.ProjectBrief,
                project.Description,
                project.CoverPhotoId))
            .ToListAsync(cancellationToken);

        if (projects.Count == 0)
        {
            return Array.Empty<BrochureProjectListItemVm>();
        }

        var projectIds = projects.Select(project => project.ProjectId).ToArray();
        var capabilityRows = await _db.ProjectCapabilityStatements
            .AsNoTracking()
            .Where(statement => projectIds.Contains(statement.ProjectId))
            .Select(statement => new CapabilityRow(statement.ProjectId, statement.Statement))
            .ToListAsync(cancellationToken);
        var capabilityWords = capabilityRows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(row => BrochureLayoutPlanner.CountWords(row.Statement)));

        var photoRows = await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => projectIds.Contains(photo.ProjectId))
            .Select(photo => new PublicationPhotoRow(
                photo.ProjectId,
                photo.Id,
                photo.IsCover,
                photo.IsLowResolution,
                photo.Ordinal))
            .ToListAsync(cancellationToken);
        var photosByProject = photoRows
            .GroupBy(photo => photo.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PublicationPhotoRow>)group
                    .OrderByDescending(photo => photo.IsCover)
                    .ThenBy(photo => photo.IsLowResolution)
                    .ThenBy(photo => photo.Ordinal)
                    .ThenBy(photo => photo.PhotoId)
                    .ToArray());

        return projects.Select(project =>
        {
            var projectPhotos = photosByProject.GetValueOrDefault(project.ProjectId);
            var selectedPhoto = SelectPhoto(project.CoverPhotoId, projectPhotos);
            return new BrochureProjectListItemVm(
                project.ProjectId,
                project.ProjectName,
                LifecycleDisplay(project.LifecycleStatus),
                project.ProjectCategory,
                project.TechnicalCategory,
                HasMeaningfulText(project.ProjectBrief),
                capabilityWords.GetValueOrDefault(project.ProjectId) > 0,
                HasMeaningfulText(project.Description),
                BrochureLayoutPlanner.CountWords(project.ProjectBrief),
                capabilityWords.GetValueOrDefault(project.ProjectId),
                BrochureLayoutPlanner.CountWords(project.Description),
                selectedPhoto is not null,
                selectedPhoto is not null && !selectedPhoto.IsLowResolution);
        }).ToArray();
    }

    public async Task<BrochurePublicationData> BuildAsync(
        IReadOnlyList<int> orderedProjectIds,
        BrochureBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedProjectIds);
        ArgumentNullException.ThrowIfNull(options);

        var orderedIds = orderedProjectIds
            .Where(id => id > 0)
            .Distinct()
            .Take(100)
            .ToArray();
        if (orderedIds.Length == 0)
        {
            throw new InvalidOperationException("Select at least one project for the brochure.");
        }

        var rows = await _db.Projects
            .AsNoTracking()
            .Where(project => orderedIds.Contains(project.Id)
                              && !project.IsDeleted
                              && !project.IsArchived
                              && (project.LifecycleStatus == ProjectLifecycleStatus.Active
                                  || project.LifecycleStatus == ProjectLifecycleStatus.Completed))
            .Select(project => new PublicationProjectRow(
                project.Id,
                project.Name,
                project.ProjectBrief,
                project.Description,
                project.Category != null ? project.Category.Name : null,
                project.TechnicalCategory != null ? project.TechnicalCategory.Name : null,
                project.CoverPhotoId))
            .ToListAsync(cancellationToken);

        var rowById = rows.ToDictionary(row => row.ProjectId);
        var validOrderedIds = orderedIds.Where(rowById.ContainsKey).ToArray();
        if (validOrderedIds.Length == 0)
        {
            throw new InvalidOperationException("The selected projects are no longer available for publication.");
        }

        var capabilityRows = await _db.ProjectCapabilityStatements
            .AsNoTracking()
            .Where(statement => validOrderedIds.Contains(statement.ProjectId))
            .OrderBy(statement => statement.ProjectId)
            .ThenBy(statement => statement.DisplayOrder)
            .ThenBy(statement => statement.Id)
            .Select(statement => new CapabilityRow(statement.ProjectId, statement.Statement))
            .ToListAsync(cancellationToken);
        var capabilitiesByProject = capabilityRows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(row => CleanLine(row.Statement))
                    .Where(HasMeaningfulText)
                    .ToArray());

        var photoRows = await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => validOrderedIds.Contains(photo.ProjectId))
            .Select(photo => new PublicationPhotoRow(
                photo.ProjectId,
                photo.Id,
                photo.IsCover,
                photo.IsLowResolution,
                photo.Ordinal))
            .ToListAsync(cancellationToken);
        var photosByProject = photoRows
            .GroupBy(photo => photo.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PublicationPhotoRow>)group
                    .OrderByDescending(photo => photo.IsCover)
                    .ThenBy(photo => photo.IsLowResolution)
                    .ThenBy(photo => photo.Ordinal)
                    .ThenBy(photo => photo.PhotoId)
                    .ToArray());

        var projects = new List<BrochurePublicationProject>(validOrderedIds.Length);
        var missingNarrative = 0;
        var missingPhoto = 0;
        var lowResolution = 0;
        var longNarrative = 0;

        foreach (var projectId in validOrderedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rowById[projectId];
            var capabilities = capabilitiesByProject.GetValueOrDefault(projectId) ?? Array.Empty<string>();
            var (narrative, hasNarrative) = ResolveNarrative(row, capabilities, options.NarrativeSource);
            if (!hasNarrative)
            {
                missingNarrative++;
            }

            var wordCount = BrochureLayoutPlanner.CountWords(narrative);
            if (wordCount > 210)
            {
                longNarrative++;
            }

            var selectedPhoto = SelectPhoto(row.CoverPhotoId, photosByProject.GetValueOrDefault(projectId));
            ProjectBriefingPhotoContent? photoContent = null;
            if (selectedPhoto is not null)
            {
                photoContent = await _photoLoader.LoadAsync(projectId, selectedPhoto.PhotoId, cancellationToken);
            }

            if (photoContent is null)
            {
                missingPhoto++;
            }
            else if (selectedPhoto?.IsLowResolution == true)
            {
                lowResolution++;
            }

            projects.Add(new BrochurePublicationProject(
                row.ProjectId,
                row.ProjectName,
                row.ProjectCategory,
                row.TechnicalCategory,
                narrative,
                wordCount,
                photoContent?.Content,
                selectedPhoto?.IsLowResolution == true,
                photoContent?.SourceVariant));
        }

        return new BrochurePublicationData(
            options,
            projects,
            new BrochurePreflight(
                projects.Count,
                missingNarrative,
                missingPhoto,
                lowResolution,
                longNarrative));
    }

    private static (string Narrative, bool HasNarrative) ResolveNarrative(
        PublicationProjectRow project,
        IReadOnlyList<string> capabilities,
        BrochureNarrativeSource source)
    {
        return source switch
        {
            BrochureNarrativeSource.ProjectBrief => ResolveText(
                project.ProjectBrief,
                "Project brief not recorded."),
            BrochureNarrativeSource.CapabilityOverview => capabilities.Count > 0
                ? (string.Join("\n\n", capabilities.Select(statement => $"• {statement}")), true)
                : ("Capability overview not recorded.", false),
            BrochureNarrativeSource.FullDescription => ResolveText(
                project.Description,
                "Project description not recorded."),
            _ => throw new InvalidOperationException("The selected brochure narrative source is invalid.")
        };
    }

    private static (string Text, bool HasText) ResolveText(string? value, string fallback)
    {
        if (!HasMeaningfulText(value))
        {
            return (fallback, false);
        }

        var normalized = NormalizeNarrative(value!);
        return string.IsNullOrWhiteSpace(normalized)
            ? (fallback, false)
            : (normalized, true);
    }

    private static PublicationPhotoRow? SelectPhoto(
        int? configuredCoverPhotoId,
        IReadOnlyList<PublicationPhotoRow>? photos)
    {
        if (photos is null || photos.Count == 0)
        {
            return null;
        }

        if (configuredCoverPhotoId.HasValue)
        {
            var configured = photos.FirstOrDefault(photo => photo.PhotoId == configuredCoverPhotoId.Value);
            if (configured is not null)
            {
                return configured;
            }
        }

        return photos
            .OrderByDescending(photo => photo.IsCover)
            .ThenBy(photo => photo.IsLowResolution)
            .ThenBy(photo => photo.Ordinal)
            .ThenBy(photo => photo.PhotoId)
            .First();
    }

    private static string NormalizeNarrative(string value)
    {
        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        normalized = MarkdownImageRegex().Replace(normalized, string.Empty);
        normalized = MarkdownLinkRegex().Replace(normalized, "$1");
        normalized = MarkdownHeadingRegex().Replace(normalized, "$1");
        normalized = MarkdownDecorationRegex().Replace(normalized, string.Empty);
        normalized = HorizontalWhitespaceRegex().Replace(normalized, " ");

        var lines = normalized
            .Split('\n')
            .Select(line => CleanLine(line))
            .ToArray();
        normalized = string.Join("\n", lines);
        normalized = ExcessiveNewlinesRegex().Replace(normalized, "\n\n").Trim();
        return normalized;
    }

    private static string CleanLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var line = value.Trim();
        line = ListPrefixRegex().Replace(line, match =>
            string.Equals(match.Groups[1].Value, "-", StringComparison.Ordinal)
                || string.Equals(match.Groups[1].Value, "*", StringComparison.Ordinal)
                || string.Equals(match.Groups[1].Value, "+", StringComparison.Ordinal)
                    ? "• "
                    : match.Value);
        return line;
    }

    private static bool HasMeaningfulText(string? value)
        => !string.IsNullOrWhiteSpace(value);

    private static string LifecycleDisplay(ProjectLifecycleStatus status)
        => status switch
        {
            ProjectLifecycleStatus.Active => "Ongoing",
            ProjectLifecycleStatus.Completed => "Completed",
            _ => status.ToString()
        };

    private sealed record ProjectOptionRow(
        int ProjectId,
        string ProjectName,
        ProjectLifecycleStatus LifecycleStatus,
        string? ProjectCategory,
        string? TechnicalCategory,
        string? ProjectBrief,
        string? Description,
        int? CoverPhotoId);

    private sealed record PublicationProjectRow(
        int ProjectId,
        string ProjectName,
        string? ProjectBrief,
        string? Description,
        string? ProjectCategory,
        string? TechnicalCategory,
        int? CoverPhotoId);

    private sealed record CapabilityRow(int ProjectId, string Statement);

    private sealed record PublicationPhotoRow(
        int ProjectId,
        int PhotoId,
        bool IsCover,
        bool IsLowResolution,
        int Ordinal);

    [GeneratedRegex(@"!\[[^\]]*\]\([^\)]+\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^\)]+\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"(?m)^\s{0,3}#{1,6}\s+(.+)$", RegexOptions.Compiled)]
    private static partial Regex MarkdownHeadingRegex();

    [GeneratedRegex(@"(\*\*|__|~~|`)", RegexOptions.Compiled)]
    private static partial Regex MarkdownDecorationRegex();

    [GeneratedRegex(@"[\t ]{2,}", RegexOptions.Compiled)]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.Compiled)]
    private static partial Regex ExcessiveNewlinesRegex();

    [GeneratedRegex(@"^(?:\s*)([-*+]|\d+[.)])\s+", RegexOptions.Compiled)]
    private static partial Regex ListPrefixRegex();
}
