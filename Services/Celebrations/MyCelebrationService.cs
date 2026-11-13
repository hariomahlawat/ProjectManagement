using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Services.Celebrations;

/// <summary>An entry on Celebrations that is probably this person's: "Is this you?".</summary>
public sealed record MyCelebrationSuggestion(Guid Id, string Name, byte Day, byte Month);

/// <summary>
/// One line of "Birthday and anniversary" on My account. <see cref="Id"/> is the linked
/// Celebrations entry, or null when none is linked yet; then <see cref="Suggestion"/> may offer one.
/// </summary>
public sealed record MyCelebrationLine(
    CelebrationType Type,
    Guid? Id,
    byte Day,
    byte Month,
    short? Year,
    string? SpouseName,
    MyCelebrationSuggestion? Suggestion)
{
    public bool IsLinked => Id.HasValue;
}

/// <summary>
/// A person's own birthday and anniversary, from My account (docs/ux/celebration-wish.md). It
/// keeps nothing of its own: every read and write is the Celebrations entry linked to the
/// account, the same entry the Celebrations managers see, so there is one record of the date.
/// Every method takes the signed-in person's id and never touches another account's entry.
/// </summary>
public interface IMyCelebrationService
{
    /// <summary>Birthday then anniversary. Empty for an account that cannot be linked (service, test, disabled).</summary>
    Task<IReadOnlyList<MyCelebrationLine>> GetAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Adds the person's own entry, or changes the date on the one already linked to them.</summary>
    Task<AdminOperationResult> SaveAsync(
        string userId,
        CelebrationType type,
        byte day,
        byte month,
        short? year,
        string? spouseName,
        CancellationToken cancellationToken = default);

    /// <summary>"Yes, that's me": links the entry PRISM suggested. Refuses any other entry.</summary>
    Task<AdminOperationResult> ClaimAsync(string userId, Guid celebrationId, CancellationToken cancellationToken = default);

    /// <summary>Stops the wishes. The entry stays on the Calendar for the Celebrations managers to keep or delete.</summary>
    Task<AdminOperationResult> UnlinkAsync(string userId, CelebrationType type, CancellationToken cancellationToken = default);
}

public sealed class MyCelebrationService : IMyCelebrationService
{
    private static readonly CelebrationType[] Types = { CelebrationType.Birthday, CelebrationType.Anniversary };

    private readonly ApplicationDbContext _db;
    private readonly ICelebrationAdministrationService _celebrations;

