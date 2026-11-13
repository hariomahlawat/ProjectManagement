using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using ProjectManagement.Services.Search;

namespace ProjectManagement.Services.Admin.MasterData;

public sealed record CelebrationDirectoryRequest(
    string? Search,
    string? Type,
    string? Window,
    int Page,
    int PageSize,
    string? Link = null);

/// <summary>An account a celebration can be linked to, as the picker shows it.</summary>
public sealed record CelebrationUserOption(string Id, string Label);

public sealed record CelebrationAdminRow(
    Guid Id,
    CelebrationType EventType,
    string DisplayName,
    string Name,
    string? SpouseName,
    byte Day,
    byte Month,
    short? Year,
    DateOnly NextOccurrence,
    int DaysAway,
    DateTimeOffset UpdatedUtc,
    string? LinkedUserLabel = null,
    CelebrationUserOption? SuggestedUser = null);

public sealed record CelebrationDirectoryResult(
    IReadOnlyList<CelebrationAdminRow> Rows,
    int Total,
    int Birthdays,
    int Anniversaries,
    int NextThirtyDays,
    int FilteredCount,
    int Page,
    int PageSize,
    int TotalPages,
    string Search,
    string Type,
    string Window,
    string Link = "all");

public sealed record CelebrationEditItem(
    Guid Id,
    CelebrationType EventType,
    string Name,
    string? SpouseName,
    byte Day,
    byte Month,
    short? Year,
    string? UserId = null);

public sealed record CelebrationSaveCommand(
    Guid? Id,
    CelebrationType EventType,
    string Name,
    string? SpouseName,
    byte Day,
    byte Month,
    short? Year,
    string ActorUserId,
    string? UserId = null);

public interface ICelebrationAdministrationService
{
    Task<CelebrationDirectoryResult> ListAsync(CelebrationDirectoryRequest request, CancellationToken cancellationToken = default);
    Task<CelebrationEditItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AdminOperationResult<Guid>> SaveAsync(CelebrationSaveCommand command, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> DeleteAsync(Guid id, string actorUserId, CancellationToken cancellationToken = default);

    /// <summary>The accounts a celebration can be linked to: people who can sign in today.</summary>
    Task<IReadOnlyList<CelebrationUserOption>> ListLinkableUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Links one celebration to an account without changing anything else about it.</summary>
    Task<AdminOperationResult> LinkAsync(Guid id, string userId, string actorUserId, CancellationToken cancellationToken = default);
}

public sealed class CelebrationAdministrationService : ICelebrationAdministrationService
{
    private const int MaximumPageSize = 100;

    private readonly ApplicationDbContext _db;
    private readonly IAdminTimeService _time;
    private readonly IAdminAuditService _audit;
    private readonly ILogger<CelebrationAdministrationService> _logger;

    public CelebrationAdministrationService(
        ApplicationDbContext db,
        IAdminTimeService time,
        IAdminAuditService audit,
        ILogger<CelebrationAdministrationService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CelebrationDirectoryResult> ListAsync(
        CelebrationDirectoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var search = request.Search?.Trim() ?? string.Empty;
        var type = NormalizeType(request.Type);
        var window = NormalizeWindow(request.Window);
        var link = NormalizeLink(request.Link);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 25 : request.PageSize, 10, MaximumPageSize);
        var page = Math.Max(1, request.Page);

        var source = _db.Celebrations.AsNoTracking().Where(item => item.DeletedUtc == null);
        var total = await source.CountAsync(cancellationToken);
        var birthdays = await source.CountAsync(item => item.EventType == CelebrationType.Birthday, cancellationToken);
        var anniversaries = total - birthdays;

        if (type == "birthday") source = source.Where(item => item.EventType == CelebrationType.Birthday);
        if (type == "anniversary") source = source.Where(item => item.EventType == CelebrationType.Anniversary);
        if (search.Length > 0)
        {
            var pattern = SearchLikePattern.Contains(search);
            source = source.Where(item =>
                EF.Functions.ILike(item.Name, pattern, SearchLikePattern.EscapeCharacter)
                || (item.SpouseName != null && EF.Functions.ILike(item.SpouseName, pattern, SearchLikePattern.EscapeCharacter)));
        }

        var entities = await source
            .OrderBy(item => item.Month)
            .ThenBy(item => item.Day)
            .ThenBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                item.EventType,
                item.Name,
                item.SpouseName,
                item.Day,
                item.Month,
                item.Year,
                item.UpdatedUtc,
                item.UserId
            })
            .ToListAsync(cancellationToken);

