using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Services.Notebook;

public sealed class NotebookService : INotebookService
{
    private static readonly TimeZoneInfo Ist = IstClock.TimeZone;

    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<NotebookService> _logger;

    public NotebookService(ApplicationDbContext db, IAuditService audit, IClock clock, ILogger<NotebookService> logger)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    // SECTION: Read model composition
    public async Task<NotebookIndexVm> GetIndexAsync(
        string ownerId,
        string view,
        string? query,
        string? filter,
        string? tag,
        Guid? selectedId,
        CancellationToken ct = default)
    {
        view = NormalizeView(view);
        filter = NormalizeFilter(filter);
        tag = NormalizeTagFilter(tag);
        var bounds = TodayBounds(_clock.UtcNow);

        var accessibleQuery = _db.NotebookItems
            .AsNoTracking()
            .Include(item => item.Owner)
            .Include(item => item.Collaborators).ThenInclude(collaborator => collaborator.User)
            .Include(item => item.Tags)
            .ThenInclude(itemTag => itemTag.NotebookTag)
            .Include(item => item.ChecklistItems)
            .Where(item => item.OwnerId == ownerId || item.Collaborators.Any(collaborator => collaborator.UserId == ownerId));

        var ownedQuery = accessibleQuery.Where(item => item.OwnerId == ownerId);
        var sharedQuery = accessibleQuery.Where(item =>
            item.OwnerId != ownerId &&
            item.Collaborators.Any(collaborator => collaborator.UserId == ownerId));

        // Active shared notes participate in the main notebook experience, just as they do in
        // Google Keep. Owner-only lifecycle views remain isolated so a collaborator cannot
        // accidentally browse or operate on the owner's archive, completed items or trash.
        var scopedQuery = view switch
        {
            "shared" => sharedQuery,
            "home" or "today" or "reminders" => accessibleQuery,
            _ => ownedQuery
        };

        var baseQuery = view == "trash"
            ? scopedQuery.Where(item => item.DeletedAtUtc != null)
            : scopedQuery.Where(item => item.DeletedAtUtc == null);

        var filteredBaseQuery = ApplySearch(baseQuery, query);

        if (!string.IsNullOrWhiteSpace(tag))
        {
            filteredBaseQuery = filteredBaseQuery.Where(item => item.Tags.Any(itemTag =>
                itemTag.NotebookTag != null && itemTag.NotebookTag.NormalizedName == tag));
        }

        filteredBaseQuery = ApplyTypeFilter(filteredBaseQuery, filter);

        var activeQuery = ActiveItems(filteredBaseQuery);
        var remindersQuery = ReminderItems(activeQuery);

        var filteredQuery = view switch
        {
            "today" => activeQuery.Where(item =>
                item.ReminderAtUtc != null &&
                item.ReminderAtUtc < bounds.EndUtc),
            "labels" when !string.IsNullOrWhiteSpace(tag) => activeQuery,
            "labels" => activeQuery.Where(_ => false),
            "reminders" => remindersQuery,
            "shared" => activeQuery,
            "archive" or "archived" => filteredBaseQuery.Where(item => item.Status == NotebookItemStatus.Archived),
            "completed" => filteredBaseQuery.Where(item => item.Status == NotebookItemStatus.Completed),
            "trash" => filteredBaseQuery,
            _ => activeQuery
        };

        var items = (await filteredQuery
                .OrderByDescending(item => item.IsPinned)
                .ThenBy(item => item.ReminderAtUtc == null)
                .ThenBy(item => item.ReminderAtUtc)
                .ThenByDescending(item => item.UpdatedAtUtc)
                .Take(80)
                .ToListAsync(ct))
            .Select(item => ToListVm(item, bounds, ownerId))
            .ToArray();

        var pinned = (await activeQuery
                .Where(item => item.Status == NotebookItemStatus.Active &&
                               item.OwnerId == ownerId &&
                               item.IsPinned)
                .OrderBy(item => item.SortOrder == 0 ? int.MaxValue : item.SortOrder)
                .ThenByDescending(item => item.UpdatedAtUtc)
                .Take(80)
                .ToListAsync(ct))
            .Select(item => ToListVm(item, bounds, ownerId))
            .ToArray();

        var sticky = (await activeQuery
                .Where(item => item.Type == NotebookItemType.Sticky && item.Status == NotebookItemStatus.Active)
                .OrderByDescending(item => item.UpdatedAtUtc)
                .Take(12)
                .ToListAsync(ct))
            .Select(item => ToListVm(item, bounds, ownerId))
            .ToArray();

        var due = (await activeQuery
                .Where(item =>
                    item.Status == NotebookItemStatus.Active &&
                    item.ReminderAtUtc != null &&
                    item.ReminderAtUtc < bounds.EndUtc)
                .OrderBy(item => item.ReminderAtUtc)
                .Take(6)
                .ToListAsync(ct))
            .Select(item => ToListVm(item, bounds, ownerId))
            .ToArray();


        // SECTION: Home board uses a manual-order friendly Others section.
        var recent = (await activeQuery
                .Where(item => item.Status == NotebookItemStatus.Active &&
                               (item.OwnerId != ownerId || !item.IsPinned))
                .OrderBy(item => item.SortOrder == 0 ? int.MaxValue : item.SortOrder)
                .ThenByDescending(item => item.UpdatedAtUtc)
                .Take(80)
                .ToListAsync(ct))
            .Select(item => ToListVm(item, bounds, ownerId))
            .ToArray();

        // SECTION: Board-first selection
        NotebookItemDetailVm? selected = null;
        if (selectedId.HasValue)
        {
            selected = await LoadSelectedAsync(ownerId, selectedId.Value, bounds, ct);
        }

        return new NotebookIndexVm
        {
            View = view,
            Query = query,
            Filter = filter,
            Tag = tag,
            Items = items,
            PinnedItems = pinned,
            StickyItems = sticky,
            DueItems = due,
            OtherItems = recent,
            SelectedItem = selected,
            Summary = await BuildSummary(ownerId, bounds, ct),
            RailItems = await BuildRail(ownerId, view, bounds, ct),
            Tags = await BuildTags(ownerId, ct)
        };
    }


    public async Task<NotebookWidgetVm> GetWidgetAsync(
        string ownerId,
        int take = 5,
        CancellationToken ct = default)
    {
        var nowUtc = _clock.UtcNow;
        var bounds = TodayBounds(nowUtc);

        var baseQuery = _db.NotebookItems
            .AsNoTracking()
            .Where(item =>
                item.OwnerId == ownerId &&
                item.DeletedAtUtc == null &&
                item.Status == NotebookItemStatus.Active);

        var reminderQuery = baseQuery.Where(item => item.ReminderAtUtc != null);

        var dueItems = await reminderQuery
            .Where(item => item.ReminderAtUtc < bounds.EndUtc)
            .OrderBy(item => item.ReminderAtUtc)
            .Take(take)
            .Select(item => new NotebookWidgetItemVm
            {
                Id = item.Id,
                Title = item.Title,
                Type = item.Type,
                ReminderAtUtc = item.ReminderAtUtc,
                IsOverdue = item.ReminderAtUtc < nowUtc,
                OpenUrl = $"/Notebook?view=today&note={item.Id}"
            })
            .ToListAsync(ct);

        var pinnedItems = await baseQuery
            .Where(item => item.IsPinned)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(take)
            .Select(item => new NotebookWidgetItemVm
            {
                Id = item.Id,
                Title = item.Title,
                Type = item.Type,
                ReminderAtUtc = item.ReminderAtUtc,
                OpenUrl = $"/Notebook?view=home&note={item.Id}"
            })
            .ToListAsync(ct);

        var stickyItems = await baseQuery
            .Where(item => item.Type == NotebookItemType.Sticky)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(take)
            .Select(item => new NotebookWidgetItemVm
            {
                Id = item.Id,
                Title = item.Title,
                Type = item.Type,
                ReminderAtUtc = item.ReminderAtUtc,
                OpenUrl = $"/Notebook?view=sticky&note={item.Id}"
            })
            .ToListAsync(ct);

        return new NotebookWidgetVm
        {
            DueTodayCount = await reminderQuery.CountAsync(item =>
                item.ReminderAtUtc >= bounds.StartUtc &&
                item.ReminderAtUtc < bounds.EndUtc,
                ct),
            OverdueCount = await reminderQuery.CountAsync(item => item.ReminderAtUtc < bounds.StartUtc, ct),
            DueItems = dueItems,
            PinnedItems = pinnedItems,
            StickyItems = stickyItems
        };
    }



