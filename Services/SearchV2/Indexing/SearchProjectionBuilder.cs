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
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Query;

namespace ProjectManagement.Services.SearchV2.Indexing;

public interface ISearchProjectionBuilder
{
    Task<IReadOnlyList<SearchProjection>> BuildAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SearchProjection>> BuildEntityAsync(string entityType, string entityKey, CancellationToken cancellationToken);
}

public sealed partial class SearchProjectionBuilder : ISearchProjectionBuilder
{
    private readonly ApplicationDbContext _db;
    private readonly IUrlBuilder _urls;
    private readonly ISearchQueryNormalizer _normalizer;
    private readonly SearchV2Options _options;

    public SearchProjectionBuilder(
        ApplicationDbContext db,
        IUrlBuilder urls,
        ISearchQueryNormalizer normalizer,
        IOptions<SearchV2Options> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _urls = urls ?? throw new ArgumentNullException(nameof(urls));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<IReadOnlyList<SearchProjection>> BuildAllAsync(CancellationToken cancellationToken)
    {
        var results = new List<SearchProjection>(4096);
        results.AddRange(await BuildProjectsAsync(null, includeProjectDocuments: false, cancellationToken));
        results.AddRange(await BuildProjectDocumentsAsync(null, null, cancellationToken));
        results.AddRange(await BuildDocRepoDocumentsAsync(null, cancellationToken));
        results.AddRange(await BuildFfcAsync(null, cancellationToken));
        results.AddRange(await BuildIprAsync(null, cancellationToken));
        results.AddRange(await BuildActivitiesAsync(null, cancellationToken));
        results.AddRange(await BuildVisitsAsync(null, cancellationToken));
        results.AddRange(await BuildSocialMediaAsync(null, cancellationToken));
        results.AddRange(await BuildTrainingAsync(null, cancellationToken));
        results.AddRange(await BuildTotsAsync(null, cancellationToken));
        results.AddRange(await BuildProliferationAsync(null, cancellationToken));
        results.AddRange(await BuildArppAsync(null, cancellationToken));
        return results;
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
            _ => Array.Empty<SearchProjection>()
        };
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
            .ToDictionary(group => group.Key, group => string.Join(" · ", group.Select(row => $"ToT {row.Status}").Distinct()));

        var iprSummaries = (await _db.IprRecords.AsNoTracking()
                .Where(row => row.ProjectId.HasValue
                              && ids.Contains(row.ProjectId.Value)
                              && row.Project != null
                              && !row.Project.IsBuild)
                .Select(row => new { ProjectId = row.ProjectId!.Value, row.Status, row.Type })
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.ProjectId)
            .ToDictionary(group => group.Key, group => string.Join(" · ", group.Select(row => $"IPR {row.Type} {row.Status}").Distinct()));

        var stages = (await _db.ProjectStages.AsNoTracking()
                .Where(stage => ids.Contains(stage.ProjectId))
                .Select(stage => new { stage.ProjectId, stage.StageCode, stage.SortOrder, stage.Status })
                .ToListAsync(cancellationToken))
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Where(stage => stage.Status == StageStatus.InProgress)
                    .OrderByDescending(stage => stage.SortOrder)
                    .Select(stage => stage.StageCode)
                    .FirstOrDefault()
                    ?? group.Where(stage => stage.Status == StageStatus.NotStarted)
                        .OrderBy(stage => stage.SortOrder)
                        .Select(stage => stage.StageCode)
                        .FirstOrDefault());

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
                null,
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

