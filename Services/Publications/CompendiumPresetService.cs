using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;
using ProjectManagement.Models.Publications;
using ProjectManagement.Services;
using ProjectManagement.Services.Compendiums;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Publications;

public interface ICompendiumPresetService
{
    Task<IReadOnlyList<CompendiumPresetSummaryVm>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<CompendiumPresetLoadResult> LoadAsync(
        long presetId,
        CancellationToken cancellationToken = default);

    Task<CompendiumPresetMutationResult> CreateAsync(
        string actorUserId,
        string name,
        string? description,
        CompendiumPresetConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<CompendiumPresetMutationResult> UpdateAsync(
        long presetId,
        string actorUserId,
        string rowVersion,
        CompendiumPresetConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<CompendiumPresetMutationResult> RenameAsync(
        long presetId,
        string actorUserId,
        string rowVersion,
        string name,
        CancellationToken cancellationToken = default);

    Task<CompendiumPresetMutationResult> DuplicateAsync(
        long presetId,
        string actorUserId,
        string rowVersion,
        string name,
        string? description,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        long presetId,
        string actorUserId,
        string rowVersion,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence boundary for shared Compendium configurations.
///
/// The preset stores publication choices only: identity, handling marking, membership and order.
/// Project facts are deliberately never copied into the preset. Every load rehydrates the ordered
/// project list against current PRISM records. HoD/Comdt may maintain shared presets; every
/// authenticated Publications user may list and load them.
/// </summary>
public sealed class CompendiumPresetService : ICompendiumPresetService
{
    private const int CurrentSchemaVersion = 3;
    private const int MaximumProjects = 500;

    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ILogger<CompendiumPresetService> _logger;

    public CompendiumPresetService(
        ApplicationDbContext db,
        IHttpContextAccessor httpContextAccessor,
        IAuditService audit,
        IClock clock,
        ILogger<CompendiumPresetService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<CompendiumPresetSummaryVm>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.CompendiumPresets
            .AsNoTracking()
            .Where(preset => preset.IsActive)
            .OrderByDescending(preset => preset.UpdatedAtUtc)
            .ThenBy(preset => preset.Name)
            .Select(preset => new
            {
                preset.Id,
                preset.Name,
                preset.Description,
                ProjectCount = preset.Projects.Count(item => item.ProjectId != null),
                preset.UpdatedAtUtc,
                UpdatedByDisplay = preset.LastModifiedByUser.FullName != string.Empty
                    ? preset.LastModifiedByUser.FullName
                    : preset.LastModifiedByUser.UserName ?? "Unknown user",
                preset.RowVersion
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new CompendiumPresetSummaryVm(
                row.Id,
                row.Name,
                row.Description,
                row.ProjectCount,
                row.UpdatedAtUtc,
                row.UpdatedByDisplay,
                Encode(row.RowVersion)))
            .ToArray();
    }

    public async Task<CompendiumPresetLoadResult> LoadAsync(
        long presetId,
        CancellationToken cancellationToken = default)
    {
        if (presetId <= 0)
        {
            throw new KeyNotFoundException("The saved Compendium was not found.");
        }

        var preset = await _db.CompendiumPresets
            .AsNoTracking()
            .Include(row => row.Projects)
            .Include(row => row.LastModifiedByUser)
            .FirstOrDefaultAsync(row => row.Id == presetId && row.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("The saved Compendium was not found.");

        if (preset.SettingsSchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                "This saved Compendium was created by a newer publication configuration version and cannot be loaded by this build.");
        }

        var orderedItems = preset.Projects
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .ToArray();

        var savedProjectIds = orderedItems
            .Where(item => item.ProjectId is > 0)
            .Select(item => item.ProjectId!.Value)
            .Distinct()
            .ToArray();

        var currentProjects = savedProjectIds.Length == 0
            ? new Dictionary<int, string>()
            : await _db.Projects
                .AsNoTracking()
                .Where(project => savedProjectIds.Contains(project.Id)
                                  && !project.IsDeleted
                                  && !project.IsArchived
                                  && (project.LifecycleStatus == ProjectLifecycleStatus.Active
                                      || project.LifecycleStatus == ProjectLifecycleStatus.Completed))
                .Select(project => new { project.Id, project.Name })
                .ToDictionaryAsync(project => project.Id, project => project.Name, cancellationToken);

        var photoRows = savedProjectIds.Length == 0
            ? Array.Empty<SavedPhotoRow>()
            : await _db.ProjectPhotos
                .AsNoTracking()
                .Where(photo => savedProjectIds.Contains(photo.ProjectId))
                .Select(photo => new SavedPhotoRow(photo.Id, photo.ProjectId))
                .ToArrayAsync(cancellationToken);
        var photoIdsByProject = photoRows
            .GroupBy(photo => photo.ProjectId)
            .ToDictionary(group => group.Key, group => group.Select(photo => photo.PhotoId).ToHashSet());

        var diagnostics = new List<CompendiumPresetDiagnostic>();
        var projectConfigurations = new List<CompendiumPresetProjectConfiguration>(orderedItems.Length);

        foreach (var item in orderedItems)
        {
            if (item.ProjectId is not int projectId
                || !currentProjects.TryGetValue(projectId, out var currentName))
            {
                diagnostics.Add(new CompendiumPresetDiagnostic(
                    CompendiumPresetDiagnosticSeverity.Warning,
                    "projectUnavailable",
                    $"{item.ProjectNameSnapshot} is part of this saved Compendium but is no longer available for publication.",
                    item.ProjectId,
                    item.ProjectNameSnapshot));
                continue;
            }

            var mode = ParseImageMode(item.ImageSelectionMode);
            var primaryPhotoId = item.PrimaryPhotoId;
            if (mode == CompendiumImageSelectionMode.Explicit)
            {
                var availablePhotoIds = photoIdsByProject.GetValueOrDefault(projectId) ?? new HashSet<int>();
                if (!primaryPhotoId.HasValue || !availablePhotoIds.Contains(primaryPhotoId.Value))
                {
                    diagnostics.Add(new CompendiumPresetDiagnostic(
                        CompendiumPresetDiagnosticSeverity.Warning,
                        "publicationImageUnavailable",
                        $"{currentName}'s saved publication image is no longer available. PRISM will resolve the current best project image automatically; review the project before final issue.",
                        projectId,
                        currentName));
                    primaryPhotoId = null;
                    mode = CompendiumImageSelectionMode.Automatic;
                }
            }
            else
            {
                primaryPhotoId = null;
            }

            projectConfigurations.Add(new CompendiumPresetProjectConfiguration(
                projectId,
                primaryPhotoId,
                ClampFocal(item.PrimaryFocalX),
                ClampFocal(item.PrimaryFocalY),
                mode));

            if (!string.Equals(currentName, item.ProjectNameSnapshot, StringComparison.Ordinal))
            {
                diagnostics.Add(new CompendiumPresetDiagnostic(
                    CompendiumPresetDiagnosticSeverity.Information,
                    "projectRenamed",
                    $"{item.ProjectNameSnapshot} is now named {currentName}. Current PRISM data will be used.",
                    projectId,
                    currentName));
            }
        }

        var cover = new CompendiumCoverConfiguration(
            ParseCoverMode(preset.CoverImageMode),
            preset.CoverHeroProjectId,
            preset.CoverHeroPhotoId,
            ClampFocal(preset.CoverFocalX),
            ClampFocal(preset.CoverFocalY));

        if (cover.ImageMode == CompendiumCoverImageMode.Explicit)
        {
            var valid = cover.HeroProjectId is int heroProjectId
                        && cover.HeroPhotoId is int heroPhotoId
                        && currentProjects.ContainsKey(heroProjectId)
                        && (photoIdsByProject.GetValueOrDefault(heroProjectId)?.Contains(heroPhotoId) ?? false);
            if (!valid)
            {
                diagnostics.Add(new CompendiumPresetDiagnostic(
                    CompendiumPresetDiagnosticSeverity.Warning,
                    "coverHeroUnavailable",
                    "The saved Compendium cover hero is no longer available. Automatic cover imagery will be used until you choose another hero."));
                cover = new CompendiumCoverConfiguration();
            }
        }

        var configuration = new CompendiumPresetConfiguration(
            preset.Title,
            preset.Subtitle,
            preset.Edition,
            preset.HandlingMarking,
            projectConfigurations)
        {
            Cover = cover
        };

        return new CompendiumPresetLoadResult(
            ToSummary(preset),
            configuration,
            diagnostics);
    }

    public async Task<CompendiumPresetMutationResult> CreateAsync(
        string actorUserId,
        string name,
        string? description,
        CompendiumPresetConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        EnsureCanManage();
        var userId = NormalizeUserId(actorUserId);
        var cleanName = CleanName(name);
        var normalizedName = NormalizeName(cleanName);
        await EnsureUniqueNameAsync(normalizedName, excludedPresetId: null, cancellationToken);

        var prepared = await PrepareConfigurationAsync(configuration, cancellationToken);
        var now = _clock.UtcNow.ToUniversalTime();
        var preset = new CompendiumPreset
        {
            Name = cleanName,
            NormalizedName = normalizedName,
            Description = NormalizeDescription(description),
            SettingsSchemaVersion = CurrentSchemaVersion,
            CreatedByUserId = userId,
            LastModifiedByUserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            RowVersion = NewRowVersion(),
            Projects = prepared.Projects
        };
        ApplyConfiguration(preset, prepared.Configuration);

        _db.CompendiumPresets.Add(preset);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Shared Compendium preset {PresetId} created by {UserId} with {ProjectCount} projects.",
            preset.Id,
            userId,
            preset.Projects.Count);
        await TryAuditAsync(
            "Publications.CompendiumPresetCreated",
            "Shared Compendium configuration created.",
            userId,
            preset,
            new Dictionary<string, string?>
            {
                ["PresetId"] = preset.Id.ToString(),
                ["PresetName"] = preset.Name,
                ["ProjectCount"] = preset.Projects.Count.ToString()
            });

        return new CompendiumPresetMutationResult(
            await GetSummaryAsync(preset.Id, cancellationToken));
    }

    public async Task<CompendiumPresetMutationResult> UpdateAsync(
        long presetId,
        string actorUserId,
        string rowVersion,
        CompendiumPresetConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        EnsureCanManage();
        var userId = NormalizeUserId(actorUserId);
        var preset = await LoadTrackedAsync(presetId, includeProjects: true, cancellationToken);
        EnsureVersion(preset, rowVersion);
        var prepared = await PrepareConfigurationAsync(configuration, cancellationToken);

        // Reordering replaces rows protected by a unique (PresetId, SortOrder) constraint. Flush the
        // old sequence before adding the replacement sequence, but keep both writes in one database
        // transaction so a failed/concurrent update cannot leave a partially changed preset.
        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational())
        {
            transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            _db.CompendiumPresetProjects.RemoveRange(preset.Projects);
            await _db.SaveChangesAsync(cancellationToken);

            foreach (var item in prepared.Projects)
            {
                item.PresetId = preset.Id;
            }

            ApplyConfiguration(preset, prepared.Configuration);
            _db.CompendiumPresetProjects.AddRange(prepared.Projects);
            preset.Projects = prepared.Projects;
            preset.LastModifiedByUserId = userId;
            preset.UpdatedAtUtc = _clock.UtcNow.ToUniversalTime();
            preset.RowVersion = NewRowVersion();

            await SaveWithConcurrencyAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        _logger.LogInformation(
            "Shared Compendium preset {PresetId} updated by {UserId} with {ProjectCount} projects.",
            preset.Id,
            userId,
            prepared.Projects.Count);
        await TryAuditAsync(
            "Publications.CompendiumPresetUpdated",
            "Shared Compendium configuration updated.",
            userId,
            preset,
            new Dictionary<string, string?>
            {
                ["PresetId"] = preset.Id.ToString(),
                ["PresetName"] = preset.Name,
                ["ProjectCount"] = prepared.Projects.Count.ToString()
            });

        return new CompendiumPresetMutationResult(
            await GetSummaryAsync(preset.Id, cancellationToken));
    }

    public async Task<CompendiumPresetMutationResult> RenameAsync(
        long presetId,
        string actorUserId,
        string rowVersion,
        string name,
        CancellationToken cancellationToken = default)
    {
        EnsureCanManage();
        var userId = NormalizeUserId(actorUserId);
        var preset = await LoadTrackedAsync(presetId, includeProjects: false, cancellationToken);
        EnsureVersion(preset, rowVersion);

        var cleanName = CleanName(name);
        var normalizedName = NormalizeName(cleanName);
        await EnsureUniqueNameAsync(normalizedName, preset.Id, cancellationToken);

        preset.Name = cleanName;
        preset.NormalizedName = normalizedName;
        preset.LastModifiedByUserId = userId;
        preset.UpdatedAtUtc = _clock.UtcNow.ToUniversalTime();
        preset.RowVersion = NewRowVersion();
        await SaveWithConcurrencyAsync(cancellationToken);
        await TryAuditAsync(
            "Publications.CompendiumPresetRenamed",
            "Shared Compendium configuration renamed.",
            userId,
            preset,
            new Dictionary<string, string?>
            {
                ["PresetId"] = preset.Id.ToString(),
                ["PresetName"] = preset.Name
            });

        return new CompendiumPresetMutationResult(
            await GetSummaryAsync(preset.Id, cancellationToken));
    }

    public async Task<CompendiumPresetMutationResult> DuplicateAsync(
        long presetId,
        string actorUserId,
        string rowVersion,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        EnsureCanManage();
        var userId = NormalizeUserId(actorUserId);
        var source = await LoadTrackedAsync(presetId, includeProjects: true, cancellationToken);
        EnsureVersion(source, rowVersion);

        var cleanName = CleanName(name);
        var normalizedName = NormalizeName(cleanName);
        await EnsureUniqueNameAsync(normalizedName, excludedPresetId: null, cancellationToken);

        var now = _clock.UtcNow.ToUniversalTime();
        var duplicate = new CompendiumPreset
        {
            Name = cleanName,
            NormalizedName = normalizedName,
            Description = NormalizeDescription(description) ?? source.Description,
            SettingsSchemaVersion = CurrentSchemaVersion,
            Title = source.Title,
            Subtitle = source.Subtitle,
            Edition = source.Edition,
            HandlingMarking = source.HandlingMarking,
            CoverImageMode = source.CoverImageMode,
            CoverHeroProjectId = source.CoverHeroProjectId,
            CoverHeroPhotoId = source.CoverHeroPhotoId,
            CoverFocalX = source.CoverFocalX,
            CoverFocalY = source.CoverFocalY,
            CreatedByUserId = userId,
            LastModifiedByUserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            RowVersion = NewRowVersion(),
            Projects = source.Projects
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .Select(item => new CompendiumPresetProject
                {
                    ProjectId = item.ProjectId,
                    ProjectNameSnapshot = item.ProjectNameSnapshot,
                    SortOrder = item.SortOrder,
                    PrimaryPhotoId = item.PrimaryPhotoId,
                    PrimaryFocalX = item.PrimaryFocalX,
                    PrimaryFocalY = item.PrimaryFocalY,
                    ImageSelectionMode = item.ImageSelectionMode
                })
                .ToList()
        };

        _db.CompendiumPresets.Add(duplicate);
        await _db.SaveChangesAsync(cancellationToken);
        await TryAuditAsync(
            "Publications.CompendiumPresetDuplicated",
            "Shared Compendium configuration duplicated.",
            userId,
            duplicate,
            new Dictionary<string, string?>
            {
                ["PresetId"] = duplicate.Id.ToString(),
                ["SourcePresetId"] = source.Id.ToString(),
                ["PresetName"] = duplicate.Name,
                ["ProjectCount"] = duplicate.Projects.Count.ToString()
            });

        return new CompendiumPresetMutationResult(
            await GetSummaryAsync(duplicate.Id, cancellationToken));
    }

    public async Task DeleteAsync(
        long presetId,
        string actorUserId,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        EnsureCanManage();
        var userId = NormalizeUserId(actorUserId);
        var preset = await LoadTrackedAsync(presetId, includeProjects: false, cancellationToken);
        EnsureVersion(preset, rowVersion);

        preset.IsActive = false;
        // Release the active unique name while preserving the human-readable name for history.
        preset.NormalizedName = $"#DELETED#{preset.Id}#{Guid.NewGuid():N}";
        preset.LastModifiedByUserId = userId;
        preset.UpdatedAtUtc = _clock.UtcNow.ToUniversalTime();
        preset.RowVersion = NewRowVersion();
        await SaveWithConcurrencyAsync(cancellationToken);
        await TryAuditAsync(
            "Publications.CompendiumPresetDeleted",
            "Shared Compendium configuration deleted.",
            userId,
            preset,
            new Dictionary<string, string?>
            {
                ["PresetId"] = preset.Id.ToString(),
                ["PresetName"] = preset.Name
            });
    }

    private async Task<(CompendiumPresetConfiguration Configuration, List<CompendiumPresetProject> Projects)>
        PrepareConfigurationAsync(
            CompendiumPresetConfiguration configuration,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var seen = new HashSet<int>();
        var requestedProjects = (configuration.Projects ?? Array.Empty<CompendiumPresetProjectConfiguration>())
            .Where(project => project.ProjectId > 0 && seen.Add(project.ProjectId))
            .Take(MaximumProjects + 1)
            .Select(NormalizeProjectConfiguration)
            .ToArray();

        if (requestedProjects.Length == 0)
        {
            throw new InvalidOperationException("Select at least one project before saving a Compendium.");
        }

        if (requestedProjects.Length > MaximumProjects)
        {
            throw new InvalidOperationException(
                $"A saved Compendium may contain at most {MaximumProjects} projects.");
        }

        var projectIds = requestedProjects.Select(project => project.ProjectId).ToArray();
        var projectNames = await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id)
                              && !project.IsDeleted
                              && !project.IsArchived
                              && (project.LifecycleStatus == ProjectLifecycleStatus.Active
                                  || project.LifecycleStatus == ProjectLifecycleStatus.Completed))
            .Select(project => new { project.Id, project.Name })
            .ToDictionaryAsync(project => project.Id, project => project.Name, cancellationToken);

        if (projectIds.Any(projectId => !projectNames.ContainsKey(projectId)))
        {
            throw new InvalidOperationException(
                "One or more selected projects are no longer available. Refresh the Compendium before saving.");
        }

        var explicitPhotoIds = requestedProjects
            .Where(project => project.ImageSelectionMode == CompendiumImageSelectionMode.Explicit
                              && project.PrimaryPhotoId.HasValue)
            .Select(project => project.PrimaryPhotoId!.Value)
            .Distinct()
            .ToArray();
        var explicitPhotos = explicitPhotoIds.Length == 0
            ? new Dictionary<int, SavedPhotoRow>()
            : await _db.ProjectPhotos
                .AsNoTracking()
                .Where(photo => explicitPhotoIds.Contains(photo.Id))
                .Select(photo => new SavedPhotoRow(photo.Id, photo.ProjectId))
                .ToDictionaryAsync(photo => photo.PhotoId, cancellationToken);

        foreach (var project in requestedProjects)
        {
            if (project.ImageSelectionMode != CompendiumImageSelectionMode.Explicit)
            {
                continue;
            }

            if (!project.PrimaryPhotoId.HasValue
                || !explicitPhotos.TryGetValue(project.PrimaryPhotoId.Value, out var photo)
                || photo.ProjectId != project.ProjectId)
            {
                throw new InvalidOperationException(
                    $"The selected publication image for {projectNames[project.ProjectId]} is no longer available. Refresh the project review before saving.");
            }
        }

        var normalizedCover = NormalizeCoverConfiguration(configuration.Cover);
        if (normalizedCover.ImageMode == CompendiumCoverImageMode.Explicit)
        {
            if (normalizedCover.HeroProjectId is not int heroProjectId
                || normalizedCover.HeroPhotoId is not int heroPhotoId
                || !projectIds.Contains(heroProjectId))
            {
                throw new InvalidOperationException(
                    "The selected cover hero must belong to a project included in this Compendium.");
            }

            var coverPhoto = await _db.ProjectPhotos
                .AsNoTracking()
                .Where(photo => photo.Id == heroPhotoId)
                .Select(photo => new SavedPhotoRow(photo.Id, photo.ProjectId))
                .SingleOrDefaultAsync(cancellationToken);
            if (coverPhoto is null || coverPhoto.ProjectId != heroProjectId)
            {
                throw new InvalidOperationException(
                    "The selected Compendium cover hero is no longer available. Choose another hero or use automatic imagery.");
            }
        }

        var currentYear = TimeZoneInfo.ConvertTime(
            _clock.UtcNow.ToUniversalTime(),
            TimeZoneHelper.GetIst()).Year;
        var normalizedConfiguration = NormalizeConfiguration(configuration, requestedProjects, currentYear);

        var rows = normalizedConfiguration.Projects
            .Select((project, sortOrder) => new CompendiumPresetProject
            {
                ProjectId = project.ProjectId,
                ProjectNameSnapshot = CleanRequired(
                    projectNames[project.ProjectId],
                    $"Project {project.ProjectId}",
                    160),
                SortOrder = sortOrder,
                PrimaryPhotoId = project.ImageSelectionMode == CompendiumImageSelectionMode.Explicit
                    ? project.PrimaryPhotoId
                    : null,
                PrimaryFocalX = project.PrimaryFocalX,
                PrimaryFocalY = project.PrimaryFocalY,
                ImageSelectionMode = project.ImageSelectionMode.ToString()
            })
            .ToList();

        return (normalizedConfiguration, rows);
    }

    private static CompendiumPresetConfiguration NormalizeConfiguration(
        CompendiumPresetConfiguration configuration,
        IReadOnlyList<CompendiumPresetProjectConfiguration> projects,
        int currentYear)
        => new(
            CleanRequired(configuration.Title, "SDD Simulators Compendium", 120),
            CleanRequired(configuration.Subtitle, "Detailed Project Reference", 160),
            CleanRequired(configuration.Edition, $"Capability Edition · {currentYear}", 80),
            CleanOptional(configuration.HandlingMarking, 80),
            projects.ToArray())
        {
            Cover = NormalizeCoverConfiguration(configuration.Cover)
        };

    private static CompendiumPresetProjectConfiguration NormalizeProjectConfiguration(
        CompendiumPresetProjectConfiguration project)
    {
        var mode = Enum.IsDefined(project.ImageSelectionMode)
            ? project.ImageSelectionMode
            : CompendiumImageSelectionMode.Automatic;
        var photoId = mode == CompendiumImageSelectionMode.Explicit && project.PrimaryPhotoId is > 0
            ? project.PrimaryPhotoId
            : null;

        return project with
        {
            PrimaryPhotoId = photoId,
            PrimaryFocalX = ClampFocal(project.PrimaryFocalX),
            PrimaryFocalY = ClampFocal(project.PrimaryFocalY),
            ImageSelectionMode = mode
        };
    }

    private static void ApplyConfiguration(
        CompendiumPreset preset,
        CompendiumPresetConfiguration configuration)
    {
        preset.SettingsSchemaVersion = CurrentSchemaVersion;
        preset.Title = configuration.Title;
        preset.Subtitle = configuration.Subtitle;
        preset.Edition = configuration.Edition;
        preset.HandlingMarking = configuration.HandlingMarking;
        preset.CoverImageMode = configuration.Cover.ImageMode.ToString();
        preset.CoverHeroProjectId = configuration.Cover.ImageMode == CompendiumCoverImageMode.Explicit
            ? configuration.Cover.HeroProjectId
            : null;
        preset.CoverHeroPhotoId = configuration.Cover.ImageMode == CompendiumCoverImageMode.Explicit
            ? configuration.Cover.HeroPhotoId
            : null;
        preset.CoverFocalX = ClampFocal(configuration.Cover.FocalX);
        preset.CoverFocalY = ClampFocal(configuration.Cover.FocalY);
    }

    private async Task<CompendiumPreset> LoadTrackedAsync(
        long presetId,
        bool includeProjects,
        CancellationToken cancellationToken)
    {
        IQueryable<CompendiumPreset> query = _db.CompendiumPresets;
        if (includeProjects)
        {
            query = query.Include(preset => preset.Projects);
        }

        return await query.FirstOrDefaultAsync(
                   preset => preset.Id == presetId && preset.IsActive,
                   cancellationToken)
               ?? throw new KeyNotFoundException("The saved Compendium was not found.");
    }

    private async Task<CompendiumPresetSummaryVm> GetSummaryAsync(
        long presetId,
        CancellationToken cancellationToken)
    {
        var preset = await _db.CompendiumPresets
            .AsNoTracking()
            .Include(row => row.Projects)
            .Include(row => row.LastModifiedByUser)
            .FirstAsync(row => row.Id == presetId, cancellationToken);
        return ToSummary(preset);
    }

    private static CompendiumPresetSummaryVm ToSummary(CompendiumPreset preset)
    {
        var display = preset.LastModifiedByUser?.FullName;
        if (string.IsNullOrWhiteSpace(display))
        {
            display = preset.LastModifiedByUser?.UserName;
        }

        return new CompendiumPresetSummaryVm(
            preset.Id,
            preset.Name,
            preset.Description,
            preset.Projects.Count(item => item.ProjectId != null),
            preset.UpdatedAtUtc,
            display ?? "Unknown user",
            Encode(preset.RowVersion));
    }

    private async Task EnsureUniqueNameAsync(
        string normalizedName,
        long? excludedPresetId,
        CancellationToken cancellationToken)
    {
        var exists = await _db.CompendiumPresets
            .AsNoTracking()
            .AnyAsync(
                preset => preset.IsActive
                          && preset.NormalizedName == normalizedName
                          && (!excludedPresetId.HasValue || preset.Id != excludedPresetId.Value),
                cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException("A shared Compendium with this name already exists.");
        }
    }

    private void EnsureCanManage()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true
            || (!user.IsInRole(RoleNames.HoD) && !user.IsInRole(RoleNames.Comdt)))
        {
            throw new UnauthorizedAccessException(
                "Only HoD or Comdt may maintain shared Compendium configurations.");
        }
    }

