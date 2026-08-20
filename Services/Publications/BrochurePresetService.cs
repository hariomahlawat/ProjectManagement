using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Publications;

namespace ProjectManagement.Services.Publications;

public interface IBrochurePresetService
{
    Task<IReadOnlyList<BrochurePresetSummaryVm>> ListAsync(CancellationToken cancellationToken = default);

    Task<BrochurePresetLoadResult> LoadAsync(
        long presetId,
        CancellationToken cancellationToken = default);

    Task<BrochurePresetMutationResult> CreateAsync(
        string actorUserId,
        string name,
        string? description,
        BrochurePresetConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<BrochurePresetMutationResult> UpdateAsync(
        long presetId,
        string actorUserId,
        string rowVersion,
        BrochurePresetConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<BrochurePresetMutationResult> RenameAsync(
        long presetId,
        string actorUserId,
        string rowVersion,
        string name,
        CancellationToken cancellationToken = default);

    Task<BrochurePresetMutationResult> DuplicateAsync(
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
/// Persistence boundary for shared institutional brochure configurations.
/// A preset stores builder choices only. Every load is rehydrated against current PRISM
/// projects/photos and therefore never restores approval fingerprints, preflight results,
/// or an old PDF-verification state.
/// </summary>
public sealed class BrochurePresetService : IBrochurePresetService
{
    private const int CurrentSchemaVersion = 4;
    private const int MaximumProjects = 100;

    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ILogger<BrochurePresetService> _logger;

    public BrochurePresetService(
        ApplicationDbContext db,
        IHttpContextAccessor httpContextAccessor,
        IAuditService audit,
        IClock clock,
        ILogger<BrochurePresetService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<BrochurePresetSummaryVm>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.BrochurePresets
            .AsNoTracking()
            .Where(preset => preset.IsActive)
            .OrderByDescending(preset => preset.UpdatedAtUtc)
            .ThenBy(preset => preset.Name)
            .Select(preset => new
            {
                preset.Id,
                preset.Name,
                preset.Description,
                preset.PublicationProfile,
                ProjectCount = preset.Projects.Count(item => item.ProjectId != null),
                preset.UpdatedAtUtc,
                UpdatedByDisplay = preset.LastModifiedByUser.FullName != string.Empty
                    ? preset.LastModifiedByUser.FullName
                    : preset.LastModifiedByUser.UserName ?? "Unknown user",
                preset.RowVersion
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new BrochurePresetSummaryVm(
                row.Id,
                row.Name,
                row.Description,
                ParseEnum(row.PublicationProfile, BrochurePublicationProfile.PrintCompact),
                row.ProjectCount,
                row.UpdatedAtUtc,
                row.UpdatedByDisplay,
                Encode(row.RowVersion)))
            .ToArray();
    }

    public async Task<BrochurePresetLoadResult> LoadAsync(
        long presetId,
        CancellationToken cancellationToken = default)
    {
        if (presetId <= 0)
        {
            throw new KeyNotFoundException("The saved brochure was not found.");
        }

        var preset = await _db.BrochurePresets
            .AsNoTracking()
            .Include(row => row.Projects)
            .Include(row => row.LastModifiedByUser)
            .FirstOrDefaultAsync(row => row.Id == presetId && row.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("The saved brochure was not found.");

        if (preset.SettingsSchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                "This saved brochure was created by a newer publication configuration version and cannot be loaded by this build.");
        }

        var diagnostics = new List<BrochurePresetDiagnostic>();
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
            ? new Dictionary<int, CurrentProjectRow>()
            : await _db.Projects
                .AsNoTracking()
                .Where(project => savedProjectIds.Contains(project.Id)
                                  && !project.IsDeleted
                                  && !project.IsArchived
                                  && (project.LifecycleStatus == ProjectLifecycleStatus.Active
                                      || project.LifecycleStatus == ProjectLifecycleStatus.Completed))
                .Select(project => new CurrentProjectRow(project.Id, project.Name))
                .ToDictionaryAsync(project => project.ProjectId, cancellationToken);

        var availableProjectIds = currentProjects.Keys.ToArray();
        var photos = availableProjectIds.Length == 0
            ? Array.Empty<CurrentPhotoRow>()
            : await _db.ProjectPhotos
                .AsNoTracking()
                .Where(photo => availableProjectIds.Contains(photo.ProjectId))
                .Select(photo => new CurrentPhotoRow(photo.Id, photo.ProjectId))
                .ToArrayAsync(cancellationToken);
        var photosByProject = photos
            .GroupBy(photo => photo.ProjectId)
            .ToDictionary(group => group.Key, group => group.Select(photo => photo.PhotoId).ToHashSet());

        var projects = new List<BrochurePresetProjectConfiguration>(orderedItems.Length);
        foreach (var item in orderedItems)
        {
            if (item.ProjectId is not int projectId || !currentProjects.TryGetValue(projectId, out var currentProject))
            {
                diagnostics.Add(new BrochurePresetDiagnostic(
                    BrochurePresetDiagnosticSeverity.Warning,
                    "projectUnavailable",
                    $"{item.ProjectNameSnapshot} is part of this saved brochure but is no longer available for publication.",
                    item.ProjectId,
                    item.ProjectNameSnapshot));
                continue;
            }

            var projectPhotoIds = photosByProject.GetValueOrDefault(projectId) ?? new HashSet<int>();
            var primaryPhotoId = ValidateSavedPhoto(
                item.PrimaryPhotoId,
                projectPhotoIds,
                currentProject,
                "primary",
                diagnostics);
            var secondaryPhotoId = ValidateSavedPhoto(
                item.SecondaryPhotoId,
                projectPhotoIds,
                currentProject,
                "secondary",
                diagnostics);

            var imageMode = ParseEnum(item.ImageMode, BrochureImageMode.Automatic);
            projects.Add(new BrochurePresetProjectConfiguration(
                projectId,
                primaryPhotoId,
                secondaryPhotoId,
                ClampFocal(item.PrimaryFocalX),
                ClampFocal(item.PrimaryFocalY),
                ClampFocal(item.SecondaryFocalX),
                ClampFocal(item.SecondaryFocalY),
                imageMode));
        }

        var coverHeroProjectId = preset.CoverHeroProjectId;
        var coverHeroPhotoId = preset.CoverHeroPhotoId;
        var selectedProjectIds = projects.Select(project => project.ProjectId).ToHashSet();
        if (coverHeroProjectId is int heroProjectId)
        {
            var heroProjectValid = selectedProjectIds.Contains(heroProjectId);
            var heroPhotoValid = coverHeroPhotoId is int heroPhotoId
                                 && photosByProject.GetValueOrDefault(heroProjectId)?.Contains(heroPhotoId) == true;
            if (!heroProjectValid || !heroPhotoValid)
            {
                diagnostics.Add(new BrochurePresetDiagnostic(
                    BrochurePresetDiagnosticSeverity.Warning,
                    "coverHeroUnavailable",
                    "The saved Cover B hero is no longer available. Cover selection has been returned to Automatic."));
                coverHeroProjectId = null;
                coverHeroPhotoId = null;
            }
        }
        else
        {
            coverHeroPhotoId = null;
        }

        var configuration = new BrochurePresetConfiguration(
            Title: RequireStoredText(preset.Title, "brochure title"),
            Subtitle: RequireStoredText(preset.Subtitle, "brochure subtitle"),
            Edition: RequireStoredText(preset.Edition, "brochure edition"),
            Strapline: RequireStoredText(preset.Strapline, "brochure strapline"),
            CoverStyle: ParseEnum(preset.CoverStyle, BrochureCoverStyle.Institutional),
            InstitutionalCoverArtwork: ParseEnum(preset.InstitutionalCoverArtwork, BrochureInstitutionalCoverArtwork.ReferenceOriginal),
            NarrativeSource: ParseEnum(preset.NarrativeSource, BrochureNarrativeSource.ProjectBrief),
            PublicationProfile: ParseEnum(preset.PublicationProfile, BrochurePublicationProfile.PrintCompact),
            IntroductionTitle: preset.IntroductionTitle,
            IntroductionText: preset.IntroductionText,
            PrintIntroText: preset.PrintIntroText,
            PrintFutureText: preset.PrintFutureText,
            PrintProcurementText: preset.PrintProcurementText,
            PrintProcurementHeading: preset.PrintProcurementHeading,
            PrintContactsHeading: preset.PrintContactsHeading,
            PrintDevelopingAgencyHeading: preset.PrintDevelopingAgencyHeading,
            PrintManufacturingAgencyHeading: preset.PrintManufacturingAgencyHeading,
            PrintVisionaryHeading: preset.PrintVisionaryHeading,
            PrintNewSimulatorsHeading: preset.PrintNewSimulatorsHeading,
            PrintCentreStatement: preset.PrintCentreStatement,
            PrintDevelopingAgencyText: preset.PrintDevelopingAgencyText,
            PrintManufacturingAgencyText: preset.PrintManufacturingAgencyText,
            PrintVisionaryText: preset.PrintVisionaryText,
            PrintNewSimulatorsText: preset.PrintNewSimulatorsText,
            HandlingMarking: preset.HandlingMarking,
            AllowTextOnlyProjects: preset.AllowTextOnlyProjects,
            IncludeBackCover: preset.IncludeBackCover,
            CoverHeroProjectId: coverHeroProjectId,
            CoverHeroPhotoId: coverHeroPhotoId,
            CoverHeroFocalX: coverHeroProjectId is null ? .5d : ClampFocal(preset.CoverHeroFocalX),
            CoverHeroFocalY: coverHeroProjectId is null ? .5d : ClampFocal(preset.CoverHeroFocalY),
            Projects: projects,
            FrontCoverKicker: preset.FrontCoverKicker,
            FrontCoverDescriptor: preset.FrontCoverDescriptor,
            ShowFrontCoverKicker: preset.ShowFrontCoverKicker,
            ShowFrontCoverDescriptor: preset.ShowFrontCoverDescriptor,
            ShowFrontCoverTitle: preset.ShowFrontCoverTitle,
            ShowFrontCoverSubtitle: preset.ShowFrontCoverSubtitle,
            ShowFrontCoverEdition: preset.ShowFrontCoverEdition,
            ShowFrontCoverStrapline: preset.ShowFrontCoverStrapline,
            BackCoverKicker: preset.BackCoverKicker,
            BackCoverStrapline: preset.BackCoverStrapline,
            BackCoverEdition: preset.BackCoverEdition,
            ShowBackCoverKicker: preset.ShowBackCoverKicker,
            ShowBackCoverStrapline: preset.ShowBackCoverStrapline,
            ShowBackCoverEdition: preset.ShowBackCoverEdition);

        return new BrochurePresetLoadResult(
            ToSummary(preset),
            configuration,
            diagnostics);
    }

    public async Task<BrochurePresetMutationResult> CreateAsync(
        string actorUserId,
        string name,
        string? description,
        BrochurePresetConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        EnsureCanManage();
        var userId = NormalizeUserId(actorUserId);
        var cleanName = CleanName(name);
        var normalizedName = NormalizeName(cleanName);
        await EnsureUniqueNameAsync(normalizedName, excludedPresetId: null, cancellationToken);
        var prepared = await PrepareConfigurationAsync(configuration, cancellationToken);
        var now = _clock.UtcNow.ToUniversalTime();

        var preset = new BrochurePreset
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
            RowVersion = NewRowVersion()
        };
        ApplyConfiguration(preset, prepared.Configuration);
        preset.Projects = BuildProjectEntities(prepared.Projects);

        _db.BrochurePresets.Add(preset);
        await _db.SaveChangesAsync(cancellationToken);
        await TryAuditAsync(
            "Publications.BrochurePresetCreated",
            "Shared brochure configuration created.",
            userId,
            preset,
            new Dictionary<string, string?>
            {
                ["PresetId"] = preset.Id.ToString(),
                ["PresetName"] = preset.Name,
                ["ProjectCount"] = preset.Projects.Count.ToString()
            });

        return new BrochurePresetMutationResult(await GetSummaryAsync(preset.Id, cancellationToken));
    }

    public async Task<BrochurePresetMutationResult> UpdateAsync(
        long presetId,
        string actorUserId,
        string rowVersion,
        BrochurePresetConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        EnsureCanManage();
        var userId = NormalizeUserId(actorUserId);
        var preset = await LoadTrackedAsync(presetId, includeProjects: true, cancellationToken);
        EnsureVersion(preset, rowVersion);
        var prepared = await PrepareConfigurationAsync(configuration, cancellationToken);

        var replacementProjects = BuildProjectEntities(prepared.Projects);
        foreach (var project in replacementProjects)
        {
            project.PresetId = preset.Id;
        }

        // Replacing ordered children in one SQL batch can collide with the unique
        // (PresetId, SortOrder) constraint when projects swap positions. On relational
        // providers, delete the old child set first inside one transaction, then insert
        // the replacement set and update the preset concurrency token atomically.
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            if (_db.Database.IsRelational())
            {
                transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                _db.BrochurePresetProjects.RemoveRange(preset.Projects);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                _db.BrochurePresetProjects.RemoveRange(preset.Projects);
            }

            ApplyConfiguration(preset, prepared.Configuration);
            _db.BrochurePresetProjects.AddRange(replacementProjects);
            preset.Projects = replacementProjects;
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
        await TryAuditAsync(
            "Publications.BrochurePresetUpdated",
            "Shared brochure configuration updated.",
            userId,
            preset,
            new Dictionary<string, string?>
            {
                ["PresetId"] = preset.Id.ToString(),
                ["PresetName"] = preset.Name,
                ["ProjectCount"] = preset.Projects.Count.ToString()
            });

        return new BrochurePresetMutationResult(await GetSummaryAsync(preset.Id, cancellationToken));
    }

    public async Task<BrochurePresetMutationResult> RenameAsync(
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
            "Publications.BrochurePresetRenamed",
            "Shared brochure configuration renamed.",
            userId,
            preset,
            new Dictionary<string, string?>
            {
                ["PresetId"] = preset.Id.ToString(),
                ["PresetName"] = preset.Name
            });

        return new BrochurePresetMutationResult(await GetSummaryAsync(preset.Id, cancellationToken));
    }

    public async Task<BrochurePresetMutationResult> DuplicateAsync(
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

        var duplicate = ClonePreset(source);
        duplicate.Id = 0;
        duplicate.Name = cleanName;
        duplicate.NormalizedName = normalizedName;
        duplicate.Description = NormalizeDescription(description) ?? source.Description;
        duplicate.CreatedByUserId = userId;
        duplicate.LastModifiedByUserId = userId;
        duplicate.CreatedAtUtc = now;
        duplicate.UpdatedAtUtc = now;
        duplicate.IsActive = true;
        duplicate.RowVersion = NewRowVersion();
        duplicate.Projects = source.Projects
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item => new BrochurePresetProject
            {
                ProjectId = item.ProjectId,
                ProjectNameSnapshot = item.ProjectNameSnapshot,
                SortOrder = item.SortOrder,
                PrimaryPhotoId = item.PrimaryPhotoId,
                SecondaryPhotoId = item.SecondaryPhotoId,
                PrimaryFocalX = item.PrimaryFocalX,
                PrimaryFocalY = item.PrimaryFocalY,
                SecondaryFocalX = item.SecondaryFocalX,
                SecondaryFocalY = item.SecondaryFocalY,
                ImageMode = item.ImageMode
            })
            .ToList();

        _db.BrochurePresets.Add(duplicate);
        await _db.SaveChangesAsync(cancellationToken);
        await TryAuditAsync(
            "Publications.BrochurePresetDuplicated",
            "Shared brochure configuration duplicated.",
            userId,
            duplicate,
            new Dictionary<string, string?>
            {
                ["PresetId"] = duplicate.Id.ToString(),
                ["SourcePresetId"] = source.Id.ToString(),
                ["PresetName"] = duplicate.Name
            });

        return new BrochurePresetMutationResult(await GetSummaryAsync(duplicate.Id, cancellationToken));
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
        // Preserve the human-readable name for audit/history while releasing the active
        // unique name for future reuse.
        preset.NormalizedName = $"#DELETED#{preset.Id}#{Guid.NewGuid():N}";
        preset.LastModifiedByUserId = userId;
        preset.UpdatedAtUtc = _clock.UtcNow.ToUniversalTime();
        preset.RowVersion = NewRowVersion();
        await SaveWithConcurrencyAsync(cancellationToken);

        await TryAuditAsync(
            "Publications.BrochurePresetDeleted",
            "Shared brochure configuration retired.",
            userId,
            preset,
            new Dictionary<string, string?>
            {
                ["PresetId"] = preset.Id.ToString(),
                ["PresetName"] = preset.Name
            });
    }

    private async Task<BrochurePreset> LoadTrackedAsync(
        long presetId,
        bool includeProjects,
        CancellationToken cancellationToken)
    {
        if (presetId <= 0)
        {
            throw new KeyNotFoundException("The saved brochure was not found.");
        }

        IQueryable<BrochurePreset> query = _db.BrochurePresets;
        if (includeProjects)
        {
            query = query.Include(preset => preset.Projects);
        }

        return await query.FirstOrDefaultAsync(
                   preset => preset.Id == presetId && preset.IsActive,
                   cancellationToken)
               ?? throw new KeyNotFoundException("The saved brochure was not found.");
    }

    private async Task<BrochurePresetSummaryVm> GetSummaryAsync(
        long presetId,
        CancellationToken cancellationToken)
    {
        var row = await _db.BrochurePresets
            .AsNoTracking()
            .Where(preset => preset.Id == presetId && preset.IsActive)
            .Select(preset => new
            {
                preset.Id,
                preset.Name,
                preset.Description,
                preset.PublicationProfile,
                ProjectCount = preset.Projects.Count(item => item.ProjectId != null),
                preset.UpdatedAtUtc,
                UpdatedByDisplay = preset.LastModifiedByUser.FullName != string.Empty
                    ? preset.LastModifiedByUser.FullName
                    : preset.LastModifiedByUser.UserName ?? "Unknown user",
                preset.RowVersion
            })
            .SingleAsync(cancellationToken);

        return new BrochurePresetSummaryVm(
            row.Id,
            row.Name,
            row.Description,
            ParseEnum(row.PublicationProfile, BrochurePublicationProfile.PrintCompact),
            row.ProjectCount,
            row.UpdatedAtUtc,
            row.UpdatedByDisplay,
            Encode(row.RowVersion));
    }

    private async Task<PreparedConfiguration> PrepareConfigurationAsync(
        BrochurePresetConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateConfiguration(configuration);

        var distinctProjects = configuration.Projects
            .Where(project => project.ProjectId > 0)
            .Take(MaximumProjects + 1)
            .ToArray();
        if (distinctProjects.Length == 0)
        {
            throw new InvalidOperationException("Select at least one project before saving a shared brochure.");
        }
        if (distinctProjects.Length > MaximumProjects)
        {
            throw new InvalidOperationException($"A saved brochure can contain up to {MaximumProjects} projects.");
        }
        if (distinctProjects.Select(project => project.ProjectId).Distinct().Count() != distinctProjects.Length)
        {
            throw new InvalidOperationException("A saved brochure cannot contain the same project more than once.");
        }

        var projectIds = distinctProjects.Select(project => project.ProjectId).ToArray();
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id)
                              && !project.IsDeleted
                              && !project.IsArchived
                              && (project.LifecycleStatus == ProjectLifecycleStatus.Active
                                  || project.LifecycleStatus == ProjectLifecycleStatus.Completed))
            .Select(project => new CurrentProjectRow(project.Id, project.Name))
            .ToDictionaryAsync(project => project.ProjectId, cancellationToken);
        if (projects.Count != projectIds.Length)
        {
            throw new InvalidOperationException("One or more selected projects are no longer available for publication. Refresh the brochure before saving it.");
        }

        var photos = await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => projectIds.Contains(photo.ProjectId))
            .Select(photo => new CurrentPhotoRow(photo.Id, photo.ProjectId))
            .ToArrayAsync(cancellationToken);
        var photosByProject = photos
            .GroupBy(photo => photo.ProjectId)
            .ToDictionary(group => group.Key, group => group.Select(photo => photo.PhotoId).ToHashSet());