    private async Task<IReadOnlyList<SearchProjection>> BuildProjectDocumentsAsync(
        int? documentId,
        int? projectId,
        CancellationToken cancellationToken)
    {
        var query = _db.ProjectDocuments.AsNoTracking()
            .Where(document => document.Status == ProjectDocumentStatus.Published && !document.IsArchived);
        if (documentId.HasValue) query = query.Where(document => document.Id == documentId.Value);
        if (projectId.HasValue) query = query.Where(document => document.ProjectId == projectId.Value);

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
                null,
                terms,
                new
                {
                    projectId = row.ProjectId,
                    documentId = row.Id,
                    currentStage = projectContext?.CurrentStage,
                    parentProjectStatus = projectContext?.LifecycleStatus,
                    parentProjectCategory = projectContext?.Category,
                    parentTechnicalCategory = projectContext?.TechnicalCategory,
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

    private async Task<IReadOnlyList<SearchProjection>> BuildDocRepoDocumentsAsync(Guid? documentId, CancellationToken cancellationToken)
    {
        var query = _db.Documents.AsNoTracking().Where(document => document.IsActive && !document.IsDeleted);
        if (documentId.HasValue) query = query.Where(document => document.Id == documentId.Value);

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
                Policies.Documents.View,
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
                "Foreign Field Cooperation",
                _urls.FfcRecordDetails(row.Id),
                null,
                null,
                JoinNonEmpty(row.Country, row.Year.ToString(CultureInfo.InvariantCulture), string.Join(" · ", row.Projects), string.Join(" · ", row.Attachments), string.Join(" · ", stageValues!)),
                JoinNonEmpty(row.OverallRemarks, row.IpaRemarks, row.GslRemarks, row.DeliveryRemarks, row.InstallationRemarks, string.Join(" · ", row.ProjectRemarks.Where(value => !string.IsNullOrWhiteSpace(value)))),
                null,
                null,
                row.UpdatedAt,
                row.UpdatedAt,
                null,
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
            ProjectName = record.Project != null && !record.Project.IsBuild ? record.Project.Name : null,
            Attachments = record.Attachments.Where(attachment => !attachment.IsArchived).Select(attachment => attachment.OriginalFileName).ToArray()
        }).ToListAsync(cancellationToken);

        var projectContexts = await LoadProjectContextsAsync(
            rows.Where(row => row.ProjectId.HasValue && !row.ProjectIsBuild).Select(row => row.ProjectId!.Value),
            cancellationToken);
        return rows.Select(row =>
        {
            var title = string.IsNullOrWhiteSpace(row.Title) ? row.IprFilingNumber : row.Title;
            var identifiers = Values(row.IprFilingNumber);
            var effectiveProjectId = row.ProjectId.HasValue && !row.ProjectIsBuild ? row.ProjectId : null;
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
                Policies.Ipr.View,
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
            null,
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
                ProjectOfficeReportsPolicies.ViewVisits,
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
                row.LastModifiedAtUtc ?? row.CreatedAtUtc, null,
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
            Projects = row.ProjectLinks.Where(link => link.Project != null).Select(link => link.Project!.Name).ToArray(),
            ProjectIds = row.ProjectLinks.Select(link => link.ProjectId).ToArray(),
            Trainees = row.Trainees.Select(trainee => trainee.Rank + " " + trainee.Name + " " + trainee.UnitName + " " + trainee.ArmyNumber).ToArray()
        }).ToListAsync(cancellationToken);

        var projectContexts = await LoadProjectContextsAsync(rows.SelectMany(row => row.ProjectIds), cancellationToken);
        return rows.Select(row =>
        {
            var year = row.TrainingYear?.ToString(CultureInfo.InvariantCulture);
            var title = JoinNonEmpty(row.Type, year) ?? "Training record";
            var linked = row.ProjectIds.Where(projectContexts.ContainsKey).Select(id => projectContexts[id]).ToArray();
            var stages = linked.Select(context => context.CurrentStage).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var eventDate = row.StartDate.HasValue ? new DateTimeOffset(row.StartDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : row.CreatedAtUtc;
            return Project(
                "Training", row.Id.ToString(), "Training", row.Id.ToString(), linked.Length == 1 ? linked[0].Id : null,
                "Training tracker", "Trackers", title, string.Join(" · ", row.Projects),
                _urls.ProjectOfficeTrainingView(row.Id), null, null,
                JoinNonEmpty(row.Type, year, string.Join(" · ", row.Projects), string.Join(" · ", row.Trainees), string.Join(" · ", stages!)), row.Notes,
                null, null, eventDate, row.LastModifiedAtUtc ?? row.CreatedAtUtc,
                ProjectOfficeReportsPolicies.ViewTrainingTracker,
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
                "ToT", "Trackers", $"{row.ProjectName} — Transfer of Technology", row.Status.ToString(),
                _urls.ProjectOfficeTotTracker(row.ProjectId), null, string.Join(' ', genuineAliases),
                JoinNonEmpty(row.ProjectName, row.ProjectCase, row.Status.ToString(), "Transfer of Technology", projectContext?.Category, projectContext?.TechnicalCategory, projectContext?.CurrentStage), row.MetDetails,
                row.Status.ToString(), null, eventDate, updated, ProjectOfficeReportsPolicies.ViewTotTracker,
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
                        ["Transfer of Technology"] = JoinNonEmpty(row.Status.ToString(), row.MetDetails)
                    }
                });
        }).ToArray();
    }

    private async Task<IReadOnlyList<SearchProjection>> BuildProliferationAsync(Guid? recordId, CancellationToken cancellationToken)
    {
        var query = _db.ProliferationGranularEntries.AsNoTracking();
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
                ProjectOfficeReportsPolicies.ViewProliferationTracker,
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
                ProjectName = entry.Project != null ? entry.Project.Name : null
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
                ProjectOfficeReportsPolicies.ViewArpp,
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
                Category = project.Category != null ? project.Category.Name : null,
                TechnicalCategory = project.TechnicalCategory != null ? project.TechnicalCategory.Name : null
            })
            .ToListAsync(cancellationToken);

        var stages = (await _db.ProjectStages.AsNoTracking()
                .Where(stage => ids.Contains(stage.ProjectId))
                .Select(stage => new { stage.ProjectId, stage.StageCode, stage.SortOrder, stage.Status })
                .ToListAsync(cancellationToken))
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Where(stage => stage.Status == StageStatus.InProgress)
                    .OrderByDescending(stage => stage.SortOrder)
                    .Select(stage => stage.StageCode)
                    .FirstOrDefault()
                    ?? group.Where(stage => stage.Status == StageStatus.NotStarted)
                        .OrderBy(stage => stage.SortOrder)
                        .Select(stage => stage.StageCode)
                        .FirstOrDefault());

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
        string? requiredPolicy,
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
            requiredPolicy,
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
            target.Add(new SearchProjectionTerm(value, _normalizer.NormalizeExact(value), kind));
        }
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