    public async Task<IReadOnlyDictionary<string, int>> GetCountsAsync(string ownerId, CancellationToken ct = default)
    {
        var bounds = TodayBounds(_clock.UtcNow);
        var accessible = _db.NotebookItems
            .AsNoTracking()
            .Where(item =>
                item.DeletedAtUtc == null &&
                (item.OwnerId == ownerId || item.Collaborators.Any(collaborator => collaborator.UserId == ownerId)));
        var owned = accessible.Where(item => item.OwnerId == ownerId);
        var active = accessible.Where(item => item.Status == NotebookItemStatus.Active);
        return new Dictionary<string, int>
        {
            ["home"] = await active.CountAsync(ct),
            ["today"] = await active.CountAsync(item => item.ReminderAtUtc != null && item.ReminderAtUtc < bounds.EndUtc, ct),
            ["reminders"] = await ReminderItems(active).CountAsync(ct),
            ["shared"] = await _db.NotebookItemCollaborators.AsNoTracking().CountAsync(collaborator => collaborator.UserId == ownerId && collaborator.NotebookItem.DeletedAtUtc == null && collaborator.NotebookItem.Status == NotebookItemStatus.Active, ct),
            ["labels"] = await _db.NotebookTags.AsNoTracking().CountAsync(tag => tag.OwnerId == ownerId, ct),
            ["archive"] = await owned.CountAsync(item => item.Status == NotebookItemStatus.Archived, ct),
            ["completed"] = await owned.CountAsync(item => item.Status == NotebookItemStatus.Completed, ct),
            ["trash"] = await _db.NotebookItems.AsNoTracking().CountAsync(item => item.OwnerId == ownerId && item.DeletedAtUtc != null, ct),
            ["pinned"] = await active.CountAsync(item => item.IsPinned, ct),
            ["others"] = await active.CountAsync(item => !item.IsPinned, ct)
        };
    }

    public async Task<NotebookItemDetailVm?> GetDetailAsync(string ownerId, Guid id, CancellationToken ct = default)
    {
        return await LoadSelectedAsync(ownerId, id, TodayBounds(_clock.UtcNow), ct);
    }

    // SECTION: Mutations
    public Task<Guid> QuickCaptureAsync(
        string ownerId,
        string input,
        NotebookItemType? forcedType = null,
        CancellationToken ct = default)
    {
        var parsed = NotebookQuickCaptureParser.Parse(input, _clock.UtcNow, forcedType);
        return CreateIdAsync(ownerId, new NotebookCreateInput
        {
            Title = parsed.Title,
            Type = parsed.Type,
            Priority = parsed.Priority,
            ReminderAtUtc = parsed.ReminderAtUtc,
            Tags = parsed.Tags,
            ColorKey = parsed.Type == NotebookItemType.Sticky ? "blue" : null
        }, ct);
    }

    public async Task<NotebookItemDetailVm> CreateAsync(string ownerId, NotebookCreateInput input, CancellationToken ct = default)
    {
        if (input.ClientRequestId is Guid clientRequestId)
        {
            var existing = await FindByClientRequestIdAsync(ownerId, clientRequestId, ct);

            if (existing is not null)
            {
                return MapDetail(existing);
            }
        }

        var now = _clock.UtcNow;
        var initialSortOrder = await GetTopSortOrderAsync(ownerId, input.IsPinned, ct);
        var item = new NotebookItem
        {
            OwnerId = ownerId,
            Title = CleanTitle(input.Title),
            BodyMarkdown = input.BodyMarkdown,
            Type = input.Type,
            Priority = input.Priority,
            ReminderAtUtc = input.ReminderAtUtc,
            IsPinned = input.IsPinned,
            IsFavorite = input.IsFavorite,
            ColorKey = CleanColor(input.ColorKey, input.Type),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = Guid.NewGuid(),
            ClientRequestId = input.ClientRequestId,
            SortOrder = initialSortOrder
        };

        ApplyChecklist(item, input.ChecklistRows.Any() ? input.ChecklistRows : input.ChecklistItems.Select((text, index) => new NotebookChecklistEditRow { Text = text, SortOrder = index }).ToArray(), now);
        _db.NotebookItems.Add(item);
        if (item.OwnerId == ownerId)
        {
            await SyncTags(item, ownerId, input.Tags, ct);
        }
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsClientRequestIdConflict(ex) && input.ClientRequestId is Guid duplicateRequestId)
        {
            var duplicate = await FindByClientRequestIdAsync(ownerId, duplicateRequestId, ct);
            if (duplicate is not null)
            {
                return MapDetail(duplicate);
            }

            throw;
        }

