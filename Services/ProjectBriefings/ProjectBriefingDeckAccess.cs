using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

public enum ProjectBriefingDeckAccessLevel
{
    None = 0,
    View = 1,
    Edit = 2
}

// The one rule for who may see and change a briefing deck (docs/ux/briefing-decks.md, "Plan: Blocks,
// Project Officer decks and focus view", PR 2). Command users (Comdt, HoD) share every command deck,
// as before, and may look at a Project Officer's deck without changing it. Anyone else who reaches
// the decks (a Project Officer) sees and changes only the personal decks they made. Every service
// and page path that loads a deck goes through these rules, so hiding a button is never the guard.
public static class ProjectBriefingDeckAccessRules
{
    public static ProjectBriefingDeckAccessLevel Evaluate(
        ProjectBriefingDeckScope scope,
        string ownerUserId,
        string userId,
        bool isCommand)
    {
        if (scope == ProjectBriefingDeckScope.Command)
        {
            return isCommand ? ProjectBriefingDeckAccessLevel.Edit : ProjectBriefingDeckAccessLevel.None;
        }

        if (string.Equals(ownerUserId, userId, StringComparison.Ordinal))
        {
            return ProjectBriefingDeckAccessLevel.Edit;
        }

        return isCommand ? ProjectBriefingDeckAccessLevel.View : ProjectBriefingDeckAccessLevel.None;
    }

    // A command user's new deck is shared with command; anyone else's is their own.
    public static ProjectBriefingDeckScope ScopeForNewDeck(bool isCommand)
        => isCommand ? ProjectBriefingDeckScope.Command : ProjectBriefingDeckScope.Personal;

    public static IQueryable<ProjectBriefingDeck> VisibleTo(
        this IQueryable<ProjectBriefingDeck> decks,
        string userId,
        bool isCommand)
        => isCommand
            ? decks.Where(deck => deck.Scope == ProjectBriefingDeckScope.Command
                || deck.Scope == ProjectBriefingDeckScope.Personal)
            : decks.Where(deck => deck.Scope == ProjectBriefingDeckScope.Personal && deck.OwnerUserId == userId);

    public static IQueryable<ProjectBriefingDeck> EditableBy(
        this IQueryable<ProjectBriefingDeck> decks,
        string userId,
        bool isCommand)
        => isCommand
            ? decks.Where(deck => deck.Scope == ProjectBriefingDeckScope.Command
                || (deck.Scope == ProjectBriefingDeckScope.Personal && deck.OwnerUserId == userId))
            : decks.Where(deck => deck.Scope == ProjectBriefingDeckScope.Personal && deck.OwnerUserId == userId);

    // Names are unique among live decks of one scope: command decks among themselves, and a
    // personal deck among its owner's personal decks.
    public static IQueryable<ProjectBriefingDeck> SameNameSpace(
        this IQueryable<ProjectBriefingDeck> decks,
        ProjectBriefingDeckScope scope,
        string ownerUserId)
        => scope == ProjectBriefingDeckScope.Command
            ? decks.Where(deck => deck.Scope == ProjectBriefingDeckScope.Command)
            : decks.Where(deck => deck.Scope == ProjectBriefingDeckScope.Personal && deck.OwnerUserId == ownerUserId);
}

// Whether a person counts as command for briefing decks, from their roles in the database (the same
// roles the ProjectBriefingDecks.Manage policy reads), looked up once per request.
public interface IProjectBriefingDeckRoleResolver
{
    Task<bool> IsCommandAsync(string userId, CancellationToken cancellationToken = default);

    // The person's role names, from the same store, for rules beyond deck access (who may fix a
    // project's content).
    Task<IReadOnlyCollection<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class ProjectBriefingDeckRoleResolver : IProjectBriefingDeckRoleResolver
{
    private static readonly string[] CommandRoles = { RoleNames.Comdt, RoleNames.HoD };

    private readonly ApplicationDbContext _db;
    private readonly Dictionary<string, IReadOnlyCollection<string>> _cache = new(StringComparer.Ordinal);

    public ProjectBriefingDeckRoleResolver(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<bool> IsCommandAsync(string userId, CancellationToken cancellationToken = default)
        => (await GetRolesAsync(userId, cancellationToken)).Any(name => CommandRoles.Contains(name));

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<string>();
        }

        if (_cache.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(link => link.UserId == userId)
            .Join(_db.Roles, link => link.RoleId, role => role.Id, (link, role) => role.Name)
            .Where(name => name != null)
            .Select(name => name!)
            .ToListAsync(cancellationToken);
        _cache[userId] = roles;
        return roles;
    }
}
