using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Chat;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Services.Admin.Recovery;

/// <summary>One tab of "Restore deleted items": a kind of deleted thing and how many can come back.</summary>
public sealed record AdminRestoreTab(
    string Key,
    string Label,
    string NavigationKey,
    int? Count,
    bool OpensAnotherPage,
    string? Fragment = null)
{
    /// <summary>The tab's address: its page, at <see cref="Fragment"/> when it has one.</summary>
    public string? HrefFrom(string? pagePath) =>
        pagePath is null || Fragment is null ? pagePath : $"{pagePath}#{Fragment}";
}

/// <summary>
/// The tabs of "Restore deleted items" (docs/ux/admin-module.md, PR 5). Each kind keeps its own
/// page, route and confirmed actions; the tabs make them one place. A tab appears only if the
/// viewer can open its page, and its count is what can still be restored.
/// </summary>
public interface IAdminRestoreTabsService
{
    Task<IReadOnlyList<AdminRestoreTab>> GetAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

public static class AdminRestoreTabKeys
{
    public const string Overview = "overview";
    public const string Projects = "projects";
    public const string ProjectDocuments = "project-documents";
    public const string RepositoryDocuments = "repository-documents";
    public const string CalendarEvents = "calendar-events";
    public const string BriefingDecks = "briefing-decks";
    public const string ChatFiles = "chat-files";
}

public sealed class AdminRestoreTabsService : IAdminRestoreTabsService
{
    private readonly ApplicationDbContext _db;
    private readonly ChatStorageService _chat;
    private readonly IAdminNavigationAccess _access;
    private readonly IClock _clock;
    private readonly ILogger<AdminRestoreTabsService> _logger;

    public AdminRestoreTabsService(
        ApplicationDbContext db,
        ChatStorageService chat,
        IAdminNavigationAccess access,
        IClock clock,
        ILogger<AdminRestoreTabsService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<AdminRestoreTab>> GetAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var openable = (await _access.GetOpenableEntriesAsync(user))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        var tabs = new List<AdminRestoreTab>();

        async Task AddAsync(string key, string label, string navigationKey, bool opensAnotherPage, Func<Task<int>>? count, string? fragment = null)
        {
            if (!openable.Contains(navigationKey))
            {
                return;
            }

            tabs.Add(new AdminRestoreTab(key, label, navigationKey, count is null ? null : await CountAsync(label, count), opensAnotherPage, fragment));
        }

        await AddAsync(AdminRestoreTabKeys.Overview, "Overview", AdminNavigationKeys.RecoveryCentre, false, null);
        // The bins are defined once (AdminBins), so these counts match Home and the overview.
        await AddAsync(AdminRestoreTabKeys.Projects, "Projects", AdminNavigationKeys.ProjectTrash, false,
            () => AdminBins.CountProjectsAsync(_db, cancellationToken));
        await AddAsync(AdminRestoreTabKeys.ProjectDocuments, "Project documents", AdminNavigationKeys.DocumentRecycle, false,
            () => AdminBins.CountProjectDocumentsAsync(_db, cancellationToken));
        await AddAsync(AdminRestoreTabKeys.RepositoryDocuments, "Repository documents", AdminNavigationKeys.RepositoryTrash, true,
            () => AdminBins.CountRepositoryDocumentsAsync(_db, cancellationToken));
        await AddAsync(AdminRestoreTabKeys.CalendarEvents, "Calendar events", AdminNavigationKeys.DeletedEvents, false,
            () => AdminBins.CountEventsAsync(_db, cancellationToken));
        await AddAsync(AdminRestoreTabKeys.BriefingDecks, "Briefing decks", AdminNavigationKeys.DeletedBriefingDecks, false,
            () => AdminBins.CountBriefingDecksAsync(_db, _clock.UtcNow, cancellationToken));
        await AddAsync(AdminRestoreTabKeys.ChatFiles, "Chat files", AdminNavigationKeys.ChatStorage, true,
            () => _chat.CountRestorableByAdministratorAsync(cancellationToken),
            // Chat files open at the clean-ups that can be undone, not at the top of the page.
            fragment: "chatStorageBatches");

        return tabs;
    }

    // A count that cannot be read leaves its tab without a number; the page still opens.
    private async Task<int?> CountAsync(string label, Func<Task<int>> count)
    {
        try
        {
            return await count();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Restore deleted items could not count {Tab}; the tab is shown without a number.", label);
            return null;
        }
    }
}