    public MyCelebrationService(ApplicationDbContext db, ICelebrationAdministrationService celebrations)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _celebrations = celebrations ?? throw new ArgumentNullException(nameof(celebrations));
    }

    public async Task<IReadOnlyList<MyCelebrationLine>> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var users = await _celebrations.ListLinkableUsersAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(userId) || users.All(user => user.Id != userId))
        {
            return Array.Empty<MyCelebrationLine>();
        }

        var live = await _db.Celebrations.AsNoTracking()
            .Where(item => item.DeletedUtc == null)
            .OrderBy(item => item.Month).ThenBy(item => item.Day).ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);
        var taken = live
            .Where(item => item.UserId != null)
            .Select(item => (item.UserId!, item.EventType))
            .ToHashSet();

        return Types.Select(type =>
        {
            var mine = live.FirstOrDefault(item => item.UserId == userId && item.EventType == type);
            if (mine is not null)
            {
                return new MyCelebrationLine(type, mine.Id, mine.Day, mine.Month, mine.Year, mine.SpouseName, null);
            }

            // The same rule as the managers' list: an entry is offered only when its name fits
            // exactly one account that has no entry of this type yet, and that account is this one.
            var free = users.Where(user => !taken.Contains((user.Id, type))).ToArray();
            var suggested = live.FirstOrDefault(item =>
                item.EventType == type
                && !IsLinkedToSomeone(item, users)
                && CelebrationUserMatcher.Suggest(item.Name, free)?.Id == userId);
            return new MyCelebrationLine(type, null, 0, 0, null, null,
                suggested is null ? null : new MyCelebrationSuggestion(suggested.Id, suggested.Name, suggested.Day, suggested.Month));
        }).ToArray();
    }

    public async Task<AdminOperationResult> SaveAsync(
        string userId,
        CelebrationType type,
        byte day,
        byte month,
        short? year,
        string? spouseName,
        CancellationToken cancellationToken = default)
    {
        var lines = await GetAsync(userId, cancellationToken);
        var line = lines.FirstOrDefault(item => item.Type == type);
        if (line is null)
        {
            return AdminOperationResult.Failure("This account cannot have a birthday or anniversary.", "CelebrationUserNotFound");
        }

        // A birthday has no spouse name, whatever the form sent.
        var spouse = type == CelebrationType.Anniversary ? spouseName : null;
        string name;
        if (line.Id is Guid id)
        {
            // The name on the Calendar stays as it is written; only the date and spouse change here.
            var existing = await _celebrations.GetAsync(id, cancellationToken);
            if (existing is null || existing.UserId != userId)
            {
                return AdminOperationResult.Failure("The celebration could not be found.", "CelebrationNotFound");
            }

            name = existing.Name;
        }
        else
        {
            var user = await _db.Users.AsNoTracking()
                .Where(item => item.Id == userId)
                .Select(item => new { item.Rank, item.FullName, item.UserName })
                .SingleAsync(cancellationToken);
            name = CelebrationUserMatcher.Label(user.Rank, user.FullName, user.UserName);
        }

        var result = await _celebrations.SaveAsync(
            new CelebrationSaveCommand(line.Id, type, name, spouse, day, month, year, userId, userId),
            cancellationToken);
        return result.Succeeded
            ? AdminOperationResult.Success(type == CelebrationType.Anniversary ? "Your anniversary is saved." : "Your birthday is saved.")
            : AdminOperationResult.Failure(result.UserMessage ?? "It could not be saved.", result.ErrorCode);
    }

    public async Task<AdminOperationResult> ClaimAsync(string userId, Guid celebrationId, CancellationToken cancellationToken = default)
    {
        var lines = await GetAsync(userId, cancellationToken);
        if (lines.All(line => line.Suggestion?.Id != celebrationId))
        {
            // Not the entry PRISM offered this person: it may be someone else's, or already linked.
            return AdminOperationResult.Failure("That entry can no longer be linked from here. Add your own instead.", "CelebrationNotSuggested");
        }

        // The offer above is only a first look. The write itself refuses an entry that has been
        // linked to anyone since, so overlapping requests cannot take another person's entry.
        var result = await _celebrations.LinkAsync(celebrationId, userId, userId, cancellationToken, onlyIfNotLinked: true);
        return result.Succeeded ? AdminOperationResult.Success("Linked. PRISM will wish you on the day.") : result;
    }

    public async Task<AdminOperationResult> UnlinkAsync(string userId, CelebrationType type, CancellationToken cancellationToken = default)
    {
        var mine = await _db.Celebrations.AsNoTracking()
            .Where(item => item.DeletedUtc == null && item.UserId == userId && item.EventType == type)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var existing = mine is Guid id ? await _celebrations.GetAsync(id, cancellationToken) : null;
        if (existing is null)
        {
            return AdminOperationResult.Failure("The celebration could not be found.", "CelebrationNotFound");
        }

        var result = await _celebrations.SaveAsync(
            new CelebrationSaveCommand(existing.Id, existing.EventType, existing.Name, existing.SpouseName, existing.Day, existing.Month, existing.Year, userId, null),
            cancellationToken);
        return result.Succeeded
            ? AdminOperationResult.Success("Unlinked. PRISM will no longer wish you on that day; the entry stays on the Calendar.")
            : AdminOperationResult.Failure(result.UserMessage ?? "It could not be unlinked.", result.ErrorCode);
    }

    // An id whose account has gone, or can no longer sign in, reads as not linked.
    private static bool IsLinkedToSomeone(Celebration item, IReadOnlyList<CelebrationUserOption> users) =>
        item.UserId != null && users.Any(user => user.Id == item.UserId);
}