        await TryWriteAuditAsync("Notebook.Create", ownerId, item.Id, ct);
        return MapDetail(item, ownerId);
    }


    public Task<NotebookItemDetailVm> CreateAsync(string ownerId, NotebookEditInput input, CancellationToken ct = default)
    {
        // SECTION: Legacy form adapter keeps old Razor handlers on the create-only contract.
        return CreateAsync(ownerId, new NotebookCreateInput
        {
            ClientRequestId = input.ClientRequestId,
            Title = input.Title,
            BodyMarkdown = input.BodyMarkdown,
            Type = input.Type,
            Priority = input.Priority,
            ReminderAtUtc = input.ReminderAtUtc,
            ColorKey = input.ColorKey,
            IsPinned = input.IsPinned,
            IsFavorite = input.IsFavorite,
            Tags = input.Tags,
            ChecklistItems = input.ChecklistItems,
            ChecklistRows = input.ChecklistRows
        }, ct);
    }

    private async Task<Guid> CreateIdAsync(string ownerId, NotebookCreateInput input, CancellationToken ct = default)
    {
        var created = await CreateAsync(ownerId, input, ct);
        return created.Id;
    }

    public async Task<NotebookItemDetailVm> UpdateAsync(string ownerId, Guid id, NotebookUpdateInput input, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadAccessibleForUpdate(ownerId, id, expectedVersion, NotebookAccessLevel.Editor, ct);
        item.Title = CleanTitle(input.Title);
        item.BodyMarkdown = input.BodyMarkdown;
        item.Priority = input.Priority ?? item.Priority;
        item.ReminderAtUtc = input.ReminderAtUtc;
        item.ColorKey = CleanColor(input.ColorKey, item.Type);
        Touch(item, _clock.UtcNow);

        SyncChecklistItems(item, input.ChecklistRows.Any() ? input.ChecklistRows : input.ChecklistItems.Select((text, index) => new NotebookChecklistEditRow { Text = text, SortOrder = index }).ToArray(), item.UpdatedAtUtc);
        await SyncTags(item, ownerId, input.Tags, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw await CreateConcurrencyExceptionAsync(ownerId, id, expectedVersion, ex, ct);
        }

        await TryWriteAuditAsync("Notebook.Update", ownerId, item.Id, ct);
        return MapDetail(item, ownerId);
    }


    public async Task<NotebookItemDetailVm> UpdateContentAsync(string ownerId, Guid id, string? title, string? body, Guid expectedVersion, CancellationToken ct = default)
    {
        // SECTION: Content autosave updates text only and preserves notebook metadata.
        var item = await LoadAccessibleForUpdate(ownerId, id, expectedVersion, NotebookAccessLevel.Editor, ct);
        item.Title = CleanTitle(title ?? string.Empty);
        item.BodyMarkdown = body;
        Touch(item, _clock.UtcNow);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw await CreateConcurrencyExceptionAsync(ownerId, id, expectedVersion, ex, ct);
        }

        await TryWriteAuditAsync("Notebook.UpdateContent", ownerId, item.Id, ct);
        return MapDetail(item, ownerId);
    }


    public async Task<NotebookItemDetailVm> UpdateChecklistAsync(string ownerId, Guid itemId, string? title, string? body, IReadOnlyList<NotebookChecklistEditRow> checklistRows, Guid expectedVersion, CancellationToken ct = default)
    {
        // SECTION: Checklist autosave updates editable checklist content only and preserves metadata.
        var item = await LoadAccessibleForUpdate(ownerId, itemId, expectedVersion, NotebookAccessLevel.Editor, ct);
        if (item.Type != NotebookItemType.Checklist)
        {
            throw new NotebookValidationException("The selected notebook item is not a checklist.");
        }

        // Validate the complete checklist request before mutating the tracked item.
        // This prevents failed validation from changing title, body, Version or row state.
        ValidateChecklistRows(item, checklistRows);

        item.Title = CleanTitle(title ?? string.Empty);
        item.BodyMarkdown = body;
        Touch(item, _clock.UtcNow);
        var createdRowClientKeys = SyncChecklistItems(item, checklistRows, item.UpdatedAtUtc);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw await CreateConcurrencyExceptionAsync(ownerId, itemId, expectedVersion, ex, ct);
        }

        await TryWriteAuditAsync("Notebook.UpdateChecklist", ownerId, item.Id, ct);
        var detail = MapDetail(item, ownerId);
        HydrateResponseClientKeys(detail, createdRowClientKeys);
        return detail;
    }


    public Task<NotebookItemDetailVm> UpdateAsync(string ownerId, Guid id, NotebookEditInput input, Guid expectedVersion, CancellationToken ct = default)
    {
        // SECTION: Legacy form adapter intentionally drops pin/favourite state for generic updates.
        return UpdateAsync(ownerId, id, new NotebookUpdateInput
        {
            Title = input.Title,
            BodyMarkdown = input.BodyMarkdown,
            Priority = input.Priority,
            ReminderAtUtc = input.ReminderAtUtc,
            ColorKey = input.ColorKey,
            Tags = input.Tags,
            ChecklistItems = input.ChecklistItems,
            ChecklistRows = input.ChecklistRows
        }, expectedVersion, ct);
    }

    public async Task<NotebookItemDetailVm> ArchiveAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        await ArchiveLoadedAsync(item, ownerId, ct);
        return MapDetail(item, ownerId);
    }

    public async Task<NotebookItemDetailVm> RestoreAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        await RestoreLoadedAsync(item, ct);
        return MapDetail(item, ownerId);
    }

    public async Task<NotebookItemDetailVm> ReopenAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        await ReopenLoadedAsync(item, ct);
        return MapDetail(item, ownerId);
    }

    public Task<NotebookItemDetailVm> DeleteAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default)
        => MoveToTrashAsync(ownerId, id, expectedVersion, ct);

    public async Task<NotebookItemDetailVm> MoveToTrashAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        await MoveToTrashLoadedAsync(item, ownerId, ct);
        return MapDetail(item, ownerId);
    }

    public async Task<NotebookItemDetailVm> RestoreFromTrashAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedTrashForUpdate(ownerId, id, expectedVersion, ct);
        item.DeletedAtUtc = null;
        Touch(item, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        await TryWriteAuditAsync("Notebook.RestoreFromTrash", ownerId, item.Id, ct);
        return MapDetail(item, ownerId);
    }

    public async Task DeletePermanentlyAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedTrashForUpdate(ownerId, id, expectedVersion, ct);
        _db.NotebookItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        await TryWriteAuditAsync("Notebook.DeletePermanently", ownerId, id, ct);
    }

    public async Task<int> EmptyTrashAsync(string ownerId, CancellationToken ct = default)
    {
        var items = await _db.NotebookItems.Where(item => item.OwnerId == ownerId && item.DeletedAtUtc != null).ToListAsync(ct);
        if (items.Count == 0) return 0;
        _db.NotebookItems.RemoveRange(items);
        await _db.SaveChangesAsync(ct);
        try { await _audit.LogAsync("Notebook.EmptyTrash", userId: ownerId, data: new Dictionary<string, string?> { ["Count"] = items.Count.ToString() }); }
        catch (Exception ex) { _logger.LogError(ex, "Notebook trash was emptied for owner {OwnerId}, but audit logging failed.", ownerId); }
        return items.Count;
    }

    public async Task<int> PurgeExpiredTrashAsync(DateTimeOffset cutoffUtc, CancellationToken ct = default)
    {
        var expired = await _db.NotebookItems.Where(item => item.DeletedAtUtc != null && item.DeletedAtUtc < cutoffUtc).ToListAsync(ct);
        if (expired.Count == 0) return 0;
        _db.NotebookItems.RemoveRange(expired);
        await _db.SaveChangesAsync(ct);
        return expired.Count;
    }

    public async Task<NotebookItemDetailVm> SetPinnedAsync(string ownerId, Guid id, bool isPinned, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        if (item.IsPinned != isPinned)
        {
            item.IsPinned = isPinned;
            item.SortOrder = await GetTopSortOrderAsync(ownerId, isPinned, ct, item.Id);
            Touch(item, _clock.UtcNow);
            await _db.SaveChangesAsync(ct);
        }
        return MapDetail(item, ownerId);
    }

    public async Task ReorderAsync(
        string ownerId,
        NotebookBoardSection section,
        IReadOnlyList<NotebookOrderItem> items,
        CancellationToken ct = default)
    {
        if (items.Count > 200)
        {
            throw new NotebookValidationException("Too many notebook items were submitted for reordering.");
        }

        var duplicateIds = items.GroupBy(item => item.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicateIds is not null)
        {
            throw new NotebookValidationException("A notebook item was submitted more than once.");
        }

        var isPinned = section == NotebookBoardSection.Pinned;
        var databaseItems = await _db.NotebookItems
            .Where(item =>
                item.OwnerId == ownerId &&
                item.DeletedAtUtc == null &&
                item.Status == NotebookItemStatus.Active &&
                item.IsPinned == isPinned)
            .ToListAsync(ct);

        if (databaseItems.Count != items.Count || databaseItems.Select(item => item.Id).ToHashSet().SetEquals(items.Select(item => item.Id)) is false)
        {
            throw new NotebookValidationException("The notebook board changed before the new order could be saved. Reload the page and try again.");
        }

        var submittedById = items.ToDictionary(item => item.Id);
        foreach (var databaseItem in databaseItems)
        {
            var submitted = submittedById[databaseItem.Id];
            if (databaseItem.Version != submitted.Version)
            {
                throw new NotebookConcurrencyException(databaseItem.Id, submitted.Version, databaseItem.Version, MapDetail(databaseItem));
            }
        }

        var order = 1000;
        foreach (var submitted in items)
        {
            var databaseItem = databaseItems.Single(item => item.Id == submitted.Id);
            databaseItem.SortOrder = order;
            order += 1000;
        }

        try
        {
            // SaveChanges is transactional. Order is board metadata, so Version and UpdatedAtUtc remain unchanged.
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var first = items.FirstOrDefault();
            if (first is null) throw;
            throw await CreateConcurrencyExceptionAsync(ownerId, first.Id, first.Version, ex, ct);
        }

        await TryWriteAuditAsync("Notebook.Reorder", ownerId, Guid.Empty, ct, new Dictionary<string, string?> { ["Section"] = section.ToString(), ["ItemCount"] = items.Count.ToString() });
    }

    public async Task<NotebookItemDetailVm> SetColourAsync(string ownerId, Guid id, string? colorKey, Guid expectedVersion, CancellationToken ct = default)
    {
        // SECTION: Dedicated colour mutation preserves all non-colour notebook metadata.
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        item.ColorKey = CleanColor(colorKey, item.Type);
        Touch(item, _clock.UtcNow);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw await CreateConcurrencyExceptionAsync(ownerId, id, expectedVersion, ex, ct);
        }

        await TryWriteAuditAsync("Notebook.SetColour", ownerId, item.Id, ct);
        return MapDetail(item, ownerId);
    }

    public async Task<NotebookItemDetailVm> SetLabelsAsync(string ownerId, Guid id, IReadOnlyList<string> labels, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        var cleanLabels = ValidateLabelNames(labels);
        await SyncTags(item, ownerId, cleanLabels, ct);
        Touch(item, _clock.UtcNow);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex) { throw await CreateConcurrencyExceptionAsync(ownerId, id, expectedVersion, ex, ct); }
        await TryWriteAuditAsync("Notebook.SetLabels", ownerId, item.Id, ct);
        return MapDetail(item, ownerId);
    }

    public Task<IReadOnlyList<NotebookTagVm>> GetLabelsAsync(string ownerId, CancellationToken ct = default)
        => BuildAllTags(ownerId, ct);

    public async Task<NotebookTagVm> CreateLabelAsync(string ownerId, string name, CancellationToken ct = default)
    {
        var cleanName = ValidateLabelName(name);
        var normalized = NormalizeTag(cleanName);
        var existing = await _db.NotebookTags.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.NormalizedName == normalized, ct);

        if (existing is not null)
        {
            var labels = await BuildAllTags(ownerId, ct);
            return labels.First(label => label.Id == existing.Id);
        }

        var tag = new NotebookTag
        {
            OwnerId = ownerId,
            Name = cleanName,
            NormalizedName = normalized
        };

        _db.NotebookTags.Add(tag);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var concurrent = await _db.NotebookTags.AsNoTracking()
                .FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.NormalizedName == normalized, ct);
            if (concurrent is null) throw;
            var labels = await BuildAllTags(ownerId, ct);
            return labels.First(label => label.Id == concurrent.Id);
        }

        await TryWriteAuditAsync("Notebook.LabelCreated", ownerId, Guid.Empty, ct, new Dictionary<string, string?>
        {
            ["LabelId"] = tag.Id.ToString(),
            ["LabelName"] = tag.Name
        });

        return new NotebookTagVm
        {
            Id = tag.Id,
            Name = tag.Name,
            Count = 0
        };
    }


    public async Task<(IReadOnlyList<NotebookTagVm> Labels, IReadOnlyList<Guid> AffectedItemIds)> RenameLabelAsync(string ownerId, int labelId, string name, CancellationToken ct = default)
    {
        var cleanName = ValidateLabelName(name);
        var tag = await _db.NotebookTags.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == labelId && x.OwnerId == ownerId, ct)
            ?? throw new KeyNotFoundException("The label could not be found.");
        var affected = tag.Items.Select(x => x.NotebookItemId).Distinct().ToArray();
        var normalized = NormalizeTag(cleanName);
        var target = await _db.NotebookTags.Include(x => x.Items).FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.NormalizedName == normalized && x.Id != labelId, ct);
        if (target is not null)
        {
            var existing = target.Items.Select(x => x.NotebookItemId).ToHashSet();
            foreach (var join in tag.Items.ToList()) if (!existing.Contains(join.NotebookItemId)) target.Items.Add(new NotebookItemTag { NotebookItemId = join.NotebookItemId, NotebookTagId = target.Id });
            _db.NotebookTags.Remove(tag);
        }
        else { tag.Name = cleanName; tag.NormalizedName = normalized; }
        await _db.SaveChangesAsync(ct);
        return (await BuildAllTags(ownerId, ct), affected);
    }

    public async Task<(IReadOnlyList<NotebookTagVm> Labels, IReadOnlyList<Guid> AffectedItemIds)> DeleteLabelAsync(string ownerId, int labelId, CancellationToken ct = default)
    {
        var tag = await _db.NotebookTags.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == labelId && x.OwnerId == ownerId, ct)
            ?? throw new KeyNotFoundException("The label could not be found.");
        var affected = tag.Items.Select(x => x.NotebookItemId).Distinct().ToArray();
        _db.NotebookTags.Remove(tag);
        await _db.SaveChangesAsync(ct);
        return (await BuildAllTags(ownerId, ct), affected);
    }

    public async Task<NotebookItemDetailVm> CompleteAsync(string ownerId, Guid id, bool isComplete, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadAccessibleForUpdate(ownerId, id, expectedVersion, NotebookAccessLevel.Editor, ct);
        await CompleteLoadedAsync(item, isComplete, ct);
        return MapDetail(item, ownerId);
    }

    public async Task<NotebookItemDetailVm> ConvertTypeAsync(string ownerId, Guid id, NotebookItemType newType, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadAccessibleForUpdate(ownerId, id, expectedVersion, NotebookAccessLevel.Editor, ct);
        if (item.Type == newType)
        {
            return MapDetail(item, ownerId);
        }

        var now = _clock.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        if (newType == NotebookItemType.Checklist)
        {
            var lines = (item.BodyMarkdown ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();

            item.ChecklistItems.Clear();
            var sortOrder = 0;
            foreach (var line in lines)
            {
                item.ChecklistItems.Add(new NotebookChecklistItem
                {
                    NotebookItemId = item.Id,
                    Text = ValidateChecklistText(line),
                    SortOrder = sortOrder++,
                    CreatedAtUtc = now
                });
            }

            item.BodyMarkdown = null;
        }
        else if (item.Type == NotebookItemType.Checklist && newType != NotebookItemType.Checklist)
        {
            item.BodyMarkdown = string.Join(Environment.NewLine, item.ChecklistItems
                .OrderBy(row => row.SortOrder)
                .Select(row => row.Text.Trim())
                .Where(text => text.Length > 0));
            item.ChecklistItems.Clear();
        }

        item.Type = newType;
        item.ColorKey = CleanColor(item.ColorKey, newType);
        Touch(item, now);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return MapDetail(item, ownerId);
    }

    public async Task<NotebookItemDetailVm> DuplicateAsync(string ownerId, Guid id, CancellationToken ct = default)
    {
        var source = await LoadAccessible(ownerId, id, NotebookAccessLevel.Viewer, ct);
        var now = _clock.UtcNow;
        var copy = new NotebookItem
        {
            OwnerId = ownerId,
            Title = CleanTitle($"{source.Title} copy"),
            BodyMarkdown = source.BodyMarkdown,
            Type = source.Type,
            Priority = source.Priority,
            ReminderAtUtc = null,
            IsPinned = false,
            IsFavorite = false,
            ColorKey = CleanColor(source.ColorKey, source.Type),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = Guid.NewGuid()
        };

        foreach (var row in source.ChecklistItems.OrderBy(row => row.SortOrder))
        {
            copy.ChecklistItems.Add(new NotebookChecklistItem
            {
                Text = row.Text,
                IsDone = row.IsDone,
                SortOrder = row.SortOrder,
                CreatedAtUtc = now,
                CompletedAtUtc = row.IsDone ? now : null
            });
        }

        if (source.OwnerId == ownerId)
        {
            foreach (var tag in source.Tags.Where(tag => tag.NotebookTag is not null))
            {
                copy.Tags.Add(new NotebookItemTag { NotebookItem = copy, NotebookTag = tag.NotebookTag });
            }
        }

        _db.NotebookItems.Add(copy);
        await _db.SaveChangesAsync(ct);
        await TryWriteAuditAsync("Notebook.Duplicate", ownerId, copy.Id, ct, new Dictionary<string, string?> { ["SourceNotebookItemId"] = source.Id.ToString() });
        return MapDetail(copy, ownerId);
    }

    public async Task<NotebookItemDetailVm> ToggleChecklistItemAsync(string ownerId, Guid itemId, int checklistItemId, bool isDone, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadAccessibleForUpdate(ownerId, itemId, expectedVersion, NotebookAccessLevel.Editor, ct);
        var checklistItem = item.ChecklistItems.FirstOrDefault(row => row.Id == checklistItemId) ?? throw new KeyNotFoundException();
        checklistItem.IsDone = isDone;
        var now = _clock.UtcNow;
        checklistItem.CompletedAtUtc = isDone ? now : null;
        Touch(item, now);
        await _db.SaveChangesAsync(ct);
        return MapDetail(item, ownerId);
    }

    public async Task<IReadOnlyList<NotebookCollaboratorVm>> GetCollaboratorsAsync(string userId, Guid itemId, CancellationToken ct = default)
    {
        var item = await LoadAccessible(userId, itemId, NotebookAccessLevel.Viewer, ct);
        return BuildCollaborators(item);
    }

    public async Task<IReadOnlyList<NotebookCollaboratorSearchVm>> SearchCollaboratorsAsync(string userId, Guid itemId, string query, int take = 10, CancellationToken ct = default)
    {
        var item = await LoadAccessible(userId, itemId, NotebookAccessLevel.Owner, ct);
        var term = (query ?? string.Empty).Trim();
        if (term.Length < 2) return Array.Empty<NotebookCollaboratorSearchVm>();
        take = Math.Clamp(take, 1, 20);
        var pattern = $"%{term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
        var excluded = item.Collaborators.Select(row => row.UserId).Append(item.OwnerId).ToArray();
        return await _db.Users.AsNoTracking()
            .Where(user => !user.IsDisabled && !user.PendingDeletion && !excluded.Contains(user.Id) &&
                (EF.Functions.ILike(user.FullName, pattern) || (user.Email != null && EF.Functions.ILike(user.Email, pattern)) || EF.Functions.ILike(user.Rank, pattern)))
            .OrderBy(user => user.FullName)
            .Take(take)
            .Select(user => new NotebookCollaboratorSearchVm
            {
                UserId = user.Id,
                DisplayName = string.IsNullOrWhiteSpace(user.FullName) ? (user.Email ?? user.UserName ?? "User") : user.FullName,
                Email = user.Email ?? string.Empty,
                Initials = string.Empty
            }).ToListAsync(ct);
    }

    public async Task<NotebookItemDetailVm> AddCollaboratorAsync(string ownerId, Guid itemId, string collaboratorUserId, NotebookCollaborationRole role, Guid expectedVersion, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(role)) throw new NotebookValidationException("Invalid collaborator role.");
        if (string.IsNullOrWhiteSpace(collaboratorUserId) || collaboratorUserId == ownerId) throw new NotebookValidationException("Select another active PRISM user.");
        var item = await LoadAccessibleForUpdate(ownerId, itemId, expectedVersion, NotebookAccessLevel.Owner, ct);
        var user = await _db.Users.FirstOrDefaultAsync(row => row.Id == collaboratorUserId && !row.IsDisabled && !row.PendingDeletion, ct)
            ?? throw new KeyNotFoundException("The selected user could not be found.");
        if (item.Collaborators.Any(row => row.UserId == collaboratorUserId)) return MapDetail(item, ownerId);
        item.Collaborators.Add(new NotebookItemCollaborator
        {
            NotebookItemId = item.Id,
            UserId = user.Id,
            User = user,
            Role = role,
            AddedByUserId = ownerId,
            AddedAtUtc = _clock.UtcNow,
            Version = Guid.NewGuid()
        });
        Touch(item, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        await TryWriteAuditAsync("Notebook.CollaboratorAdded", ownerId, item.Id, ct, new Dictionary<string, string?> { ["CollaboratorUserId"] = user.Id, ["Role"] = role.ToString() });
        return MapDetail(item, ownerId);
    }

    public async Task<NotebookItemDetailVm> RemoveCollaboratorAsync(string ownerId, Guid itemId, string collaboratorUserId, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadAccessibleForUpdate(ownerId, itemId, expectedVersion, NotebookAccessLevel.Owner, ct);
        var collaboration = item.Collaborators.FirstOrDefault(row => row.UserId == collaboratorUserId) ?? throw new KeyNotFoundException();
        item.Collaborators.Remove(collaboration);
        Touch(item, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        await TryWriteAuditAsync("Notebook.CollaboratorRemoved", ownerId, item.Id, ct, new Dictionary<string, string?> { ["CollaboratorUserId"] = collaboratorUserId });
        return MapDetail(item, ownerId);
    }

    public async Task LeaveCollaborationAsync(string userId, Guid itemId, CancellationToken ct = default)
    {
        var collaboration = await _db.NotebookItemCollaborators.FirstOrDefaultAsync(row => row.NotebookItemId == itemId && row.UserId == userId, ct)
            ?? throw new KeyNotFoundException();
        _db.NotebookItemCollaborators.Remove(collaboration);
        await _db.SaveChangesAsync(ct);
        await TryWriteAuditAsync("Notebook.CollaborationLeft", userId, itemId, ct);
    }

    // SECTION: Helpers


    private async Task ArchiveLoadedAsync(NotebookItem item, string ownerId, CancellationToken ct)
    {
        item.Status = NotebookItemStatus.Archived;
        item.ArchivedAtUtc = _clock.UtcNow;
        Touch(item, item.ArchivedAtUtc.Value);
        await _db.SaveChangesAsync(ct);
        await TryWriteAuditAsync("Notebook.Archive", ownerId, item.Id, ct);
    }

    private async Task RestoreLoadedAsync(NotebookItem item, CancellationToken ct)
    {
        item.Status = NotebookItemStatus.Active;
        item.ArchivedAtUtc = null;
        Touch(item, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    private async Task ReopenLoadedAsync(NotebookItem item, CancellationToken ct)
    {
        item.Status = NotebookItemStatus.Active;
        item.CompletedAtUtc = null;
        Touch(item, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    private async Task MoveToTrashLoadedAsync(NotebookItem item, string ownerId, CancellationToken ct)
    {
        item.DeletedAtUtc = _clock.UtcNow;
        Touch(item, item.DeletedAtUtc.Value);
        await _db.SaveChangesAsync(ct);
        await TryWriteAuditAsync("Notebook.MoveToTrash", ownerId, item.Id, ct);
    }

    private async Task CompleteLoadedAsync(NotebookItem item, bool isComplete, CancellationToken ct)
    {
        item.Status = isComplete ? NotebookItemStatus.Completed : NotebookItemStatus.Active;
        var now = _clock.UtcNow;
        item.CompletedAtUtc = isComplete ? now : null;
        Touch(item, now);
        await _db.SaveChangesAsync(ct);
    }

    private async Task TryWriteAuditAsync(string action, string ownerId, Guid itemId, CancellationToken ct, Dictionary<string, string?>? data = null)
    {
        try
        {
            data ??= new Dictionary<string, string?>();
            data["Id"] = itemId.ToString();
            await _audit.LogAsync(action, userId: ownerId, data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notebook action {Action} succeeded for item {ItemId}, but audit logging failed.", action, itemId);
        }
    }

    private static void Touch(NotebookItem item, DateTimeOffset now)
    {
        item.UpdatedAtUtc = now;
        item.Version = Guid.NewGuid();
    }


    private async Task<NotebookItem?> FindByClientRequestIdAsync(string ownerId, Guid clientRequestId, CancellationToken ct)
    {
        return await _db.NotebookItems
            .AsNoTracking()
            .Include(item => item.Owner)
            .Include(item => item.Collaborators).ThenInclude(collaborator => collaborator.User)
            .Include(item => item.Tags)
                .ThenInclude(itemTag => itemTag.NotebookTag)
            .Include(item => item.ChecklistItems)
            .Include(item => item.Attachments)
            .FirstOrDefaultAsync(item =>
                item.OwnerId == ownerId &&
                item.ClientRequestId == clientRequestId &&
                item.DeletedAtUtc == null,
                ct);
    }

    private static bool IsClientRequestIdConflict(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException postgresException)
        {
            return postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                   postgresException.ConstraintName?.Contains("ClientRequestId", StringComparison.OrdinalIgnoreCase) == true;
        }

        return ex.InnerException?.Message.Contains("ClientRequestId", StringComparison.OrdinalIgnoreCase) == true ||
               ex.Message.Contains("ClientRequestId", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<NotebookItem> LoadAccessible(string userId, Guid id, NotebookAccessLevel minimumAccess, CancellationToken ct)
    {
        var item = await _db.NotebookItems
            .Include(row => row.Owner)
            .Include(row => row.Collaborators).ThenInclude(row => row.User)
            .Include(row => row.Tags).ThenInclude(row => row.NotebookTag)
            .Include(row => row.ChecklistItems)
            .Include(row => row.Attachments)
            .FirstOrDefaultAsync(row => row.Id == id && row.DeletedAtUtc == null &&
                (row.OwnerId == userId || row.Collaborators.Any(collaborator => collaborator.UserId == userId)), ct)
            ?? throw new KeyNotFoundException();

        if (ResolveAccess(item, userId) < minimumAccess) throw new UnauthorizedAccessException();
        return item;
    }

    private async Task<NotebookItem> LoadAccessibleForUpdate(string userId, Guid id, Guid expectedVersion, NotebookAccessLevel minimumAccess, CancellationToken ct)
    {
        if (expectedVersion == Guid.Empty) throw new ArgumentException("A valid notebook version is required.", nameof(expectedVersion));
        var item = await LoadAccessible(userId, id, minimumAccess, ct);
        if (item.Version != expectedVersion)
            throw new NotebookConcurrencyException(id, expectedVersion, item.Version, MapDetail(item, userId));
        return item;
    }

    private async Task<NotebookItem> LoadOwned(string ownerId, Guid id, CancellationToken ct) =>
        await _db.NotebookItems
            .Include(item => item.Owner)
            .Include(item => item.Collaborators).ThenInclude(row => row.User)
            .Include(item => item.Tags)
                .ThenInclude(itemTag => itemTag.NotebookTag)
            .Include(item => item.ChecklistItems)
            .FirstOrDefaultAsync(item =>
                item.Id == id &&
                item.OwnerId == ownerId &&
                item.DeletedAtUtc == null,
                ct) ?? throw new KeyNotFoundException();


    private async Task<NotebookItem> LoadOwnedTrashForUpdate(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct)
    {
        if (expectedVersion == Guid.Empty) throw new ArgumentException("A valid notebook version is required.", nameof(expectedVersion));
        var item = await _db.NotebookItems
            .Include(x => x.Tags).ThenInclude(x => x.NotebookTag)
            .Include(x => x.ChecklistItems)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId && x.DeletedAtUtc != null, ct)
            ?? throw new KeyNotFoundException();
        if (item.Version != expectedVersion) throw new NotebookConcurrencyException(id, expectedVersion, item.Version, MapDetail(item));
        return item;
    }

    private async Task<NotebookItem> LoadOwnedForUpdate(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct)
    {
        var query = _db.NotebookItems
            .Include(item => item.Owner)
            .Include(item => item.Collaborators).ThenInclude(row => row.User)
            .Include(item => item.Tags)
                .ThenInclude(itemTag => itemTag.NotebookTag)
            .Include(item => item.ChecklistItems)
            .Where(item =>
                item.Id == id &&
                item.OwnerId == ownerId &&
                item.DeletedAtUtc == null);

        if (expectedVersion == Guid.Empty)
        {
            throw new ArgumentException("A valid notebook version is required.", nameof(expectedVersion));
        }

        var item = await query.FirstOrDefaultAsync(ct) ?? throw new KeyNotFoundException();
        if (item.Version != expectedVersion)
        {
            throw new NotebookConcurrencyException(
                id,
                expectedVersion,
                item.Version,
                MapDetail(item));
        }

        return item;
    }


    private async Task<NotebookConcurrencyException> CreateConcurrencyExceptionAsync(
        string ownerId,
        Guid itemId,
        Guid expectedVersion,
        DbUpdateConcurrencyException innerException,
        CancellationToken ct)
    {
        // SECTION: Reload the authoritative database state for a useful 409 response.
        // Clear tracked state first so the failed local mutation cannot leak into the response.
        _db.ChangeTracker.Clear();

        var currentItem = await _db.NotebookItems
            .AsNoTracking()
            .Include(item => item.Owner)
            .Include(item => item.Collaborators).ThenInclude(collaborator => collaborator.User)
            .Include(item => item.Tags)
                .ThenInclude(itemTag => itemTag.NotebookTag)
            .Include(item => item.ChecklistItems)
            .Include(item => item.Attachments)
            .FirstOrDefaultAsync(item =>
                item.Id == itemId &&
                (item.OwnerId == ownerId || item.Collaborators.Any(collaborator => collaborator.UserId == ownerId)) &&
                item.DeletedAtUtc == null,
                ct);

        return new NotebookConcurrencyException(
            itemId,
            expectedVersion,
            currentItem?.Version ?? Guid.Empty,
            currentItem is null ? null : MapDetail(currentItem, ownerId),
            innerException);
    }


    private NotebookItemDetailVm MapDetail(NotebookItem item, string? currentUserId = null)
    {
        var detail = ToDetailShell(item, TodayBounds(_clock.UtcNow), currentUserId);
        detail.BodyMarkdown = item.BodyMarkdown;
        detail.ChecklistItems = item.ChecklistItems
            .OrderBy(checklistItem => checklistItem.SortOrder)
            .Select(checklistItem => new NotebookChecklistItemVm
            {
                Id = checklistItem.Id,
                Text = checklistItem.Text,
                IsDone = checklistItem.IsDone,
                SortOrder = checklistItem.SortOrder
            })
            .ToArray();
        detail.Attachments = item.Attachments
            .Select(attachment => new NotebookAttachmentVm
            {
                Id = attachment.Id,
                FileName = attachment.OriginalFileName,
                ContentType = attachment.ContentType ?? string.Empty,
                SizeBytes = attachment.SizeBytes
            })
            .ToArray();

        return detail;
    }


    private static NotebookItemDetailVm ToDetailShell(NotebookItem item, (DateTimeOffset StartUtc, DateTimeOffset EndUtc) bounds, string? currentUserId = null)
    {
        var list = ToListVm(item, bounds, currentUserId);
        return new NotebookItemDetailVm
        {
            Id = list.Id,
            Title = list.Title,
            Preview = list.Preview,
            Type = list.Type,
            Status = list.Status,
            Priority = list.Priority,
            ReminderAtUtc = list.ReminderAtUtc,
            ReminderDisplay = list.ReminderDisplay,
            IsPinned = list.IsPinned,
            IsFavorite = list.IsFavorite,
            ColorKey = list.ColorKey,
            UpdatedAtUtc = list.UpdatedAtUtc,
            Tags = list.Tags,
            ChecklistTotal = list.ChecklistTotal,
            ChecklistDone = list.ChecklistDone,
            ChecklistPreviewItems = list.ChecklistPreviewItems,
            IsOverdue = list.IsOverdue,
            IsDueToday = list.IsDueToday,
            Version = list.Version,
            OwnerId = list.OwnerId,
            OwnerDisplayName = list.OwnerDisplayName,
            AccessLevel = list.AccessLevel,
            IsShared = list.IsShared,
            Collaborators = list.Collaborators
        };
    }

    private async Task<int> GetTopSortOrderAsync(string ownerId, bool isPinned, CancellationToken ct, Guid? excludeId = null)
    {
        var query = _db.NotebookItems.Where(item =>
            item.OwnerId == ownerId &&
            item.DeletedAtUtc == null &&
            item.Status == NotebookItemStatus.Active &&
            item.IsPinned == isPinned);

        if (excludeId.HasValue)
        {
            query = query.Where(item => item.Id != excludeId.Value);
        }

        var minimum = await query.Select(item => (int?)item.SortOrder).MinAsync(ct);
        if (!minimum.HasValue || minimum.Value == 0) return 1000;
        return minimum.Value <= int.MinValue + 1000 ? int.MinValue + 1000 : minimum.Value - 1000;
    }

    private static string CleanTitle(string title)
    {
        var trimmed = title.Trim();
        if (trimmed.Length > NotebookLimits.TitleMaxLength)
        {
            throw new NotebookValidationException($"Title cannot exceed {NotebookLimits.TitleMaxLength} characters.");
        }

        return string.IsNullOrWhiteSpace(trimmed) ? "Untitled" : trimmed;
    }

    private static string CleanColor(string? color, NotebookItemType type)
    {
        var allowedColors = new[] { "white", "blue", "amber", "green", "rose", "slate" };
        return allowedColors.Contains(color, StringComparer.OrdinalIgnoreCase)
            ? color!.Trim().ToLowerInvariant()
            : "white";
    }

    private static string NormalizeView(string? view)
    {
        var legacyTypeViews = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sticky"] = "home",
            ["notes"] = "home",
            ["checklists"] = "home"
        };

        if (!string.IsNullOrWhiteSpace(view) && legacyTypeViews.TryGetValue(view, out var normalizedLegacy))
        {
            return normalizedLegacy;
        }

        var allowedViews = new[] { "home", "today", "reminders", "shared", "labels", "archive", "archived", "completed", "trash" };
        if (string.Equals(view, "archived", StringComparison.OrdinalIgnoreCase)) return "archive";
        return allowedViews.Contains(view) ? view! : "home";
    }

    private static string? NormalizeFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        return filter.Trim().ToLowerInvariant() switch
        {
            "note" or "notes" => "notes",
            "sticky" => "sticky",
            "checklist" or "checklists" => "checklists",
            _ => null
        };
    }

    private static string? NormalizeTagFilter(string? tag)
    {
        return string.IsNullOrWhiteSpace(tag) ? null : NormalizeTag(tag.Trim());
    }


    private static IQueryable<NotebookItem> ApplySearch(IQueryable<NotebookItem> query, string? rawSearch)
    {
        if (string.IsNullOrWhiteSpace(rawSearch))
        {
            return query;
        }

        var search = rawSearch.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var pattern = $"%{search}%";
        return query.Where(item =>
            EF.Functions.ILike(item.Title, pattern) ||
            (item.BodyMarkdown != null && EF.Functions.ILike(item.BodyMarkdown, pattern)) ||
            item.Tags.Any(tag => tag.NotebookTag != null && EF.Functions.ILike(tag.NotebookTag.Name, pattern)) ||
            item.ChecklistItems.Any(row => EF.Functions.ILike(row.Text, pattern)));
    }

    private static IQueryable<NotebookItem> ApplyTypeFilter(IQueryable<NotebookItem> query, string? filter)
    {
        return filter switch
        {
            "notes" => query.Where(item => item.Type == NotebookItemType.Note),
            "sticky" => query.Where(item => item.Type == NotebookItemType.Sticky),
            "checklists" => query.Where(item => item.Type == NotebookItemType.Checklist),
            _ => query
        };
    }

    private static IQueryable<NotebookItem> VisibleItems(IQueryable<NotebookItem> query)
    {
        return query.Where(item => item.Status == NotebookItemStatus.Active);
    }

    private static IQueryable<NotebookItem> ActiveItems(IQueryable<NotebookItem> query)
    {
        return query.Where(item => item.Status == NotebookItemStatus.Active);
    }

    private static IQueryable<NotebookItem> ReminderItems(IQueryable<NotebookItem> query)
    {
        return query.Where(item =>
            item.Type == NotebookItemType.Reminder ||
            item.ReminderAtUtc != null);
    }

    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) TodayBounds(DateTimeOffset nowUtc)
    {
        var nowIst = TimeZoneInfo.ConvertTime(nowUtc, Ist);
        var startLocal = nowIst.Date;
        var endLocal = startLocal.AddDays(1);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified),
            Ist);

        var endUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified),
            Ist);

        return (
            new DateTimeOffset(startUtc, TimeSpan.Zero),
            new DateTimeOffset(endUtc, TimeSpan.Zero));
    }

    private static NotebookItemListVm ToListVm(NotebookItem item, (DateTimeOffset StartUtc, DateTimeOffset EndUtc) bounds, string? currentUserId = null) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Preview = item.BodyMarkdown,
        Type = item.Type,
        Status = item.Status,
        Priority = item.Priority,
        ReminderAtUtc = item.ReminderAtUtc,
        ReminderDisplay = NotebookReminderFormatter.FormatReminder(item.ReminderAtUtc),
        IsPinned = item.OwnerId == currentUserId && item.IsPinned,
        IsFavorite = item.IsFavorite,
        ColorKey = item.ColorKey ?? "white",
        UpdatedAtUtc = item.UpdatedAtUtc,
        DeletedAtUtc = item.DeletedAtUtc,
        Version = item.Version,
        OwnerId = item.OwnerId,
        OwnerDisplayName = DisplayName(item.Owner),
        AccessLevel = ResolveAccess(item, currentUserId),
        IsShared = item.Collaborators.Count > 0,
        Collaborators = BuildCollaborators(item),
        Tags = item.OwnerId == currentUserId || string.IsNullOrWhiteSpace(currentUserId)
            ? item.Tags.Select(tag => tag.NotebookTag?.Name ?? string.Empty).Where(tag => tag.Length > 0).ToArray()
            : Array.Empty<string>(),
        ChecklistTotal = item.ChecklistItems.Count,
        ChecklistDone = item.ChecklistItems.Count(checklistItem => checklistItem.IsDone),
        ChecklistPreviewItems = item.ChecklistItems
            .OrderBy(checklistItem => checklistItem.SortOrder)
            .Take(10)
            .Select(checklistItem => new NotebookChecklistItemVm
            {
                Id = checklistItem.Id,
                Text = checklistItem.Text,
                IsDone = checklistItem.IsDone,
                SortOrder = checklistItem.SortOrder
            })
            .ToArray(),
        IsOverdue = item.ReminderAtUtc < bounds.StartUtc && item.Status == NotebookItemStatus.Active,
        IsDueToday = item.ReminderAtUtc >= bounds.StartUtc && item.ReminderAtUtc < bounds.EndUtc
    };

    private async Task<NotebookItemDetailVm?> LoadSelectedAsync(
        string ownerId,
        Guid? id,
        (DateTimeOffset StartUtc, DateTimeOffset EndUtc) bounds,
        CancellationToken ct)
    {
        if (id is null)
        {
            return null;
        }

        var item = await _db.NotebookItems
            .AsNoTracking()
            .Include(notebookItem => notebookItem.Owner)
            .Include(notebookItem => notebookItem.Collaborators).ThenInclude(collaborator => collaborator.User)
            .Include(notebookItem => notebookItem.Tags)
            .ThenInclude(itemTag => itemTag.NotebookTag)
            .Include(notebookItem => notebookItem.ChecklistItems)
            .Include(notebookItem => notebookItem.Attachments)
            .FirstOrDefaultAsync(notebookItem =>
                notebookItem.Id == id &&
                (notebookItem.OwnerId == ownerId || notebookItem.Collaborators.Any(collaborator => collaborator.UserId == ownerId)) &&
                notebookItem.DeletedAtUtc == null,
                ct);

        if (item is null)
        {
            return null;
        }

        var detail = ToDetailShell(item, bounds, ownerId);
        detail.BodyMarkdown = item.BodyMarkdown;
        detail.ChecklistItems = item.ChecklistItems
            .OrderBy(checklistItem => checklistItem.SortOrder)
            .Select(checklistItem => new NotebookChecklistItemVm
            {
                Id = checklistItem.Id,
                Text = checklistItem.Text,
                IsDone = checklistItem.IsDone,
                SortOrder = checklistItem.SortOrder
            })
            .ToArray();
        detail.Attachments = item.Attachments
            .Select(attachment => new NotebookAttachmentVm
            {
                Id = attachment.Id,
                FileName = attachment.OriginalFileName,
                ContentType = attachment.ContentType ?? string.Empty,
                SizeBytes = attachment.SizeBytes
            })
            .ToArray();

        return detail;
    }

    private async Task<NotebookSummaryVm> BuildSummary(
        string ownerId,
        (DateTimeOffset StartUtc, DateTimeOffset EndUtc) bounds,
        CancellationToken ct)
    {
        var query = _db.NotebookItems
            .AsNoTracking()
            .Where(item =>
                item.DeletedAtUtc == null &&
                item.Status == NotebookItemStatus.Active &&
                (item.OwnerId == ownerId || item.Collaborators.Any(collaborator => collaborator.UserId == ownerId)));

        return new NotebookSummaryVm
        {
            TotalActive = await query.CountAsync(item => item.Status == NotebookItemStatus.Active, ct),
            DueToday = await query.CountAsync(item => item.ReminderAtUtc >= bounds.StartUtc && item.ReminderAtUtc < bounds.EndUtc, ct),
            Overdue = await query.CountAsync(item => item.ReminderAtUtc < bounds.StartUtc && item.Status == NotebookItemStatus.Active, ct),
            StickyCount = await query.CountAsync(item => item.Type == NotebookItemType.Sticky, ct),
            PinnedCount = await query.CountAsync(item => item.OwnerId == ownerId && item.IsPinned, ct),
            ChecklistCount = await query.CountAsync(item => item.Type == NotebookItemType.Checklist, ct),
            CompletedCount = await _db.NotebookItems.AsNoTracking().CountAsync(item => item.OwnerId == ownerId && item.DeletedAtUtc == null && item.Status == NotebookItemStatus.Completed, ct)
        };
    }

    private async Task<IReadOnlyList<NotebookRailItemVm>> BuildRail(
        string ownerId,
        string active,
        (DateTimeOffset StartUtc, DateTimeOffset EndUtc) bounds,
        CancellationToken ct)
    {
        var accessibleQuery = _db.NotebookItems
            .AsNoTracking()
            .Where(item =>
                item.DeletedAtUtc == null &&
                (item.OwnerId == ownerId || item.Collaborators.Any(collaborator => collaborator.UserId == ownerId)));
        var ownedQuery = accessibleQuery.Where(item => item.OwnerId == ownerId);

        async Task<int> CountAsync(string view) => view switch
        {
            "home" => await accessibleQuery.CountAsync(item => item.Status == NotebookItemStatus.Active, ct),
            "today" => await accessibleQuery.CountAsync(item =>
                item.Status == NotebookItemStatus.Active &&
                item.ReminderAtUtc != null &&
                item.ReminderAtUtc < bounds.EndUtc,
                ct),
            "reminders" => await ReminderItems(ActiveItems(accessibleQuery)).CountAsync(ct),
            "shared" => await _db.NotebookItemCollaborators.AsNoTracking().CountAsync(collaborator => collaborator.UserId == ownerId && collaborator.NotebookItem.DeletedAtUtc == null && collaborator.NotebookItem.Status == NotebookItemStatus.Active, ct),
            "archive" or "archived" => await ownedQuery.CountAsync(item => item.Status == NotebookItemStatus.Archived, ct),
            "completed" => await ownedQuery.CountAsync(item => item.Status == NotebookItemStatus.Completed, ct),
            "trash" => await _db.NotebookItems.AsNoTracking().CountAsync(item => item.OwnerId == ownerId && item.DeletedAtUtc != null, ct),
            _ => 0
        };

        var rows = new[]
        {
            ("home", "All Notes", "bi-journal-text"),
            ("today", "Today", "bi-calendar-check"),
            ("reminders", "Reminders", "bi-bell"),
            ("shared", "Shared with me", "bi-people"),
            ("archive", "Archive", "bi-archive"),
            ("completed", "Completed", "bi-check2-circle"),
            ("trash", "Trash", "bi-trash3")
        };

        var list = new List<NotebookRailItemVm>();
        foreach (var row in rows)
        {
            list.Add(new NotebookRailItemVm
            {
                Key = row.Item1 == "archived" ? "archive" : row.Item1,
                Label = row.Item2,
                Icon = row.Item3,
                Url = $"/Notebook?view={row.Item1}",
                Count = await CountAsync(row.Item1),
                IsActive = row.Item1 == active
            });
        }

        return list;
    }

    private async Task<IReadOnlyList<NotebookTagVm>> BuildTags(string ownerId, CancellationToken ct)
    {
        return await _db.NotebookTags
            .AsNoTracking()
            .Where(tag => tag.OwnerId == ownerId)
            .Select(tag => new NotebookTagVm
            {
                Id = tag.Id,
                Name = tag.Name,
                Count = tag.Items.Count(join =>
                    join.NotebookItem != null &&
                    join.NotebookItem.DeletedAtUtc == null &&
                    join.NotebookItem.Status == NotebookItemStatus.Active)
            })
            .OrderBy(tag => tag.Name)
            .ToArrayAsync(ct);
    }

    private static NotebookAccessLevel ResolveAccess(NotebookItem item, string? currentUserId)
    {
        if (string.IsNullOrWhiteSpace(currentUserId) || item.OwnerId == currentUserId) return NotebookAccessLevel.Owner;
        var collaboration = item.Collaborators.FirstOrDefault(row => row.UserId == currentUserId);
        return collaboration?.Role switch
        {
            NotebookCollaborationRole.Editor => NotebookAccessLevel.Editor,
            NotebookCollaborationRole.Viewer => NotebookAccessLevel.Viewer,
            _ => NotebookAccessLevel.None
        };
    }

    private static IReadOnlyList<NotebookCollaboratorVm> BuildCollaborators(NotebookItem item)
    {
        var rows = new List<NotebookCollaboratorVm>
        {
            new()
            {
                UserId = item.OwnerId,
                DisplayName = DisplayName(item.Owner),
                Email = item.Owner?.Email ?? string.Empty,
                Initials = Initials(DisplayName(item.Owner)),
                Role = NotebookCollaborationRole.Editor,
                IsOwner = true
            }
        };
        rows.AddRange(item.Collaborators.OrderBy(row => DisplayName(row.User)).Select(row => new NotebookCollaboratorVm
        {
            UserId = row.UserId,
            DisplayName = DisplayName(row.User),
            Email = row.User?.Email ?? string.Empty,
            Initials = Initials(DisplayName(row.User)),
            Role = row.Role,
            IsOwner = false
        }));
        return rows;
    }

    private static string DisplayName(ApplicationUser? user)
        => string.IsNullOrWhiteSpace(user?.FullName) ? (user?.Email ?? user?.UserName ?? "Unknown user") : user.FullName.Trim();

    private static string Initials(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    private static void ApplyChecklist(NotebookItem item, IReadOnlyList<NotebookChecklistEditRow> rows, DateTimeOffset now)
    {
        var sortOrder = 0;
        foreach (var row in rows.Where(row => !string.IsNullOrWhiteSpace(row.Text)).OrderBy(row => row.SortOrder))
        {
            var text = row.Text.Trim();
            item.ChecklistItems.Add(new NotebookChecklistItem
            {
                Text = ValidateChecklistText(text),
                IsDone = row.IsDone,
                SortOrder = sortOrder++,
                CreatedAtUtc = now,
                CompletedAtUtc = row.IsDone ? now : null
            });
        }
    }

    private static void HydrateResponseClientKeys(NotebookItemDetailVm detail, IReadOnlyList<(NotebookChecklistItem Entity, string? ClientKey)> createdRows)
    {
        if (createdRows.Count == 0) return;

        var clientKeysByRowId = createdRows
            .Where(row => row.Entity.Id != 0 && !string.IsNullOrWhiteSpace(row.ClientKey))
            .ToDictionary(row => row.Entity.Id, row => row.ClientKey);

        foreach (var row in detail.ChecklistItems)
        {
            if (clientKeysByRowId.TryGetValue(row.Id, out var clientKey))
            {
                row.ClientKey = clientKey;
            }
        }
    }

    private static void ValidateChecklistRows(NotebookItem item, IReadOnlyList<NotebookChecklistEditRow> rows)
    {
        var requestedRows = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Text))
            .OrderBy(row => row.SortOrder)
            .ToList();

        var submittedIds = requestedRows
            .Where(row => row.Id.HasValue)
            .Select(row => row.Id!.Value)
            .ToArray();

        if (submittedIds.Length != submittedIds.Distinct().Count())
        {
            throw new NotebookValidationException("The checklist contains duplicate row identifiers.");
        }

        var duplicateClientKeys = requestedRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ClientKey))
            .GroupBy(row => row.ClientKey!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateClientKeys.Length > 0)
        {
            throw new NotebookValidationException("The checklist contains duplicate client row identifiers.");
        }

        var existingIds = item.ChecklistItems
            .Where(row => row.Id != 0)
            .Select(row => row.Id)
            .ToHashSet();

        if (submittedIds.Any(id => !existingIds.Contains(id)))
        {
            throw new NotebookValidationException("Checklist row ids must belong to the notebook item.");
        }

        foreach (var requested in requestedRows)
        {
            ValidateChecklistText(requested.Text.Trim());
        }
    }

    private List<(NotebookChecklistItem Entity, string? ClientKey)> SyncChecklistItems(NotebookItem item, IReadOnlyList<NotebookChecklistEditRow> rows, DateTimeOffset now)
    {
        var requestedRows = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Text))
            .OrderBy(row => row.SortOrder)
            .ToList();

        var submittedIds = requestedRows.Where(row => row.Id.HasValue).Select(row => row.Id!.Value).ToArray();
        if (submittedIds.Length != submittedIds.Distinct().Count())
        {
            throw new NotebookValidationException("The checklist contains duplicate row identifiers.");
        }

        var duplicateClientKeys = requestedRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ClientKey))
            .GroupBy(row => row.ClientKey!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateClientKeys.Length > 0)
        {
            throw new NotebookValidationException("The checklist contains duplicate client row identifiers.");
        }

        var existingById = item.ChecklistItems.ToDictionary(row => row.Id);
        if (submittedIds.Any(id => !existingById.ContainsKey(id)))
        {
            throw new NotebookValidationException("Checklist row ids must belong to the notebook item.");
        }

        var requestedIds = submittedIds.ToHashSet();
        var createdRows = new List<(NotebookChecklistItem Entity, string? ClientKey)>();
        var nextSortOrder = 0;

        foreach (var requested in requestedRows)
        {
            var text = ValidateChecklistText(requested.Text.Trim());

            if (requested.Id.HasValue && existingById.TryGetValue(requested.Id.Value, out var existing))
            {
                existing.Text = text;
                existing.IsDone = requested.IsDone;
                existing.CompletedAtUtc = requested.IsDone ? existing.CompletedAtUtc ?? now : null;
                existing.SortOrder = nextSortOrder++;
                continue;
            }

            var created = new NotebookChecklistItem
            {
                NotebookItemId = item.Id,
                Text = text,
                IsDone = requested.IsDone,
                SortOrder = nextSortOrder++,
                CreatedAtUtc = now,
                CompletedAtUtc = requested.IsDone ? now : null
            };
            item.ChecklistItems.Add(created);
            createdRows.Add((created, requested.ClientKey));
        }

        foreach (var row in item.ChecklistItems.Where(row => row.Id != 0 && !requestedIds.Contains(row.Id)).ToList())
        {
            item.ChecklistItems.Remove(row);
        }

        return createdRows;
    }

    private static string ValidateChecklistText(string text)
    {
        if (text.Length > NotebookLimits.ChecklistTextMaxLength)
        {
            throw new ArgumentException($"Checklist text cannot exceed {NotebookLimits.ChecklistTextMaxLength} characters.");
        }

        return text;
    }

    private async Task SyncTags(NotebookItem item, string ownerId, IReadOnlyList<string> tags, CancellationToken ct)
    {
        var requestedNames = tags
            .Select(tag => tag.Trim().TrimStart('#'))
            .Where(tag => tag.Length > 0)
            .Select(tag => tag[..Math.Min(tag.Length, NotebookLimits.LabelNameMaxLength)])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(NotebookLimits.MaxLabelsPerItem)
            .ToList();

        var requestedKeys = requestedNames
            .Select(NormalizeTag)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingJoins = item.Tags.ToList();
        var existingByKey = existingJoins
            .Where(join => join.NotebookTag is not null)
            .ToDictionary(
                join => join.NotebookTag!.NormalizedName,
                join => join,
                StringComparer.OrdinalIgnoreCase);

        foreach (var join in existingJoins)
        {
            var existingKey = join.NotebookTag?.NormalizedName;
            if (string.IsNullOrWhiteSpace(existingKey) || !requestedKeys.Contains(existingKey))
            {
                item.Tags.Remove(join);
            }
        }

        foreach (var requestedName in requestedNames)
        {
            var normalizedName = NormalizeTag(requestedName);
            if (existingByKey.ContainsKey(normalizedName))
            {
                continue;
            }

            var tag = await _db.NotebookTags.FirstOrDefaultAsync(
                notebookTag => notebookTag.OwnerId == ownerId && notebookTag.NormalizedName == normalizedName,
                ct);

            if (tag is null)
            {
                tag = new NotebookTag
                {
                    OwnerId = ownerId,
                    Name = requestedName,
                    NormalizedName = normalizedName
                };

                _db.NotebookTags.Add(tag);
            }

            item.Tags.Add(new NotebookItemTag
            {
                NotebookItem = item,
                NotebookTag = tag
            });
        }
    }

    private async Task<IReadOnlyList<NotebookTagVm>> BuildAllTags(string ownerId, CancellationToken ct)
    {
        return await _db.NotebookTags.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .Select(x => new NotebookTagVm { Id = x.Id, Name = x.Name, Count = x.Items.Count(j => j.NotebookItem != null && j.NotebookItem.DeletedAtUtc == null && j.NotebookItem.Status == NotebookItemStatus.Active) })
            .OrderBy(x => x.Name).ToArrayAsync(ct);
    }

    private static IReadOnlyList<string> ValidateLabelNames(IReadOnlyList<string>? labels)
    {
        var result = (labels ?? Array.Empty<string>()).Select(ValidateLabelName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (result.Length > NotebookLimits.MaxLabelsPerItem) throw new NotebookValidationException($"A note can have at most {NotebookLimits.MaxLabelsPerItem} labels.");
        return result;
    }

    private static string ValidateLabelName(string? name)
    {
        var value = (name ?? string.Empty).Trim().TrimStart('#').Trim();
        if (value.Length == 0) throw new NotebookValidationException("Label name is required.");
        if (value.Length > NotebookLimits.LabelNameMaxLength) throw new NotebookValidationException($"Label name cannot exceed {NotebookLimits.LabelNameMaxLength} characters.");
        return value;
    }

    private static string NormalizeTag(string tag) => tag.ToUpperInvariant();
}
