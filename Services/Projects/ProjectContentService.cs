using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

public interface IProjectContentService
{
    Task<ProjectContentSaveResult> SaveBriefAsync(
        int projectId,
        string? brief,
        string rowVersion,
        string userId,
        string userDisplay,
        CancellationToken cancellationToken = default);

    Task<ProjectContentSaveResult> SaveCapabilitiesAsync(
        int projectId,
        IReadOnlyList<string?> statements,
        string rowVersion,
        string userId,
        string userDisplay,
        CancellationToken cancellationToken = default);

    Task<ProjectContentSaveResult> SaveDescriptionAsync(
        int projectId,
        string? description,
        string rowVersion,
        string userId,
        string userDisplay,
        CancellationToken cancellationToken = default);
}

public sealed record ProjectContentSaveResult(
    bool Succeeded,
    bool NotFound = false,
    bool ConcurrencyConflict = false,
    string? Error = null)
{
    public static ProjectContentSaveResult Success() => new(true);
    public static ProjectContentSaveResult Missing() => new(false, NotFound: true);
    public static ProjectContentSaveResult Conflict() => new(false, ConcurrencyConflict: true,
        Error: "This project was changed by another user. Reload the page and review the latest content before saving again.");
    public static ProjectContentSaveResult Invalid(string error) => new(false, Error: error);
}