        // An id whose account has gone, or can no longer sign in, reads as not linked.
        var users = await ListLinkableUsersAsync(cancellationToken);
        var userLabels = users.ToDictionary(user => user.Id, user => user.Label, StringComparer.Ordinal);
        var taken = (await _db.Celebrations.AsNoTracking()
                .Where(item => item.DeletedUtc == null && item.UserId != null)
                .Select(item => new { item.UserId, item.EventType })
                .ToListAsync(cancellationToken))
            .Select(item => (item.UserId!, item.EventType))
            .ToHashSet();

        var today = _time.TodayIst;
        var rows = entities.Select(item =>
        {
            var model = new Celebration
            {
                Id = item.Id,
                EventType = item.EventType,
                Name = item.Name,
                SpouseName = item.SpouseName,
                Day = item.Day,
                Month = item.Month,
                Year = item.Year,
                UserId = item.UserId
            };
            var next = CelebrationHelpers.NextOccurrenceLocal(model, today);
            var linkedLabel = item.UserId != null && userLabels.TryGetValue(item.UserId, out var label) ? label : null;
            var suggestion = linkedLabel is null
                ? CelebrationUserMatcher.Suggest(item.Name, users.Where(user => !taken.Contains((user.Id, item.EventType))))
                : null;
            return new CelebrationAdminRow(
                item.Id,
                item.EventType,
                CelebrationHelpers.DisplayName(model, userLabels),
                item.Name,
                item.SpouseName,
                item.Day,
                item.Month,
                item.Year,
                next,
                CelebrationHelpers.DaysAway(today, next),
                item.UpdatedUtc,
                linkedLabel,
                suggestion);
        }).ToArray();

        var nextThirtyDays = rows.Count(item => item.DaysAway < 30);
        IEnumerable<CelebrationAdminRow> filtered = window switch
        {
            "today" => rows.Where(item => item.DaysAway == 0),
            "7" => rows.Where(item => item.DaysAway < 7),
            "15" => rows.Where(item => item.DaysAway < 15),
            "30" => rows.Where(item => item.DaysAway < 30),
            _ => rows
        };
        if (link == "unlinked") filtered = filtered.Where(item => item.LinkedUserLabel is null);

