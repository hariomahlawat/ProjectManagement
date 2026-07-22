using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services;

namespace ProjectManagement.Services.ProjectBriefings;

public interface IProjectBriefingDeckService
{
    Task<IReadOnlyList<ProjectBriefingDeckSummaryVm>> ListAsync(
        string requestingUserId,
        CancellationToken cancellationToken = default);

    Task<ProjectBriefingDeck?> GetEntityAsync(
        long deckId,
        string requestingUserId,
        bool includeItems,
        CancellationToken cancellationToken = default);

    Task<long> CreateAsync(
        string requestingUserId,
        string name,
        string? description,
        CancellationToken cancellationToken = default);

    Task<long> DuplicateAsync(
        long sourceDeckId,
        string requestingUserId,
        CancellationToken cancellationToken = default);

    Task UpdateSettingsAsync(
        long deckId,
        string requestingUserId,
        ProjectBriefingDeckSettingsCommand command,
        CancellationToken cancellationToken = default);

    Task<int> AddProjectsAsync(
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

    Task RemoveProjectAsync(
        long deckId,
        int projectId,
        string requestingUserId,
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

    Task DeleteAsync(
        long deckId,
        string requestingUserId,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task MarkGeneratedAsync(
        long deckId,
        string requestingUserId,
        int slideCount,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectBriefingDeckService : IProjectBriefingDeckService
{
    private readonly ApplicationDbContext _db;
    private readonly IProjectBriefingSelectionService _selectionService;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ILogger<ProjectBriefingDeckService> _logger;

    public ProjectBriefingDeckService(
        ApplicationDbContext db,
        IProjectBriefingSelectionService selectionService,
        IAuditService audit,
        IClock clock,
        ILogger<ProjectBriefingDeckService> logger)
    {
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
        _ = NormalizeUserId(requestingUserId);
        var rows = await _db.Set<ProjectBriefingDeck>()
            .AsNoTracking()
            .OrderByDescending(deck => deck.UpdatedAtUtc)
            .ThenBy(deck => deck.Name)
            .Select(deck => new
            {
                deck.Id,
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
                Encode(row.RowVersion)))
            .ToList();
    }

    public Task<ProjectBriefingDeck?> GetEntityAsync(
        long deckId,
        string requestingUserId,
        bool includeItems,
        CancellationToken cancellationToken = default)
    {
        _ = NormalizeUserId(requestingUserId);
        IQueryable<ProjectBriefingDeck> query = _db.Set<ProjectBriefingDeck>();
        if (includeItems)
        {
            query = query
                .Include(deck => deck.Items)
                .ThenInclude(item => item.Project);
        }

        return query.FirstOrDefaultAsync(
            deck => deck.Id == deckId,
            cancellationToken);
    }

    public async Task<long> CreateAsync(
        string requestingUserId,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var normalizedName = NormalizeName(name);
        await EnsureUniqueNameAsync(normalizedName, excludedDeckId: null, cancellationToken);

        var now = _clock.UtcNow.ToUniversalTime();
        var deck = new ProjectBriefingDeck
        {
            OwnerUserId = userId,
            LastModifiedByUserId = userId,
            Name = CleanName(name),
            NormalizedName = normalizedName,
            Description = NormalizeDescription(description),
            PresentationMode = ProjectBriefingPresentationMode.Combined,
            CostMode = ProjectBriefingCostMode.Both,
            IncludeStageSummary = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = NewRowVersion()
        };

        _db.Add(deck);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProjectBriefing.DeckCreated", userId, deck, "Project briefing deck created.");
        return deck.Id;
    }

    public async Task<long> DuplicateAsync(
        long sourceDeckId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var source = await GetEntityAsync(sourceDeckId, userId, includeItems: true, cancellationToken)
            ?? throw new KeyNotFoundException("The shared command deck was not found.");

        var name = await BuildDuplicateNameAsync(source.Name, cancellationToken);
        var now = _clock.UtcNow.ToUniversalTime();
        var duplicate = new ProjectBriefingDeck
        {
            OwnerUserId = userId,
            LastModifiedByUserId = userId,
            Name = name,
            NormalizedName = NormalizeName(name),
            Description = source.Description,
            PresentationMode = source.PresentationMode,
            CostMode = source.CostMode,
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
                    AddedAtUtc = now
                })
                .ToList()
        };

        _db.Add(duplicate);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProjectBriefing.DeckDuplicated", userId, duplicate, $"Deck duplicated from {sourceDeckId}.");
        return duplicate.Id;
    }

    public async Task UpdateSettingsAsync(
        long deckId,
        string requestingUserId,
        ProjectBriefingDeckSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = NormalizeUserId(requestingUserId);
        var deck = await GetEntityAsync(deckId, userId, includeItems: false, cancellationToken)
            ?? throw new KeyNotFoundException("The shared command deck was not found.");

        EnsureVersion(deck, command.RowVersion);
        ValidateEnums(command.PresentationMode, command.CostMode);
        var normalizedName = NormalizeName(command.Name);
        await EnsureUniqueNameAsync(normalizedName, deckId, cancellationToken);

        deck.Name = CleanName(command.Name);
        deck.NormalizedName = normalizedName;
        deck.Description = NormalizeDescription(command.Description);
        deck.PresentationMode = command.PresentationMode;
        deck.CostMode = command.CostMode;
        deck.IncludeStageSummary = command.IncludeStageSummary;
        deck.IncludeProjectCategorySummary = command.IncludeProjectCategorySummary;
        deck.IncludeTechnicalCategorySummary = command.IncludeTechnicalCategorySummary;
        deck.HandlingMarking = NormalizeMarking(command.HandlingMarking);
        Touch(deck, userId);

        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProjectBriefing.DeckUpdated", userId, deck, "Project briefing deck settings updated.");
    }

    public async Task<int> AddProjectsAsync(
        long deckId,
        string requestingUserId,
        IReadOnlyCollection<int> projectIds,
        string? selectionRulesJson,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var deck = await GetEntityAsync(deckId, userId, includeItems: true, cancellationToken)
            ?? throw new KeyNotFoundException("The shared command deck was not found.");
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
            return 0;
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
            deck.SelectionRulesJson = selectionRulesJson;
        }
        Touch(deck, userId);

        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync(
            "ProjectBriefing.ProjectsAdded",
            userId,
            deck,
            $"{additions.Length} project(s) added to briefing deck.",
            new Dictionary<string, string?> { ["ProjectIds"] = string.Join(",", additions) });
        return additions.Length;
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
        var deck = await GetEntityAsync(deckId, userId, includeItems: true, cancellationToken)
            ?? throw new KeyNotFoundException("The shared command deck was not found.");
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

    public async Task RemoveProjectAsync(
        long deckId,
        int projectId,
        string requestingUserId,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var deck = await GetEntityAsync(deckId, userId, includeItems: true, cancellationToken)
            ?? throw new KeyNotFoundException("The shared command deck was not found.");
        EnsureVersion(deck, rowVersion);
        var item = deck.Items.FirstOrDefault(candidate => candidate.ProjectId == projectId)
            ?? throw new KeyNotFoundException("The project is not part of this deck.");

        _db.Remove(item);
        Touch(deck, userId);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProjectBriefing.ProjectRemoved", userId, deck, "Project removed from briefing deck.",
            new Dictionary<string, string?> { ["ProjectId"] = projectId.ToString() });
    }

    public async Task<string> ReorderAsync(
        long deckId,
        string requestingUserId,
        IReadOnlyList<int> orderedProjectIds,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var deck = await GetEntityAsync(deckId, userId, includeItems: true, cancellationToken)
            ?? throw new KeyNotFoundException("The shared command deck was not found.");
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
        var deck = await GetEntityAsync(deckId, userId, includeItems: true, cancellationToken)
            ?? throw new KeyNotFoundException("The shared command deck was not found.");
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

    public async Task DeleteAsync(
        long deckId,
        string requestingUserId,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var deck = await GetEntityAsync(deckId, userId, includeItems: false, cancellationToken)
            ?? throw new KeyNotFoundException("The shared command deck was not found.");
        EnsureVersion(deck, rowVersion);

        _db.Remove(deck);
        await _db.SaveChangesAsync(cancellationToken);
        await TryAuditAsync(
            "ProjectBriefing.DeckDeleted",
            "Project briefing deck deleted.",
            userId,
            new Dictionary<string, string?>
            {
                ["DeckId"] = deckId.ToString(),
                ["DeckName"] = deck.Name
            });
    }

    public async Task MarkGeneratedAsync(
        long deckId,
        string requestingUserId,
        int slideCount,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(requestingUserId);
        var now = _clock.UtcNow.ToUniversalTime();

        // Generation metadata must not invalidate a shared editor's optimistic-concurrency token.
        // ExecuteUpdate bypasses the tracked SaveChanges row-version rotation intentionally.
        var updated = await _db.Set<ProjectBriefingDeck>()
            .Where(deck => deck.Id == deckId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(deck => deck.LastGeneratedAtUtc, now),
                cancellationToken);
        if (updated == 0)
        {
            throw new KeyNotFoundException("The shared command deck was not found.");
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
        long? excludedDeckId,
        CancellationToken cancellationToken)
    {
        var exists = await _db.Set<ProjectBriefingDeck>()
            .AsNoTracking()
            .AnyAsync(deck => deck.NormalizedName == normalizedName
                && (!excludedDeckId.HasValue || deck.Id != excludedDeckId.Value), cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException("A shared command deck with this name already exists.");
        }
    }

    private async Task<string> BuildDuplicateNameAsync(
        string sourceName,
        CancellationToken cancellationToken)
    {
        for (var number = 1; number <= 99; number++)
        {
            var candidate = number == 1 ? $"{sourceName} — Copy" : $"{sourceName} — Copy {number}";
            if (!await _db.Set<ProjectBriefingDeck>().AsNoTracking().AnyAsync(
                    deck => deck.NormalizedName == NormalizeName(candidate),
                    cancellationToken))
            {
                return candidate;
            }
        }

        return $"{sourceName} — {_clock.UtcNow:yyyyMMddHHmmss}";
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
        var data = new Dictionary<string, string?>
        {
            ["DeckId"] = deck.Id.ToString(),
            ["DeckName"] = deck.Name,
            ["PresentationMode"] = deck.PresentationMode.ToString(),
            ["CostMode"] = deck.CostMode.ToString()
        };
        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                data[pair.Key] = pair.Value;
            }
        }

        await TryAuditAsync(action, message, userId, data);
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
        ProjectBriefingPresentationMode presentationMode,
        ProjectBriefingCostMode costMode)
    {
        if (!Enum.IsDefined(presentationMode) || !Enum.IsDefined(costMode))
        {
            throw new InvalidOperationException("The deck format or cost setting is invalid.");
        }
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
