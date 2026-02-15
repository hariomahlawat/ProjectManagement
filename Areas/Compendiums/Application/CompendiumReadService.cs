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
    private readonly ApplicationDbContext _db;
    private readonly IProjectPhotoService _projectPhotoService;
    private readonly IProliferationMetricsService _metricsService;

    public CompendiumReadService(
        ApplicationDbContext db,
        IProjectPhotoService projectPhotoService,
        IProliferationMetricsService metricsService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _projectPhotoService = projectPhotoService ?? throw new ArgumentNullException(nameof(projectPhotoService));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
    }

    public async Task<IReadOnlyList<CompendiumProjectCardDto>> GetEligibleProjectsAsync(CancellationToken cancellationToken)
    {
        // SECTION: Eligible project list query with required ordering
        return await BuildEligibleProjectQuery()
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
        var project = await BuildEligibleProjectQuery()
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
        var projects = await BuildEligibleProjectQuery().ToListAsync(cancellationToken);
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

    // SECTION: Base eligibility + ordering projection
    private IQueryable<CompendiumProjection> BuildEligibleProjectQuery()
    {
        return from project in _db.Projects.AsNoTracking()
               join techStatus in _db.ProjectTechStatuses.AsNoTracking()
                   on project.Id equals techStatus.ProjectId
               join costFact in _db.ProjectProductionCostFacts.AsNoTracking()
                   on project.Id equals costFact.ProjectId into costJoin
               from costFact in costJoin.DefaultIfEmpty()
               join tot in _db.ProjectTots.AsNoTracking()
                   on project.Id equals tot.ProjectId into totJoin
               from tot in totJoin.DefaultIfEmpty()
               where !project.IsDeleted
                     && !project.IsArchived
                     && project.LifecycleStatus == ProjectLifecycleStatus.Completed
                     && techStatus.AvailableForProliferation
               orderby project.SponsoringLineDirectorate != null ? project.SponsoringLineDirectorate.Name : string.Empty,
                   project.CompletedYear ?? (project.CompletedOn.HasValue ? project.CompletedOn.Value.Year : 0) descending,
                   project.Name
               select new CompendiumProjection(
                   project.Id,
                   project.Name,
                   project.Description,
                   project.CompletedYear,
                   project.CompletedOn,
                   project.SponsoringLineDirectorate != null ? project.SponsoringLineDirectorate.Name : null,
                   project.ArmService,
                   costFact != null ? costFact.ApproxProductionCost : null,
                   project.CoverPhotoId,
                   project.CoverPhotoVersion,
                   project.CostLakhs,
                   tot != null ? (ProjectTotStatus?)tot.Status : null,
                   tot != null ? tot.CompletedOn : null);
    }

    private static string ResolveCompletionYearText(int? completedYear, DateOnly? completedOn)
    {
        if (completedYear.HasValue)
        {
            return completedYear.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (completedOn.HasValue)
        {
            return completedOn.Value.Year.ToString(CultureInfo.InvariantCulture);
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

            result[project.Id] = new HistoricalExtrasDto
            {
                RdCostLakhs = project.CostLakhs,
                TotStatusText = project.TotStatus.HasValue ? FormatEnum(project.TotStatus.Value) : "Not recorded",
                TotCompletedOnText = project.TotStatus == ProjectTotStatus.Completed && project.TotCompletedOn.HasValue
                    ? project.TotCompletedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                    : "Not recorded",
                ProliferationSddAllTime = sddTotal,
                ProliferationAbw515AllTime = abwTotal,
                ProliferationTotalAllTime = sddTotal + abwTotal
            };
        }

        return result;
    }

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
        int CoverPhotoVersion,
        decimal? CostLakhs,
        ProjectTotStatus? TotStatus,
        DateOnly? TotCompletedOn);
}