        var preparedProjects = new List<PreparedProject>(distinctProjects.Length);
        for (var index = 0; index < distinctProjects.Length; index++)
        {
            var project = distinctProjects[index];
            var current = projects[project.ProjectId];
            var projectPhotoIds = photosByProject.GetValueOrDefault(project.ProjectId) ?? new HashSet<int>();
            ValidateCurrentPhoto(project.PrimaryPhotoId, projectPhotoIds, current.ProjectName, "primary");
            ValidateCurrentPhoto(project.SecondaryPhotoId, projectPhotoIds, current.ProjectName, "secondary");
            preparedProjects.Add(new PreparedProject(
                project.ProjectId,
                current.ProjectName,
                index,
                project.PrimaryPhotoId,
                project.SecondaryPhotoId,
                ClampFocal(project.PrimaryFocalX),
                ClampFocal(project.PrimaryFocalY),
                ClampFocal(project.SecondaryFocalX),
                ClampFocal(project.SecondaryFocalY),
                project.ImageMode));
        }

        var coverHeroProjectId = configuration.CoverHeroProjectId is > 0
            ? configuration.CoverHeroProjectId
            : null;
        var coverHeroPhotoId = configuration.CoverHeroPhotoId is > 0
            ? configuration.CoverHeroPhotoId
            : null;
        if (coverHeroProjectId is not null || coverHeroPhotoId is not null)
        {
            if (coverHeroProjectId is not int validHeroProjectId
                || coverHeroPhotoId is not int validHeroPhotoId
                || !projectIds.Contains(validHeroProjectId)
                || photosByProject.GetValueOrDefault(validHeroProjectId)?.Contains(validHeroPhotoId) != true)
            {
                throw new InvalidOperationException("The selected Cover B hero is no longer available. Refresh the brochure and choose a current publication image.");
            }
        }