        var ordered = filtered.OrderBy(item => item.NextOccurrence).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        var filteredCount = ordered.Length;
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Min(page, totalPages);
        var pageRows = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return new CelebrationDirectoryResult(
            pageRows,
            total,
            birthdays,
            anniversaries,
            nextThirtyDays,
            filteredCount,
            page,
            pageSize,
            totalPages,
            search,
            type,
            window,
            link);
    }

    public async Task<CelebrationEditItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _db.Celebrations.AsNoTracking()
            .Where(item => item.Id == id && item.DeletedUtc == null)
            .Select(item => new CelebrationEditItem(
                item.Id,
                item.EventType,
                item.Name,
                item.SpouseName,
                item.Day,
                item.Month,
                item.Year,
                item.UserId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CelebrationUserOption>> ListLinkableUsersAsync(CancellationToken cancellationToken = default)
    {
        var names = await ProjectManagement.Services.Celebrations.CelebrationAccountNames.LoadAsync(_db, cancellationToken);
        return names
            .Select(user => new CelebrationUserOption(user.Key, user.Value))
            .OrderBy(user => user.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AdminOperationResult> LinkAsync(
        Guid id,
        string userId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        // Only the account changes: the entry is read and written here, so a correction someone
        // else saved a moment ago to its name or date is never written back over.
        var entity = await _db.Celebrations.SingleOrDefaultAsync(
            item => item.Id == id && item.DeletedUtc == null,
            cancellationToken);
        var linkedUserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        if (entity is null || linkedUserId is null)
        {
            return AdminOperationResult.Failure("The celebration could not be found.", "CelebrationNotFound");
        }

        var refusal = await CheckLinkAsync(linkedUserId, entity.EventType, entity.Id, cancellationToken);
        if (refusal is not null)
        {
            return AdminOperationResult.Failure(refusal.Value.Message, refusal.Value.Code);
        }

        var before = Snapshot(entity);
        entity.UserId = linkedUserId;
        entity.UpdatedUtc = _time.UtcNow;

        try
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.RecordAsync(new AdminAuditEntry(
                "CelebrationUpdated",
                nameof(Celebration),
                entity.Id.ToString(),
                Before: before,
                After: Snapshot(entity),
                ActorUserId: actorUserId,
                Origin: "Admin.MasterData.Celebrations",
                Message: $"Linked {Label(entity.EventType).ToLowerInvariant()} entry '{CelebrationHelpers.DisplayName(entity)}' to an account."), cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult.Success("Linked to the account.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Celebration link failed for {CelebrationId}.", id);
            return AdminOperationResult.Failure("The celebration could not be linked.", "CelebrationLinkFailed");
        }
    }

    // Null when the account can be linked to a celebration of this type; otherwise why not.
    private async Task<(string Message, string Code)?> CheckLinkAsync(
        string userId,
        CelebrationType eventType,
        Guid? celebrationId,
        CancellationToken cancellationToken)
    {
        var canLink = await _db.Users.AsNoTracking().AnyAsync(
            user => user.Id == userId
                    && user.AccountKind == UserAccountKind.Human
                    && !user.IsDisabled
                    && !user.PendingDeletion,
            cancellationToken);
        if (!canLink)
        {
            return ("Choose an account from the list.", "CelebrationUserNotFound");
        }

        var alreadyLinked = await _db.Celebrations.AsNoTracking().AnyAsync(
            item => item.DeletedUtc == null
                    && item.UserId == userId
                    && item.EventType == eventType
                    && (celebrationId == null || item.Id != celebrationId),
            cancellationToken);
        return alreadyLinked
            ? ($"This account already has {(eventType == CelebrationType.Anniversary ? "an anniversary" : "a birthday")} linked to it.", "CelebrationUserAlreadyLinked")
            : null;
    }

    public async Task<AdminOperationResult<Guid>> SaveAsync(
        CelebrationSaveCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(command.EventType))
        {
            return AdminOperationResult<Guid>.Failure("Select a valid celebration type.", "InvalidCelebrationType");
        }

        var name = command.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return AdminOperationResult<Guid>.Failure("Name is required.", "CelebrationNameRequired");
        }
        if (name.Length > 120)
        {
            return AdminOperationResult<Guid>.Failure("Name must be 120 characters or fewer.", "CelebrationNameTooLong");
        }
        if (command.Month is < 1 or > 12 || command.Day < 1)
        {
            return AdminOperationResult<Guid>.Failure("Select a valid annual date.", "InvalidCelebrationDate");
        }

        var calendarYear = command.Year ?? 2000;
        if (calendarYear is < 1 or > 9999 || command.Day > DateTime.DaysInMonth(calendarYear, command.Month))
        {
            return AdminOperationResult<Guid>.Failure("The selected date is invalid for this month.", "InvalidCelebrationDate");
        }

        var spouseName = string.IsNullOrWhiteSpace(command.SpouseName) ? null : command.SpouseName.Trim();
        if (spouseName?.Length > 120)
        {
            return AdminOperationResult<Guid>.Failure("Spouse name must be 120 characters or fewer.", "CelebrationSpouseNameTooLong");
        }

        var linkedUserId = string.IsNullOrWhiteSpace(command.UserId) ? null : command.UserId.Trim();
        if (linkedUserId is not null)
        {
            var refusal = await CheckLinkAsync(linkedUserId, command.EventType, command.Id, cancellationToken);
            if (refusal is not null)
            {
                return AdminOperationResult<Guid>.Failure(refusal.Value.Message, refusal.Value.Code);
            }
        }

        Celebration entity;
        object? before = null;
        var requestedCelebrationId = command.Id;
        var created = !requestedCelebrationId.HasValue;

        if (created)
        {
            entity = new Celebration
            {
                Id = Guid.NewGuid(),
                CreatedById = command.ActorUserId,
                CreatedUtc = _time.UtcNow
            };

            _db.Celebrations.Add(entity);
        }
        else
        {
            // GetValueOrDefault is deliberate here: the branch above has already
            // established that this is an update, and it avoids nullable-flow
            // ambiguity in Visual Studio/Roslyn analyzers.
            var celebrationId = requestedCelebrationId.GetValueOrDefault();
            var existing = await _db.Celebrations.SingleOrDefaultAsync(
                item => item.Id == celebrationId && item.DeletedUtc == null,
                cancellationToken);

            if (existing is null)
            {
                return AdminOperationResult<Guid>.Failure(
                    "The celebration could not be found.",
                    "CelebrationNotFound");
            }

            entity = existing;
            before = Snapshot(existing);
        }

        entity.EventType = command.EventType;
        entity.Name = name;
        entity.SpouseName = spouseName;
        entity.Day = command.Day;
        entity.Month = command.Month;
        entity.Year = command.Year;
        entity.UserId = linkedUserId;
        entity.UpdatedUtc = _time.UtcNow;

        try
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.RecordAsync(new AdminAuditEntry(
                created ? "CelebrationCreated" : "CelebrationUpdated",
                nameof(Celebration),
                entity.Id.ToString(),
                Before: before,
                After: Snapshot(entity),
                ActorUserId: command.ActorUserId,
                Origin: "Admin.MasterData.Celebrations",
                Message: created
                    ? $"Created {Label(entity.EventType).ToLowerInvariant()} entry '{CelebrationHelpers.DisplayName(entity)}'."
                    : $"Updated {Label(entity.EventType).ToLowerInvariant()} entry '{CelebrationHelpers.DisplayName(entity)}'."), cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

            return AdminOperationResult<Guid>.Success(
                entity.Id,
                created ? $"{Label(entity.EventType)} added." : $"{Label(entity.EventType)} updated.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Celebration save failed for {CelebrationId}.", entity.Id);
            return AdminOperationResult<Guid>.Failure(
                "The celebration could not be saved. Review the application log using the request trace.",
                "CelebrationSaveFailed");
        }
    }

    public async Task<AdminOperationResult> DeleteAsync(
        Guid id,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.Celebrations.SingleOrDefaultAsync(
            item => item.Id == id && item.DeletedUtc == null,
            cancellationToken);
        if (entity is null)
        {
            return AdminOperationResult.Failure("The celebration could not be found.", "CelebrationNotFound");
        }

        var before = Snapshot(entity);
        entity.DeletedUtc = _time.UtcNow;
        entity.UpdatedUtc = _time.UtcNow;

        try
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.RecordAsync(new AdminAuditEntry(
                "CelebrationDeleted",
                nameof(Celebration),
                entity.Id.ToString(),
                Before: before,
                After: new { entity.Id, entity.DeletedUtc, ActorUserId = actorUserId },
                ActorUserId: actorUserId,
                Origin: "Admin.MasterData.Celebrations",
                Message: $"Deleted {Label(entity.EventType).ToLowerInvariant()} entry '{CelebrationHelpers.DisplayName(entity)}'."), cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult.Success($"{Label(entity.EventType)} deleted.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Celebration deletion failed for {CelebrationId}.", id);
            return AdminOperationResult.Failure("The celebration could not be deleted.", "CelebrationDeleteFailed");
        }
    }

    private static object Snapshot(Celebration item) => new
    {
        item.Id,
        item.EventType,
        item.Name,
        item.SpouseName,
        item.Day,
        item.Month,
        item.Year,
        item.UserId,
        item.UpdatedUtc,
        item.DeletedUtc
    };

    private static string Label(CelebrationType type) => type == CelebrationType.Anniversary ? "Anniversary" : "Birthday";
    private static string NormalizeType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "birthday" => "birthday",
        "anniversary" => "anniversary",
        _ => "all"
    };
    private static string NormalizeLink(string? value) =>
        string.Equals(value?.Trim(), "unlinked", StringComparison.OrdinalIgnoreCase) ? "unlinked" : "all";
    private static string NormalizeWindow(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "today" => "today",
        "7" => "7",
        "15" => "15",
        "30" => "30",
        _ => "all"
    };
}
