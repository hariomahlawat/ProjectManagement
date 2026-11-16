using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.SearchV2.Query;
using ProjectManagement.Services.SearchV2.Security;

namespace ProjectManagement.Services.SearchV2.Indexing;

/// <param name="ContextStamp">Fingerprint of the project facts every document row of the project copies.</param>
/// <param name="DocumentCount">How many of the project's documents are indexable.</param>
public sealed record SearchProjectDocumentsState(string ContextStamp, int DocumentCount);

internal static class SearchProjectionBatches
{
    public static async IAsyncEnumerable<IReadOnlyList<SearchProjection>> Once(
        Func<CancellationToken, Task<IReadOnlyList<SearchProjection>>> build,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return await build(cancellationToken);
    }
}

public interface ISearchProjectionBuilder
{
    Task<IReadOnlyList<SearchProjection>> BuildAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The full-rebuild projections in batches: one module at a time, and documents a page at
    /// a time, so a rebuild never holds the text of every document at once.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<SearchProjection>> BuildAllInBatchesAsync(CancellationToken cancellationToken) =>
        SearchProjectionBatches.Once(BuildAllAsync, cancellationToken);

    Task<IReadOnlyList<SearchProjection>> BuildEntityAsync(string entityType, string entityKey, CancellationToken cancellationToken);

    /// <summary>
    /// Records from other modules whose index rows carry this project's name, stage or
    /// visibility: its ToT, IPR, Proliferation, Training, FFC and ARPP rows. They are
    /// re-indexed with the project, so a rename, stage change, trash or archive reaches
    /// them at once instead of at the next full rebuild.
    /// </summary>
    Task<IReadOnlyList<SearchEntityRef>> FindProjectDependentsAsync(int projectId, CancellationToken cancellationToken);

    /// <summary>
    /// A stamp of the project facts its document rows copy (name, case number, categories,
    /// lifecycle, stage) and how many documents it has; null when the project is not indexed,
    /// so its documents must leave the index with it. Null by default: always rewrite.
    /// </summary>
    Task<SearchProjectDocumentsState?> GetProjectDocumentsStateAsync(int projectId, CancellationToken cancellationToken) =>
        Task.FromResult<SearchProjectDocumentsState?>(null);

    /// <summary>
    /// The project's row, then its documents a page at a time, so re-indexing a project with
    /// hundreds of scanned documents never holds all their text at once. One batch by default.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<SearchProjection>> BuildProjectWithDocumentsInBatchesAsync(int projectId, CancellationToken cancellationToken) =>
        SearchProjectionBatches.Once(ct => BuildEntityAsync("Project", projectId.ToString(CultureInfo.InvariantCulture), ct), cancellationToken);

    /// <summary>The project's own index row, without its documents.</summary>
    Task<IReadOnlyList<SearchProjection>> BuildProjectRowAsync(int projectId, CancellationToken cancellationToken) =>
        BuildEntityAsync("Project", projectId.ToString(CultureInfo.InvariantCulture), cancellationToken);
}

/// <summary>An indexed record, named as its work items and index rows name it.</summary>
public readonly record struct SearchEntityRef(string EntityType, string EntityKey);

public sealed partial class SearchProjectionBuilder : ISearchProjectionBuilder
{
    private readonly ApplicationDbContext _db;
    private readonly IUrlBuilder _urls;
    private readonly ISearchQueryNormalizer _normalizer;
    private readonly SearchV2Options _options;
    private readonly IWorkflowStageMetadataProvider _workflowStages;

    public SearchProjectionBuilder(
        ApplicationDbContext db,
        IUrlBuilder urls,
        ISearchQueryNormalizer normalizer,
        IOptions<SearchV2Options> options,
        IWorkflowStageMetadataProvider? workflowStages = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _urls = urls ?? throw new ArgumentNullException(nameof(urls));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _workflowStages = workflowStages ?? new WorkflowStageMetadataProvider();
    }

    /// <summary>
    /// Documents read per batch in a full rebuild. Each can carry up to 200,000 characters of
    /// OCR text, so a page bounds the rebuild's memory however many documents there are.
    /// </summary>
    internal const int DocumentPageSize = 100;

    public async Task<IReadOnlyList<SearchProjection>> BuildAllAsync(CancellationToken cancellationToken)
    {
        var results = new List<SearchProjection>(4096);
        await foreach (var batch in BuildAllInBatchesAsync(cancellationToken)) results.AddRange(batch);
        return results;
    }

    public async IAsyncEnumerable<IReadOnlyList<SearchProjection>> BuildAllInBatchesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return await BuildProjectsAsync(null, includeProjectDocuments: false, cancellationToken);

        // Ids first, then a page of documents at a time. A document added meanwhile is queued
        // by its own trigger; one removed meanwhile is simply not returned.
        var projectDocumentIds = await IndexableProjectDocuments()
            .OrderBy(document => document.Id)
            .Select(document => document.Id)
            .ToListAsync(cancellationToken);
        foreach (var page in projectDocumentIds.Chunk(DocumentPageSize))
        {
            yield return await BuildProjectDocumentsAsync(null, null, cancellationToken, page);
        }

        var docRepoIds = await IndexableDocRepoDocuments()
            .OrderBy(document => document.Id)
            .Select(document => document.Id)
            .ToListAsync(cancellationToken);
        foreach (var page in docRepoIds.Chunk(DocumentPageSize))
        {
            yield return await BuildDocRepoDocumentsAsync(null, cancellationToken, page);
        }