        var normalizedConfiguration = configuration with
        {
            Title = RequireText(configuration.Title, 120, "Brochure title"),
            Subtitle = RequireText(configuration.Subtitle, 160, "Brochure subtitle"),
            Edition = RequireText(configuration.Edition, 80, "Brochure edition"),
            Strapline = RequireText(configuration.Strapline, 180, "Cover strapline"),
            FrontCoverKicker = NormalizeOptional(configuration.FrontCoverKicker, 120),
            FrontCoverDescriptor = NormalizeOptional(configuration.FrontCoverDescriptor, 160),
            BackCoverKicker = NormalizeOptional(configuration.BackCoverKicker, 120),
            BackCoverStrapline = NormalizeOptional(configuration.BackCoverStrapline, 180),
            BackCoverEdition = NormalizeOptional(configuration.BackCoverEdition, 80),
            IntroductionTitle = NormalizeOptional(configuration.IntroductionTitle, 120),
            IntroductionText = NormalizeOptional(configuration.IntroductionText, 3000, preserveLineBreaks: true),
            PrintIntroText = NormalizeOptional(configuration.PrintIntroText, 5000, preserveLineBreaks: true),
            PrintFutureText = NormalizeOptional(configuration.PrintFutureText, 3500, preserveLineBreaks: true),
            PrintProcurementText = NormalizeOptional(configuration.PrintProcurementText, 3500, preserveLineBreaks: true),
            PrintProcurementHeading = NormalizeOptional(configuration.PrintProcurementHeading, 120),
            PrintContactsHeading = NormalizeOptional(configuration.PrintContactsHeading, 120),
            PrintDevelopingAgencyHeading = NormalizeOptional(configuration.PrintDevelopingAgencyHeading, 120),
            PrintManufacturingAgencyHeading = NormalizeOptional(configuration.PrintManufacturingAgencyHeading, 120),
            PrintVisionaryHeading = NormalizeOptional(configuration.PrintVisionaryHeading, 160),
            PrintNewSimulatorsHeading = NormalizeOptional(configuration.PrintNewSimulatorsHeading, 120),
            PrintCentreStatement = NormalizeOptional(configuration.PrintCentreStatement, 1200, preserveLineBreaks: true),
            PrintDevelopingAgencyText = NormalizeOptional(configuration.PrintDevelopingAgencyText, 1800, preserveLineBreaks: true),
            PrintManufacturingAgencyText = NormalizeOptional(configuration.PrintManufacturingAgencyText, 1200, preserveLineBreaks: true),
            PrintVisionaryText = NormalizeOptional(configuration.PrintVisionaryText, 4500, preserveLineBreaks: true),
            PrintNewSimulatorsText = NormalizeOptional(configuration.PrintNewSimulatorsText, 1800, preserveLineBreaks: true),
            HandlingMarking = NormalizeOptional(configuration.HandlingMarking, 80)?.ToUpperInvariant(),
            CoverHeroProjectId = coverHeroProjectId,
            CoverHeroPhotoId = coverHeroPhotoId,
            CoverHeroFocalX = coverHeroProjectId is null ? .5d : ClampFocal(configuration.CoverHeroFocalX),
            CoverHeroFocalY = coverHeroProjectId is null ? .5d : ClampFocal(configuration.CoverHeroFocalY),
            Projects = preparedProjects
                .Select(project => new BrochurePresetProjectConfiguration(
                    project.ProjectId,
                    project.PrimaryPhotoId,
                    project.SecondaryPhotoId,
                    project.PrimaryFocalX,
                    project.PrimaryFocalY,
                    project.SecondaryFocalX,
                    project.SecondaryFocalY,
                    project.ImageMode))
                .ToArray()
        };

