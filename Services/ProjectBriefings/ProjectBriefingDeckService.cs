using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services;

namespace ProjectManagement.Services.ProjectBriefings;

public interface IProjectBriefingDeckService
{
    Task<IReadOnlyList<ProjectBriefingDeckSummaryVm>> ListAsync(
        string requestingUserId,
        CancellationToken cancellationToken = default);

    // The deck when the person may change it; null when it does not exist, is deleted, or is
    // theirs only to look at (ProjectBriefingDeckAccessRules).
    Task<ProjectBriefingDeck?> GetEntityAsync(
        long deckId,
        string requestingUserId,
        bool includeItems,
        CancellationToken cancellationToken = default);

    // What the person may do with a live deck: nothing, look at it, or change it.
    // Records that a deck's PowerPoint was downloaded (review 4 fixes, Low 7). Best effort: the
    // file has already been made, and a download changes nothing.
    Task RecordDownloadAsync(long deckId, string requestingUserId, int slideCount);

    // Whether the person is Comdt or HoD, from the same roles every access check uses.
    Task<bool> IsCommandUserAsync(string requestingUserId, CancellationToken cancellationToken = default);

    Task<ProjectBriefingDeckAccessLevel> GetAccessAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default);

    // Creates a deck; with a preset, the new deck starts from that template.
    Task<long> CreateAsync(
        string requestingUserId,
        string name,
        string? description,
        CancellationToken cancellationToken = default,
        ProjectBriefingDeckPreset? preset = null);

    Task<long> DuplicateAsync(
        long sourceDeckId,
        string requestingUserId,
        CancellationToken cancellationToken = default);

    // Each change returns the deck's version after its own save, so the page keeps that version and
    // not one read afterwards (another user's save in between would otherwise be adopted silently).
    Task<string> UpdateSettingsAsync(
        long deckId,
        string requestingUserId,
        ProjectBriefingDeckSettingsCommand command,
        CancellationToken cancellationToken = default);

    Task<string> UpdateInstitutionalProfileAsync(
        long deckId,
        string requestingUserId,
        ProjectBriefingInstitutionalProfileOptions options,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task<string> UpdateRoleCharterAsync(
        long deckId,
        string requestingUserId,
        ProjectBriefingRoleCharterOptions options,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task<string> UpdateFfcGlobalFootprintAsync(
        long deckId,
        string requestingUserId,
        ProjectBriefingFfcGlobalFootprintOptions options,
        string rowVersion,
        CancellationToken cancellationToken = default);

    // The Deck tab's block list: which blocks are on and the order of the overview and opening
    // institution slides. The deck's existing settings (cover, summaries, what the deck shows,
    // institution slides) are set to match, so everything that reads them sees the same deck.
    // Returns the deck's new version.
    Task<string> UpdateBlocksAsync(
        long deckId,
        string requestingUserId,
        IReadOnlyList<ProjectBriefingDeckBlock> blocks,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task<ProjectBriefingProjectsAdded> AddProjectsAsync(
        long deckId,
        string requestingUserId,
        IReadOnlyCollection<int> projectIds,
        string? selectionRulesJson,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task<ProjectBriefingMembershipUpdateResult> UpdateMembershipAsync(
        long deckId,
        string requestingUserId,
        IReadOnlyCollection<int> addProjectIds,
        IReadOnlyCollection<int> removeProjectIds,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task<string> ReorderAsync(
        long deckId,
        string requestingUserId,
        IReadOnlyList<int> orderedProjectIds,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task<string> UpdateBriefDescriptionAsync(
        long deckId,
        int projectId,
        string requestingUserId,
        string? value,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task<string> UpdateItemPresentationAsync(
        long deckId,
        int projectId,
        string requestingUserId,
        ProjectBriefingItemPresentation presentation,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task<string> UpdateItemSpeakerNotesAsync(
        long deckId,
        int projectId,
        string requestingUserId,
        string? notes,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task<string> UpdateItemPhotoAsync(
        long deckId,
        int projectId,
        string requestingUserId,
        int? photoId,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        long deckId,
        string requestingUserId,
        string rowVersion,
        CancellationToken cancellationToken = default);

    // Recently deleted decks the person may change. Restore deleted items (Admin, under
    // AdminPolicies.RecoveryManage) passes asRecoveryAdministrator to see and restore every deck,
    // command or personal, as it did before decks had owners.
    Task<IReadOnlyList<ProjectBriefingDeletedDeckVm>> ListDeletedAsync(
        string requestingUserId,
        CancellationToken cancellationToken = default,
        bool asRecoveryAdministrator = false);

    Task<ProjectBriefingDeckRestoreResult> RestoreAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default,
        bool asRecoveryAdministrator = false);

    Task MarkGeneratedAsync(
        long deckId,
        string requestingUserId,
        int slideCount,
        CancellationToken cancellationToken = default,
        string? slideFactsJson = null);
}

public sealed class ProjectBriefingDeckService : IProjectBriefingDeckService
{
    private readonly ApplicationDbContext _db;
    private readonly IProjectBriefingSelectionService _selectionService;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ILogger<ProjectBriefingDeckService> _logger;
    private readonly IProjectBriefingDeckRoleResolver _roles;

    public ProjectBriefingDeckService(
        ApplicationDbContext db,
        IProjectBriefingSelectionService selectionService,
        IAuditService audit,
        IClock clock,
        ILogger<ProjectBriefingDeckService> logger,
        IProjectBriefingDeckRoleResolver roles)
    {
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<ProjectBriefingDeckSummaryVm>> ListAsync(
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var isCommand = await _roles.IsCommandAsync(userId, cancellationToken);
        var rows = await _db.Set<ProjectBriefingDeck>().Live()
            .AsNoTracking()
            .VisibleTo(userId, isCommand)
            .OrderByDescending(deck => deck.UpdatedAtUtc)
            .ThenBy(deck => deck.Name)
            .Select(deck => new
            {
                deck.Id,
                deck.OwnerUserId,
                deck.Scope,
                deck.LastModifiedByUserId,
                deck.SelectionRulesJson,
                deck.NarrativeMode,
                deck.LastGeneratedSlideCount,
                deck.LastGeneratedFactsJson,
                deck.Layout,
                ProjectNames = deck.Items.Select(item => item.Project.Name).ToList(),
                deck.Name,
                deck.Description,
                deck.PresentationMode,
                deck.CostMode,
                ProjectCount = deck.Items.Count,
                deck.UpdatedAtUtc,
                deck.LastGeneratedAtUtc,
                CreatedByDisplay = deck.OwnerUser.FullName != string.Empty
                    ? deck.OwnerUser.FullName
                    : deck.OwnerUser.UserName ?? "Unknown user",
                LastModifiedByDisplay = deck.LastModifiedByUser != null
                    ? (deck.LastModifiedByUser.FullName != string.Empty
                        ? deck.LastModifiedByUser.FullName
                        : deck.LastModifiedByUser.UserName ?? "Unknown user")
                    : (deck.OwnerUser.FullName != string.Empty
                        ? deck.OwnerUser.FullName
                        : deck.OwnerUser.UserName ?? "Unknown user"),
                deck.RowVersion
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new ProjectBriefingDeckSummaryVm(
                row.Id,
                row.Name,
                row.Description,
                row.PresentationMode,
                row.CostMode,
                row.ProjectCount,
                row.UpdatedAtUtc,
                row.LastGeneratedAtUtc,
                row.CreatedByDisplay,
                row.LastModifiedByDisplay,
                Encode(row.RowVersion))
            {
                Design = ProjectBriefingProjectSlideDesigns.DisplayName(ProjectBriefingProjectSlideDesigns.Of(
                    row.Layout,
                    row.NarrativeMode,
                    ProjectBriefingDeckConfigurationCodec.Read(row.SelectionRulesJson).StandardSlideOptions)),
                IsMine = row.OwnerUserId == userId || row.LastModifiedByUserId == userId,
                LastEditedByMe = (row.LastModifiedByUserId ?? row.OwnerUserId) == userId,
                LastGeneratedSlideCount = row.LastGeneratedSlideCount,
                ProjectNames = row.ProjectNames,
                Layout = row.Layout,
                Scope = row.Scope,
                CanEdit = ProjectBriefingDeckAccessRules.Evaluate(row.Scope, row.OwnerUserId, userId, isCommand)
                    == ProjectBriefingDeckAccessLevel.Edit,
                GeneratedFacts = row.LastGeneratedAtUtc is null ? null : ProjectBriefingSlideFacts.Read(row.LastGeneratedFactsJson)
            })
            .ToList();
    }

    public async Task<ProjectBriefingDeck?> GetEntityAsync(
        long deckId,
        string requestingUserId,
        bool includeItems,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadLiveAsync(deckId, includeItems, cancellationToken);
        return deck is not null
            && await AccessToAsync(deck, userId, cancellationToken) == ProjectBriefingDeckAccessLevel.Edit
            ? deck
            : null;
    }

    public Task RecordDownloadAsync(long deckId, string requestingUserId, int slideCount)
        => TryAuditAsync(
            "ProjectBriefing.PowerPointDownloaded",
            "Project briefing PowerPoint downloaded.",
            NormalizeUserId(requestingUserId),
            new Dictionary<string, string?>
            {
                ["DeckId"] = deckId.ToString(),
                ["SlideCount"] = slideCount.ToString()
            });

    public Task<bool> IsCommandUserAsync(string requestingUserId, CancellationToken cancellationToken = default)
        => _roles.IsCommandAsync(NormalizeUserId(requestingUserId), cancellationToken);

    public async Task<ProjectBriefingDeckAccessLevel> GetAccessAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var owner = await _db.Set<ProjectBriefingDeck>().Live()
            .AsNoTracking()
            .Where(deck => deck.Id == deckId)
            .Select(deck => new { deck.Scope, deck.OwnerUserId })
            .FirstOrDefaultAsync(cancellationToken);
        return owner is null
            ? ProjectBriefingDeckAccessLevel.None
            : ProjectBriefingDeckAccessRules.Evaluate(
                owner.Scope,
                owner.OwnerUserId,
                userId,
                await _roles.IsCommandAsync(userId, cancellationToken));
    }

    private Task<ProjectBriefingDeck?> LoadLiveAsync(long deckId, bool includeItems, CancellationToken cancellationToken)
    {
        IQueryable<ProjectBriefingDeck> query = _db.Set<ProjectBriefingDeck>().Live();
        if (includeItems)
        {
            // Items only: nothing here reads the project rows, and loading them for up to 120
            // projects (descriptions, briefs) made every single-item edit heavy.
            query = query.Include(deck => deck.Items);
        }

        return query.FirstOrDefaultAsync(
            deck => deck.Id == deckId,
            cancellationToken);
    }

    private async Task<ProjectBriefingDeckAccessLevel> AccessToAsync(
        ProjectBriefingDeck deck,
        string userId,
        CancellationToken cancellationToken)
        => ProjectBriefingDeckAccessRules.Evaluate(
            deck.Scope,
            deck.OwnerUserId,
            userId,
            await _roles.IsCommandAsync(userId, cancellationToken));

    // Every change to a deck loads it here. A deck the person cannot see reads as not found (its
    // existence is not disclosed); one they may only look at says so.
    private async Task<ProjectBriefingDeck> LoadEditableAsync(
        long deckId,
        string userId,
        bool includeItems,
        CancellationToken cancellationToken)
    {
        var deck = await LoadLiveAsync(deckId, includeItems, cancellationToken);
        var access = deck is null
            ? ProjectBriefingDeckAccessLevel.None
            : await AccessToAsync(deck, userId, cancellationToken);
        return access switch
        {
            ProjectBriefingDeckAccessLevel.Edit => deck!,
            ProjectBriefingDeckAccessLevel.View => throw new InvalidOperationException(ViewOnlyMessage),
            _ => throw new KeyNotFoundException(NotFoundMessage)
        };
    }

    private async Task<ProjectBriefingDeck> LoadViewableAsync(
        long deckId,
        string userId,
        bool includeItems,
        CancellationToken cancellationToken)
    {
        var deck = await LoadLiveAsync(deckId, includeItems, cancellationToken);
        return deck is not null
            && await AccessToAsync(deck, userId, cancellationToken) != ProjectBriefingDeckAccessLevel.None
            ? deck
            : throw new KeyNotFoundException(NotFoundMessage);
    }

    public const string NotFoundMessage = "The briefing deck was not found.";

    public const string ViewOnlyMessage =
        "This is a Project Officer's own deck: you can download or duplicate it, but not change it.";

    public async Task<long> CreateAsync(
        string requestingUserId,
        string name,
        string? description,
        CancellationToken cancellationToken = default,
        ProjectBriefingDeckPreset? preset = null)
    {
        if (preset is { } requested && !ProjectBriefingDeckPresets.IsTemplate(requested))
        {
            throw new InvalidOperationException("The selected template is not available.");
        }

        var userId = NormalizeUserId(requestingUserId);
        var scope = ProjectBriefingDeckAccessRules.ScopeForNewDeck(await _roles.IsCommandAsync(userId, cancellationToken));
        var normalizedName = NormalizeName(name);
        await EnsureUniqueNameAsync(normalizedName, scope, userId, excludedDeckId: null, cancellationToken);

        var now = _clock.UtcNow.ToUniversalTime();
        var deck = new ProjectBriefingDeck
        {
            OwnerUserId = userId,
            Scope = scope,
            LastModifiedByUserId = userId,
            Name = CleanName(name),
            NormalizedName = normalizedName,
            Description = NormalizeDescription(description),
            Layout = ProjectBriefingLayout.StandardBriefing,
            PresentationMode = ProjectBriefingPresentationMode.Combined,
            CostMode = ProjectBriefingCostMode.Both,
            NarrativeMode = ProjectBriefingNarrativeMode.CapabilityOverview,
            PresentationTheme = ProjectBriefingPresentationTheme.EditorialLight,
            BrandingScope = ProjectBriefingBrandingScope.AllSlides,
            IncludeCoverSlide = true,
            IncludePortfolioSummarySlide = true,
            IncludeStageSummary = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = NewRowVersion()
        };

        // The template's settings go on before the deck's one save, so a template that cannot be
        // applied leaves no deck behind with default settings (review fixes PR 8).
        if (preset is { } template)
        {
            SetFromPreset(deck, template);
        }

        _db.Add(deck);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync(
            "ProjectBriefing.DeckCreated",
            userId,
            deck,
            preset is { } chosen
                ? $"Project briefing deck created from the {ProjectBriefingDeckPresets.Get(chosen).Title} template."
                : "Project briefing deck created.");

        return deck.Id;
    }

    public async Task<long> DuplicateAsync(
        long sourceDeckId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var source = await LoadViewableAsync(sourceDeckId, userId, includeItems: true, cancellationToken);

        // The copy belongs to the person who made it: a command user's copy of a Project Officer's
        // deck is a command deck they can change.
        var scope = ProjectBriefingDeckAccessRules.ScopeForNewDeck(await _roles.IsCommandAsync(userId, cancellationToken));
        var name = await BuildDuplicateNameAsync(source.Name, scope, userId, cancellationToken);
        var now = _clock.UtcNow.ToUniversalTime();
        var duplicate = new ProjectBriefingDeck
        {
            OwnerUserId = userId,
            Scope = scope,
            LastModifiedByUserId = userId,
            Name = name,
            NormalizedName = NormalizeName(name),
            Description = source.Description,
            Layout = source.Layout,
            PresentationMode = source.PresentationMode,
            CostMode = source.CostMode,
            NarrativeMode = source.NarrativeMode,
            PresentationTheme = source.PresentationTheme,
            BrandingScope = source.BrandingScope,
            IncludeCoverSlide = source.IncludeCoverSlide,
            IncludePortfolioSummarySlide = source.IncludePortfolioSummarySlide,
            IncludeStageSummary = source.IncludeStageSummary,
            IncludeProjectCategorySummary = source.IncludeProjectCategorySummary,
            IncludeTechnicalCategorySummary = source.IncludeTechnicalCategorySummary,
            HandlingMarking = source.HandlingMarking,
            SelectionRulesJson = source.SelectionRulesJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = NewRowVersion(),
            Items = source.Items
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .Select(item => new ProjectBriefingDeckItem
                {
                    ProjectId = item.ProjectId,
                    SortOrder = item.SortOrder,
                    BriefDescriptionOverride = item.BriefDescriptionOverride,
                    Presentation = item.Presentation,
                    PhotoId = item.PhotoId,
                    SpeakerNotes = item.SpeakerNotes,
                    AddedAtUtc = now
                })
                .ToList()
        };

        _db.Add(duplicate);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProjectBriefing.DeckDuplicated", userId, duplicate, $"Deck duplicated from {sourceDeckId}.");
        return duplicate.Id;
    }

    public async Task<string> UpdateSettingsAsync(
        long deckId,
        string requestingUserId,
        ProjectBriefingDeckSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: false, cancellationToken);

        EnsureVersion(deck, command.RowVersion);
        ValidateEnums(
            command.Layout,
            command.PresentationMode,
            command.CostMode,
            command.NarrativeMode,
            command.ProjectBriefLayout,
            command.PresentationTheme,
            command.ClosingSlideType,
            command.BrandingScope);
        var updateSheetOptions = ValidateUpdateSheetOptions(
            command.UpdateSheetRows,
            command.HideEmptyUpdateSheetValues);
        var standardSlideOptions = ProjectBriefingStandardSlideOptions.Normalize(
            command.ProjectBriefLayout,
            command.ShowPresentStage,
            command.ShowPresentStatus,
            command.IncludeStageDistributionTable,
            command.KeyPoints,
            command.KeyPointsOnly);
        var institutionalProfileOptions = NormalizeProfile(command.InstitutionalProfileOptions);

        // Only check the SDD profile when this save changes it. A deck-tab save (name, theme, cost
        // mode…) passes the stored profile straight through; re-checking it would block those saves
        // on a section the user never touched (e.g. a profile switched on by an older preset).
        var storedProfile = NormalizeProfile(
            ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson).InstitutionalProfileOptions);
        if (!SameProfile(institutionalProfileOptions, storedProfile))
        {
            ValidateInstitutionalProfile(institutionalProfileOptions);
        }

        var normalizedName = NormalizeName(command.Name);
        await EnsureUniqueNameAsync(normalizedName, deck.Scope, deck.OwnerUserId, deckId, cancellationToken);

        deck.Name = CleanName(command.Name);
        deck.NormalizedName = normalizedName;
        deck.Description = NormalizeDescription(command.Description);
        deck.Layout = command.Layout;
        deck.PresentationMode = command.PresentationMode;
        deck.CostMode = command.CostMode;
        deck.NarrativeMode = command.NarrativeMode;
        deck.PresentationTheme = command.PresentationTheme;
        deck.BrandingScope = command.BrandingScope;
        // The established Standard PRISM Briefing always retains its cover and
        // portfolio-summary slides. The new formal update-sheet layout exposes
        // these as explicit user choices.
        // The block list switches the cover and portfolio summary for every layout (PR 4).
        deck.IncludeCoverSlide = command.IncludeCoverSlide;
        deck.IncludePortfolioSummarySlide = command.IncludePortfolioSummarySlide;
        deck.IncludeStageSummary = command.IncludeStageSummary;
        deck.IncludeProjectCategorySummary = command.IncludeProjectCategorySummary;
        deck.IncludeTechnicalCategorySummary = command.IncludeTechnicalCategorySummary;
        deck.SelectionRulesJson = ProjectBriefingDeckConfigurationCodec.WithPresentationOptions(
            deck.SelectionRulesJson,
            updateSheetOptions,
            standardSlideOptions,
            command.ClosingSlideType,
            institutionalProfileOptions);
        deck.HandlingMarking = NormalizeMarking(command.HandlingMarking);
        Touch(deck, userId);

        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProjectBriefing.DeckUpdated", userId, deck, "Project briefing deck settings updated.");
        return Encode(deck.RowVersion);
    }

    // A template's settings on a deck, in memory (nothing is saved): used when a deck is created from
    // a template and when a template is applied. Returns whether the institution slides changed.
    private static bool SetFromPreset(ProjectBriefingDeck deck, ProjectBriefingDeckPreset preset)
    {
        var target = ProjectBriefingDeckPresets.Get(preset).State;
        var configuration = ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson);

        deck.Layout = target.Layout;
        deck.PresentationMode = target.PresentationMode;
        deck.NarrativeMode = target.NarrativeMode;
        deck.CostMode = target.CostMode;
        deck.PresentationTheme = target.PresentationTheme;
        deck.IncludeStageSummary = target.IncludeStageSummary;
        deck.IncludeCoverSlide = target.Layout == ProjectBriefingLayout.StandardBriefing || target.IncludeCoverSlide;
        deck.IncludePortfolioSummarySlide = target.Layout == ProjectBriefingLayout.StandardBriefing
            || target.IncludePortfolioSummarySlide;

        var standard = ProjectBriefingStandardSlideOptions.Normalize(
            configuration.StandardSlideOptions.ProjectBriefLayout,
            target.ShowPresentStage,
            target.ShowPresentStatus,
            configuration.StandardSlideOptions.IncludeStageDistributionTable,
            // Presets leave key points as the deck had them.
            configuration.StandardSlideOptions.KeyPoints,
            configuration.StandardSlideOptions.KeyPointsOnly);
        var updateSheet = ProjectBriefingUpdateSheetOptions.Normalize(
            target.UpdateSheetRows,
            target.HideEmptyUpdateSheetValues);

        // A preset that shows the SDD profile switches it on (and adds it if the deck never had it);
        // one that does not leaves the profile exactly as the deck had it.
        var profile = configuration.InstitutionalProfileOptions;
        var order = configuration.AdditionalSlideOrder.ToList();
        var additionalSlidesChanged = false;
        if (target.IncludesInstitutionalProfile)
        {
            if (!profile.IncludeSlide)
            {
                profile = PrepareProfileForPreset(profile);
                ValidateInstitutionalProfile(profile);
                additionalSlidesChanged = true;
            }

            if (!order.Contains(ProjectBriefingAdditionalSlideType.InstitutionalProfile))
            {
                order.Insert(0, ProjectBriefingAdditionalSlideType.InstitutionalProfile);
                additionalSlidesChanged = true;
            }
        }

        var json = ProjectBriefingDeckConfigurationCodec.WithPresentationOptions(
            deck.SelectionRulesJson,
            updateSheet,
            standard,
            target.ClosingSlideType,
            profile);
        if (additionalSlidesChanged)
        {
            json = ProjectBriefingDeckConfigurationCodec.WithAdditionalSlideOrder(json, order);
        }

        deck.SelectionRulesJson = ProjectBriefingDeckConfigurationCodec.WithPreset(json, preset);
        return additionalSlidesChanged;
    }

    /// <summary>
    /// Switches the SDD profile on for a preset without leaving it in a state the editor would refuse:
    /// the partnerships module needs at least one MoU or partner, so a profile with none drops that
    /// module (the user can add it back with entries), and a profile left with nothing to show keeps
    /// its history timeline.
    /// </summary>
    internal static ProjectBriefingInstitutionalProfileOptions PrepareProfileForPreset(
        ProjectBriefingInstitutionalProfileOptions profile)
    {
        var modules = profile.PartnershipEntries.Count == 0
            ? profile.Modules
                .Where(module => module != ProjectBriefingInstitutionalProfileModule.Partnerships)
                .ToArray()
            : profile.Modules;

        return profile with
        {
            IncludeSlide = true,
            Modules = modules,
            IncludeHistory = profile.IncludeHistory || modules.Count == 0
        };
    }

    private static ProjectBriefingInstitutionalProfileOptions NormalizeProfile(
        ProjectBriefingInstitutionalProfileOptions options) =>
        ProjectBriefingInstitutionalProfileOptions.Normalize(
            options.IncludeSlide,
            options.Title,
            options.IncludeHistory,
            options.HistoryMilestones,
            options.Modules,
            options.ProjectScope,
            options.MaximumDetailRows,
            options.TrainingHighlightTechnicalCategory,
            options.PartnershipEntries,
            options.IncludeFooterStrip,
            options.FooterStripText,
            options.FooterStripEmphasisValue,
            options.FooterStripStyle,
            options.FooterStripAlignment);

    // The options record holds lists, so record equality compares references; compare the values.
    private static bool SameProfile(
        ProjectBriefingInstitutionalProfileOptions left,
        ProjectBriefingInstitutionalProfileOptions right) =>
        string.Equals(
            System.Text.Json.JsonSerializer.Serialize(left),
            System.Text.Json.JsonSerializer.Serialize(right),
            StringComparison.Ordinal);

    private static void ValidateInstitutionalProfile(ProjectBriefingInstitutionalProfileOptions options)
    {
        if (options.IncludeSlide
            && !options.IncludeHistory
            && options.Modules.Count == 0)
        {
            throw new InvalidOperationException("Select at least one history or institutional-output section for the SDD profile slide.");
        }

        if (options.IncludeSlide
            && options.Modules.Contains(ProjectBriefingInstitutionalProfileModule.Partnerships)
            && options.PartnershipEntries.Count == 0)
        {
            throw new InvalidOperationException("Add at least one MoU or partner when the Military–Academia–Industry Synergy module is selected.");
        }
    }

    private static void ValidateRoleCharter(ProjectBriefingRoleCharterOptions options)
    {
        if (!options.IncludeSlide)
        {
            return;
        }

        if (options.Layout != ProjectBriefingRoleCharterLayout.CharterOnly
            && options.RoleStatements.Count == 0)
        {
            throw new InvalidOperationException("Add at least one role statement or choose the Charter-only layout.");
        }

        if (options.CharterItems.Count == 0)
        {
            throw new InvalidOperationException("Add at least one charter item for the Role & Charter slide.");
        }
    }

    public async Task<string> UpdateInstitutionalProfileAsync(
        long deckId,
        string requestingUserId,
        ProjectBriefingInstitutionalProfileOptions options,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: false, cancellationToken);

        EnsureVersion(deck, rowVersion);
        var normalized = ProjectBriefingInstitutionalProfileOptions.Normalize(
            // Whether the slide is in the deck belongs to the block list (PR 4): saving the
            // editor's content keeps it as it is.
            ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson).InstitutionalProfileOptions.IncludeSlide,
            options.Title,
            options.IncludeHistory,
            options.HistoryMilestones,
            options.Modules,
            options.ProjectScope,
            options.MaximumDetailRows,
            options.TrainingHighlightTechnicalCategory,
            options.PartnershipEntries,
            options.IncludeFooterStrip,
            options.FooterStripText,
            options.FooterStripEmphasisValue,
            options.FooterStripStyle,
            options.FooterStripAlignment);

        ValidateInstitutionalProfile(normalized);
        var configuration = ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson);
        var order = normalized.IncludeSlide
            && !configuration.AdditionalSlideOrder.Contains(ProjectBriefingAdditionalSlideType.InstitutionalProfile)
                ? configuration.AdditionalSlideOrder
                    .Prepend(ProjectBriefingAdditionalSlideType.InstitutionalProfile)
                    .ToArray()
                : configuration.AdditionalSlideOrder;
        deck.SelectionRulesJson = ProjectBriefingDeckConfigurationCodec.WithAdditionalSlides(
            deck.SelectionRulesJson,
            normalized,
            configuration.RoleCharterOptions,
            configuration.FfcGlobalFootprintOptions,
            order);
        Touch(deck, userId);

        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync(
            "ProjectBriefing.InstitutionalProfileUpdated",
            userId,
            deck,
            "SDD institutional profile slide configuration updated.");
        return Encode(deck.RowVersion);
    }

    public async Task<string> UpdateRoleCharterAsync(
        long deckId,
        string requestingUserId,
        ProjectBriefingRoleCharterOptions options,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: false, cancellationToken);

        EnsureVersion(deck, rowVersion);
        var normalized = ProjectBriefingRoleCharterOptions.Normalize(
            // Whether the slide is in the deck belongs to the block list (PR 4): saving the
            // editor's content keeps it as it is.
            ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson).RoleCharterOptions.IncludeSlide,
            options.Title,
            options.Layout,
            options.UseSharedContent,
            options.RoleStatements,
            options.CharterItems);
        ValidateRoleCharter(normalized);

        var configuration = ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson);
        var order = normalized.IncludeSlide
            && !configuration.AdditionalSlideOrder.Contains(ProjectBriefingAdditionalSlideType.RoleAndCharter)
                ? configuration.AdditionalSlideOrder
                    .Append(ProjectBriefingAdditionalSlideType.RoleAndCharter)
                    .ToArray()
                : configuration.AdditionalSlideOrder;
        deck.SelectionRulesJson = ProjectBriefingDeckConfigurationCodec.WithAdditionalSlides(
            deck.SelectionRulesJson,
            configuration.InstitutionalProfileOptions,
            normalized,
            configuration.FfcGlobalFootprintOptions,
            order);
        Touch(deck, userId);

        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync(
            "ProjectBriefing.RoleCharterUpdated",
            userId,
            deck,
            "Role & Charter additional slide configuration updated.");
        return Encode(deck.RowVersion);
    }

    public async Task<string> UpdateFfcGlobalFootprintAsync(
        long deckId,
        string requestingUserId,
        ProjectBriefingFfcGlobalFootprintOptions options,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: false, cancellationToken);

        EnsureVersion(deck, rowVersion);
        var normalized = ProjectBriefingFfcGlobalFootprintOptions.Normalize(
            // Whether the slide is in the deck belongs to the block list (PR 4): saving the
            // editor's content keeps it as it is.
            ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson).FfcGlobalFootprintOptions.IncludeSlide,
            options.Title,
            options.Layout,
            options.MaximumCountryRows,
            options.IncludeCountryWiseBreakdown);
        var configuration = ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson);
        var order = normalized.IncludeSlide
            && !configuration.AdditionalSlideOrder.Contains(ProjectBriefingAdditionalSlideType.FfcGlobalFootprint)
                ? configuration.AdditionalSlideOrder.Append(ProjectBriefingAdditionalSlideType.FfcGlobalFootprint).ToArray()
                : configuration.AdditionalSlideOrder;
        deck.SelectionRulesJson = ProjectBriefingDeckConfigurationCodec.WithAdditionalSlides(
            deck.SelectionRulesJson,
            configuration.InstitutionalProfileOptions,
            configuration.RoleCharterOptions,
            normalized,
            order);
        Touch(deck, userId);

        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync(
            "ProjectBriefing.FfcGlobalFootprintUpdated",
            userId,
            deck,
            "FFC Global Footprint additional slide configuration updated.");
        return Encode(deck.RowVersion);
    }

    public async Task<string> UpdateBlocksAsync(
        long deckId,
        string requestingUserId,
        IReadOnlyList<ProjectBriefingDeckBlock> blocks,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (blocks.Any(block => !Enum.IsDefined(block.Type))
            || blocks.Select(block => block.Type).Distinct().Count() != blocks.Count)
        {
            throw new InvalidOperationException("The slide list is not valid. Reload and try again.");
        }

        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: false, cancellationToken);
        EnsureVersion(deck, rowVersion);

        var on = blocks.ToDictionary(block => block.Type, block => block.Enabled);
        bool Switch(ProjectBriefingDeckBlockType type, bool current) => on.TryGetValue(type, out var value) ? value : current;

        deck.IncludeCoverSlide = Switch(ProjectBriefingDeckBlockType.Cover, deck.IncludeCoverSlide);
        deck.IncludePortfolioSummarySlide = Switch(ProjectBriefingDeckBlockType.PortfolioSummary, deck.IncludePortfolioSummarySlide);
        if (deck.Layout == ProjectBriefingLayout.StandardBriefing)
        {
            // The stage-wise distribution took the stage summary's setting (PR 7); a page from before
            // it may still send the stage summary.
            deck.IncludeStageSummary = Switch(
                ProjectBriefingDeckBlockType.StageDistribution,
                Switch(ProjectBriefingDeckBlockType.StageSummary, deck.IncludeStageSummary));
            deck.IncludeProjectCategorySummary = Switch(ProjectBriefingDeckBlockType.ProjectCategorySummary, deck.IncludeProjectCategorySummary);
            deck.IncludeTechnicalCategorySummary = Switch(ProjectBriefingDeckBlockType.TechnicalCategorySummary, deck.IncludeTechnicalCategorySummary);

            var table = Switch(
                ProjectBriefingDeckBlockType.StatusTable,
                deck.PresentationMode is ProjectBriefingPresentationMode.ExecutiveTable or ProjectBriefingPresentationMode.Combined);
            var slides = Switch(
                ProjectBriefingDeckBlockType.ProjectSlides,
                deck.PresentationMode is ProjectBriefingPresentationMode.DetailedProjects or ProjectBriefingPresentationMode.Combined);
            if (!table && !slides)
            {
                throw new InvalidOperationException("Keep the status table or the project slides on: a deck needs one of them to show its projects.");
            }

            deck.PresentationMode = table && slides
                ? ProjectBriefingPresentationMode.Combined
                : table ? ProjectBriefingPresentationMode.ExecutiveTable : ProjectBriefingPresentationMode.DetailedProjects;
        }

        var configuration = ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson);
        var additionalOrder = configuration.AdditionalSlideOrder.ToList();
        foreach (var block in blocks)
        {
            if (ProjectBriefingDeckBlocks.AdditionalSlideFor(block.Type) is not { } slideType)
            {
                continue;
            }

            var inOrder = additionalOrder.Contains(slideType);
            var stored = slideType switch
            {
                ProjectBriefingAdditionalSlideType.InstitutionalProfile => configuration.InstitutionalProfileOptions.IncludeSlide,
                ProjectBriefingAdditionalSlideType.RoleAndCharter => configuration.RoleCharterOptions.IncludeSlide,
                _ => configuration.FfcGlobalFootprintOptions.IncludeSlide
            };
            if ((inOrder && stored) != block.Enabled || (!inOrder && stored))
            {
                var (profile, roleCharter, ffc) = SwitchAdditionalSlide(configuration, slideType, block.Enabled);
                configuration = configuration with
                {
                    InstitutionalProfileOptions = profile,
                    RoleCharterOptions = roleCharter,
                    FfcGlobalFootprintOptions = ffc
                };
            }

            // An opening slide joins the stored order even while off, so moving it in the list is kept.
            var opening = ProjectBriefingAdditionalSlideCatalog.Get(slideType).Placement == ProjectBriefingAdditionalSlidePlacement.AfterCover;
            if (!inOrder && (block.Enabled || opening))
            {
                additionalOrder.Add(slideType);
            }
        }

        // The opening institution slides take the list's order; the others keep their places.
        var openingOrder = blocks
            .Select(block => ProjectBriefingDeckBlocks.AdditionalSlideFor(block.Type))
            .OfType<ProjectBriefingAdditionalSlideType>()
            .Where(type => additionalOrder.Contains(type)
                && ProjectBriefingAdditionalSlideCatalog.Get(type).Placement == ProjectBriefingAdditionalSlidePlacement.AfterCover)
            .ToList();
        var openingSlots = additionalOrder
            .Select((type, index) => (type, index))
            .Where(item => openingOrder.Contains(item.type))
            .Select(item => item.index)
            .ToArray();
        for (var slot = 0; slot < openingSlots.Length; slot++)
        {
            additionalOrder[openingSlots[slot]] = openingOrder[slot];
        }

        var json = ProjectBriefingDeckConfigurationCodec.WithAdditionalSlides(
            deck.SelectionRulesJson,
            configuration.InstitutionalProfileOptions,
            configuration.RoleCharterOptions,
            configuration.FfcGlobalFootprintOptions,
            additionalOrder);
        deck.SelectionRulesJson = ProjectBriefingDeckConfigurationCodec.WithOverviewOrder(
            json,
            blocks
                .Select(block => block.Type)
                .Where(type => ProjectBriefingDeckBlocks.ZoneOf(type) == ProjectBriefingDeckZone.Overview));
        Touch(deck, userId);

        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProjectBriefing.DeckBlocksUpdated", userId, deck, "Project briefing deck slides updated.");
        return Encode(deck.RowVersion);
    }

    // One institution slide switched on or off, its other choices kept; switching the SDD profile or
    // Role & Charter on checks its content as the editors do.
    private static (ProjectBriefingInstitutionalProfileOptions Profile, ProjectBriefingRoleCharterOptions RoleCharter, ProjectBriefingFfcGlobalFootprintOptions Ffc)
        SwitchAdditionalSlide(
            ProjectBriefingDeckConfiguration configuration,
            ProjectBriefingAdditionalSlideType slideType,
            bool enabled)
    {
        var profile = configuration.InstitutionalProfileOptions;
        var roleCharter = configuration.RoleCharterOptions;
        var ffcGlobalFootprint = configuration.FfcGlobalFootprintOptions;
        switch (slideType)
        {
            case ProjectBriefingAdditionalSlideType.InstitutionalProfile:
                profile = ProjectBriefingInstitutionalProfileOptions.Normalize(
                    enabled,
                    profile.Title,
                    profile.IncludeHistory,
                    profile.HistoryMilestones,
                    profile.Modules,
                    profile.ProjectScope,
                    profile.MaximumDetailRows,
                    profile.TrainingHighlightTechnicalCategory,
                    profile.PartnershipEntries,
                    profile.IncludeFooterStrip,
                    profile.FooterStripText,
                    profile.FooterStripEmphasisValue,
                    profile.FooterStripStyle,
                    profile.FooterStripAlignment);
                ValidateInstitutionalProfile(profile);
                break;
            case ProjectBriefingAdditionalSlideType.RoleAndCharter:
                roleCharter = ProjectBriefingRoleCharterOptions.Normalize(
                    enabled,
                    roleCharter.Title,
                    roleCharter.Layout,
                    roleCharter.UseSharedContent,
                    roleCharter.RoleStatements,
                    roleCharter.CharterItems);
                ValidateRoleCharter(roleCharter);
                break;
            case ProjectBriefingAdditionalSlideType.FfcGlobalFootprint:
                ffcGlobalFootprint = ProjectBriefingFfcGlobalFootprintOptions.Normalize(
                    enabled,
                    ffcGlobalFootprint.Title,
                    ffcGlobalFootprint.Layout,
                    ffcGlobalFootprint.MaximumCountryRows,
                    ffcGlobalFootprint.IncludeCountryWiseBreakdown);
                break;
        }

        return (profile, roleCharter, ffcGlobalFootprint);
    }

    public async Task<ProjectBriefingProjectsAdded> AddProjectsAsync(
        long deckId,
        string requestingUserId,
        IReadOnlyCollection<int> projectIds,
        string? selectionRulesJson,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: true, cancellationToken);
        EnsureVersion(deck, rowVersion);

        var validIds = await _selectionService.ValidateProjectIdsAsync(projectIds, cancellationToken);
        var existing = deck.Items.Select(item => item.ProjectId).ToHashSet();
        var additions = validIds.Where(existing.Add).ToArray();
        const int maximumProjectsPerDeck = 120;
        if (deck.Items.Count + additions.Length > maximumProjectsPerDeck)
        {
            throw new InvalidOperationException($"A briefing deck can contain up to {maximumProjectsPerDeck} projects. Remove projects or use a separate deck.");
        }
        if (additions.Length == 0)
        {
            return new ProjectBriefingProjectsAdded(0, Encode(deck.RowVersion));
        }

        var nextOrder = deck.Items.Count == 0 ? 10 : deck.Items.Max(item => item.SortOrder) + 10;
        var now = _clock.UtcNow.ToUniversalTime();
        foreach (var projectId in additions)
        {
            deck.Items.Add(new ProjectBriefingDeckItem
            {
                ProjectId = projectId,
                SortOrder = nextOrder,
                AddedAtUtc = now
            });
            nextOrder += 10;
        }

        if (!string.IsNullOrWhiteSpace(selectionRulesJson))
        {
            deck.SelectionRulesJson = ProjectBriefingDeckConfigurationCodec.WithSelectionRules(
                deck.SelectionRulesJson,
                selectionRulesJson);
        }
        Touch(deck, userId);

        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync(
            "ProjectBriefing.ProjectsAdded",
            userId,
            deck,
            $"{additions.Length} project(s) added to briefing deck.",
            new Dictionary<string, string?> { ["ProjectIds"] = string.Join(",", additions) });
        return new ProjectBriefingProjectsAdded(additions.Length, Encode(deck.RowVersion));
    }

    public async Task<ProjectBriefingMembershipUpdateResult> UpdateMembershipAsync(
        long deckId,
        string requestingUserId,
        IReadOnlyCollection<int> addProjectIds,
        IReadOnlyCollection<int> removeProjectIds,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: true, cancellationToken);
        EnsureVersion(deck, rowVersion);

        var requestedRemovals = (removeProjectIds ?? Array.Empty<int>())
            .Where(projectId => projectId > 0)
            .Distinct()
            .ToHashSet();
        var requestedAdditions = (addProjectIds ?? Array.Empty<int>())
            .Where(projectId => projectId > 0 && !requestedRemovals.Contains(projectId))
            .Distinct()
            .ToArray();
        var validAdditions = requestedAdditions.Length == 0
            ? Array.Empty<int>()
            : (await _selectionService.ValidateProjectIdsAsync(requestedAdditions, cancellationToken)).ToArray();

        var removedItems = deck.Items
            .Where(item => requestedRemovals.Contains(item.ProjectId))
            .ToArray();
        if (removedItems.Length > 0)
        {
            _db.RemoveRange(removedItems);
            foreach (var item in removedItems)
            {
                deck.Items.Remove(item);
            }
        }

        var existing = deck.Items.Select(item => item.ProjectId).ToHashSet();
        var additions = validAdditions.Where(existing.Add).ToArray();
        const int maximumProjectsPerDeck = 120;
        if (deck.Items.Count + additions.Length > maximumProjectsPerDeck)
        {
            throw new InvalidOperationException($"A briefing deck can contain up to {maximumProjectsPerDeck} projects. Remove projects or use a separate deck.");
        }

        if (removedItems.Length == 0 && additions.Length == 0)
        {
            return new ProjectBriefingMembershipUpdateResult(Encode(deck.RowVersion), 0, 0);
        }

        var nextOrder = deck.Items.Count == 0 ? 10 : deck.Items.Max(item => item.SortOrder) + 10;
        var now = _clock.UtcNow.ToUniversalTime();
        foreach (var projectId in additions)
        {
            deck.Items.Add(new ProjectBriefingDeckItem
            {
                ProjectId = projectId,
                SortOrder = nextOrder,
                AddedAtUtc = now
            });
            nextOrder += 10;
        }

        Touch(deck, userId);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync(
            "ProjectBriefing.MembershipUpdated",
            userId,
            deck,
            $"Briefing deck membership updated: {additions.Length} added, {removedItems.Length} removed.",
            new Dictionary<string, string?>
            {
                ["AddedProjectIds"] = string.Join(",", additions),
                ["RemovedProjectIds"] = string.Join(",", removedItems.Select(item => item.ProjectId))
            });

        return new ProjectBriefingMembershipUpdateResult(Encode(deck.RowVersion), additions.Length, removedItems.Length);
    }

    public async Task<string> ReorderAsync(
        long deckId,
        string requestingUserId,
        IReadOnlyList<int> orderedProjectIds,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: true, cancellationToken);
        EnsureVersion(deck, rowVersion);

        var ordered = orderedProjectIds.Where(id => id > 0).Distinct().ToArray();
        var current = deck.Items.Select(item => item.ProjectId).OrderBy(id => id).ToArray();
        if (!current.SequenceEqual(ordered.OrderBy(id => id)))
        {
            throw new InvalidOperationException("The project order is incomplete or contains projects outside this deck.");
        }

        var orderByProject = ordered
            .Select((projectId, index) => new { projectId, order = (index + 1) * 10 })
            .ToDictionary(row => row.projectId, row => row.order);
        foreach (var item in deck.Items)
        {
            item.SortOrder = orderByProject[item.ProjectId];
        }
        Touch(deck, userId);

        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProjectBriefing.ProjectsReordered", userId, deck, "Projects reordered in briefing deck.");
        return Encode(deck.RowVersion);
    }

    public async Task<string> UpdateBriefDescriptionAsync(
        long deckId,
        int projectId,
        string requestingUserId,
        string? value,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: true, cancellationToken);
        EnsureVersion(deck, rowVersion);
        var item = deck.Items.FirstOrDefault(candidate => candidate.ProjectId == projectId)
            ?? throw new KeyNotFoundException("The project is not part of this deck.");

        item.BriefDescriptionOverride = NormalizeBriefDescription(value);
        Touch(deck, userId);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProjectBriefing.DescriptionUpdated", userId, deck, "Project briefing description updated.",
            new Dictionary<string, string?> { ["ProjectId"] = projectId.ToString() });
        return Encode(deck.RowVersion);
    }

    public async Task<string> UpdateItemPresentationAsync(
        long deckId,
        int projectId,
        string requestingUserId,
        ProjectBriefingItemPresentation presentation,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(presentation))
        {
            throw new InvalidOperationException("Choose how the project appears in the deck.");
        }

        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: true, cancellationToken);
        EnsureVersion(deck, rowVersion);
        var item = deck.Items.FirstOrDefault(candidate => candidate.ProjectId == projectId)
            ?? throw new KeyNotFoundException("The project is not part of this deck.");
        if (item.Presentation == presentation)
        {
            return Encode(deck.RowVersion);
        }

        var previous = item.Presentation;
        item.Presentation = presentation;
        Touch(deck, userId);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProjectBriefing.ItemPresentationChanged", userId, deck, "Project briefing item presentation changed.",
            new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["From"] = previous.ToString(),
                ["To"] = presentation.ToString()
            });
        return Encode(deck.RowVersion);
    }

    public async Task<string> UpdateItemSpeakerNotesAsync(
        long deckId,
        int projectId,
        string requestingUserId,
        string? notes,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeSpeakerNotes(notes);
        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: true, cancellationToken);
        EnsureVersion(deck, rowVersion);
        var item = deck.Items.FirstOrDefault(candidate => candidate.ProjectId == projectId)
            ?? throw new KeyNotFoundException("The project is not part of this deck.");
        if (string.Equals(item.SpeakerNotes, normalized, StringComparison.Ordinal))
        {
            return Encode(deck.RowVersion);
        }

        item.SpeakerNotes = normalized;
        Touch(deck, userId);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProjectBriefing.SpeakerNotesUpdated", userId, deck, "Project briefing speaker notes updated.",
            new Dictionary<string, string?> { ["ProjectId"] = projectId.ToString() });
        return Encode(deck.RowVersion);
    }

    public const int SpeakerNotesMaxLength = 4000;

    private static string? NormalizeSpeakerNotes(string? value)
    {
        var text = value?.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length > SpeakerNotesMaxLength
            ? throw new InvalidOperationException($"Speaker notes can hold up to {SpeakerNotesMaxLength} characters.")
            : text;
    }

    public async Task<string> UpdateItemPhotoAsync(
        long deckId,
        int projectId,
        string requestingUserId,
        int? photoId,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: true, cancellationToken);
        EnsureVersion(deck, rowVersion);
        var item = deck.Items.FirstOrDefault(candidate => candidate.ProjectId == projectId)
            ?? throw new KeyNotFoundException("The project is not part of this deck.");
        if (photoId is { } chosen
            && !await _db.ProjectPhotos.AnyAsync(photo => photo.Id == chosen && photo.ProjectId == projectId, cancellationToken))
        {
            throw new InvalidOperationException("That photograph is not one of the project's photos.");
        }

        if (item.PhotoId == photoId)
        {
            return Encode(deck.RowVersion);
        }

        item.PhotoId = photoId;
        Touch(deck, userId);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProjectBriefing.ItemPhotoChanged", userId, deck, "Project briefing item photo changed.",
            new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["PhotoId"] = photoId?.ToString()
            });
        return Encode(deck.RowVersion);
    }

    public async Task DeleteAsync(
        long deckId,
        string requestingUserId,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var deck = await LoadEditableAsync(deckId, userId, includeItems: false, cancellationToken);
        EnsureVersion(deck, rowVersion);

        // Soft delete: the deck, its projects, notes and choices stay intact and can be restored
        // for DeletedRetentionDays.
        var now = _clock.UtcNow.ToUniversalTime();
        deck.DeletedAtUtc = now;
        deck.DeletedByUserId = userId;
        deck.RowVersion = NewRowVersion();

        // The delete and its record are one transaction, as a restore is (review 4 fixes, M3): a
        // deck never moves to Recently deleted without a record of who deleted it.
        await using (var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken))
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                await _audit.LogAsync(
                    "ProjectBriefing.DeckDeleted",
                    $"Project briefing deck deleted; it can be restored for {DeletedRetentionDays} days.",
                    userId: userId,
                    data: new Dictionary<string, string?>
                    {
                        ["DeckId"] = deckId.ToString(),
                        ["DeckName"] = deck.Name
                    });
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                // Nothing was committed; leave no half-deleted deck tracked in this request's context.
                _db.Entry(deck).State = EntityState.Detached;
                throw;
            }
        }

        await PurgeExpiredAsync(userId, now, cancellationToken);
    }

    // Decks deleted longer ago than DeletedRetentionDays are removed for good, each with its own
    // audit entry (review 2 fixes, Low 3). A separate step after the delete, in its own transaction
    // with those audit entries: either the decks go and every entry is written, or nothing changes
    // and the next delete tries again. The delete itself is already saved and audited, so a purge
    // that fails is logged and never undoes it.
    private async Task PurgeExpiredAsync(string userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expiredBefore = now.AddDays(-DeletedRetentionDays);
        var expired = new List<ProjectBriefingDeck>();
        try
        {
            expired = await _db.Set<ProjectBriefingDeck>()
                .Where(candidate => candidate.DeletedAtUtc != null && candidate.DeletedAtUtc < expiredBefore)
                .ToListAsync(cancellationToken);
            if (expired.Count == 0)
            {
                return;
            }

            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            _db.RemoveRange(expired);
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var removed in expired)
            {
                // Required, not best effort: a purge without its record is rolled back.
                await _audit.LogAsync(
                    "ProjectBriefing.DeckPurged",
                    $"Project briefing deck removed for good, {DeletedRetentionDays} days after it was deleted.",
                    userId: userId,
                    data: new Dictionary<string, string?>
                    {
                        ["DeckId"] = removed.Id.ToString(),
                        ["DeckName"] = removed.Name,
                        ["DeletedAtUtc"] = removed.DeletedAtUtc?.ToString("O"),
                        ["DeletedByUserId"] = removed.DeletedByUserId
                    });
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Expired project briefing decks were not removed; the next delete tries again. DeckIds={DeckIds}",
                string.Join(",", expired.Select(item => item.Id)));
            // The transaction rolled back; stop tracking what it would have changed.
            foreach (var entry in _db.ChangeTracker.Entries()
                .Where(entry => entry.Entity is ProjectBriefingDeck deck && expired.Contains(deck)
                    || entry.Entity is AuditLog log && log.Action == "ProjectBriefing.DeckPurged")
                .ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    public const int DeletedRetentionDays = 30;

    public async Task<IReadOnlyList<ProjectBriefingDeletedDeckVm>> ListDeletedAsync(
        string requestingUserId,
        CancellationToken cancellationToken = default,
        bool asRecoveryAdministrator = false)
    {
        var userId = NormalizeUserId(requestingUserId);
        IQueryable<ProjectBriefingDeck> restorable = _db.Set<ProjectBriefingDeck>()
            .AsNoTracking()
            .Restorable(_clock.UtcNow);
        if (!asRecoveryAdministrator)
        {
            restorable = restorable.EditableBy(userId, await _roles.IsCommandAsync(userId, cancellationToken));
        }

        var rows = await restorable
            .OrderByDescending(deck => deck.DeletedAtUtc)
            .Select(deck => new
            {
                deck.Id,
                deck.Name,
                ProjectCount = deck.Items.Count,
                DeletedAtUtc = deck.DeletedAtUtc!.Value,
                DeletedByDisplay = deck.DeletedByUser != null
                    ? (deck.DeletedByUser.FullName != string.Empty
                        ? deck.DeletedByUser.FullName
                        : deck.DeletedByUser.UserName ?? "Unknown user")
                    : "Unknown user",
                OwnerDisplay = deck.OwnerUser != null
                    ? (deck.OwnerUser.FullName != string.Empty
                        ? deck.OwnerUser.FullName
                        : deck.OwnerUser.UserName ?? "Unknown user")
                    : "Unknown user"
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new ProjectBriefingDeletedDeckVm(
                row.Id,
                row.Name,
                row.ProjectCount,
                row.DeletedAtUtc,
                row.DeletedByDisplay,
                row.DeletedAtUtc.AddDays(DeletedRetentionDays),
                row.OwnerDisplay))
            .ToList();
    }

    public async Task<ProjectBriefingDeckRestoreResult> RestoreAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default,
        bool asRecoveryAdministrator = false)
    {
        var userId = NormalizeUserId(requestingUserId);
        var deck = await _db.Set<ProjectBriefingDeck>()
            .Restorable(_clock.UtcNow)
            .FirstOrDefaultAsync(candidate => candidate.Id == deckId, cancellationToken)
            ?? throw new KeyNotFoundException("The deleted deck was not found or can no longer be restored.");
        if (!asRecoveryAdministrator
            && await AccessToAsync(deck, userId, cancellationToken) != ProjectBriefingDeckAccessLevel.Edit)
        {
            throw new KeyNotFoundException("The deleted deck was not found or can no longer be restored.");
        }

        // Another deck may have taken the name since: restore under a free variant instead.
        var originalName = deck.Name;
        var name = originalName;
        var number = 1;
        while (await NameTakenAsync(name, deck.Scope, deck.OwnerUserId, cancellationToken))
        {
            name = number switch
            {
                1 => $"{TrimForSuffix(originalName, 11)} (restored)",
                <= 99 => $"{TrimForSuffix(originalName, 14)} (restored {number})",
                _ => $"{TrimForSuffix(originalName, 17)} — {_clock.UtcNow:yyyyMMddHHmmss}"
            };
            if (number > 99) break;
            number++;
        }

        deck.Name = name;
        deck.NormalizedName = NormalizeName(name);
        deck.DeletedAtUtc = null;
        deck.DeletedByUserId = null;
        Touch(deck, userId);

        // The restore and its audit entry commit together, as Calendar recovery does
        // (docs/ux/admin-review-4-fixes.md, PR 2). The audit write is required here, not
        // best-effort: if it fails, the restore is rolled back and the caller sees the failure,
        // so a deck never comes back without a record of who restored it. This holds for the
        // owner's restore in Briefing Studio as well as for Restore deleted items, so both paths
        // keep one rule. A second restore of the same deck racing this one fails the row-version
        // check with a DbUpdateConcurrencyException; the pages report that as already restored.
        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync(
                "ProjectBriefing.DeckRestored",
                "Project briefing deck restored.",
                userId: userId,
                data: BuildAuditData(deck, extra: null));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Nothing was committed; leave no half-restored deck tracked in this request's context.
            _db.Entry(deck).State = EntityState.Detached;
            throw;
        }

        return new ProjectBriefingDeckRestoreResult(
            deck.Id,
            deck.Name,
            !string.Equals(name, originalName, StringComparison.Ordinal));
    }

    private Task<bool> NameTakenAsync(
        string name,
        ProjectBriefingDeckScope scope,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeName(name);
        return _db.Set<ProjectBriefingDeck>().Live().AsNoTracking()
            .SameNameSpace(scope, ownerUserId)
            .AnyAsync(deck => deck.NormalizedName == normalized, cancellationToken);
    }

    // Leaves room for a suffix within the 160-character name limit.
    private static string TrimForSuffix(string name, int suffixLength)
    {
        var limit = 160 - suffixLength;
        return name.Length <= limit ? name : name[..limit].TrimEnd();
    }

    public async Task MarkGeneratedAsync(
        long deckId,
        string requestingUserId,
        int slideCount,
        CancellationToken cancellationToken = default,
        string? slideFactsJson = null)
    {
        var userId = NormalizeUserId(requestingUserId);
        _ = await LoadEditableAsync(deckId, userId, includeItems: false, cancellationToken);
        var now = _clock.UtcNow.ToUniversalTime();

        // Generation metadata must not invalidate a shared editor's optimistic-concurrency token.
        // ExecuteUpdate bypasses the tracked SaveChanges row-version rotation intentionally.
        var updated = await _db.Set<ProjectBriefingDeck>().Live()
            .Where(deck => deck.Id == deckId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(deck => deck.LastGeneratedAtUtc, now)
                    .SetProperty(deck => deck.LastGeneratedSlideCount, slideCount)
                    .SetProperty(deck => deck.LastGeneratedFactsJson, slideFactsJson),
                cancellationToken);
        if (updated == 0)
        {
            throw new KeyNotFoundException(NotFoundMessage);
        }

        await TryAuditAsync(
            "ProjectBriefing.PowerPointGenerated",
            "Project briefing PowerPoint generated.",
            userId,
            new Dictionary<string, string?>
            {
                ["DeckId"] = deckId.ToString(),
                ["SlideCount"] = slideCount.ToString()
            });
    }

    private async Task EnsureUniqueNameAsync(
        string normalizedName,
        ProjectBriefingDeckScope scope,
        string ownerUserId,
        long? excludedDeckId,
        CancellationToken cancellationToken)
    {
        var exists = await _db.Set<ProjectBriefingDeck>().Live()
            .AsNoTracking()
            .SameNameSpace(scope, ownerUserId)
            .AnyAsync(deck => deck.NormalizedName == normalizedName
                && (!excludedDeckId.HasValue || deck.Id != excludedDeckId.Value), cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException(scope == ProjectBriefingDeckScope.Command
                ? "A shared command deck with this name already exists."
                : "You already have a deck with this name.");
        }
    }

    private async Task<string> BuildDuplicateNameAsync(
        string sourceName,
        ProjectBriefingDeckScope scope,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        for (var number = 1; number <= 99; number++)
        {
            var candidate = number == 1
                ? $"{TrimForSuffix(sourceName, 7)} — Copy"
                : $"{TrimForSuffix(sourceName, 10)} — Copy {number}";
            if (!await _db.Set<ProjectBriefingDeck>().Live().AsNoTracking().SameNameSpace(scope, ownerUserId).AnyAsync(
                    deck => deck.NormalizedName == NormalizeName(candidate),
                    cancellationToken))
            {
                return candidate;
            }
        }

        return $"{TrimForSuffix(sourceName, 17)} — {_clock.UtcNow:yyyyMMddHHmmss}";
    }

    private void Touch(ProjectBriefingDeck deck, string userId)
    {
        deck.UpdatedAtUtc = _clock.UtcNow.ToUniversalTime();
        deck.LastModifiedByUserId = userId;
        deck.RowVersion = NewRowVersion();
    }

    private async Task AuditAsync(
        string action,
        string userId,
        ProjectBriefingDeck deck,
        string message,
        IDictionary<string, string?>? extra = null)
    {
        await TryAuditAsync(action, message, userId, BuildAuditData(deck, extra));
    }

    private static Dictionary<string, string?> BuildAuditData(
        ProjectBriefingDeck deck,
        IDictionary<string, string?>? extra)
    {
        var configuration = ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson);
        var updateSheetOptions = configuration.UpdateSheetOptions;
        var standardSlideOptions = configuration.StandardSlideOptions;
        var data = new Dictionary<string, string?>
        {
            ["DeckId"] = deck.Id.ToString(),
            ["DeckName"] = deck.Name,
            ["Layout"] = deck.Layout.ToString(),
            ["PresentationMode"] = deck.PresentationMode.ToString(),
            ["CostMode"] = deck.CostMode.ToString(),
            ["NarrativeMode"] = deck.NarrativeMode.ToString(),
            ["PresentationTheme"] = deck.PresentationTheme.ToString(),
            ["ClosingSlideType"] = configuration.ClosingSlideType.ToString(),
            ["BrandingScope"] = deck.BrandingScope.ToString(),
            ["IncludeCoverSlide"] = deck.IncludeCoverSlide.ToString(),
            ["IncludePortfolioSummarySlide"] = deck.IncludePortfolioSummarySlide.ToString(),
            ["UpdateSheetRows"] = string.Join(",", updateSheetOptions.Rows),
            ["HideEmptyUpdateSheetValues"] = updateSheetOptions.HideEmptyValues.ToString(),
            ["ProjectBriefLayout"] = standardSlideOptions.ProjectBriefLayout.ToString(),
            ["ShowPresentStage"] = standardSlideOptions.ShowPresentStage.ToString(),
            ["ShowPresentStatus"] = standardSlideOptions.ShowPresentStatus.ToString(),
            ["KeyPoints"] = standardSlideOptions.KeyPoints.ToString(),
            ["KeyPointsOnly"] = standardSlideOptions.KeyPointsOnly.ToString()
        };
        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                data[pair.Key] = pair.Value;
            }
        }

        return data;
    }

    private async Task TryAuditAsync(
        string action,
        string message,
        string userId,
        IDictionary<string, string?> data)
    {
        try
        {
            await _audit.LogAsync(action, message, userId: userId, data: data);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Project briefing audit write failed. Action={Action}, UserId={UserId}",
                action,
                userId);
        }
    }

    private static void EnsureVersion(ProjectBriefingDeck deck, string encodedVersion)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(encodedVersion ?? string.Empty);
        }
        catch (FormatException)
        {
            throw new DbUpdateConcurrencyException("The saved deck version is invalid. Reload the page and try again.");
        }

        if (expected.Length == 0 || !deck.RowVersion.SequenceEqual(expected))
        {
            throw new DbUpdateConcurrencyException("This deck was updated by another user. Reload the page before saving.");
        }
    }

    private static void ValidateEnums(
        ProjectBriefingLayout layout,
        ProjectBriefingPresentationMode presentationMode,
        ProjectBriefingCostMode costMode,
        ProjectBriefingNarrativeMode narrativeMode,
        ProjectBriefingProjectBriefLayout projectBriefLayout,
        ProjectBriefingPresentationTheme presentationTheme,
        ProjectBriefingClosingSlideType closingSlideType,
        ProjectBriefingBrandingScope brandingScope)
    {
        if (!Enum.IsDefined(layout)
            || !Enum.IsDefined(presentationMode)
            || !Enum.IsDefined(costMode)
            || !Enum.IsDefined(narrativeMode)
            || !Enum.IsDefined(projectBriefLayout)
            || !Enum.IsDefined(presentationTheme)
            || !Enum.IsDefined(closingSlideType)
            || !Enum.IsDefined(brandingScope))
        {
            throw new InvalidOperationException("The presentation template, deck format, project content, project-brief layout, theme, closing slide or branding setting is invalid.");
        }
    }

    private static ProjectBriefingUpdateSheetOptions ValidateUpdateSheetOptions(
        IReadOnlyList<ProjectBriefingUpdateSheetRow>? rows,
        bool hideEmptyValues)
    {
        var selected = (rows ?? Array.Empty<ProjectBriefingUpdateSheetRow>())
            .Where(Enum.IsDefined)
            .Distinct()
            .ToArray();
        if (selected.Length == 0)
        {
            throw new InvalidOperationException("Select at least one information row for Project Update Sheets.");
        }

        return new ProjectBriefingUpdateSheetOptions(selected, hideEmptyValues);
    }

    private static string NormalizeUserId(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new UnauthorizedAccessException("The current user could not be resolved.")
            : value.Trim();

    private static string CleanName(string value)
    {
        var cleaned = string.Join(" ", (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length is < 3 or > 160)
        {
            throw new InvalidOperationException("Deck name must contain between 3 and 160 characters.");
        }
        return cleaned;
    }

    private static string NormalizeName(string value) => CleanName(value).ToUpperInvariant();

    private static string? NormalizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length <= 600 ? cleaned : cleaned[..600];
    }

    private static string? NormalizeBriefDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = value.Trim()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        return cleaned.Length <= 1200 ? cleaned : cleaned[..1200];
    }

    private static string? NormalizeMarking(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > 80)
        {
            throw new InvalidOperationException("Handling/classification marking must be 80 characters or fewer.");
        }
        return cleaned.ToUpperInvariant();
    }

    private static byte[] NewRowVersion() => Guid.NewGuid().ToByteArray();
    private static string Encode(byte[] value) => value is { Length: > 0 } ? Convert.ToBase64String(value) : string.Empty;
}

public static class ProjectBriefingDeckQueries
{
    // Decks a user works with: everything except decks in the deleted list.
    internal static IQueryable<ProjectBriefingDeck> Live(this IQueryable<ProjectBriefingDeck> decks)
        => decks.Where(deck => deck.DeletedAtUtc == null);

    /// <summary>
    /// Deleted decks that can still be restored: deleted within the last
    /// <see cref="ProjectBriefingDeckService.DeletedRetentionDays"/> days. Studio's "Recently
    /// deleted", its Restore and Administration's Restore deleted items all read this one rule.
    /// </summary>
    public static IQueryable<ProjectBriefingDeck> Restorable(this IQueryable<ProjectBriefingDeck> decks, DateTimeOffset nowUtc)
    {
        var restorableAfter = nowUtc.ToUniversalTime().AddDays(-ProjectBriefingDeckService.DeletedRetentionDays);
        return decks.Where(deck => deck.DeletedAtUtc != null && deck.DeletedAtUtc >= restorableAfter);
    }
}