        yield return await BuildFfcAsync(null, cancellationToken);
        yield return await BuildIprAsync(null, cancellationToken);
        yield return await BuildActivitiesAsync(null, cancellationToken);
        yield return await BuildVisitsAsync(null, cancellationToken);
        yield return await BuildSocialMediaAsync(null, cancellationToken);
        yield return await BuildTrainingAsync(null, cancellationToken);
        yield return await BuildTotsAsync(null, cancellationToken);
        yield return await BuildProliferationAsync(null, cancellationToken);
        yield return await BuildArppAsync(null, cancellationToken);
        yield return await BuildItProjectsAsync(null, cancellationToken);
    }

    public async Task<IReadOnlyList<SearchProjection>> BuildEntityAsync(
        string entityType,
        string entityKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityKey))
        {
            return Array.Empty<SearchProjection>();
        }

        return entityType switch
        {
            "Project" when int.TryParse(entityKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var projectId)
                => await BuildProjectsAsync(projectId, includeProjectDocuments: true, cancellationToken),
            "ProjectDocument" when int.TryParse(entityKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var projectDocumentId)
                => await BuildProjectDocumentsAsync(projectDocumentId, null, cancellationToken),
            "DocRepoDocument" when Guid.TryParse(entityKey, out var docRepoId)
                => await BuildDocRepoDocumentsAsync(docRepoId, cancellationToken),
            "FfcRecord" when long.TryParse(entityKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ffcId)
                => await BuildFfcAsync(ffcId, cancellationToken),
            "IprRecord" when int.TryParse(entityKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iprId)
                => await BuildIprAsync(iprId, cancellationToken),
            "Activity" when int.TryParse(entityKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var activityId)
                => await BuildActivitiesAsync(activityId, cancellationToken),
            "Visit" when Guid.TryParse(entityKey, out var visitId)
                => await BuildVisitsAsync(visitId, cancellationToken),
            "SocialMediaEvent" when Guid.TryParse(entityKey, out var socialId)
                => await BuildSocialMediaAsync(socialId, cancellationToken),
            "Training" when Guid.TryParse(entityKey, out var trainingId)
                => await BuildTrainingAsync(trainingId, cancellationToken),
            "ProjectTot" when int.TryParse(entityKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var totProjectId)
                => await BuildTotsAsync(totProjectId, cancellationToken),
            "ProliferationGranular" when Guid.TryParse(entityKey, out var proliferationId)
                => await BuildProliferationAsync(proliferationId, cancellationToken),
            "ArppIssue" when long.TryParse(entityKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var arppId)
                => await BuildArppAsync(arppId, cancellationToken),
            "ItProject" when int.TryParse(entityKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itProjectId)
                => await BuildItProjectsAsync(itProjectId, cancellationToken),
            _ => Array.Empty<SearchProjection>()
        };
    }

    public async Task<IReadOnlyList<SearchEntityRef>> FindProjectDependentsAsync(int projectId, CancellationToken cancellationToken)
    {
        var dependents = new List<SearchEntityRef>
        {
            // Keyed by project id; the builder decides whether the ToT is still eligible.
            new("ProjectTot", projectId.ToString(CultureInfo.InvariantCulture))
        };

        dependents.AddRange((await _db.IprRecords.AsNoTracking()
                .Where(row => row.ProjectId == projectId)
                .Select(row => row.Id)
                .ToListAsync(cancellationToken))
            .Select(id => new SearchEntityRef("IprRecord", id.ToString(CultureInfo.InvariantCulture))));
        dependents.AddRange((await _db.ProliferationGranularEntries.AsNoTracking()
                .Where(row => row.ProjectId == projectId)
                .Select(row => row.Id)
                .ToListAsync(cancellationToken))
            .Select(id => new SearchEntityRef("ProliferationGranular", id.ToString())));
        dependents.AddRange((await _db.TrainingProjects.AsNoTracking()
                .Where(row => row.ProjectId == projectId)
                .Select(row => row.TrainingId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(id => new SearchEntityRef("Training", id.ToString())));
        dependents.AddRange((await _db.FfcProjects.AsNoTracking()
                .Where(row => row.LinkedProjectId == projectId)
                .Select(row => row.FfcRecordId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(id => new SearchEntityRef("FfcRecord", id.ToString(CultureInfo.InvariantCulture))));
        dependents.AddRange((await _db.ArppEntries.AsNoTracking()
                .Where(row => row.ProjectId == projectId)
                .Select(row => row.ArppIssueId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(id => new SearchEntityRef("ArppIssue", id.ToString(CultureInfo.InvariantCulture))));

        return dependents;
    }

    private async Task<IReadOnlyList<SearchProjection>> BuildProjectsAsync(
        int? projectId,
        bool includeProjectDocuments,
        CancellationToken cancellationToken)
    {
        var baseQuery = _db.Projects.AsNoTracking().Where(project => !project.IsDeleted && !project.IsArchived);
        if (projectId.HasValue) baseQuery = baseQuery.Where(project => project.Id == projectId.Value);

        var projects = await baseQuery
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.ProjectBrief,
                project.Description,
                project.CaseFileNumber,
                project.ArmService,
                project.LifecycleStatus,
                project.WorkflowVersion,
                project.CreatedAt,
                project.ContentUpdatedAtUtc,
                Category = project.Category != null ? project.Category.Name : null,
                TechnicalCategory = project.TechnicalCategory != null ? project.TechnicalCategory.Name : null,
                SponsoringUnit = project.SponsoringUnit != null ? project.SponsoringUnit.Name : null,
                LineDirectorate = project.SponsoringLineDirectorate != null ? project.SponsoringLineDirectorate.Name : null
            })
            .ToListAsync(cancellationToken);

        if (projects.Count == 0) return Array.Empty<SearchProjection>();
        var ids = projects.Select(project => project.Id).ToArray();

        var capabilities = (await _db.ProjectCapabilityStatements.AsNoTracking()
                .Where(row => ids.Contains(row.ProjectId))
                .OrderBy(row => row.DisplayOrder)
                .Select(row => new { row.ProjectId, row.Statement })
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.ProjectId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Statement).ToArray());

        var specifications = (await _db.ProjectTechnicalSpecificationItems.AsNoTracking()
                .Where(row => ids.Contains(row.ProjectId))
                .OrderBy(row => row.DisplayOrder)
                .Select(row => new { row.ProjectId, row.Text })
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.ProjectId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Text).ToArray());

        var totSummaries = (await _db.ProjectTots.AsNoTracking()
                .Where(ProjectTotApplicabilityPolicy.EligibleTotPredicate)
                .Where(row => ids.Contains(row.ProjectId))
                .Select(row => new { row.ProjectId, row.Status })
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.ProjectId)
            .ToDictionary(group => group.Key, group => string.Join(" · ", group.Select(row => TotSummary(row.Status)).Distinct()));

        var iprSummaries = (await _db.IprRecords.AsNoTracking()
                .Where(row => row.ProjectId.HasValue
                              && ids.Contains(row.ProjectId.Value)
                              && row.Project != null
                              && !row.Project.IsBuild)
                .Select(row => new { ProjectId = row.ProjectId!.Value, row.Status, row.Type })
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.ProjectId)
            .ToDictionary(group => group.Key, group => string.Join(" · ", group.Select(row => $"IPR {row.Type} {row.Status}").Distinct()));

        var stages = await LoadCurrentStagesAsync(
            projects.ToDictionary(project => project.Id, project => (project.LifecycleStatus, project.WorkflowVersion)),
            cancellationToken);

        var results = new List<SearchProjection>(projects.Count + 8);
        foreach (var project in projects)
        {
            capabilities.TryGetValue(project.Id, out var capabilityRows);
            specifications.TryGetValue(project.Id, out var specificationRows);
            stages.TryGetValue(project.Id, out var currentStage);
            totSummaries.TryGetValue(project.Id, out var totSummary);
            iprSummaries.TryGetValue(project.Id, out var iprSummary);
            var aliases = ExtractAliases(project.Name);
            var identifiers = Values(project.CaseFileNumber);
            var structured = JoinNonEmpty(
                project.CaseFileNumber,
                project.ArmService,
                project.Category,
                project.TechnicalCategory,
                project.SponsoringUnit,
                project.LineDirectorate,
                currentStage,
                totSummary,
                iprSummary,
                string.Join(" · ", capabilityRows ?? Array.Empty<string>()),
                string.Join(" · ", specificationRows ?? Array.Empty<string>()));

            var narrative = JoinNonEmpty(project.ProjectBrief, project.Description);
            var terms = BuildTerms(identifiers, aliases);
            var updatedAt = project.ContentUpdatedAtUtc
                ?? new DateTimeOffset(DateTime.SpecifyKind(project.CreatedAt, DateTimeKind.Utc));

            results.Add(Project(
                "Project",
                project.Id.ToString(CultureInfo.InvariantCulture),
                "Project",
                project.Id.ToString(CultureInfo.InvariantCulture),
                project.Id,
                "Projects",
                "Projects",
                project.Name,
                currentStage is null ? project.LifecycleStatus.ToString() : $"{project.LifecycleStatus} · {currentStage}",
                _urls.ProjectOverview(project.Id),
                string.Join(' ', identifiers),
                string.Join(' ', aliases),
                structured,
                narrative,
                project.LifecycleStatus.ToString(),
                null,
                updatedAt,
                updatedAt,
                terms,
                new
                {
                    projectId = project.Id,
                    currentStage,
                    matchFields = new Dictionary<string, string?>
                    {
                        ["Case File Number"] = project.CaseFileNumber,
                        ["Project Brief"] = project.ProjectBrief,
                        ["Description"] = project.Description,
                        ["Capability"] = string.Join(" · ", capabilityRows ?? Array.Empty<string>()),
                        ["Technical Specification"] = string.Join(" · ", specificationRows ?? Array.Empty<string>()),
                        ["Category"] = JoinNonEmpty(project.Category, project.TechnicalCategory, project.ArmService),
                        ["Organisation"] = JoinNonEmpty(project.SponsoringUnit, project.LineDirectorate),
                        ["Lifecycle Stage"] = currentStage,
                        ["Transfer of Technology"] = totSummary,
                        ["IPR"] = iprSummary
                    }
                }));
        }

        if (includeProjectDocuments && projectId.HasValue)
        {
            results.AddRange(await BuildProjectDocumentsAsync(null, projectId, cancellationToken));
        }

        return results;
    }

    public async Task<SearchProjectDocumentsState?> GetProjectDocumentsStateAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await _db.Projects.AsNoTracking()
            .Where(row => row.Id == projectId && !row.IsDeleted && !row.IsArchived)
            .Select(row => new
            {
                row.Name,
                row.CaseFileNumber,
                Category = row.Category != null ? row.Category.Name : null,
                TechnicalCategory = row.TechnicalCategory != null ? row.TechnicalCategory.Name : null
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (project is null) return null;

        var contexts = await LoadProjectContextsAsync(new[] { projectId }, cancellationToken);
        contexts.TryGetValue(projectId, out var context);
        var documents = await IndexableProjectDocuments().CountAsync(document => document.ProjectId == projectId, cancellationToken);
        return new SearchProjectDocumentsState(
            ProjectDocumentContextStamp(project.Name, project.CaseFileNumber, project.Category, project.TechnicalCategory, context),
            documents);
    }

    public async IAsyncEnumerable<IReadOnlyList<SearchProjection>> BuildProjectWithDocumentsInBatchesAsync(
        int projectId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return await BuildProjectsAsync(projectId, includeProjectDocuments: false, cancellationToken);

        var documentIds = await IndexableProjectDocuments()
            .Where(document => document.ProjectId == projectId)
            .OrderBy(document => document.Id)
            .Select(document => document.Id)
            .ToListAsync(cancellationToken);
        foreach (var page in documentIds.Chunk(DocumentPageSize))
        {
            yield return await BuildProjectDocumentsAsync(null, projectId, cancellationToken, page);
        }
    }

    public Task<IReadOnlyList<SearchProjection>> BuildProjectRowAsync(int projectId, CancellationToken cancellationToken) =>
        BuildProjectsAsync(projectId, includeProjectDocuments: false, cancellationToken);

    /// <summary>
    /// Fingerprint of everything a document row copies from its project. Written into each
    /// document's metadata; a project edit rewrites the documents only when it changes.
    /// </summary>
    private static string ProjectDocumentContextStamp(
        string? projectName,
        string? caseFileNumber,
        string? category,
        string? technicalCategory,
        ProjectSearchContext? context)
    {
        var facts = string.Join(
            '\u001f',
            projectName,
            caseFileNumber,
            category,
            technicalCategory,
            context?.Category,
            context?.TechnicalCategory,
            context?.LifecycleStatus,
            context?.CurrentStage);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(facts)), 0, 12);
    }

    // Documents of trashed or archived projects are hidden with their project; the
    // project page itself is unreachable, so the document must not be discoverable.
    private IQueryable<ProjectDocument> IndexableProjectDocuments() =>
        _db.ProjectDocuments.AsNoTracking()
            .Where(document => document.Status == ProjectDocumentStatus.Published && !document.IsArchived)
            .Where(document => !document.Project.IsDeleted && !document.Project.IsArchived);

    // The repository's own rule for what an ordinary reader may open (active, not deleted).
    private IQueryable<ProjectManagement.Data.DocRepo.Document> IndexableDocRepoDocuments() =>
        ProjectManagement.Services.DocRepo.DocRepoAccess.ReadableFilter(_db.Documents.AsNoTracking(), canViewInactive: false);

    private async Task<IReadOnlyList<SearchProjection>> BuildProjectDocumentsAsync(
        int? documentId,
        int? projectId,
        CancellationToken cancellationToken,
        IReadOnlyCollection<int>? documentIds = null)
    {
        var query = IndexableProjectDocuments();
        if (documentId.HasValue) query = query.Where(document => document.Id == documentId.Value);
        if (projectId.HasValue) query = query.Where(document => document.ProjectId == projectId.Value);
        if (documentIds is not null) query = query.Where(document => documentIds.Contains(document.Id));

        var rows = await query.Select(document => new
        {
            document.Id,
            document.ProjectId,
            document.Title,
            document.Description,
            document.OriginalFileName,
            document.ContentType,
            document.UploadedAtUtc,
            ProjectName = document.Project.Name,
            ProjectCase = document.Project.CaseFileNumber,
            ProjectCategory = document.Project.Category != null ? document.Project.Category.Name : null,
            ProjectTechnicalCategory = document.Project.TechnicalCategory != null ? document.Project.TechnicalCategory.Name : null,
            OcrText = document.DocumentText != null ? document.DocumentText.OcrText : null,
            OcrUpdated = document.DocumentText != null ? (DateTimeOffset?)document.DocumentText.UpdatedAtUtc : null
        }).ToListAsync(cancellationToken);

        var projectContexts = await LoadProjectContextsAsync(rows.Select(row => row.ProjectId), cancellationToken);
        return rows.Select(row =>
        {
            projectContexts.TryGetValue(row.ProjectId, out var projectContext);
            var contexts = Values(
                row.OriginalFileName,
                row.ProjectName,
                row.ProjectCase,
                row.ProjectCategory,
                row.ProjectTechnicalCategory,
                projectContext?.LifecycleStatus,
                projectContext?.CurrentStage,
                row.Description)
                .Concat(ExtractAliases(row.ProjectName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var terms = BuildTerms(
                names: Values(row.ProjectName),
                contexts: contexts);
            var updated = row.OcrUpdated ?? row.UploadedAtUtc;
            return Project(
                "ProjectDocument",
                row.Id.ToString(CultureInfo.InvariantCulture),
                "ProjectDocument",
                row.Id.ToString(CultureInfo.InvariantCulture),
                row.ProjectId,
                "Project documents",
                "Documents",
                row.Title,
                row.ProjectName,
                _urls.ProjectDocumentPreview(row.Id),
                null,
                null,
                JoinNonEmpty(
                    row.ProjectName,
                    row.ProjectCase,
                    row.ProjectCategory,
                    row.ProjectTechnicalCategory,
                    projectContext?.LifecycleStatus,
                    projectContext?.CurrentStage,
                    row.Description,
                    row.OriginalFileName),
                row.OcrText,
                "Published",
                FileType(row.OriginalFileName, row.ContentType),
                row.UploadedAtUtc,
                updated,
                terms,
                new
                {
                    projectId = row.ProjectId,
                    documentId = row.Id,
                    currentStage = projectContext?.CurrentStage,
                    parentProjectStatus = projectContext?.LifecycleStatus,
                    parentProjectCategory = projectContext?.Category,
                    parentTechnicalCategory = projectContext?.TechnicalCategory,
                    projectContextStamp = ProjectDocumentContextStamp(row.ProjectName, row.ProjectCase, row.ProjectCategory, row.ProjectTechnicalCategory, projectContext),
                    matchFields = new Dictionary<string, string?>
                    {
                        ["Project"] = JoinNonEmpty(row.ProjectName, row.ProjectCase),
                        ["Project category"] = JoinNonEmpty(row.ProjectCategory, row.ProjectTechnicalCategory),
                        ["Lifecycle Stage"] = projectContext?.CurrentStage,
                        ["Filename"] = row.OriginalFileName,
                        ["Document metadata"] = row.Description
                    }
                });
        }).ToArray();
    }

    private async Task<IReadOnlyList<SearchProjection>> BuildDocRepoDocumentsAsync(
        Guid? documentId,
        CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? documentIds = null)
    {
        var query = IndexableDocRepoDocuments();
        if (documentId.HasValue) query = query.Where(document => document.Id == documentId.Value);
        if (documentIds is not null) query = query.Where(document => documentIds.Contains(document.Id));

        var rows = await query.Select(document => new
        {
            document.Id,
            document.Subject,
            document.ReceivedFrom,
            document.DocumentDate,
            document.OriginalFileName,
            document.MimeType,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            OfficeCategory = document.OfficeCategory.Name,
            DocumentCategory = document.DocumentCategory.Name,
            OcrText = document.DocumentText != null ? document.DocumentText.OcrText : null,
            OcrUpdated = document.DocumentText != null ? (DateTime?)document.DocumentText.UpdatedAtUtc : null
        }).ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var updated = new DateTimeOffset(DateTime.SpecifyKind(row.OcrUpdated ?? row.UpdatedAtUtc ?? row.CreatedAtUtc, DateTimeKind.Utc));
            var eventDate = row.DocumentDate.HasValue
                ? new DateTimeOffset(row.DocumentDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
            var terms = BuildTerms(
                organisations: Values(row.ReceivedFrom),
                contexts: Values(row.OriginalFileName, row.DocumentCategory, row.OfficeCategory));
            return Project(
                "DocRepoDocument",
                row.Id.ToString(),
                "DocRepoDocument",
                row.Id.ToString(),
                null,
                "Document repository",
                "Documents",
                row.Subject,
                JoinNonEmpty(row.DocumentCategory, row.OfficeCategory, row.ReceivedFrom),
                _urls.DocumentRepositoryView(row.Id),
                null,
                null,
                JoinNonEmpty(row.ReceivedFrom, row.DocumentCategory, row.OfficeCategory, row.OriginalFileName),
                row.OcrText,
                "Active",
                FileType(row.OriginalFileName, row.MimeType),
                eventDate,
                updated,
                terms,
                new
                {
                    documentId = row.Id,
                    matchFields = new Dictionary<string, string?>
                    {
                        ["Organisation"] = row.ReceivedFrom,
                        ["Document category"] = JoinNonEmpty(row.DocumentCategory, row.OfficeCategory),
                        ["Filename"] = row.OriginalFileName
                    }
                });
        }).ToArray();
    }

    private async Task<IReadOnlyList<SearchProjection>> BuildFfcAsync(long? recordId, CancellationToken cancellationToken)
    {
        var query = _db.FfcRecords.AsNoTracking().Where(record => !record.IsDeleted);
        if (recordId.HasValue) query = query.Where(record => record.Id == recordId.Value);
        var rows = await query.Select(record => new
        {
            record.Id,
            record.Year,
            record.CreatedAt,
            record.UpdatedAt,
            Country = record.Country.Name,
            record.OverallRemarks,
            record.IpaRemarks,
            record.GslRemarks,
            record.DeliveryRemarks,
            record.InstallationRemarks,
            Projects = record.Projects.Select(project => project.Name).ToArray(),
            LinkedProjectIds = record.Projects.Where(project => project.LinkedProjectId.HasValue).Select(project => project.LinkedProjectId!.Value).ToArray(),
            ProjectRemarks = record.Projects.Select(project => project.Remarks).ToArray(),
            Attachments = record.Attachments.Select(attachment => attachment.Caption).ToArray()
        }).ToListAsync(cancellationToken);

        var projectContexts = await LoadProjectContextsAsync(rows.SelectMany(row => row.LinkedProjectIds), cancellationToken);
        return rows.Select(row =>
        {
            var title = $"{row.Country} {row.Year.ToString(CultureInfo.InvariantCulture)}";
            var linkedContexts = row.LinkedProjectIds
                .Where(projectContexts.ContainsKey)
                .Select(id => projectContexts[id])
                .ToArray();
            var stageValues = linkedContexts.Select(context => context.CurrentStage).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var terms = BuildTerms(
                names: row.Projects,
                locations: Values(row.Country),
                contexts: row.Attachments.Concat(Values(row.Year.ToString(CultureInfo.InvariantCulture))));
            return Project(
                "FfcRecord",
                row.Id.ToString(CultureInfo.InvariantCulture),
                "FfcRecord",
                row.Id.ToString(CultureInfo.InvariantCulture),
                linkedContexts.Length == 1 ? linkedContexts[0].Id : null,
                "FFC",
                "Trackers",
                title,
                "Friendly Foreign Countries",
                _urls.FfcRecordDetails(row.Id),
                null,
                null,
                JoinNonEmpty(row.Country, row.Year.ToString(CultureInfo.InvariantCulture), string.Join(" · ", row.Projects), string.Join(" · ", row.Attachments), string.Join(" · ", stageValues!)),
                JoinNonEmpty(row.OverallRemarks, row.IpaRemarks, row.GslRemarks, row.DeliveryRemarks, row.InstallationRemarks, string.Join(" · ", row.ProjectRemarks.Where(value => !string.IsNullOrWhiteSpace(value)))),
                null,
                null,
                row.UpdatedAt,
                row.UpdatedAt,
                terms,
                new
                {
                    ffcRecordId = row.Id,
                    country = row.Country,
                    currentStage = stageValues.Length == 1 ? stageValues[0] : null,
                    projectStages = stageValues,
                    matchFields = new Dictionary<string, string?>
                    {
                        ["Location"] = row.Country,
                        ["Project"] = string.Join(" · ", row.Projects),
                        ["Lifecycle Stage"] = string.Join(" · ", stageValues!),
                        ["Attachment"] = string.Join(" · ", row.Attachments)
                    }
                });
        }).ToArray();
    }

    private async Task<IReadOnlyList<SearchProjection>> BuildIprAsync(int? recordId, CancellationToken cancellationToken)
    {
        var query = _db.IprRecords.AsNoTracking();
        if (recordId.HasValue) query = query.Where(record => record.Id == recordId.Value);
        var rows = await query.Select(record => new
        {
            record.Id,
            record.IprFilingNumber,
            record.Title,
            record.Notes,
            record.Type,
            record.Status,
            record.FiledBy,
            record.FiledAtUtc,
            record.GrantedAtUtc,
            record.ProjectId,
            ProjectIsBuild = record.Project != null && record.Project.IsBuild,
            // A trashed project is not shown as the record's project either.
            ProjectIsDeleted = record.Project != null && record.Project.IsDeleted,
            ProjectName = record.Project != null && !record.Project.IsBuild && !record.Project.IsDeleted ? record.Project.Name : null,
            Attachments = record.Attachments.Where(attachment => !attachment.IsArchived).Select(attachment => attachment.OriginalFileName).ToArray()
        }).ToListAsync(cancellationToken);

        var projectContexts = await LoadProjectContextsAsync(
            rows.Where(row => row.ProjectId.HasValue && !row.ProjectIsBuild && !row.ProjectIsDeleted).Select(row => row.ProjectId!.Value),
            cancellationToken);
        return rows.Select(row =>
        {
            var title = string.IsNullOrWhiteSpace(row.Title) ? row.IprFilingNumber : row.Title;
            var identifiers = Values(row.IprFilingNumber);
            var effectiveProjectId = row.ProjectId.HasValue && !row.ProjectIsBuild && !row.ProjectIsDeleted ? row.ProjectId : null;
            ProjectSearchContext? projectContext = null;
            if (effectiveProjectId.HasValue) projectContexts.TryGetValue(effectiveProjectId.Value, out projectContext);
            var contexts = Values(row.ProjectName, projectContext?.Category, projectContext?.TechnicalCategory, projectContext?.LifecycleStatus, projectContext?.CurrentStage)
                .Concat(row.Attachments)
                .ToArray();
            var updated = row.GrantedAtUtc ?? row.FiledAtUtc ?? DateTimeOffset.UnixEpoch;
            return Project(
                "IprRecord",
                row.Id.ToString(CultureInfo.InvariantCulture),
                effectiveProjectId.HasValue ? "Project" : "IprRecord",
                effectiveProjectId?.ToString(CultureInfo.InvariantCulture) ?? row.Id.ToString(CultureInfo.InvariantCulture),
                effectiveProjectId,
                "IPR",
                "Trackers",
                title,
                JoinNonEmpty(row.IprFilingNumber, row.ProjectName, row.Status.ToString()),
                _urls.IprRecordView(row.Id),
                string.Join(' ', identifiers),
                null,
                JoinNonEmpty(row.IprFilingNumber, row.Type.ToString(), row.Status.ToString(), row.FiledBy, row.ProjectName, projectContext?.Category, projectContext?.TechnicalCategory, projectContext?.CurrentStage, string.Join(" · ", row.Attachments)),
                row.Notes,
                row.Status.ToString(),
                null,
                row.GrantedAtUtc ?? row.FiledAtUtc,
                updated,
                BuildTerms(
                    identifiers: identifiers,
                    names: Values(row.ProjectName),
                    people: Values(row.FiledBy),
                    contexts: contexts),
                new
                {
                    iprRecordId = row.Id,
                    projectId = effectiveProjectId,
                    currentStage = projectContext?.CurrentStage,
                    parentProjectStatus = projectContext?.LifecycleStatus,
                    matchFields = new Dictionary<string, string?>
                    {
                        ["IPR Filing Number"] = row.IprFilingNumber,
                        ["Project"] = row.ProjectName,
                        ["Person / Organisation"] = row.FiledBy,
                        ["Lifecycle Stage"] = projectContext?.CurrentStage,
                        ["Attachment"] = string.Join(" · ", row.Attachments)
                    }
                });
        }).ToArray();
    }

    private async Task<IReadOnlyList<SearchProjection>> BuildActivitiesAsync(int? activityId, CancellationToken cancellationToken)
    {
        var query = _db.Activities.AsNoTracking().Where(activity => !activity.IsDeleted);
        if (activityId.HasValue) query = query.Where(activity => activity.Id == activityId.Value);
        var rows = await query.Select(activity => new
        {
            activity.Id,
            activity.Title,
            activity.Description,
            activity.Location,
            activity.ScheduledStartUtc,
            activity.CreatedAtUtc,
            activity.LastModifiedAtUtc,
            Type = activity.ActivityType.Name
        }).ToListAsync(cancellationToken);

        return rows.Select(row => Project(
            "Activity",
            row.Id.ToString(CultureInfo.InvariantCulture),
            "Activity",
            row.Id.ToString(CultureInfo.InvariantCulture),
            null,
            "Activities",
            "Organisation",
            row.Title,
            JoinNonEmpty(row.Type, row.Location),
            _urls.ActivityDetails(row.Id),
            null,
            null,
            JoinNonEmpty(row.Type, row.Location),
            row.Description,
            null,
            null,
            row.ScheduledStartUtc ?? row.CreatedAtUtc,
            row.LastModifiedAtUtc ?? row.CreatedAtUtc,
            BuildTerms(locations: Values(row.Location), contexts: Values(row.Type)),
            new
            {
                activityId = row.Id,
                matchFields = new Dictionary<string, string?>
                {
                    ["Location"] = row.Location,
                    ["Activity type"] = row.Type,
                    ["Description"] = row.Description
                }
            })).ToArray();
    }

    private async Task<IReadOnlyList<SearchProjection>> BuildVisitsAsync(Guid? visitId, CancellationToken cancellationToken)
    {
        var query = _db.Visits.AsNoTracking();
        if (visitId.HasValue) query = query.Where(visit => visit.Id == visitId.Value);
        var rows = await query.Select(visit => new
        {
            visit.Id,
            visit.VisitorName,
            visit.Strength,
            visit.Remarks,
            visit.DateOfVisit,
            visit.CreatedAtUtc,
            visit.LastModifiedAtUtc,
            Type = visit.VisitType != null ? visit.VisitType.Name : null
        }).ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var eventDate = new DateTimeOffset(row.DateOfVisit.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            return Project(
                "Visit",
                row.Id.ToString(),
                "Visit",
                row.Id.ToString(),
                null,
                "Visits",
                "Trackers",
                row.VisitorName,
                JoinNonEmpty(row.Type, $"Strength {row.Strength}"),
                _urls.ProjectOfficeVisitDetails(row.Id),
                null,
                null,
                JoinNonEmpty(row.Type, row.VisitorName, row.Strength.ToString(CultureInfo.InvariantCulture)),
                row.Remarks,
                null,
                null,
                eventDate,
                row.LastModifiedAtUtc ?? row.CreatedAtUtc,
                BuildTerms(people: Values(row.VisitorName), contexts: Values(row.Type)),
                new
                {
                    visitId = row.Id,
                    matchFields = new Dictionary<string, string?>
                    {
                        ["Person"] = row.VisitorName,
                        ["Visit type"] = row.Type,
                        ["Remarks"] = row.Remarks
                    }
                });
        }).ToArray();
    }

    private async Task<IReadOnlyList<SearchProjection>> BuildSocialMediaAsync(Guid? eventId, CancellationToken cancellationToken)
    {
        var query = _db.SocialMediaEvents.AsNoTracking();
        if (eventId.HasValue) query = query.Where(row => row.Id == eventId.Value);
        var rows = await query.Select(row => new
        {
            row.Id,
            row.Title,
            row.Description,
            row.DateOfEvent,
            row.CreatedAtUtc,
            row.LastModifiedAtUtc,
            EventType = row.SocialMediaEventType != null ? row.SocialMediaEventType.Name : null,
            Platform = row.SocialMediaPlatform != null ? row.SocialMediaPlatform.Name : null
        }).ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var date = new DateTimeOffset(row.DateOfEvent.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            return Project(
                "SocialMediaEvent", row.Id.ToString(), "SocialMediaEvent", row.Id.ToString(), null,
                "Social media", "Trackers", row.Title, JoinNonEmpty(row.Platform, row.EventType),
                _urls.ProjectOfficeSocialMediaDetails(row.Id), null, null,
                JoinNonEmpty(row.Platform, row.EventType), row.Description, null, null, date,
                row.LastModifiedAtUtc ?? row.CreatedAtUtc,
                BuildTerms(contexts: Values(row.Platform, row.EventType)),
                new
                {
                    socialMediaEventId = row.Id,
                    matchFields = new Dictionary<string, string?>
                    {
                        ["Platform"] = row.Platform,
                        ["Event type"] = row.EventType,
                        ["Description"] = row.Description
                    }
                });
        }).ToArray();
    }

    private async Task<IReadOnlyList<SearchProjection>> BuildTrainingAsync(Guid? trainingId, CancellationToken cancellationToken)
    {
        var query = _db.Trainings.AsNoTracking();
        if (trainingId.HasValue) query = query.Where(row => row.Id == trainingId.Value);
        var rows = await query.Select(row => new
        {
            row.Id,
            row.StartDate,
            row.EndDate,
            row.TrainingMonth,
            row.TrainingYear,
            row.Notes,
            row.CreatedAtUtc,
            row.LastModifiedAtUtc,
            Type = row.TrainingType != null ? row.TrainingType.Name : null,
            Projects = row.ProjectLinks.Where(link => link.Project != null && !link.Project.IsDeleted).Select(link => link.Project!.Name).ToArray(),
            ProjectIds = row.ProjectLinks.Select(link => link.ProjectId).ToArray(),
            Trainees = row.Trainees.Select(trainee => trainee.Rank + " " + trainee.Name + " " + trainee.UnitName + " " + trainee.ArmyNumber).ToArray()
        }).ToListAsync(cancellationToken);

        var projectContexts = await LoadProjectContextsAsync(rows.SelectMany(row => row.ProjectIds), cancellationToken);
        return rows.Select(row =>
        {
            var year = row.TrainingYear?.ToString(CultureInfo.InvariantCulture);
            var linked = row.ProjectIds.Where(projectContexts.ContainsKey).Select(id => projectContexts[id]).ToArray();
            var title = TrainingTitle(row.Projects, row.Type, year);
            var subtitle = JoinNonEmpty(
                row.Type is null ? null : $"{row.Type} training",
                year,
                row.Trainees.Length == 0 ? null : row.Trainees.Length == 1 ? "1 trainee" : $"{row.Trainees.Length} trainees");
            var stages = linked.Select(context => context.CurrentStage).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var eventDate = row.StartDate.HasValue ? new DateTimeOffset(row.StartDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : row.CreatedAtUtc;
            // A training for exactly one Project belongs to that Project's result cluster, as
            // ToT and Proliferation rows already do; multi-project trainings stand alone.
            var canonicalType = linked.Length == 1 ? "Project" : "Training";
            var canonicalKey = linked.Length == 1 ? linked[0].Id.ToString(CultureInfo.InvariantCulture) : row.Id.ToString();
            return Project(
                "Training", row.Id.ToString(), canonicalType, canonicalKey, linked.Length == 1 ? linked[0].Id : null,
                "Training tracker", "Trackers", title, subtitle,
                _urls.ProjectOfficeTrainingView(row.Id), null, null,
                JoinNonEmpty(row.Type, year, string.Join(" · ", row.Projects), string.Join(" · ", row.Trainees), string.Join(" · ", stages!)), row.Notes,
                null, null, eventDate, row.LastModifiedAtUtc ?? row.CreatedAtUtc,
                BuildTerms(names: row.Projects, people: row.Trainees, contexts: Values(row.Type, year).Concat(stages!)),
                new
                {
                    trainingId = row.Id,
                    currentStage = stages.Length == 1 ? stages[0] : null,
                    projectStages = stages,
                    matchFields = new Dictionary<string, string?>
                    {
                        ["Project"] = string.Join(" · ", row.Projects),
                        ["Person"] = string.Join(" · ", row.Trainees),
                        ["Training type"] = row.Type,
                        ["Lifecycle Stage"] = string.Join(" · ", stages!)
                    }
                });
        }).ToArray();
    }

    private async Task<IReadOnlyList<SearchProjection>> BuildTotsAsync(int? projectId, CancellationToken cancellationToken)
    {
        var query = _db.ProjectTots
            .AsNoTracking()
            .Where(ProjectTotApplicabilityPolicy.EligibleTotPredicate);
        if (projectId.HasValue) query = query.Where(row => row.ProjectId == projectId.Value);
        var rows = await query.Select(row => new
        {
            row.Id,
            row.ProjectId,
            row.Status,
            row.MetDetails,
            row.StartedOn,
            row.CompletedOn,
            row.LastApprovedOnUtc,
            ProjectName = row.Project.Name,
            ProjectCase = row.Project.CaseFileNumber,
            ProjectCreated = row.Project.CreatedAt
        }).ToListAsync(cancellationToken);

        var projectContexts = await LoadProjectContextsAsync(rows.Select(row => row.ProjectId), cancellationToken);
        return rows.Select(row =>
        {
            projectContexts.TryGetValue(row.ProjectId, out var projectContext);
            var genuineAliases = Values("ToT", "Transfer of Technology");
            var eventDate = row.CompletedOn.HasValue
                ? new DateTimeOffset(row.CompletedOn.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : row.StartedOn.HasValue
                    ? new DateTimeOffset(row.StartedOn.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                    : new DateTimeOffset(DateTime.SpecifyKind(row.ProjectCreated, DateTimeKind.Utc));
            var updated = row.LastApprovedOnUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(row.LastApprovedOnUtc.Value, DateTimeKind.Utc))
                : eventDate;
            return Project(
                "ProjectTot", row.ProjectId.ToString(CultureInfo.InvariantCulture), "Project", row.ProjectId.ToString(CultureInfo.InvariantCulture), row.ProjectId,
                "ToT", "Trackers", $"{row.ProjectName} — Transfer of Technology", ProjectTotPresentation.StatusLabel(row.Status),
                _urls.ProjectOfficeTotTracker(row.ProjectId), null, string.Join(' ', genuineAliases),
                JoinNonEmpty(row.ProjectName, row.ProjectCase, ProjectTotPresentation.StatusLabel(row.Status), "Transfer of Technology", projectContext?.Category, projectContext?.TechnicalCategory, projectContext?.CurrentStage), row.MetDetails,
                ProjectTotPresentation.StatusLabel(row.Status), null, eventDate, updated,
                BuildTerms(
                    aliases: genuineAliases,
                    names: Values(row.ProjectName),
                    contexts: Values(row.ProjectCase, projectContext?.Category, projectContext?.TechnicalCategory, projectContext?.LifecycleStatus, projectContext?.CurrentStage)),
                new
                {
                    projectId = row.ProjectId,
                    totId = row.Id,
                    currentStage = projectContext?.CurrentStage,
                    parentProjectStatus = projectContext?.LifecycleStatus,
                    matchFields = new Dictionary<string, string?>
                    {
                        ["Project"] = JoinNonEmpty(row.ProjectName, row.ProjectCase),
                        ["Lifecycle Stage"] = projectContext?.CurrentStage,
                        ["Transfer of Technology"] = JoinNonEmpty(ProjectTotPresentation.StatusLabel(row.Status), row.MetDetails)
                    }
                });
        }).ToArray();
    }

    private async Task<IReadOnlyList<SearchProjection>> BuildProliferationAsync(Guid? recordId, CancellationToken cancellationToken)
    {
        // ViewProliferationTracker readers only ever see Approved entries; pending and
        // rejected entries belong to the submit/approve workflow (ProliferationController).
        // Entries of trashed or archived projects drop out, as they do from the tracker's
        // totals and its project page (ProliferationProjectEligibility.CountedProjects).
        var countedProjectIds = ProliferationProjectEligibility.CountedProjects(_db.Projects).Select(project => project.Id);
        var query = _db.ProliferationGranularEntries.AsNoTracking()
            .Where(row => row.ApprovalStatus == ApprovalStatus.Approved)
            .Where(row => countedProjectIds.Contains(row.ProjectId));
        if (recordId.HasValue) query = query.Where(row => row.Id == recordId.Value);
        var rows = await query.Select(row => new
        {
            row.Id,
            row.ProjectId,
            row.Source,
            row.UnitName,
            row.ProliferationDate,
            row.Quantity,
            row.Remarks,
            row.ApprovalStatus,
            row.LastUpdatedOnUtc,
            ProjectName = _db.Projects.Where(project => project.Id == row.ProjectId).Select(project => project.Name).FirstOrDefault()
        }).ToListAsync(cancellationToken);

        var projectContexts = await LoadProjectContextsAsync(rows.Select(row => row.ProjectId), cancellationToken);
        return rows.Select(row =>
        {
            projectContexts.TryGetValue(row.ProjectId, out var projectContext);
            var projectName = string.IsNullOrWhiteSpace(row.ProjectName) ? $"Project {row.ProjectId}" : row.ProjectName;
            var date = new DateTimeOffset(row.ProliferationDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var updated = new DateTimeOffset(DateTime.SpecifyKind(row.LastUpdatedOnUtc, DateTimeKind.Utc));
            return Project(
                "ProliferationGranular", row.Id.ToString(), "Project", row.ProjectId.ToString(CultureInfo.InvariantCulture), row.ProjectId,
                "Proliferation", "Trackers", $"{projectName} — Proliferation", $"{row.Source} · {row.UnitName} · Qty {row.Quantity}",
                _urls.ProjectOfficeProliferationProject(row.ProjectId),
                null, null,
                JoinNonEmpty(projectName, row.UnitName, row.Source.ToString(), row.Quantity.ToString(CultureInfo.InvariantCulture), projectContext?.Category, projectContext?.TechnicalCategory, projectContext?.CurrentStage),
                row.Remarks, row.ApprovalStatus.ToString(), null, date, updated,
                BuildTerms(
                    names: Values(projectName),
                    organisations: Values(row.UnitName),
                    contexts: Values(row.Source.ToString(), projectContext?.Category, projectContext?.TechnicalCategory, projectContext?.LifecycleStatus, projectContext?.CurrentStage)),
                new
                {
                    projectId = row.ProjectId,
                    proliferationId = row.Id,
                    currentStage = projectContext?.CurrentStage,
                    parentProjectStatus = projectContext?.LifecycleStatus,
                    matchFields = new Dictionary<string, string?>
                    {
                        ["Project"] = projectName,
                        ["Organisation"] = row.UnitName,
                        ["Source"] = row.Source.ToString(),
                        ["Lifecycle Stage"] = projectContext?.CurrentStage,
                        ["Remarks"] = row.Remarks
                    }
                });
        }).ToArray();
    }

    private async Task<IReadOnlyList<SearchProjection>> BuildArppAsync(long? issueId, CancellationToken cancellationToken)
    {
        var query = _db.ArppIssues.AsNoTracking();
        if (issueId.HasValue) query = query.Where(issue => issue.Id == issueId.Value);
        var rows = await query.Select(issue => new
        {
            issue.Id,
            issue.Name,
            issue.FinancialYearStart,
            issue.Kind,
            issue.IssueSequence,
            issue.IssueDate,
            issue.IsVerified,
            issue.VerificationNote,
            issue.UpdatedAtUtc,
            Entries = issue.Entries.Select(entry => new
            {
                entry.SerialNumber,
                entry.PppNumber,
                entry.ProjectReference,
                entry.Cfa,
                entry.Fund,
                entry.DfpdsSchedule,
                entry.Category,
                entry.ProjectId,
                // A trashed project's name stays out of the index, as for IPR and Training.
                ProjectName = entry.Project != null && !entry.Project.IsDeleted ? entry.Project.Name : null
            }).ToArray()
        }).ToListAsync(cancellationToken);

        var projectContexts = await LoadProjectContextsAsync(rows.SelectMany(row => row.Entries.Where(entry => entry.ProjectId.HasValue).Select(entry => entry.ProjectId!.Value)), cancellationToken);
        return rows.Select(row =>
        {
            var identifiers = row.Entries
                .SelectMany(entry => Values(entry.SerialNumber, entry.PppNumber))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var contexts = row.Entries
                .SelectMany(entry => Values(entry.ProjectReference, entry.ProjectName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var details = row.Entries.Select(entry => JoinNonEmpty(
                    entry.SerialNumber,
                    entry.PppNumber,
                    entry.ProjectReference,
                    entry.ProjectName,
                    entry.Category.ToString(),
                    entry.Cfa,
                    entry.Fund,
                    entry.DfpdsSchedule))
                .Where(value => !string.IsNullOrWhiteSpace(value));
            var linked = row.Entries.Where(entry => entry.ProjectId.HasValue && projectContexts.ContainsKey(entry.ProjectId.Value)).Select(entry => projectContexts[entry.ProjectId!.Value]).DistinctBy(context => context.Id).ToArray();
            var stages = linked.Select(context => context.CurrentStage).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var fy = $"FY {row.FinancialYearStart}-{(row.FinancialYearStart + 1).ToString(CultureInfo.InvariantCulture)[^2..]}";
            var eventDate = new DateTimeOffset(row.IssueDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            return Project(
                "ArppIssue", row.Id.ToString(CultureInfo.InvariantCulture), "ArppIssue", row.Id.ToString(CultureInfo.InvariantCulture), linked.Length == 1 ? linked[0].Id : null,
                "ARPP", "Trackers", row.Name, $"{fy} · {(row.IsVerified ? "Verified" : "Working")}",
                _urls.ProjectOfficeArppDetails(row.Id),
                string.Join(' ', identifiers), null,
                JoinNonEmpty(fy, row.Kind.ToString(), string.Join(" · ", details), string.Join(" · ", stages!)), row.VerificationNote,
                row.IsVerified ? "Verified" : "Working", null, eventDate, row.UpdatedAtUtc,
                BuildTerms(identifiers: identifiers, names: contexts, contexts: Values(fy, row.Kind.ToString()).Concat(stages!)),
                new
                {
                    arppIssueId = row.Id,
                    currentStage = stages.Length == 1 ? stages[0] : null,
                    projectStages = stages,
                    matchFields = new Dictionary<string, string?>
                    {
                        ["ARPP / PPP Identifier"] = string.Join(" · ", identifiers),
                        ["Project"] = string.Join(" · ", contexts),
                        ["Lifecycle Stage"] = string.Join(" · ", stages!),
                        ["Verification note"] = row.VerificationNote
                    }
                });
        }).ToArray();
    }

    /// <summary>
    /// IT, MOLTI and SRE projects (one table, <see cref="ItProject.Module"/> tells them apart).
    /// Each module is its own source, so each follows its own pages' policy, and its rows
    /// open that module's page. Status and stage come from the module's own rule
    /// (<see cref="ItProjectStatus"/>), in that module's words. Trashed projects are left out;
    /// dropped ones stay, marked Dropped, as their board keeps them.
    /// </summary>
    private async Task<IReadOnlyList<SearchProjection>> BuildItProjectsAsync(int? itProjectId, CancellationToken cancellationToken)
    {
        var query = _db.ItProjects.AsNoTracking().Where(project => !project.IsDeleted);
        if (itProjectId.HasValue) query = query.Where(project => project.Id == itProjectId.Value);
        var projects = await query
            .Include(project => project.Stages)
            .Include(project => project.FinancialYears)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        if (projects.Count == 0) return Array.Empty<SearchProjection>();

        var officerIds = projects
            .Select(project => project.ProjectOfficerUserId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToArray();
        var officers = officerIds.Length == 0
            ? new Dictionary<string, string>()
            : await _db.Users.AsNoTracking()
                .Where(user => officerIds.Contains(user.Id))
                .Select(user => new { user.Id, user.FullName, user.Rank })
                .ToDictionaryAsync(user => user.Id, user => JoinNonEmpty(user.Rank, user.FullName)?.Replace(" · ", " ", StringComparison.Ordinal) ?? string.Empty, cancellationToken);

        return projects
            .Where(project => ItProjectModules.IsKnown(project.Module))
            .Select(project =>
            {
                var module = ItProjectModules.Get(project.Module);
                var status = ItProjectStatus.Resolve(project);
                var statusText = status.Kind switch
                {
                    ItProjectStatusKind.NotStarted => "Not started",
                    ItProjectStatusKind.InProgress => "In progress",
                    ItProjectStatusKind.Completed => "Completed",
                    _ => "Dropped"
                };
                var fy = "FY " + Utilities.FinancialYearHelper.Format(project.FinancialYearStart);
                var stage = status.Kind == ItProjectStatusKind.InProgress ? status.CurrentStageLabel : null;
                var officer = project.ProjectOfficerUserId is { Length: > 0 } officerId && officers.TryGetValue(officerId, out var name) && name.Length > 0
                    ? name
                    : null;
                // The case number of the project's first year and of every later year.
                var references = Values(new[] { project.PppReferenceNumber }
                    .Concat(project.FinancialYears.OrderBy(year => year.FinancialYearStart).Select(year => year.PppReferenceNumber))
                    .ToArray());
                var key = project.Id.ToString(CultureInfo.InvariantCulture);
                return Project(
                    "ItProject",
                    key,
                    "ItProject",
                    key,
                    null,
                    module.Name,
                    "Projects",
                    project.Title,
                    JoinNonEmpty(module.Singular, string.Join(" · ", references), fy, stage is null ? statusText : $"At {stage}"),
                    $"{module.RoutePrefix}/{key}",
                    references.Length == 0 ? null : string.Join(' ', references),
                    null,
                    // Only what the subtitle does not already say: the card shows this line when the
                    // description does not match. Module, year and stage stay searchable as terms.
                    officer is null ? null : "Project Officer " + officer,
                    project.Description,
                    statusText,
                    null,
                    null,
                    new DateTimeOffset(DateTime.SpecifyKind(project.UpdatedAtUtc, DateTimeKind.Utc)),
                    BuildTerms(identifiers: references, people: officer is null ? null : Values(officer), contexts: Values(module.Singular, fy, stage)),
                    new
                    {
                        itProjectId = project.Id,
                        module = project.Module,
                        matchFields = new Dictionary<string, string?>
                        {
                            [$"{module.ListName} reference number"] = references.Length == 0 ? null : string.Join(" · ", references),
                            ["Project Officer"] = officer,
                            ["Stage"] = stage,
                            ["Description"] = project.Description
                        }
                    });
            })
            .ToArray();
    }

    /// <summary>"ToT not required", in the words the project pages use.</summary>
    internal static string TotSummary(ProjectTotStatus status) =>
        "ToT " + ProjectTotPresentation.StatusLabel(status).ToLowerInvariant();

    /// <summary>
    /// Each project's current stage code by the app's one rule (<see cref="PresentStageHelper"/>,
    /// lifecycle-aware): an active project is at its first in-progress stage, else the first
    /// stage not completed or skipped; a completed or cancelled project stays on its latest
    /// completed stage. Search must not keep a rule of its own (a Blocked stage, a finished
    /// project).
    /// </summary>
    private async Task<Dictionary<int, string?>> LoadCurrentStagesAsync(
        IReadOnlyDictionary<int, (ProjectLifecycleStatus Lifecycle, string? WorkflowVersion)> projects,
        CancellationToken cancellationToken)
    {
        var ids = projects.Keys.ToArray();
        var rows = (await _db.ProjectStages.AsNoTracking()
                .Where(stage => ids.Contains(stage.ProjectId))
                .Select(stage => new { stage.ProjectId, stage.StageCode, stage.SortOrder, stage.Status, stage.ActualStart, stage.CompletedOn })
                .ToListAsync(cancellationToken))
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ProjectStageStatusSnapshot>)group
                    .Select(stage => new ProjectStageStatusSnapshot(stage.StageCode!, stage.Status, stage.SortOrder, stage.ActualStart, stage.CompletedOn))
                    .ToList());

        return projects.ToDictionary(
            pair => pair.Key,
            pair => PresentStageHelper.ComputePresentStageAndAge(
                    rows.TryGetValue(pair.Key, out var stages) ? stages : Array.Empty<ProjectStageStatusSnapshot>(),
                    _workflowStages,
                    pair.Value.WorkflowVersion,
                    pair.Value.Lifecycle)
                .CurrentStageCode);
    }

    private async Task<IReadOnlyDictionary<int, ProjectSearchContext>> LoadProjectContextsAsync(
        IEnumerable<int> projectIds,
        CancellationToken cancellationToken)
    {
        var ids = projectIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<int, ProjectSearchContext>();

        var projects = await _db.Projects.AsNoTracking()
            .Where(project => ids.Contains(project.Id) && !project.IsDeleted)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.LifecycleStatus,
                project.WorkflowVersion,
                Category = project.Category != null ? project.Category.Name : null,
                TechnicalCategory = project.TechnicalCategory != null ? project.TechnicalCategory.Name : null
            })
            .ToListAsync(cancellationToken);

        var stages = await LoadCurrentStagesAsync(
            projects.ToDictionary(project => project.Id, project => (project.LifecycleStatus, project.WorkflowVersion)),
            cancellationToken);

        return projects.ToDictionary(
            project => project.Id,
            project =>
            {
                stages.TryGetValue(project.Id, out var currentStage);
                return new ProjectSearchContext(
                    project.Id,
                    project.Name,
                    project.Category,
                    project.TechnicalCategory,
                    project.LifecycleStatus.ToString(),
                    currentStage);
            });
    }

    private SearchProjection Project(
        string entityType,
        string entityKey,
        string canonicalEntityType,
        string canonicalEntityKey,
        int? parentProjectId,
        string sourceModule,
        string resultCategory,
        string title,
        string? subtitle,
        string url,
        string? identifierText,
        string? aliasText,
        string? structuredText,
        string? narrativeText,
        string? status,
        string? fileType,
        DateTimeOffset? eventDate,
        DateTimeOffset updatedAt,
        IReadOnlyList<SearchProjectionTerm> terms,
        object metadata)
    {
        var fuzzy = JoinNonEmpty(title, aliasText, identifierText, structuredText) ?? title;
        var metadataNode = JsonSerializer.SerializeToNode(metadata) as JsonObject ?? new JsonObject();
        metadataNode["searchTextQuality"] = SearchTextQuality.Score(narrativeText);
        return new SearchProjection(
            entityType,
            entityKey,
            canonicalEntityType,
            canonicalEntityKey,
            parentProjectId,
            sourceModule,
            resultCategory,
            title,
            _normalizer.NormalizeExact(title),
            subtitle,
            url,
            identifierText,
            aliasText,
            structuredText,
            narrativeText,
            _normalizer.NormalizeExact(fuzzy),
            status,
            fileType,
            eventDate,
            updatedAt,
            SearchVisibilityMode.Authenticated,
            SearchSourcePolicies.RequiredPolicyFor(sourceModule),
            null,
            _options.ProjectionVersion,
            terms,
            Array.Empty<SearchProjectionPrincipal>(),
            metadataNode.ToJsonString());
    }

    private IReadOnlyList<SearchProjectionTerm> BuildTerms(
        IEnumerable<string>? identifiers = null,
        IEnumerable<string>? aliases = null,
        IEnumerable<string>? names = null,
        IEnumerable<string>? organisations = null,
        IEnumerable<string>? locations = null,
        IEnumerable<string>? people = null,
        IEnumerable<string>? contexts = null)
    {
        var terms = new List<SearchProjectionTerm>();
        AddTerms(terms, identifiers, SearchTermKinds.Identifier);
        AddTerms(terms, aliases, SearchTermKinds.Alias);
        AddTerms(terms, names, SearchTermKinds.Name);
        AddTerms(terms, organisations, SearchTermKinds.Organisation);
        AddTerms(terms, locations, SearchTermKinds.Location);
        AddTerms(terms, people, SearchTermKinds.Person);
        AddTerms(terms, contexts, SearchTermKinds.Context);
        return terms
            .Where(term => !string.IsNullOrWhiteSpace(term.NormalizedTerm))
            .DistinctBy(term => $"{term.Kind}\u001f{term.NormalizedTerm}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void AddTerms(ICollection<SearchProjectionTerm> target, IEnumerable<string>? values, string kind)
    {
        if (values is null) return;
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalized = _normalizer.NormalizeExact(value);
            target.Add(new SearchProjectionTerm(value, normalized, kind));

            // Separator-insensitive companion key so PRJ0042, "prj 42" and PRJ-0042 meet.
            if (kind == SearchTermKinds.Identifier)
            {
                var key = SearchQueryNormalizer.IdentifierKey(value);
                if (key.Length >= 2) target.Add(new SearchProjectionTerm(value, key, SearchTermKinds.IdentifierKey));
            }
        }
    }

    /// <summary>
    /// Training records are identified by what was trained on, not by the generic training
    /// type (hundreds of rows titled just "Simulator" drown the Projects they relate to).
    /// </summary>
    internal static string TrainingTitle(IReadOnlyList<string> projectNames, string? trainingType, string? year)
    {
        var projects = projectNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return projects.Length switch
        {
            0 => JoinNonEmpty(trainingType is null ? "Training" : $"{trainingType} training", year)?.Replace(" · ", " ", StringComparison.Ordinal) ?? "Training record",
            1 => $"{projects[0]} — Training",
            _ => $"{projects[0]} (+{projects.Length - 1} more) — Training"
        };
    }

    private static string[] Values(params string?[] values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string? JoinNonEmpty(params string?[] values)
    {
        var filtered = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).ToArray();
        return filtered.Length == 0 ? null : string.Join(" · ", filtered);
    }

    private static string[] ExtractAliases(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return Array.Empty<string>();
        var aliases = new List<string>();
        foreach (Match match in ParentheticalRegex().Matches(title))
        {
            var value = match.Groups[1].Value.Trim();
            if (value.Length is >= 2 and <= 40) aliases.Add(value);
        }

        foreach (Match match in AcronymRegex().Matches(title))
        {
            var value = match.Value.Trim();
            if (value.Length is >= 2 and <= 20) aliases.Add(value);
        }

        return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? FileType(string? fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(extension)) return extension.TrimStart('.').ToUpperInvariant();
        if (string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase)) return "PDF";
        return null;
    }

    private sealed record ProjectSearchContext(
        int Id,
        string Name,
        string? Category,
        string? TechnicalCategory,
        string LifecycleStatus,
        string? CurrentStage);

    [GeneratedRegex("\\(([^()]{2,40})\\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParentheticalRegex();

    [GeneratedRegex("\\b[A-Z][A-Z0-9/-]{1,19}\\b", RegexOptions.CultureInvariant)]
    private static partial Regex AcronymRegex();
}
