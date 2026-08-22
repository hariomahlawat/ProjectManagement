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
/// project list against current PRISM records. Commandant/HoD/ITO may maintain shared presets; every
/// authenticated Publications user may list and load them.
/// </summary>
public sealed class CompendiumPresetService : ICompendiumPresetService
{
    private const int CurrentSchemaVersion = 12;
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
            .Include(row => row.Sections)
            .Include(row => row.Projects)
            .Include(row => row.CoverImages)
            .Include(row => row.PhotoPreferences)
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

        var sectionConfigurations = preset.Sections
            .OrderBy(section => section.SortOrder)
            .ThenBy(section => section.Id)
            .Select((section, index) => new CompendiumPresetSectionConfiguration(
                NormalizeSectionKey(section.SectionKey) ?? $"legacy-{section.Id}",
                CleanRequired(section.Name, "Section", 120),
                index))
            .ToArray();
        var sectionById = preset.Sections.ToDictionary(section => section.Id);
        var sectionByName = preset.Sections
            .GroupBy(section => NormalizeName(section.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

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
            var availablePhotoIds = photoIdsByProject.GetValueOrDefault(projectId) ?? new HashSet<int>();
            if (mode == CompendiumImageSelectionMode.Explicit)
            {
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

            CompendiumPresetSection? assignedSection = null;
            if (item.CustomSectionId.HasValue)
            {
                sectionById.TryGetValue(item.CustomSectionId.Value, out assignedSection);
            }
            if (assignedSection is null && !string.IsNullOrWhiteSpace(item.CustomSectionName))
            {
                sectionByName.TryGetValue(NormalizeName(item.CustomSectionName), out assignedSection);
            }

            projectConfigurations.Add(new CompendiumPresetProjectConfiguration(
                projectId,
                primaryPhotoId,
                ClampFocal(item.PrimaryFocalX),
                ClampFocal(item.PrimaryFocalY),
                mode)
            {
                CustomSectionKey = assignedSection is null ? null : NormalizeSectionKey(assignedSection.SectionKey),
                CustomSectionName = assignedSection is null
                    ? CleanOptional(item.CustomSectionName, 120)
                    : CleanOptional(assignedSection.Name, 120),
                NarrativeSourceOverride = ParseNullableNarrativeSource(item.NarrativeSourceOverride),
                ImageFitMode = ParseImageFitMode(item.ImageFitMode),
                DossierLayout = ParseDossierLayout(item.DossierLayout),
                BalancedTextFlowMode = preset.SettingsSchemaVersion < 8
                    ? CompendiumBalancedTextFlowMode.SideColumn
                    : ParseBalancedTextFlowMode(item.BalancedTextFlowMode, CompendiumBalancedTextFlowMode.SideColumn),
                NarrativeAlignmentOverride = preset.SettingsSchemaVersion < 9
                    ? null
                    : ParseNullableNarrativeAlignment(item.NarrativeAlignmentOverride),
                AdditionalNote = preset.SettingsSchemaVersion < 10 ? null : NormalizeAdditionalNote(item.AdditionalNote),
                DossierImageCount = Math.Clamp(item.DossierImageCount, 1, 3),
                SupportingPhoto1Id = item.SupportingPhoto1Id is > 0 && availablePhotoIds.Contains(item.SupportingPhoto1Id.Value) ? item.SupportingPhoto1Id : null,
                SupportingPhoto1FocalX = ClampFocal(item.SupportingPhoto1FocalX),
                SupportingPhoto1FocalY = ClampFocal(item.SupportingPhoto1FocalY),
                SupportingPhoto1FitMode = ParseImageFitMode(item.SupportingPhoto1FitMode),
                SupportingPhoto2Id = item.SupportingPhoto2Id is > 0 && availablePhotoIds.Contains(item.SupportingPhoto2Id.Value) ? item.SupportingPhoto2Id : null,
                SupportingPhoto2FocalX = ClampFocal(item.SupportingPhoto2FocalX),
                SupportingPhoto2FocalY = ClampFocal(item.SupportingPhoto2FocalY),
                SupportingPhoto2FitMode = ParseImageFitMode(item.SupportingPhoto2FitMode)
            });

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

        var coverImages = preset.CoverImages
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select((item, index) => new CompendiumPresetCoverImageConfiguration(
                ParseCoverSurface(item.Surface),
                CleanRequired(item.SlotKey, "Hero", 32),
                ParseCoverMode(item.ImageMode),
                item.ProjectId,
                item.PhotoId,
                ClampFocal(item.FocalX),
                ClampFocal(item.FocalY),
                ParseImageFitMode(item.FitMode),
                index))
            .ToArray();

        if (coverImages.Length == 0)
        {
            coverImages = new[]
            {
                new CompendiumPresetCoverImageConfiguration(
                    CompendiumCoverSurface.Front,
                    "Hero",
                    cover.ImageMode,
                    cover.HeroProjectId,
                    cover.HeroPhotoId,
                    cover.FocalX,
                    cover.FocalY,
                    CompendiumImageFitMode.Fill,
                    0)
            };
        }

        var photoPreferences = preset.PhotoPreferences
            .Where(item => item.ProjectId > 0 && item.PhotoId > 0)
            .Select(item => new CompendiumPresetPhotoPreferenceConfiguration(
                item.ProjectId,
                item.PhotoId,
                item.PreferredForPublication,
                item.SuitableForCoverHero))
            .ToArray();

        var coverDesign = new CompendiumCoverDesignConfiguration
        {
            FrontTemplate = ParseFrontTemplate(preset.FrontCoverTemplate),
            BackTemplate = ParseBackTemplate(preset.BackCoverTemplate),
            PublicationTheme = preset.SettingsSchemaVersion < 12
                ? CompendiumPublicationTheme.InstitutionalGreen
                : ParsePublicationTheme(preset.PublicationTheme),
            BackgroundTreatment = preset.SettingsSchemaVersion < 12
                ? CompendiumCoverBackgroundTreatment.Solid
                : CompendiumCoverIdentityPolicy.NormalizeTreatmentForTheme(
                    ParsePublicationTheme(preset.PublicationTheme),
                    ParseCoverBackgroundTreatment(preset.CoverBackgroundTreatment)),
            FrontTitle = CleanOptional(preset.FrontCoverTitle, 120),
            FrontSubtitle = CleanOptional(preset.FrontCoverSubtitle, 160),
            FrontEdition = CleanOptional(preset.FrontCoverEdition, 80),
            FrontEyebrow = CleanOptional(preset.FrontCoverEyebrow, 80),
            BackTitle = CleanOptional(preset.BackCoverTitle, 120),
            BackSubtitle = CleanOptional(preset.BackCoverSubtitle, 160),
            BackEdition = CleanOptional(preset.BackCoverEdition, 80),
            BackEyebrow = CleanOptional(preset.BackCoverEyebrow, 80),
            ShowFrontTitle = preset.ShowFrontTitle,
            ShowFrontSubtitle = preset.ShowFrontSubtitle,
            ShowFrontEdition = preset.ShowFrontEdition,
            ShowFrontLeftLogo = preset.ShowFrontLeftLogo,
            ShowFrontRightLogo = preset.ShowFrontRightLogo,
            FrontLogoPlacement = ParseLogoPlacement(preset.FrontLogoPlacement),
            ShowBackTitle = preset.ShowBackTitle,
            ShowBackSubtitle = preset.ShowBackSubtitle,
            ShowBackEdition = preset.ShowBackEdition,
            ShowBackLeftLogo = preset.ShowBackLeftLogo,
            ShowBackRightLogo = preset.ShowBackRightLogo,
            BackLogoPlacement = ParseLogoPlacement(preset.BackLogoPlacement),
            Images = coverImages
        };

        var configuration = new CompendiumPresetConfiguration(
            preset.Title,
            preset.Subtitle,
            preset.Edition,
            preset.HandlingMarking,
            projectConfigurations)
        {
            Cover = cover,
            CoverDesign = coverDesign,
            PhotoPreferences = photoPreferences,
            NarrativeSource = ParseNarrativeSource(preset.NarrativeSource),
            DefaultNarrativeAlignment = preset.SettingsSchemaVersion < 9
                ? CompendiumNarrativeAlignment.Left
                : ParseNarrativeAlignment(preset.DefaultNarrativeAlignment, CompendiumNarrativeAlignment.Left),
            ProjectParticularsStyle = preset.SettingsSchemaVersion < 11
                ? CompendiumProjectParticularsStyle.Panel
                : ParseProjectParticularsStyle(preset.ProjectParticularsStyle),
            GroupingMode = ParseGroupingMode(preset.GroupingMode),
            SortMode = ParseSortMode(preset.SortMode),
            Sections = sectionConfigurations
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
            Sections = prepared.Sections,
            Projects = prepared.Projects,
            CoverImages = prepared.CoverImages,
            PhotoPreferences = prepared.PhotoPreferences
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
            _db.CompendiumPresetSections.RemoveRange(preset.Sections);
            await _db.SaveChangesAsync(cancellationToken);
            _db.CompendiumPresetCoverImages.RemoveRange(preset.CoverImages);
            _db.CompendiumPresetPhotoPreferences.RemoveRange(preset.PhotoPreferences);
            await _db.SaveChangesAsync(cancellationToken);

            foreach (var section in prepared.Sections)
            {
                section.PresetId = preset.Id;
            }
            foreach (var item in prepared.Projects)
            {
                item.PresetId = preset.Id;
            }
            foreach (var image in prepared.CoverImages)
            {
                image.PresetId = preset.Id;
            }
            foreach (var preference in prepared.PhotoPreferences)
            {
                preference.PresetId = preset.Id;
            }

            ApplyConfiguration(preset, prepared.Configuration);
            _db.CompendiumPresetSections.AddRange(prepared.Sections);
            _db.CompendiumPresetProjects.AddRange(prepared.Projects);
            _db.CompendiumPresetCoverImages.AddRange(prepared.CoverImages);
            _db.CompendiumPresetPhotoPreferences.AddRange(prepared.PhotoPreferences);
            preset.Sections = prepared.Sections;
            preset.Projects = prepared.Projects;
            preset.CoverImages = prepared.CoverImages;
            preset.PhotoPreferences = prepared.PhotoPreferences;
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
        var sourceSections = source.Sections
            .OrderBy(section => section.SortOrder)
            .ThenBy(section => section.Id)
            .ToArray();
        var duplicateSections = sourceSections
            .Select((section, index) => new CompendiumPresetSection
            {
                SectionKey = NormalizeSectionKey(section.SectionKey) ?? NewSectionKey(),
                Name = CleanRequired(section.Name, "Section", 120),
                NormalizedName = NormalizeName(section.Name),
                SortOrder = index
            })
            .ToArray();
        var sectionBySourceId = sourceSections
            .Select((section, index) => new { section.Id, Clone = duplicateSections[index] })
            .ToDictionary(item => item.Id, item => item.Clone);

        var duplicateProjects = source.Projects
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item =>
            {
                CompendiumPresetSection? assignedSection = null;
                if (item.CustomSectionId.HasValue)
                {
                    sectionBySourceId.TryGetValue(item.CustomSectionId.Value, out assignedSection);
                }

                return new CompendiumPresetProject
                {
                    ProjectId = item.ProjectId,
                    ProjectNameSnapshot = item.ProjectNameSnapshot,
                    SortOrder = item.SortOrder,
                    PrimaryPhotoId = item.PrimaryPhotoId,
                    PrimaryFocalX = item.PrimaryFocalX,
                    PrimaryFocalY = item.PrimaryFocalY,
                    ImageSelectionMode = item.ImageSelectionMode,
                    ImageFitMode = item.ImageFitMode,
                    DossierLayout = item.DossierLayout,
                    BalancedTextFlowMode = item.BalancedTextFlowMode,
                    NarrativeAlignmentOverride = item.NarrativeAlignmentOverride,
                    AdditionalNote = item.AdditionalNote,
                    DossierImageCount = item.DossierImageCount,
                    SupportingPhoto1Id = item.SupportingPhoto1Id,
                    SupportingPhoto1FocalX = item.SupportingPhoto1FocalX,
                    SupportingPhoto1FocalY = item.SupportingPhoto1FocalY,
                    SupportingPhoto1FitMode = item.SupportingPhoto1FitMode,
                    SupportingPhoto2Id = item.SupportingPhoto2Id,
                    SupportingPhoto2FocalX = item.SupportingPhoto2FocalX,
                    SupportingPhoto2FocalY = item.SupportingPhoto2FocalY,
                    SupportingPhoto2FitMode = item.SupportingPhoto2FitMode,
                    NarrativeSourceOverride = item.NarrativeSourceOverride,
                    CustomSection = assignedSection,
                    CustomSectionName = assignedSection?.Name ?? item.CustomSectionName
                };
            })
            .ToList();

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
            NarrativeSource = source.NarrativeSource,
            DefaultNarrativeAlignment = source.DefaultNarrativeAlignment,
            ProjectParticularsStyle = source.ProjectParticularsStyle,
            GroupingMode = source.GroupingMode,
            SortMode = source.SortMode,
            CoverImageMode = source.CoverImageMode,
            CoverHeroProjectId = source.CoverHeroProjectId,
            CoverHeroPhotoId = source.CoverHeroPhotoId,
            CoverFocalX = source.CoverFocalX,
            CoverFocalY = source.CoverFocalY,
            FrontCoverTemplate = source.FrontCoverTemplate,
            BackCoverTemplate = source.BackCoverTemplate,
            PublicationTheme = source.PublicationTheme,
            CoverBackgroundTreatment = source.CoverBackgroundTreatment,
            FrontCoverTitle = source.FrontCoverTitle,
            FrontCoverSubtitle = source.FrontCoverSubtitle,
            FrontCoverEdition = source.FrontCoverEdition,
            FrontCoverEyebrow = source.FrontCoverEyebrow,
            BackCoverTitle = source.BackCoverTitle,
            BackCoverSubtitle = source.BackCoverSubtitle,
            BackCoverEdition = source.BackCoverEdition,
            BackCoverEyebrow = source.BackCoverEyebrow,
            ShowFrontTitle = source.ShowFrontTitle,
            ShowFrontSubtitle = source.ShowFrontSubtitle,
            ShowFrontEdition = source.ShowFrontEdition,
            ShowFrontLeftLogo = source.ShowFrontLeftLogo,
            ShowFrontRightLogo = source.ShowFrontRightLogo,
            FrontLogoPlacement = source.FrontLogoPlacement,
            ShowBackTitle = source.ShowBackTitle,
            ShowBackSubtitle = source.ShowBackSubtitle,
            ShowBackEdition = source.ShowBackEdition,
            ShowBackLeftLogo = source.ShowBackLeftLogo,
            ShowBackRightLogo = source.ShowBackRightLogo,
            BackLogoPlacement = source.BackLogoPlacement,
            CreatedByUserId = userId,
            LastModifiedByUserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            RowVersion = NewRowVersion(),
            Sections = duplicateSections.ToList(),
            Projects = duplicateProjects,
            CoverImages = source.CoverImages.Select(item => new CompendiumPresetCoverImage
            {
                Surface = item.Surface,
                SlotKey = item.SlotKey,
                ImageMode = item.ImageMode,
                ProjectId = item.ProjectId,
                PhotoId = item.PhotoId,
                FocalX = item.FocalX,
                FocalY = item.FocalY,
                FitMode = item.FitMode,
                SortOrder = item.SortOrder
            }).ToList(),
            PhotoPreferences = source.PhotoPreferences.Select(item => new CompendiumPresetPhotoPreference
            {
                ProjectId = item.ProjectId,
                PhotoId = item.PhotoId,
                PreferredForPublication = item.PreferredForPublication,
                SuitableForCoverHero = item.SuitableForCoverHero
            }).ToList()
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
                ["ProjectCount"] = duplicate.Projects.Count.ToString(),
                ["SectionCount"] = duplicate.Sections.Count.ToString()
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

    private async Task<(CompendiumPresetConfiguration Configuration, List<CompendiumPresetProject> Projects, List<CompendiumPresetSection> Sections, List<CompendiumPresetCoverImage> CoverImages, List<CompendiumPresetPhotoPreference> PhotoPreferences)>
        PrepareConfigurationAsync(
            CompendiumPresetConfiguration configuration,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var normalizedSections = NormalizeSections(configuration.Sections);
        var sectionByKey = normalizedSections.ToDictionary(section => section.SectionKey, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<int>();
        var requestedProjects = (configuration.Projects ?? Array.Empty<CompendiumPresetProjectConfiguration>())
            .Where(project => project.ProjectId > 0 && seen.Add(project.ProjectId))
            .Take(MaximumProjects + 1)
            .Select(project => NormalizeProjectConfiguration(project, sectionByKey))
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
            .Concat(requestedProjects.Where(project => project.SupportingPhoto1Id.HasValue).Select(project => project.SupportingPhoto1Id!.Value))
            .Concat(requestedProjects.Where(project => project.SupportingPhoto2Id.HasValue).Select(project => project.SupportingPhoto2Id!.Value))
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

        foreach (var project in requestedProjects)
        {
            foreach (var supportPhotoId in new[] { project.SupportingPhoto1Id, project.SupportingPhoto2Id }.Where(id => id.HasValue).Select(id => id!.Value))
            {
                if (!explicitPhotos.TryGetValue(supportPhotoId, out var supportPhoto) || supportPhoto.ProjectId != project.ProjectId)
                {
                    throw new InvalidOperationException(
                        $"A supporting dossier image for {projectNames[project.ProjectId]} is no longer available. Refresh project review before saving.");
                }
            }
        }

        var normalizedDesign = NormalizeCoverDesign(configuration.CoverDesign, configuration.Cover);
        var normalizedPreferences = NormalizePhotoPreferences(configuration.PhotoPreferences, projectIds);
        var referencedCoverPhotos = normalizedDesign.Images
            .Where(image => image.ImageMode != CompendiumCoverImageMode.None && image.PhotoId is > 0)
            .Select(image => image.PhotoId!.Value);
        var preferencePhotoIds = normalizedPreferences.Select(item => item.PhotoId);
        var referencedPhotoIds = explicitPhotoIds
            .Concat(referencedCoverPhotos)
            .Concat(preferencePhotoIds)
            .Distinct()
            .ToArray();
        var referencedPhotos = referencedPhotoIds.Length == 0
            ? new Dictionary<int, SavedPhotoRow>()
            : await _db.ProjectPhotos
                .AsNoTracking()
                .Where(photo => referencedPhotoIds.Contains(photo.Id))
                .Select(photo => new SavedPhotoRow(photo.Id, photo.ProjectId))
                .ToDictionaryAsync(photo => photo.PhotoId, cancellationToken);

        foreach (var image in normalizedDesign.Images.Where(image => image.ImageMode == CompendiumCoverImageMode.Explicit))
        {
            if (image.ProjectId is not int coverProjectId
                || image.PhotoId is not int coverPhotoId
                || !projectIds.Contains(coverProjectId)
                || !referencedPhotos.TryGetValue(coverPhotoId, out var coverPhoto)
                || coverPhoto.ProjectId != coverProjectId)
            {
                throw new InvalidOperationException(
                    $"The selected {image.Surface.ToString().ToLowerInvariant()} cover image for slot '{image.SlotKey}' is no longer available. Choose another image or use automatic imagery.");
            }
        }

        // Automatic project/photo ids are sticky resolution snapshots, not
        // manual selections. A stale snapshot releases only that automatic
        // slot; it must not reject an otherwise valid Compendium update.
        normalizedDesign = normalizedDesign with
        {
            Images = normalizedDesign.Images.Select(image =>
            {
                if (image.ImageMode != CompendiumCoverImageMode.Automatic)
                {
                    return image;
                }

                var valid = image.ProjectId is int projectId
                            && image.PhotoId is int photoId
                            && projectIds.Contains(projectId)
                            && referencedPhotos.TryGetValue(photoId, out var photo)
                            && photo.ProjectId == projectId;
                return valid
                    ? image
                    : image with { ProjectId = null, PhotoId = null, FocalX = .5d, FocalY = .5d };
            }).ToArray()
        };
        configuration = configuration with { CoverDesign = normalizedDesign };

        foreach (var preference in normalizedPreferences)
        {
            if (!referencedPhotos.TryGetValue(preference.PhotoId, out var photo)
                || photo.ProjectId != preference.ProjectId)
            {
                throw new InvalidOperationException("A saved publication image preference references a photograph that is no longer available. Refresh the Cover Editor and try again.");
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
        var normalizedConfiguration = NormalizeConfiguration(
            configuration,
            requestedProjects,
            normalizedSections,
            currentYear);

        var sectionRows = normalizedConfiguration.Sections
            .OrderBy(section => section.SortOrder)
            .Select((section, index) => new CompendiumPresetSection
            {
                SectionKey = section.SectionKey,
                Name = section.Name,
                NormalizedName = NormalizeName(section.Name),
                SortOrder = index
            })
            .ToList();
        var sectionRowByKey = sectionRows.ToDictionary(section => section.SectionKey, StringComparer.OrdinalIgnoreCase);

        var rows = normalizedConfiguration.Projects
            .Select((project, sortOrder) =>
            {
                var section = !string.IsNullOrWhiteSpace(project.CustomSectionKey)
                              && sectionRowByKey.TryGetValue(project.CustomSectionKey, out var matched)
                    ? matched
                    : null;

                return new CompendiumPresetProject
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
                    ImageSelectionMode = project.ImageSelectionMode.ToString(),
                    ImageFitMode = project.ImageFitMode.ToString(),
                    DossierLayout = project.DossierLayout.ToString(),
                    BalancedTextFlowMode = project.BalancedTextFlowMode.ToString(),
                    NarrativeAlignmentOverride = project.NarrativeAlignmentOverride?.ToString(),
                    AdditionalNote = NormalizeAdditionalNote(project.AdditionalNote),
                    DossierImageCount = project.DossierImageCount,
                    SupportingPhoto1Id = project.SupportingPhoto1Id,
                    SupportingPhoto1FocalX = project.SupportingPhoto1FocalX,
                    SupportingPhoto1FocalY = project.SupportingPhoto1FocalY,
                    SupportingPhoto1FitMode = project.SupportingPhoto1FitMode.ToString(),
                    SupportingPhoto2Id = project.SupportingPhoto2Id,
                    SupportingPhoto2FocalX = project.SupportingPhoto2FocalX,
                    SupportingPhoto2FocalY = project.SupportingPhoto2FocalY,
                    SupportingPhoto2FitMode = project.SupportingPhoto2FitMode.ToString(),
                    NarrativeSourceOverride = project.NarrativeSourceOverride?.ToString(),
                    CustomSection = section,
                    CustomSectionName = section?.Name
                };
            })
            .ToList();

        var coverRows = normalizedConfiguration.CoverDesign.Images
            .OrderBy(image => image.SortOrder)
            .Select((image, index) => new CompendiumPresetCoverImage
            {
                Surface = image.Surface.ToString(),
                SlotKey = image.SlotKey,
                ImageMode = image.ImageMode.ToString(),
                ProjectId = image.ImageMode != CompendiumCoverImageMode.None ? image.ProjectId : null,
                PhotoId = image.ImageMode != CompendiumCoverImageMode.None ? image.PhotoId : null,
                FocalX = ClampFocal(image.FocalX),
                FocalY = ClampFocal(image.FocalY),
                FitMode = image.FitMode.ToString(),
                SortOrder = index
            })
            .ToList();

        var preferenceRows = normalizedConfiguration.PhotoPreferences
            .Where(preference => preference.PreferredForPublication || preference.SuitableForCoverHero)
            .Select(preference => new CompendiumPresetPhotoPreference
            {
                ProjectId = preference.ProjectId,
                PhotoId = preference.PhotoId,
                PreferredForPublication = preference.PreferredForPublication,
                SuitableForCoverHero = preference.SuitableForCoverHero
            })
            .ToList();

        return (normalizedConfiguration, rows, sectionRows, coverRows, preferenceRows);
    }

    private static CompendiumPresetConfiguration NormalizeConfiguration(
        CompendiumPresetConfiguration configuration,
        IReadOnlyList<CompendiumPresetProjectConfiguration> projects,
        IReadOnlyList<CompendiumPresetSectionConfiguration> sections,
        int currentYear)
        => new(
            CleanRequired(configuration.Title, "SDD Simulators Compendium", 120),
            CleanRequired(configuration.Subtitle, "Detailed Project Reference", 160),
            CleanRequired(configuration.Edition, $"Capability Edition · {currentYear}", 80),
            CleanOptional(configuration.HandlingMarking, 80),
            projects.ToArray())
        {
            Cover = NormalizeCoverConfiguration(configuration.Cover),
            CoverDesign = NormalizeCoverDesign(configuration.CoverDesign, configuration.Cover),
            PhotoPreferences = NormalizePhotoPreferences(configuration.PhotoPreferences, projects.Select(project => project.ProjectId).ToArray()),
            NarrativeSource = NormalizeNarrativeSource(configuration.NarrativeSource),
            DefaultNarrativeAlignment = NormalizeNarrativeAlignment(configuration.DefaultNarrativeAlignment),
            ProjectParticularsStyle = NormalizeProjectParticularsStyle(configuration.ProjectParticularsStyle),
            GroupingMode = NormalizeGroupingMode(configuration.GroupingMode),
            SortMode = NormalizeSortMode(configuration.SortMode),
            Sections = sections.ToArray()
        };

    private static CompendiumPresetProjectConfiguration NormalizeProjectConfiguration(
        CompendiumPresetProjectConfiguration project,
        IReadOnlyDictionary<string, CompendiumPresetSectionConfiguration> sectionByKey)
    {
        var mode = Enum.IsDefined(project.ImageSelectionMode)
            ? project.ImageSelectionMode
            : CompendiumImageSelectionMode.Automatic;
        var photoId = mode == CompendiumImageSelectionMode.Explicit && project.PrimaryPhotoId is > 0
            ? project.PrimaryPhotoId
            : null;
        var sectionKey = NormalizeSectionKey(project.CustomSectionKey);
        var section = sectionKey is not null && sectionByKey.TryGetValue(sectionKey, out var matched)
            ? matched
            : null;

        return project with
        {
            PrimaryPhotoId = photoId,
            PrimaryFocalX = ClampFocal(project.PrimaryFocalX),
            PrimaryFocalY = ClampFocal(project.PrimaryFocalY),
            ImageSelectionMode = mode,
            ImageFitMode = Enum.IsDefined(project.ImageFitMode) ? project.ImageFitMode : CompendiumImageFitMode.Fill,
            DossierLayout = Enum.IsDefined(project.DossierLayout) ? project.DossierLayout : CompendiumDossierLayout.Automatic,
            BalancedTextFlowMode = Enum.IsDefined(project.BalancedTextFlowMode)
                ? project.BalancedTextFlowMode
                : CompendiumBalancedTextFlowMode.FlowBelowImage,
            NarrativeAlignmentOverride = NormalizeNullableNarrativeAlignment(project.NarrativeAlignmentOverride),
            AdditionalNote = NormalizeAdditionalNote(project.AdditionalNote),
            DossierImageCount = Math.Clamp(project.DossierImageCount, 1, 3),
            SupportingPhoto1Id = project.SupportingPhoto1Id is > 0 ? project.SupportingPhoto1Id : null,
            SupportingPhoto1FocalX = ClampFocal(project.SupportingPhoto1FocalX),
            SupportingPhoto1FocalY = ClampFocal(project.SupportingPhoto1FocalY),
            SupportingPhoto1FitMode = Enum.IsDefined(project.SupportingPhoto1FitMode) ? project.SupportingPhoto1FitMode : CompendiumImageFitMode.Fill,
            SupportingPhoto2Id = project.SupportingPhoto2Id is > 0 ? project.SupportingPhoto2Id : null,
            SupportingPhoto2FocalX = ClampFocal(project.SupportingPhoto2FocalX),
            SupportingPhoto2FocalY = ClampFocal(project.SupportingPhoto2FocalY),
            SupportingPhoto2FitMode = Enum.IsDefined(project.SupportingPhoto2FitMode) ? project.SupportingPhoto2FitMode : CompendiumImageFitMode.Fill,
            CustomSectionKey = section?.SectionKey,
            CustomSectionName = section?.Name,
            NarrativeSourceOverride = NormalizeNullableNarrativeSource(project.NarrativeSourceOverride)
        };
    }

    private static string? NormalizeAdditionalNote(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static IReadOnlyList<CompendiumPresetSectionConfiguration> NormalizeSections(
        IReadOnlyList<CompendiumPresetSectionConfiguration>? sections)
    {
        const int maximumSections = 100;
        var result = new List<CompendiumPresetSectionConfiguration>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in (sections ?? Array.Empty<CompendiumPresetSectionConfiguration>())
                     .OrderBy(section => section.SortOrder)
                     .Take(maximumSections))
        {
            var name = CleanOptional(source.Name, 120);
            if (name is null)
            {
                continue;
            }

            var normalizedName = NormalizeName(name);
            if (!names.Add(normalizedName))
            {
                continue;
            }

            var key = NormalizeSectionKey(source.SectionKey) ?? NewSectionKey();
            while (!keys.Add(key))
            {
                key = NewSectionKey();
            }

            result.Add(new CompendiumPresetSectionConfiguration(key, name, result.Count));
        }

        return result;
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
        preset.NarrativeSource = NormalizeNarrativeSource(configuration.NarrativeSource).ToString();
        preset.DefaultNarrativeAlignment = NormalizeNarrativeAlignment(configuration.DefaultNarrativeAlignment).ToString();
        preset.ProjectParticularsStyle = NormalizeProjectParticularsStyle(configuration.ProjectParticularsStyle).ToString();
        preset.GroupingMode = NormalizeGroupingMode(configuration.GroupingMode).ToString();
        preset.SortMode = NormalizeSortMode(configuration.SortMode).ToString();
        preset.CoverImageMode = configuration.Cover.ImageMode.ToString();
        preset.CoverHeroProjectId = configuration.Cover.ImageMode == CompendiumCoverImageMode.Explicit
            ? configuration.Cover.HeroProjectId
            : null;
        preset.CoverHeroPhotoId = configuration.Cover.ImageMode == CompendiumCoverImageMode.Explicit
            ? configuration.Cover.HeroPhotoId
            : null;
        preset.CoverFocalX = ClampFocal(configuration.Cover.FocalX);
        preset.CoverFocalY = ClampFocal(configuration.Cover.FocalY);
        var design = NormalizeCoverDesign(configuration.CoverDesign, configuration.Cover);
        preset.FrontCoverTemplate = design.FrontTemplate.ToString();
        preset.BackCoverTemplate = design.BackTemplate.ToString();
        preset.PublicationTheme = design.PublicationTheme.ToString();
        preset.CoverBackgroundTreatment = design.BackgroundTreatment.ToString();
        preset.FrontCoverTitle = CleanOptional(design.FrontTitle, 120);
        preset.FrontCoverSubtitle = CleanOptional(design.FrontSubtitle, 160);
        preset.FrontCoverEdition = CleanOptional(design.FrontEdition, 80);
        preset.FrontCoverEyebrow = CleanOptional(design.FrontEyebrow, 80);
        preset.BackCoverTitle = CleanOptional(design.BackTitle, 120);
        preset.BackCoverSubtitle = CleanOptional(design.BackSubtitle, 160);
        preset.BackCoverEdition = CleanOptional(design.BackEdition, 80);
        preset.BackCoverEyebrow = CleanOptional(design.BackEyebrow, 80);
        preset.ShowFrontTitle = design.ShowFrontTitle;
        preset.ShowFrontSubtitle = design.ShowFrontSubtitle;
        preset.ShowFrontEdition = design.ShowFrontEdition;
        preset.ShowFrontLeftLogo = design.ShowFrontLeftLogo;
        preset.ShowFrontRightLogo = design.ShowFrontRightLogo;
        preset.FrontLogoPlacement = design.FrontLogoPlacement.ToString();
        preset.ShowBackTitle = design.ShowBackTitle;
        preset.ShowBackSubtitle = design.ShowBackSubtitle;
        preset.ShowBackEdition = design.ShowBackEdition;
        preset.ShowBackLeftLogo = design.ShowBackLeftLogo;
        preset.ShowBackRightLogo = design.ShowBackRightLogo;
        preset.BackLogoPlacement = design.BackLogoPlacement.ToString();
    }

    private async Task<CompendiumPreset> LoadTrackedAsync(
        long presetId,
        bool includeProjects,
        CancellationToken cancellationToken)
    {
        IQueryable<CompendiumPreset> query = _db.CompendiumPresets;
        if (includeProjects)
        {
            query = query
                .Include(preset => preset.Sections)
                .Include(preset => preset.Projects)
                .Include(preset => preset.CoverImages)
                .Include(preset => preset.PhotoPreferences);
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
        if (!Policies.Publications.CanManageSharedPublications(user))
        {
            throw new UnauthorizedAccessException(
                "Only Commandant, HoD or ITO may maintain shared Compendium configurations.");
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

    private static CompendiumCoverDesignConfiguration NormalizeCoverDesign(
        CompendiumCoverDesignConfiguration? design,
        CompendiumCoverConfiguration? legacyCover)
    {
        design ??= new CompendiumCoverDesignConfiguration();
        var legacy = NormalizeCoverConfiguration(legacyCover);
        var images = (design.Images ?? Array.Empty<CompendiumPresetCoverImageConfiguration>())
            .Where(item => !string.IsNullOrWhiteSpace(item.SlotKey))
            .OrderBy(item => item.SortOrder)
            .Select((item, index) =>
            {
                var mode = Enum.IsDefined(item.ImageMode)
                    ? item.ImageMode
                    : CompendiumCoverImageMode.Automatic;
                var hasCompleteReference = mode != CompendiumCoverImageMode.None
                                           && item.ProjectId is > 0
                                           && item.PhotoId is > 0;
                return new CompendiumPresetCoverImageConfiguration(
                    Enum.IsDefined(item.Surface) ? item.Surface : CompendiumCoverSurface.Front,
                    CleanRequired(item.SlotKey, "Hero", 32),
                    mode,
                    hasCompleteReference ? item.ProjectId : null,
                    hasCompleteReference ? item.PhotoId : null,
                    ClampFocal(item.FocalX),
                    ClampFocal(item.FocalY),
                    Enum.IsDefined(item.FitMode) ? item.FitMode : CompendiumImageFitMode.Fill,
                    index);
            })
            .GroupBy(item => $"{item.Surface}:{item.SlotKey}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (images.Length == 0)
        {
            images = new[]
            {
                new CompendiumPresetCoverImageConfiguration(
                    CompendiumCoverSurface.Front,
                    "Hero",
                    legacy.ImageMode,
                    legacy.HeroProjectId,
                    legacy.HeroPhotoId,
                    legacy.FocalX,
                    legacy.FocalY,
                    CompendiumImageFitMode.Fill,
                    0)
            };
        }

        return new CompendiumCoverDesignConfiguration
        {
            FrontTemplate = Enum.IsDefined(design.FrontTemplate) ? design.FrontTemplate : CompendiumFrontCoverTemplate.InstitutionalHero,
            BackTemplate = Enum.IsDefined(design.BackTemplate) ? design.BackTemplate : CompendiumBackCoverTemplate.MinimalInstitutional,
            PublicationTheme = CompendiumCoverIdentityPolicy.NormalizeTheme(design.PublicationTheme),
            BackgroundTreatment = CompendiumCoverIdentityPolicy.NormalizeTreatmentForTheme(
                design.PublicationTheme, design.BackgroundTreatment),
            FrontTitle = CleanOptional(design.FrontTitle, 120),
            FrontSubtitle = CleanOptional(design.FrontSubtitle, 160),
            FrontEdition = CleanOptional(design.FrontEdition, 80),
            FrontEyebrow = CleanOptional(design.FrontEyebrow, 80),
            BackTitle = CleanOptional(design.BackTitle, 120),
            BackSubtitle = CleanOptional(design.BackSubtitle, 160),
            BackEdition = CleanOptional(design.BackEdition, 80),
            BackEyebrow = CleanOptional(design.BackEyebrow, 80),
            ShowFrontTitle = design.ShowFrontTitle,
            ShowFrontSubtitle = design.ShowFrontSubtitle,
            ShowFrontEdition = design.ShowFrontEdition,
            ShowFrontLeftLogo = design.ShowFrontLeftLogo,
            ShowFrontRightLogo = design.ShowFrontRightLogo,
            FrontLogoPlacement = Enum.IsDefined(design.FrontLogoPlacement) ? design.FrontLogoPlacement : CompendiumCoverLogoPlacement.TopCorners,
            ShowBackTitle = design.ShowBackTitle,
            ShowBackSubtitle = design.ShowBackSubtitle,
            ShowBackEdition = design.ShowBackEdition,
            ShowBackLeftLogo = design.ShowBackLeftLogo,
            ShowBackRightLogo = design.ShowBackRightLogo,
            BackLogoPlacement = Enum.IsDefined(design.BackLogoPlacement) ? design.BackLogoPlacement : CompendiumCoverLogoPlacement.TopCorners,
            Images = images
        };
    }

    private static IReadOnlyList<CompendiumPresetPhotoPreferenceConfiguration> NormalizePhotoPreferences(
        IReadOnlyList<CompendiumPresetPhotoPreferenceConfiguration>? preferences,
        IReadOnlyCollection<int> selectedProjectIds)
    {
        var selected = selectedProjectIds.ToHashSet();
        return (preferences ?? Array.Empty<CompendiumPresetPhotoPreferenceConfiguration>())
            .Where(item => item.ProjectId > 0 && item.PhotoId > 0 && selected.Contains(item.ProjectId))
            .GroupBy(item => (item.ProjectId, item.PhotoId))
            .Select(group => group.Last())
            .Where(item => item.PreferredForPublication || item.SuitableForCoverHero)
            .Take(1000)
            .ToArray();
    }

    private static CompendiumNarrativeSource? ParseNullableNarrativeSource(string? value)
        => Enum.TryParse<CompendiumNarrativeSource>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static CompendiumNarrativeSource? NormalizeNullableNarrativeSource(CompendiumNarrativeSource? value)
        => value.HasValue && Enum.IsDefined(value.Value) ? value.Value : null;

    private static string? NormalizeSectionKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = new string(value.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(40)
            .ToArray());
        return string.IsNullOrWhiteSpace(clean) ? null : clean;
    }

    private static string NewSectionKey() => $"sec-{Guid.NewGuid():N}";

    private static CompendiumNarrativeSource ParseNarrativeSource(string? value)
        => Enum.TryParse<CompendiumNarrativeSource>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumNarrativeSource.ProjectBrief;

    private static CompendiumProjectParticularsStyle ParseProjectParticularsStyle(string? value)
        => Enum.TryParse<CompendiumProjectParticularsStyle>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumProjectParticularsStyle.Panel;

    private static CompendiumGroupingMode ParseGroupingMode(string? value)
        => Enum.TryParse<CompendiumGroupingMode>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumGroupingMode.TechnicalCategory;

    private static CompendiumSortMode ParseSortMode(string? value)
        => Enum.TryParse<CompendiumSortMode>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumSortMode.Manual;

    private static CompendiumNarrativeSource NormalizeNarrativeSource(CompendiumNarrativeSource value)
        => Enum.IsDefined(value) ? value : CompendiumNarrativeSource.ProjectBrief;

    private static CompendiumProjectParticularsStyle NormalizeProjectParticularsStyle(CompendiumProjectParticularsStyle value)
        => CompendiumProjectParticularsLayoutPolicy.Normalize(value);

    private static CompendiumGroupingMode NormalizeGroupingMode(CompendiumGroupingMode value)
        => Enum.IsDefined(value) ? value : CompendiumGroupingMode.TechnicalCategory;

    private static CompendiumSortMode NormalizeSortMode(CompendiumSortMode value)
        => Enum.IsDefined(value) ? value : CompendiumSortMode.Manual;

    private static CompendiumCoverImageMode ParseCoverMode(string? value)
        => Enum.TryParse<CompendiumCoverImageMode>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumCoverImageMode.Automatic;

    private static CompendiumCoverSurface ParseCoverSurface(string? value)
        => Enum.TryParse<CompendiumCoverSurface>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumCoverSurface.Front;

    private static CompendiumImageFitMode ParseImageFitMode(string? value)
        => Enum.TryParse<CompendiumImageFitMode>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumImageFitMode.Fill;

    private static CompendiumDossierLayout ParseDossierLayout(string? value)
        => Enum.TryParse<CompendiumDossierLayout>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumDossierLayout.Automatic;

    private static CompendiumBalancedTextFlowMode ParseBalancedTextFlowMode(
        string? value,
        CompendiumBalancedTextFlowMode fallback)
        => Enum.TryParse<CompendiumBalancedTextFlowMode>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    private static CompendiumNarrativeAlignment ParseNarrativeAlignment(
        string? value,
        CompendiumNarrativeAlignment fallback)
        => Enum.TryParse<CompendiumNarrativeAlignment>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    private static CompendiumNarrativeAlignment? ParseNullableNarrativeAlignment(string? value)
        => Enum.TryParse<CompendiumNarrativeAlignment>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static CompendiumNarrativeAlignment NormalizeNarrativeAlignment(CompendiumNarrativeAlignment value)
        => Enum.IsDefined(value) ? value : CompendiumNarrativeAlignment.Left;

    private static CompendiumNarrativeAlignment? NormalizeNullableNarrativeAlignment(CompendiumNarrativeAlignment? value)
        => value.HasValue && Enum.IsDefined(value.Value) ? value.Value : null;

    private static CompendiumFrontCoverTemplate ParseFrontTemplate(string? value)
        => Enum.TryParse<CompendiumFrontCoverTemplate>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumFrontCoverTemplate.InstitutionalHero;

    private static CompendiumBackCoverTemplate ParseBackTemplate(string? value)
        => Enum.TryParse<CompendiumBackCoverTemplate>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumBackCoverTemplate.MinimalInstitutional;

    private static CompendiumPublicationTheme ParsePublicationTheme(string? value)
        => Enum.TryParse<CompendiumPublicationTheme>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumPublicationTheme.InstitutionalGreen;

    private static CompendiumCoverBackgroundTreatment ParseCoverBackgroundTreatment(string? value)
        => Enum.TryParse<CompendiumCoverBackgroundTreatment>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumCoverBackgroundTreatment.Solid;

    private static CompendiumCoverLogoPlacement ParseLogoPlacement(string? value)
        => Enum.TryParse<CompendiumCoverLogoPlacement>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumCoverLogoPlacement.TopCorners;

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