    private static void EnsureVersion(CompendiumPreset preset, string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)
            || !string.Equals(Encode(preset.RowVersion), rowVersion.Trim(), StringComparison.Ordinal))
        {
            throw new CompendiumPresetConcurrencyException(
                "This saved Compendium was updated by another user. Reload it or save your working copy as a new Compendium.");
        }
    }

    private async Task SaveWithConcurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new CompendiumPresetConcurrencyException(
                "This saved Compendium was updated by another user. Reload it or save your working copy as a new Compendium.",
                exception);
        }
    }

    private async Task TryAuditAsync(
        string action,
        string message,
        string userId,
        CompendiumPreset preset,
        IDictionary<string, string?> data)
    {
        try
        {
            await _audit.LogAsync(
                action,
                message,
                userId: userId,
                userName: _httpContextAccessor.HttpContext?.User?.Identity?.Name,
                data: data,
                http: _httpContextAccessor.HttpContext);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Compendium preset audit logging failed after successful action {Action} for preset {PresetId}.",
                action,
                preset.Id);
        }
    }

    private static CompendiumCoverConfiguration NormalizeCoverConfiguration(CompendiumCoverConfiguration? cover)
    {
        cover ??= new CompendiumCoverConfiguration();
        var mode = Enum.IsDefined(cover.ImageMode) ? cover.ImageMode : CompendiumCoverImageMode.Automatic;
        return new CompendiumCoverConfiguration(
            mode,
            mode == CompendiumCoverImageMode.Explicit && cover.HeroProjectId is > 0 ? cover.HeroProjectId : null,
            mode == CompendiumCoverImageMode.Explicit && cover.HeroPhotoId is > 0 ? cover.HeroPhotoId : null,
            ClampFocal(cover.FocalX),
            ClampFocal(cover.FocalY));
    }

    private static CompendiumCoverImageMode ParseCoverMode(string? value)
        => Enum.TryParse<CompendiumCoverImageMode>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumCoverImageMode.Automatic;

    private static CompendiumImageSelectionMode ParseImageMode(string? value)
        => Enum.TryParse<CompendiumImageSelectionMode>(value, ignoreCase: true, out var parsed)
           && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumImageSelectionMode.Automatic;

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;

    private sealed record SavedPhotoRow(int PhotoId, int ProjectId);

    private static string NormalizeUserId(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new UnauthorizedAccessException("The current user account could not be resolved.")
            : value.Trim();

    private static string CleanName(string value)
    {
        var clean = CleanRequired(value, string.Empty, 120);
        if (clean.Length < 3)
        {
            throw new InvalidOperationException("Enter a Compendium name of at least 3 characters.");
        }
        return clean;
    }

    private static string NormalizeName(string value)
        => value.Trim().ToUpperInvariant();

    private static string? NormalizeDescription(string? value)
        => CleanOptional(value, 500);

    private static string CleanRequired(string? value, string fallback, int maximumLength)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return clean.Length <= maximumLength ? clean : clean[..maximumLength].TrimEnd();
    }

    private static string? CleanOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var clean = value.Trim();
        return clean.Length <= maximumLength ? clean : clean[..maximumLength].TrimEnd();
    }

    private static byte[] NewRowVersion()
        => Guid.NewGuid().ToByteArray();

    private static string Encode(byte[] value)
        => Convert.ToBase64String(value ?? Array.Empty<byte>());
}