        return new PreparedConfiguration(normalizedConfiguration, preparedProjects);
    }

    private static void ValidateConfiguration(BrochurePresetConfiguration configuration)
    {
        if (!Enum.IsDefined(configuration.CoverStyle)
            || !Enum.IsDefined(configuration.InstitutionalCoverArtwork)
            || !Enum.IsDefined(configuration.NarrativeSource)
            || !Enum.IsDefined(configuration.PublicationProfile))
        {
            throw new InvalidOperationException("The brochure contains an unsupported publication setting.");
        }

        if (configuration.Projects.Any(project => !Enum.IsDefined(project.ImageMode)))
        {
            throw new InvalidOperationException("The brochure contains an unsupported project image treatment.");
        }
    }

    private static void ApplyConfiguration(BrochurePreset preset, BrochurePresetConfiguration configuration)
    {
        preset.SettingsSchemaVersion = CurrentSchemaVersion;
        preset.Title = configuration.Title;
        preset.Subtitle = configuration.Subtitle;
        preset.Edition = configuration.Edition;
        preset.Strapline = configuration.Strapline;
        preset.FrontCoverKicker = configuration.FrontCoverKicker;
        preset.FrontCoverDescriptor = configuration.FrontCoverDescriptor;
        preset.ShowFrontCoverKicker = configuration.ShowFrontCoverKicker;
        preset.ShowFrontCoverDescriptor = configuration.ShowFrontCoverDescriptor;
        preset.ShowFrontCoverTitle = configuration.ShowFrontCoverTitle;
        preset.ShowFrontCoverSubtitle = configuration.ShowFrontCoverSubtitle;
        preset.ShowFrontCoverEdition = configuration.ShowFrontCoverEdition;
        preset.ShowFrontCoverStrapline = configuration.ShowFrontCoverStrapline;
        preset.BackCoverKicker = configuration.BackCoverKicker;
        preset.BackCoverStrapline = configuration.BackCoverStrapline;
        preset.BackCoverEdition = configuration.BackCoverEdition;
        preset.ShowBackCoverKicker = configuration.ShowBackCoverKicker;
        preset.ShowBackCoverStrapline = configuration.ShowBackCoverStrapline;
        preset.ShowBackCoverEdition = configuration.ShowBackCoverEdition;
        preset.CoverStyle = configuration.CoverStyle.ToString();
        preset.InstitutionalCoverArtwork = configuration.InstitutionalCoverArtwork.ToString();
        preset.NarrativeSource = configuration.NarrativeSource.ToString();
        preset.PublicationProfile = configuration.PublicationProfile.ToString();
        preset.IntroductionTitle = configuration.IntroductionTitle;
        preset.IntroductionText = configuration.IntroductionText;
        preset.PrintIntroText = configuration.PrintIntroText;
        preset.PrintFutureText = configuration.PrintFutureText;
        preset.PrintProcurementText = configuration.PrintProcurementText;
        preset.PrintProcurementHeading = configuration.PrintProcurementHeading;
        preset.PrintContactsHeading = configuration.PrintContactsHeading;
        preset.PrintDevelopingAgencyHeading = configuration.PrintDevelopingAgencyHeading;
        preset.PrintManufacturingAgencyHeading = configuration.PrintManufacturingAgencyHeading;
        preset.PrintVisionaryHeading = configuration.PrintVisionaryHeading;
        preset.PrintNewSimulatorsHeading = configuration.PrintNewSimulatorsHeading;
        preset.PrintCentreStatement = configuration.PrintCentreStatement;
        preset.PrintDevelopingAgencyText = configuration.PrintDevelopingAgencyText;
        preset.PrintManufacturingAgencyText = configuration.PrintManufacturingAgencyText;
        preset.PrintVisionaryText = configuration.PrintVisionaryText;
        preset.PrintNewSimulatorsText = configuration.PrintNewSimulatorsText;
        preset.HandlingMarking = configuration.HandlingMarking;
        preset.AllowTextOnlyProjects = configuration.AllowTextOnlyProjects;
        preset.IncludeBackCover = configuration.IncludeBackCover;
        preset.CoverHeroProjectId = configuration.CoverHeroProjectId;
        preset.CoverHeroPhotoId = configuration.CoverHeroPhotoId;
        preset.CoverHeroFocalX = ClampFocal(configuration.CoverHeroFocalX);
        preset.CoverHeroFocalY = ClampFocal(configuration.CoverHeroFocalY);
    }

    private static List<BrochurePresetProject> BuildProjectEntities(IReadOnlyList<PreparedProject> projects)
        => projects
            .Select(project => new BrochurePresetProject
            {
                ProjectId = project.ProjectId,
                ProjectNameSnapshot = project.ProjectName,
                SortOrder = project.SortOrder,
                PrimaryPhotoId = project.PrimaryPhotoId,
                SecondaryPhotoId = project.SecondaryPhotoId,
                PrimaryFocalX = project.PrimaryFocalX,
                PrimaryFocalY = project.PrimaryFocalY,
                SecondaryFocalX = project.SecondaryFocalX,
                SecondaryFocalY = project.SecondaryFocalY,
                ImageMode = project.ImageMode.ToString()
            })
            .ToList();

    private static BrochurePreset ClonePreset(BrochurePreset source)
        => new()
        {
            SettingsSchemaVersion = source.SettingsSchemaVersion,
            Title = source.Title,
            Subtitle = source.Subtitle,
            Edition = source.Edition,
            Strapline = source.Strapline,
            FrontCoverKicker = source.FrontCoverKicker,
            FrontCoverDescriptor = source.FrontCoverDescriptor,
            ShowFrontCoverKicker = source.ShowFrontCoverKicker,
            ShowFrontCoverDescriptor = source.ShowFrontCoverDescriptor,
            ShowFrontCoverTitle = source.ShowFrontCoverTitle,
            ShowFrontCoverSubtitle = source.ShowFrontCoverSubtitle,
            ShowFrontCoverEdition = source.ShowFrontCoverEdition,
            ShowFrontCoverStrapline = source.ShowFrontCoverStrapline,
            BackCoverKicker = source.BackCoverKicker,
            BackCoverStrapline = source.BackCoverStrapline,
            BackCoverEdition = source.BackCoverEdition,
            ShowBackCoverKicker = source.ShowBackCoverKicker,
            ShowBackCoverStrapline = source.ShowBackCoverStrapline,
            ShowBackCoverEdition = source.ShowBackCoverEdition,
            CoverStyle = source.CoverStyle,
            InstitutionalCoverArtwork = source.InstitutionalCoverArtwork,
            NarrativeSource = source.NarrativeSource,
            PublicationProfile = source.PublicationProfile,
            IntroductionTitle = source.IntroductionTitle,
            IntroductionText = source.IntroductionText,
            PrintIntroText = source.PrintIntroText,
            PrintFutureText = source.PrintFutureText,
            PrintProcurementText = source.PrintProcurementText,
            PrintProcurementHeading = source.PrintProcurementHeading,
            PrintContactsHeading = source.PrintContactsHeading,
            PrintDevelopingAgencyHeading = source.PrintDevelopingAgencyHeading,
            PrintManufacturingAgencyHeading = source.PrintManufacturingAgencyHeading,
            PrintVisionaryHeading = source.PrintVisionaryHeading,
            PrintNewSimulatorsHeading = source.PrintNewSimulatorsHeading,
            PrintCentreStatement = source.PrintCentreStatement,
            PrintDevelopingAgencyText = source.PrintDevelopingAgencyText,
            PrintManufacturingAgencyText = source.PrintManufacturingAgencyText,
            PrintVisionaryText = source.PrintVisionaryText,
            PrintNewSimulatorsText = source.PrintNewSimulatorsText,
            HandlingMarking = source.HandlingMarking,
            AllowTextOnlyProjects = source.AllowTextOnlyProjects,
            IncludeBackCover = source.IncludeBackCover,
            CoverHeroProjectId = source.CoverHeroProjectId,
            CoverHeroPhotoId = source.CoverHeroPhotoId,
            CoverHeroFocalX = source.CoverHeroFocalX,
            CoverHeroFocalY = source.CoverHeroFocalY
        };

    private BrochurePresetSummaryVm ToSummary(BrochurePreset preset)
    {
        var display = preset.LastModifiedByUser?.FullName;
        if (string.IsNullOrWhiteSpace(display)) display = preset.LastModifiedByUser?.UserName;
        if (string.IsNullOrWhiteSpace(display)) display = "Unknown user";

        return new BrochurePresetSummaryVm(
            preset.Id,
            preset.Name,
            preset.Description,
            ParseEnum(preset.PublicationProfile, BrochurePublicationProfile.PrintCompact),
            preset.Projects.Count(item => item.ProjectId != null),
            preset.UpdatedAtUtc,
            display,
            Encode(preset.RowVersion));
    }

    private async Task EnsureUniqueNameAsync(
        string normalizedName,
        long? excludedPresetId,
        CancellationToken cancellationToken)
    {
        var exists = await _db.BrochurePresets
            .AsNoTracking()
            .AnyAsync(
                preset => preset.IsActive
                          && preset.NormalizedName == normalizedName
                          && (!excludedPresetId.HasValue || preset.Id != excludedPresetId.Value),
                cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException("A shared brochure with this name already exists.");
        }
    }

    private void EnsureCanManage()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (!Policies.Publications.CanManageSharedPublications(user))
        {
            throw new UnauthorizedAccessException(
                "Only Commandant, HoD or ITO may maintain shared brochure configurations.");
        }
    }

    private static void EnsureVersion(BrochurePreset preset, string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)
            || !string.Equals(Encode(preset.RowVersion), rowVersion.Trim(), StringComparison.Ordinal))
        {
            throw new BrochurePresetConcurrencyException(
                "This saved brochure was updated by another user after you loaded it. Reload the current version or save your working copy as a new brochure.");
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
            throw new BrochurePresetConcurrencyException(
                "This saved brochure was updated by another user after you loaded it. Reload the current version or save your working copy as a new brochure.")
            {
                Source = exception.Source
            };
        }
    }

    private async Task TryAuditAsync(
        string action,
        string message,
        string userId,
        BrochurePreset preset,
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
                "Brochure preset audit logging failed after successful action {Action} for preset {PresetId}.",
                action,
                preset.Id);
        }
    }

    private static int? ValidateSavedPhoto(
        int? photoId,
        IReadOnlySet<int> availablePhotoIds,
        CurrentProjectRow project,
        string role,
        ICollection<BrochurePresetDiagnostic> diagnostics)
    {
        if (photoId is not int candidate || candidate <= 0)
        {
            return null;
        }
        if (availablePhotoIds.Contains(candidate))
        {
            return candidate;
        }

        diagnostics.Add(new BrochurePresetDiagnostic(
            BrochurePresetDiagnosticSeverity.Warning,
            "photoUnavailable",
            $"The saved {role} publication image for {project.ProjectName} is no longer available. PRISM will resolve a current image automatically.",
            project.ProjectId,
            project.ProjectName));
        return null;
    }

    private static void ValidateCurrentPhoto(
        int? photoId,
        IReadOnlySet<int> availablePhotoIds,
        string projectName,
        string role)
    {
        if (photoId is int candidate && candidate > 0 && !availablePhotoIds.Contains(candidate))
        {
            throw new InvalidOperationException(
                $"The selected {role} publication image for {projectName} is no longer available. Refresh the brochure before saving it.");
        }
    }

    private static string CleanName(string? value)
    {
        var cleaned = string.Join(" ", (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length is < 3 or > 120)
        {
            throw new InvalidOperationException("Saved brochure name must contain between 3 and 120 characters.");
        }
        return cleaned;
    }

    private static string NormalizeName(string value) => CleanName(value).ToUpperInvariant();

    private static string? NormalizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length <= 500 ? cleaned : cleaned[..500];
    }

    private static string RequireText(string? value, int maximumLength, string label)
    {
        var cleaned = NormalizeOptional(value, maximumLength);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new InvalidOperationException($"{label} is required.");
        }
        return cleaned;
    }

    private static string RequireStoredText(string? value, string label)
        => !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"The saved {label} is invalid.");

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        bool preserveLineBreaks = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string cleaned;
        if (preserveLineBreaks)
        {
            cleaned = value.Trim()
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);
        }
        else
        {
            cleaned = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
        return cleaned.Length <= maximumLength ? cleaned : cleaned[..maximumLength];
    }

    private static string NormalizeUserId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("The current user could not be resolved.");
        }
        return value.Trim();
    }

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    private static byte[] NewRowVersion() => Guid.NewGuid().ToByteArray();

    private static string Encode(byte[] value)
        => value is { Length: > 0 } ? Convert.ToBase64String(value) : string.Empty;

    private sealed record CurrentProjectRow(int ProjectId, string ProjectName);
    private sealed record CurrentPhotoRow(int PhotoId, int ProjectId);

    private sealed record PreparedProject(
        int ProjectId,
        string ProjectName,
        int SortOrder,
        int? PrimaryPhotoId,
        int? SecondaryPhotoId,
        double PrimaryFocalX,
        double PrimaryFocalY,
        double SecondaryFocalX,
        double SecondaryFocalY,
        BrochureImageMode ImageMode);

    private sealed record PreparedConfiguration(
        BrochurePresetConfiguration Configuration,
        IReadOnlyList<PreparedProject> Projects);
}
