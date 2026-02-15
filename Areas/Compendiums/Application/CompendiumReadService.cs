using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.Compendiums.Application.Dto;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services.Projects;
using System.Globalization;

namespace ProjectManagement.Areas.Compendiums.Application;

// SECTION: Compendium read service implementation
public sealed class CompendiumReadService : ICompendiumReadService
{
    // SECTION: Build stamp for runtime verification across environments
    public const string BuildStamp = "CompendiumsQuery_2026-02-15_zip27";

    private readonly ApplicationDbContext _db;
    private readonly IProjectPhotoService _projectPhotoService;
    private readonly IProliferationMetricsService _metricsService;
    private readonly ILogger<CompendiumReadService> _logger;

    public CompendiumReadService(
        ApplicationDbContext db,
        IProjectPhotoService projectPhotoService,
        IProliferationMetricsService metricsService,
        ILogger<CompendiumReadService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _projectPhotoService = projectPhotoService ?? throw new ArgumentNullException(nameof(projectPhotoService));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<CompendiumProjectCardDto>> GetEligibleProjectsAsync(CancellationToken cancellationToken)
    {
        // SECTION: Runtime stamp to verify deployed query implementation
        _logger.LogWarning("CompendiumReadService.BuildStamp={BuildStamp}", BuildStamp);

        // SECTION: Eligible project list query with required ordering
        return await BuildEligibleProjectCardQuery()
            .Select(x => new CompendiumProjectCardDto
            {
                ProjectId = x.Id,
                Name = x.Name,
                CompletionYearText = ResolveCompletionYearText(x.CompletedYear, x.CompletedOn),
                SponsoringLineDirectorateName = x.SponsoringLineDirectorateName ?? "Not recorded",
                ArmService = string.IsNullOrWhiteSpace(x.ArmService) ? "Not recorded" : x.ArmService!,
                ProliferationCostLakhs = x.ApproxProductionCost,
                HasCoverPhoto = x.CoverPhotoId.HasValue,
                CoverPhotoId = x.CoverPhotoId,
                CoverPhotoVersion = x.CoverPhotoVersion
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<CompendiumProjectDetailDto?> GetProjectAsync(int projectId, bool includeHistoricalExtras, CancellationToken cancellationToken)
    {
        // SECTION: Eligible project detail query
        var project = await BuildEligibleProjectDetailQuery()
            .Where(x => x.Id == projectId)
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return null;
        }

        var coverPhotoBytesByProject = await ResolveCoverPhotosAsync(new[] { project }, cancellationToken);
        var coverPhotoBytes = coverPhotoBytesByProject.TryGetValue(project.Id, out var bytes) ? bytes : null;
        var coverPhotoAvailable = coverPhotoBytes is { Length: > 0 };

        var historicalExtrasByProject = includeHistoricalExtras
            ? await ResolveHistoricalExtrasAsync(new[] { project }, cancellationToken)
            : new Dictionary<int, HistoricalExtrasDto>();

        return MapToDetail(project, coverPhotoBytes, coverPhotoAvailable, historicalExtrasByProject.TryGetValue(project.Id, out var extras) ? extras : null);
    }

    public async Task<IReadOnlyList<CompendiumProjectDetailDto>> GetEligibleProjectDetailsAsync(bool includeHistoricalExtras, CancellationToken cancellationToken)
    {
        // SECTION: Bulk eligible project detail query
        var projects = await BuildEligibleProjectDetailQuery().ToListAsync(cancellationToken);
        if (projects.Count == 0)
        {
            return Array.Empty<CompendiumProjectDetailDto>();
        }

        // SECTION: Resolve external dependencies in controlled loops
        var coverPhotoBytesByProject = await ResolveCoverPhotosAsync(projects, cancellationToken);
        var historicalExtrasByProject = includeHistoricalExtras
            ? await ResolveHistoricalExtrasAsync(projects, cancellationToken)
            : new Dictionary<int, HistoricalExtrasDto>();

        // SECTION: Map projected rows to detail DTOs
        var details = new List<CompendiumProjectDetailDto>(projects.Count);
        foreach (var project in projects)
        {
            coverPhotoBytesByProject.TryGetValue(project.Id, out var coverPhotoBytes);
            historicalExtrasByProject.TryGetValue(project.Id, out var historicalExtras);

            details.Add(MapToDetail(
                project,
                coverPhotoBytes,
                coverPhotoBytes is { Length: > 0 },
                historicalExtras));
        }

        return details;
    }

    // SECTION: Base eligibility + ordering for eligible project cards
    private IQueryable<CompendiumProjectCardProjection> BuildEligibleProjectCardQuery()
    {
        // SECTION: Scalar projection for projects to avoid entity materialization on legacy nullable columns
        var projectsQ =
            from project in _db.Projects.AsNoTracking()
            let isDeleted = EF.Property<bool?>(project, nameof(Project.IsDeleted)) ?? false
            let isArchived = EF.Property<bool?>(project, nameof(Project.IsArchived)) ?? false
            let lifecycleStatus = EF.Property<ProjectLifecycleStatus?>(project, nameof(Project.LifecycleStatus)) ?? ProjectLifecycleStatus.Active
            let completedOn = EF.Property<DateOnly?>(project, nameof(Project.CompletedOn))
            let completedYear = EF.Property<int?>(project, nameof(Project.CompletedYear))
            let coverPhotoVersion = EF.Property<int?>(project, nameof(Project.CoverPhotoVersion))
            select new
            {
                project.Id,
                project.Name,
                project.CoverPhotoId,
                CoverPhotoVersion = coverPhotoVersion,
                CompletedOn = completedOn,
                CompletedYear = completedYear,
                project.ArmService,
                SponsoringLineDirectorateName = project.SponsoringLineDirectorate != null ? project.SponsoringLineDirectorate!.Name : null,
                IsDeleted = isDeleted,
                IsArchived = isArchived,
                LifecycleStatus = lifecycleStatus
            };

        // SECTION: Scalar projection for tech statuses
        var techStatusesQ =
            from techStatus in _db.ProjectTechStatuses.AsNoTracking()
            let projectId = EF.Property<int?>(techStatus, nameof(ProjectTechStatus.ProjectId))
            where projectId.HasValue
            select new
            {
                ProjectId = projectId.Value,
                AvailableForProliferation = EF.Property<bool?>(techStatus, nameof(ProjectTechStatus.AvailableForProliferation))
            };

        // SECTION: Scalar projection for optional production costs
        var productionCostsQ =
            from costFact in _db.ProjectProductionCostFacts.AsNoTracking()
            let projectId = EF.Property<int?>(costFact, nameof(ProjectProductionCostFact.ProjectId))
            where projectId.HasValue
            select new
            {
                ProjectId = projectId.Value,
                costFact.ApproxProductionCost
            };

        var query =
            from project in projectsQ
            join techStatus in techStatusesQ on project.Id equals techStatus.ProjectId
            join costFact in productionCostsQ on project.Id equals costFact.ProjectId into costJoin
            from costFact in costJoin.DefaultIfEmpty()
            where project.IsDeleted == false
                  && project.IsArchived == false
                  && project.LifecycleStatus == ProjectLifecycleStatus.Completed
                  && (techStatus.AvailableForProliferation ?? false) == true
            orderby project.SponsoringLineDirectorateName ?? string.Empty,
                project.CompletedYear descending,
                project.CompletedOn descending,
                project.Name
            select new CompendiumProjectCardProjection(
                project.Id,
                project.Name,
                project.CompletedYear,
                project.CompletedOn,
                project.SponsoringLineDirectorateName,
                project.ArmService,
                costFact != null ? costFact.ApproxProductionCost : null,
                project.CoverPhotoId,
                project.CoverPhotoVersion);

        return query;
    }

    // SECTION: Base eligibility + ordering projection for detail and historical extras paths
    // Guardrail: if a model property is non-nullable but legacy database rows can store NULL,
    // read it with EF.Property<T?> in this projection to avoid nullable materialization crashes.
    private IQueryable<CompendiumProjection> BuildEligibleProjectDetailQuery()
    {
        // SECTION: Scalar projection for projects to avoid entity materialization on legacy nullable columns
        var projectsQ =
            from project in _db.Projects.AsNoTracking()
            let isDeleted = EF.Property<bool?>(project, nameof(Project.IsDeleted)) ?? false
            let isArchived = EF.Property<bool?>(project, nameof(Project.IsArchived)) ?? false
            let lifecycleStatus = EF.Property<ProjectLifecycleStatus?>(project, nameof(Project.LifecycleStatus)) ?? ProjectLifecycleStatus.Active
            let completedOn = EF.Property<DateOnly?>(project, nameof(Project.CompletedOn))
            let completedYear = EF.Property<int?>(project, nameof(Project.CompletedYear))
            let coverPhotoVersion = EF.Property<int?>(project, nameof(Project.CoverPhotoVersion))
            let costLakhs = EF.Property<decimal?>(project, nameof(Project.CostLakhs))
            select new
            {
                project.Id,
                project.Name,
                project.Description,
                CompletedYear = completedYear,
                CompletedOn = completedOn,
                SponsoringLineDirectorateName = project.SponsoringLineDirectorate != null ? project.SponsoringLineDirectorate!.Name : null,
                project.ArmService,
                project.CoverPhotoId,
                CoverPhotoVersion = coverPhotoVersion,
                CostLakhs = costLakhs,
                IsDeleted = isDeleted,
                IsArchived = isArchived,
                LifecycleStatus = lifecycleStatus
            };

        // SECTION: Scalar projection for tech statuses
        var techStatusesQ =
            from techStatus in _db.ProjectTechStatuses.AsNoTracking()
            let projectId = EF.Property<int?>(techStatus, nameof(ProjectTechStatus.ProjectId))
            where projectId.HasValue
            select new
            {
                ProjectId = projectId.Value,
                AvailableForProliferation = EF.Property<bool?>(techStatus, nameof(ProjectTechStatus.AvailableForProliferation))
            };

        // SECTION: Scalar projection for optional production costs
        var productionCostsQ =
            from costFact in _db.ProjectProductionCostFacts.AsNoTracking()
            let projectId = EF.Property<int?>(costFact, nameof(ProjectProductionCostFact.ProjectId))
            where projectId.HasValue
            select new
            {
                ProjectId = projectId.Value,
                costFact.ApproxProductionCost
            };

        // SECTION: Scalar projection for optional ToT details
        var totsQ =
            from tot in _db.ProjectTots.AsNoTracking()
            let projectId = EF.Property<int?>(tot, nameof(ProjectTot.ProjectId))
            where projectId.HasValue
            select new
            {
                ProjectId = projectId.Value,
                Status = EF.Property<ProjectTotStatus?>(tot, nameof(ProjectTot.Status)),
                CompletedOn = EF.Property<DateOnly?>(tot, nameof(ProjectTot.CompletedOn))
            };

        var query =
            from project in projectsQ
            join techStatus in techStatusesQ on project.Id equals techStatus.ProjectId
            join costFact in productionCostsQ on project.Id equals costFact.ProjectId into costJoin
            from costFact in costJoin.DefaultIfEmpty()
            join tot in totsQ on project.Id equals tot.ProjectId into totJoin
            from tot in totJoin.DefaultIfEmpty()
            where project.IsDeleted == false
                  && project.IsArchived == false
                  && project.LifecycleStatus == ProjectLifecycleStatus.Completed
                  && (techStatus.AvailableForProliferation ?? false) == true
            orderby project.SponsoringLineDirectorateName ?? string.Empty,
                project.CompletedYear descending,
                project.CompletedOn descending,
                project.Name
            select new CompendiumProjection(
                project.Id,
                project.Name,
                project.Description,
                project.CompletedYear,
                project.CompletedOn,
                project.SponsoringLineDirectorateName,
                project.ArmService,
                costFact != null ? costFact.ApproxProductionCost : null,
                project.CoverPhotoId,
                project.CoverPhotoVersion,
                project.CostLakhs,
                tot != null ? tot.Status : null,
                tot != null ? tot.CompletedOn : null);

        return query;
    }

    private static string ResolveCompletionYearText(int? completedYear, DateOnly? completedOn)
    {
        if (completedYear.HasValue)
        {
            return completedYear.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
        }

        if (completedOn.HasValue)
        {
            return completedOn.GetValueOrDefault().Year.ToString(CultureInfo.InvariantCulture);
        }

        return "Not recorded";
    }

    private static string FormatEnum(Enum value)
    {
        var raw = value.ToString();
        return string.Concat(raw.Select((character, index) => index > 0 && char.IsUpper(character)
            ? $" {character}"
            : character.ToString()));
    }

    // SECTION: Shared mapper for single and bulk read paths
    private static CompendiumProjectDetailDto MapToDetail(
        CompendiumProjection project,
        byte[]? coverPhotoBytes,
        bool coverPhotoAvailable,
        HistoricalExtrasDto? historicalExtras)
    {
        return new CompendiumProjectDetailDto
        {
            ProjectId = project.Id,
            Name = project.Name,
            CompletionYearText = ResolveCompletionYearText(project.CompletedYear, project.CompletedOn),
            SponsoringLineDirectorateName = project.SponsoringLineDirectorateName ?? "Not recorded",
            ArmService = string.IsNullOrWhiteSpace(project.ArmService) ? "Not recorded" : project.ArmService!,
            ProliferationCostLakhs = project.ApproxProductionCost,
            Description = string.IsNullOrWhiteSpace(project.Description) ? "Not recorded" : project.Description!,
            CoverPhotoId = project.CoverPhotoId,
            CoverPhotoVersion = project.CoverPhotoVersion,
            CoverPhotoBytes = coverPhotoBytes,
            CoverPhotoAvailable = coverPhotoAvailable,
            HistoricalExtras = historicalExtras
        };
    }

    // SECTION: Cover photo resolution loop for eligible projects
    private async Task<Dictionary<int, byte[]>> ResolveCoverPhotosAsync(
        IReadOnlyCollection<CompendiumProjection> projects,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, byte[]>(projects.Count);

        foreach (var project in projects)
        {
            if (!project.CoverPhotoId.HasValue)
            {
                continue;
            }

            var derivative = await _projectPhotoService.OpenDerivativeAsync(
                project.Id,
                project.CoverPhotoId.Value,
                "xl",
                preferWebp: false,
                cancellationToken);

            if (!derivative.HasValue)
            {
                continue;
            }

            await using var stream = derivative.Value.Stream;
            await using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);

            if (memory.Length > 0)
            {
                result[project.Id] = memory.ToArray();
            }
        }

        return result;
    }

    // SECTION: Historical extras resolution loop for eligible projects
    private async Task<Dictionary<int, HistoricalExtrasDto>> ResolveHistoricalExtrasAsync(
        IReadOnlyCollection<CompendiumProjection> projects,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, HistoricalExtrasDto>(projects.Count);

        foreach (var project in projects)
        {
            var sddTotal = await _metricsService.GetAllTimeTotalAsync(project.Id, ProliferationSource.Sdd, cancellationToken);
            var abwTotal = await _metricsService.GetAllTimeTotalAsync(project.Id, ProliferationSource.Abw515, cancellationToken);
            var totStatus = project.TotStatus;
            var totCompletedOn = project.TotCompletedOn;

            result[project.Id] = new HistoricalExtrasDto
            {
                RdCostLakhs = project.CostLakhs,
                TotStatusText = totStatus.HasValue ? FormatEnum(totStatus.GetValueOrDefault()) : "Not recorded",
                TotCompletedOnText = totStatus == ProjectTotStatus.Completed && totCompletedOn.HasValue
                    ? totCompletedOn.GetValueOrDefault().ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                    : "Not recorded",
                ProliferationSddAllTime = sddTotal,
                ProliferationAbw515AllTime = abwTotal,
                ProliferationTotalAllTime = sddTotal + abwTotal
            };
        }

        return result;
    }

    private sealed record CompendiumProjectCardProjection(
        int Id,
        string Name,
        int? CompletedYear,
        DateOnly? CompletedOn,
        string? SponsoringLineDirectorateName,
        string? ArmService,
        decimal? ApproxProductionCost,
        int? CoverPhotoId,
        int? CoverPhotoVersion);

    private sealed record CompendiumProjection(
        int Id,
        string Name,
        string? Description,
        int? CompletedYear,
        DateOnly? CompletedOn,
        string? SponsoringLineDirectorateName,
        string? ArmService,
        decimal? ApproxProductionCost,
        int? CoverPhotoId,
        int? CoverPhotoVersion,
        decimal? CostLakhs,
        ProjectTotStatus? TotStatus,
        DateOnly? TotCompletedOn);
}