public static partial class ProjectContentRules
{
    [GeneratedRegex(@"\S+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    public static int CountWords(string? value) =>
        string.IsNullOrWhiteSpace(value) ? 0 : WordRegex().Matches(value).Count;

    public static string? NormalizeNarrative(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
    }

    public static IReadOnlyList<string> NormalizeCapabilities(IEnumerable<string?> values) =>
        values
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
}

public sealed class ProjectContentService : IProjectContentService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<ProjectContentService> _logger;

    public ProjectContentService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit,
        ILogger<ProjectContentService> logger)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _logger = logger;
    }

    public async Task<ProjectContentSaveResult> SaveBriefAsync(
        int projectId,
        string? brief,
        string rowVersion,
        string userId,
        string userDisplay,
        CancellationToken cancellationToken = default)
    {
        var normalized = ProjectContentRules.NormalizeNarrative(brief);
        if (normalized?.Length > ProjectFieldLimits.ProjectBriefMaxLength)
        {
            return ProjectContentSaveResult.Invalid(
                $"Project brief cannot exceed {ProjectFieldLimits.ProjectBriefMaxLength:N0} characters.");
        }

        var wordCount = ProjectContentRules.CountWords(normalized);
        if (wordCount > ProjectFieldLimits.ProjectBriefMaximumWords)
        {
            return ProjectContentSaveResult.Invalid(
                $"Project brief is {wordCount} words. Reduce it to {ProjectFieldLimits.ProjectBriefMaximumWords} words or fewer.");
        }

        if (!TryDecodeRowVersion(rowVersion, out var originalRowVersion))
        {
            return ProjectContentSaveResult.Invalid("The project version is invalid. Reload the page and try again.");
        }

        var project = await LoadTrackedProjectAsync(projectId, originalRowVersion, cancellationToken);
        if (project is null)
        {
            return ProjectContentSaveResult.Missing();
        }

        project.ProjectBrief = normalized;
        Stamp(project, userId);

        var saveResult = await SaveAsync(cancellationToken);
        if (!saveResult.Succeeded)
        {
            return saveResult;
        }

        await AuditAsync("Project.Content.BriefUpdated", project, userId, userDisplay,
            new Dictionary<string, string?> { ["WordCount"] = wordCount.ToString() });
        return ProjectContentSaveResult.Success();
    }

    public async Task<ProjectContentSaveResult> SaveCapabilitiesAsync(
        int projectId,
        IReadOnlyList<string?> statements,
        string rowVersion,
        string userId,
        string userDisplay,
        CancellationToken cancellationToken = default)
    {
        var normalized = ProjectContentRules.NormalizeCapabilities(statements);
        if (normalized.Count > ProjectFieldLimits.CapabilityMaximumCount)
        {
            return ProjectContentSaveResult.Invalid(
                $"Capability overview can contain a maximum of {ProjectFieldLimits.CapabilityMaximumCount} statements.");
        }

        var overLength = normalized.FirstOrDefault(statement =>
            statement.Length > ProjectFieldLimits.CapabilityStatementMaxLength);
        if (overLength is not null)
        {
            return ProjectContentSaveResult.Invalid(
                $"Each capability statement must be {ProjectFieldLimits.CapabilityStatementMaxLength} characters or fewer.");
        }

        var duplicate = normalized
            .GroupBy(statement => statement, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return ProjectContentSaveResult.Invalid("Remove duplicate capability statements before saving.");
        }

        if (!TryDecodeRowVersion(rowVersion, out var originalRowVersion))
        {
            return ProjectContentSaveResult.Invalid("The project version is invalid. Reload the page and try again.");
        }

        var project = await LoadTrackedProjectAsync(projectId, originalRowVersion, cancellationToken);
        if (project is null)
        {
            return ProjectContentSaveResult.Missing();
        }

        var existing = await _db.ProjectCapabilityStatements
            .Where(statement => statement.ProjectId == projectId)
            .OrderBy(statement => statement.DisplayOrder)
            .ThenBy(statement => statement.Id)
            .ToListAsync(cancellationToken);

        var sharedCount = Math.Min(existing.Count, normalized.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            existing[index].Statement = normalized[index];
            existing[index].DisplayOrder = index + 1;
        }

        for (var index = sharedCount; index < normalized.Count; index++)
        {
            _db.ProjectCapabilityStatements.Add(new ProjectCapabilityStatement
            {
                ProjectId = projectId,
                Statement = normalized[index],
                DisplayOrder = index + 1
            });
        }

        if (existing.Count > normalized.Count)
        {
            _db.ProjectCapabilityStatements.RemoveRange(existing.Skip(normalized.Count));
        }

        Stamp(project, userId);

        var saveResult = await SaveAsync(cancellationToken);
        if (!saveResult.Succeeded)
        {
            return saveResult;
        }

        await AuditAsync("Project.Content.CapabilitiesUpdated", project, userId, userDisplay,
            new Dictionary<string, string?> { ["StatementCount"] = normalized.Count.ToString() });
        return ProjectContentSaveResult.Success();
    }

    public async Task<ProjectContentSaveResult> SaveDescriptionAsync(
        int projectId,
        string? description,
        string rowVersion,
        string userId,
        string userDisplay,
        CancellationToken cancellationToken = default)
    {
        var normalized = ProjectContentRules.NormalizeNarrative(description);
        if (normalized?.Length > ProjectFieldLimits.DescriptionMaxLength)
        {
            return ProjectContentSaveResult.Invalid(
                $"Full description cannot exceed {ProjectFieldLimits.DescriptionMaxLength:N0} characters.");
        }

        if (!TryDecodeRowVersion(rowVersion, out var originalRowVersion))
        {
            return ProjectContentSaveResult.Invalid("The project version is invalid. Reload the page and try again.");
        }

        var project = await LoadTrackedProjectAsync(projectId, originalRowVersion, cancellationToken);
        if (project is null)
        {
            return ProjectContentSaveResult.Missing();
        }

        project.Description = normalized;
        Stamp(project, userId);

        var saveResult = await SaveAsync(cancellationToken);
        if (!saveResult.Succeeded)
        {
            return saveResult;
        }

        await AuditAsync("Project.Content.DescriptionUpdated", project, userId, userDisplay,
            new Dictionary<string, string?> { ["CharacterCount"] = (normalized?.Length ?? 0).ToString() });
        return ProjectContentSaveResult.Success();
    }

    private async Task<Project?> LoadTrackedProjectAsync(
        int projectId,
        byte[] originalRowVersion,
        CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .FirstOrDefaultAsync(candidate => candidate.Id == projectId && !candidate.IsDeleted, cancellationToken);

        if (project is null)
        {
            return null;
        }

        _db.Entry(project).Property(candidate => candidate.RowVersion).OriginalValue = originalRowVersion;
        return project;
    }

    private static bool TryDecodeRowVersion(string? encoded, out byte[] rowVersion)
    {
        rowVersion = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(encoded);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void Stamp(Project project, string userId)
    {
        project.ContentUpdatedAtUtc = _clock.UtcNow;
        project.ContentUpdatedByUserId = userId;
    }

    private async Task<ProjectContentSaveResult> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return ProjectContentSaveResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProjectContentSaveResult.Conflict();
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(exception, "Project content could not be persisted.");
            return ProjectContentSaveResult.Invalid(
                "The project content could not be saved because the database rejected the change. Reload the page and try again.");
        }
    }

    private async Task AuditAsync(
        string action,
        Project project,
        string userId,
        string userDisplay,
        IDictionary<string, string?> data)
    {
        data["ProjectId"] = project.Id.ToString();
        data["ProjectName"] = project.Name;
        try
        {
            await _audit.LogAsync(
                action,
                $"Updated project content for '{project.Name}'.",
                userId: userId,
                userName: userDisplay,
                data: data);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Project content audit write failed. Action={Action}, ProjectId={ProjectId}, UserId={UserId}",
                action,
                project.Id,
                userId);
        }
    }
}
