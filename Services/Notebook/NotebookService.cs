using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

        var baseQuery = _db.NotebookItems
            .AsNoTracking()
            .Include(item => item.Tags)
            .ThenInclude(itemTag => itemTag.NotebookTag)
            .Include(item => item.ChecklistItems)
            .Where(item => item.OwnerId == ownerId && item.DeletedAtUtc == null);

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
            "labels" => activeQuery,
            "reminders" => remindersQuery,
            "archive" or "archived" => filteredBaseQuery.Where(item => item.Status == NotebookItemStatus.Archived),
            "completed" => filteredBaseQuery.Where(item => item.Status == NotebookItemStatus.Completed),
            _ => activeQuery
        };

        var items = (await filteredQuery
                .OrderByDescending(item => item.IsPinned)
                .ThenBy(item => item.ReminderAtUtc == null)
                .ThenBy(item => item.ReminderAtUtc)
                .ThenByDescending(item => item.UpdatedAtUtc)
                .Take(80)
                .ToListAsync(ct))
            .Select(item => ToListVm(item, bounds))
            .ToArray();

        var pinned = (await activeQuery
                .Where(item => item.Status == NotebookItemStatus.Active && item.IsPinned)
                .OrderBy(item => item.SortOrder == 0 ? int.MaxValue : item.SortOrder)
                .ThenByDescending(item => item.UpdatedAtUtc)
                .Take(80)
                .ToListAsync(ct))
            .Select(item => ToListVm(item, bounds))
            .ToArray();

        var sticky = (await activeQuery
                .Where(item => item.Type == NotebookItemType.Sticky && item.Status == NotebookItemStatus.Active)
                .OrderByDescending(item => item.UpdatedAtUtc)
                .Take(12)
                .ToListAsync(ct))
            .Select(item => ToListVm(item, bounds))
            .ToArray();

        var due = (await activeQuery
                .Where(item =>
                    item.Status == NotebookItemStatus.Active &&
                    item.ReminderAtUtc != null &&
                    item.ReminderAtUtc < bounds.EndUtc)
                .OrderBy(item => item.ReminderAtUtc)
                .Take(6)
                .ToListAsync(ct))
            .Select(item => ToListVm(item, bounds))
            .ToArray();


        // SECTION: Home board uses a manual-order friendly Others section.
        var recent = (await activeQuery
                .Where(item => item.Status == NotebookItemStatus.Active && !item.IsPinned)
                .OrderBy(item => item.SortOrder == 0 ? int.MaxValue : item.SortOrder)
                .ThenByDescending(item => item.UpdatedAtUtc)
                .Take(80)
                .ToListAsync(ct))
            .Select(item => ToListVm(item, bounds))
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
        var query = _db.NotebookItems.AsNoTracking().Where(item => item.OwnerId == ownerId && item.DeletedAtUtc == null);
        var active = query.Where(item => item.Status == NotebookItemStatus.Active);
        return new Dictionary<string, int>
        {
            ["home"] = await active.CountAsync(ct),
            ["today"] = await active.CountAsync(item => item.ReminderAtUtc != null && item.ReminderAtUtc < bounds.EndUtc, ct),
            ["reminders"] = await ReminderItems(active).CountAsync(ct),
            ["labels"] = await _db.NotebookTags.AsNoTracking().CountAsync(tag => tag.OwnerId == ownerId, ct),
            ["archive"] = await query.CountAsync(item => item.Status == NotebookItemStatus.Archived, ct),
            ["completed"] = await query.CountAsync(item => item.Status == NotebookItemStatus.Completed, ct),
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
        return CreateIdAsync(ownerId, new NotebookEditInput
        {
            Title = parsed.Title,
            Type = parsed.Type,
            Priority = parsed.Priority,
            ReminderAtUtc = parsed.ReminderAtUtc,
            Tags = parsed.Tags,
            ColorKey = parsed.Type == NotebookItemType.Sticky ? "blue" : null
        }, ct);
    }

    public async Task<NotebookItemDetailVm> CreateAsync(string ownerId, NotebookEditInput input, CancellationToken ct = default)
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
            ClientRequestId = input.ClientRequestId
        };

        ApplyChecklist(item, input.ChecklistRows.Any() ? input.ChecklistRows : input.ChecklistItems.Select((text, index) => new NotebookChecklistEditRow { Text = text, SortOrder = index }).ToArray(), now);
        _db.NotebookItems.Add(item);
        await SyncTags(item, ownerId, input.Tags, ct);
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
        return MapDetail(item);
    }

    private async Task<Guid> CreateIdAsync(string ownerId, NotebookEditInput input, CancellationToken ct = default)
    {
        var created = await CreateAsync(ownerId, input, ct);
        return created.Id;
    }

    public async Task<NotebookItemDetailVm> UpdateAsync(string ownerId, Guid id, NotebookEditInput input, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        if (input.Type != item.Type)
        {
            throw new ArgumentException("Notebook item type must be changed through a conversion command.");
        }
        item.Title = CleanTitle(input.Title);
        item.BodyMarkdown = input.BodyMarkdown;
        item.Priority = input.Priority;
        item.ReminderAtUtc = input.ReminderAtUtc;
        item.IsPinned = input.IsPinned;
        item.IsFavorite = input.IsFavorite;
        item.ColorKey = CleanColor(input.ColorKey, input.Type);
        Touch(item, _clock.UtcNow);

        SyncChecklistItems(item, input.ChecklistRows.Any() ? input.ChecklistRows : input.ChecklistItems.Select((text, index) => new NotebookChecklistEditRow { Text = text, SortOrder = index }).ToArray(), item.UpdatedAtUtc);
        await SyncTags(item, ownerId, input.Tags, ct);
        await _db.SaveChangesAsync(ct);
        await TryWriteAuditAsync("Notebook.Update", ownerId, item.Id, ct);
        return MapDetail(item);
    }

    public async Task<NotebookItemDetailVm> ArchiveAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        await ArchiveLoadedAsync(item, ownerId, ct);
        return MapDetail(item);
    }

    public async Task<NotebookItemDetailVm> RestoreAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        await RestoreLoadedAsync(item, ct);
        return MapDetail(item);
    }

    public async Task<NotebookItemDetailVm> ReopenAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        await ReopenLoadedAsync(item, ct);
        return MapDetail(item);
    }

    public async Task<NotebookItemDetailVm> DeleteAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        await DeleteLoadedAsync(item, ownerId, ct);
        return MapDetail(item);
    }

    public async Task<NotebookItemDetailVm> SetPinnedAsync(string ownerId, Guid id, bool isPinned, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        item.IsPinned = isPinned;
        Touch(item, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        return MapDetail(item);
    }

    public async Task ToggleFavoriteAsync(string ownerId, Guid id, CancellationToken ct = default)
    {
        var item = await LoadOwned(ownerId, id, ct);
        item.IsFavorite = !item.IsFavorite;
        Touch(item, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<NotebookItemDetailVm> CompleteAsync(string ownerId, Guid id, bool isComplete, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        await CompleteLoadedAsync(item, isComplete, ct);
        return MapDetail(item);
    }

    public async Task<NotebookItemDetailVm> ConvertTypeAsync(string ownerId, Guid id, NotebookItemType newType, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, id, expectedVersion, ct);
        if (item.Type == newType)
        {
            return MapDetail(item);
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
        return MapDetail(item);
    }

    public async Task<NotebookItemDetailVm> DuplicateAsync(string ownerId, Guid id, CancellationToken ct = default)
    {
        var source = await LoadOwned(ownerId, id, ct);
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

        foreach (var tag in source.Tags.Where(tag => tag.NotebookTag is not null))
        {
            copy.Tags.Add(new NotebookItemTag { NotebookItem = copy, NotebookTag = tag.NotebookTag });
        }

        _db.NotebookItems.Add(copy);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Notebook.Duplicate", userId: ownerId, data: new Dictionary<string, string?> { ["SourceId"] = id.ToString(), ["Id"] = copy.Id.ToString() });
        return MapDetail(copy);
    }

    public async Task<NotebookItemDetailVm> ToggleChecklistItemAsync(string ownerId, Guid itemId, int checklistItemId, bool isDone, Guid expectedVersion, CancellationToken ct = default)
    {
        var item = await LoadOwnedForUpdate(ownerId, itemId, expectedVersion, ct);
        var checklistItem = item.ChecklistItems.FirstOrDefault(row => row.Id == checklistItemId) ?? throw new KeyNotFoundException();
        checklistItem.IsDone = isDone;
        var now = _clock.UtcNow;
        checklistItem.CompletedAtUtc = isDone ? now : null;
        Touch(item, now);
        await _db.SaveChangesAsync(ct);
        return MapDetail(item);
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

    private async Task DeleteLoadedAsync(NotebookItem item, string ownerId, CancellationToken ct)
    {
        item.DeletedAtUtc = _clock.UtcNow;
        Touch(item, item.DeletedAtUtc.Value);
        await _db.SaveChangesAsync(ct);
        await TryWriteAuditAsync("Notebook.Delete", ownerId, item.Id, ct);
    }

    private async Task CompleteLoadedAsync(NotebookItem item, bool isComplete, CancellationToken ct)
    {
        item.Status = isComplete ? NotebookItemStatus.Completed : NotebookItemStatus.Active;
        var now = _clock.UtcNow;
        item.CompletedAtUtc = isComplete ? now : null;
        Touch(item, now);
        await _db.SaveChangesAsync(ct);
    }

    private async Task TryWriteAuditAsync(string action, string ownerId, Guid itemId, CancellationToken ct)
    {
        try
        {
            await _audit.LogAsync(action, userId: ownerId, data: new Dictionary<string, string?> { ["Id"] = itemId.ToString() });
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
        return ex.InnerException?.Message.Contains("ClientRequestId", StringComparison.OrdinalIgnoreCase) == true ||
               ex.Message.Contains("ClientRequestId", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<NotebookItem> LoadOwned(string ownerId, Guid id, CancellationToken ct) =>
        await _db.NotebookItems
            .Include(item => item.Tags)
                .ThenInclude(itemTag => itemTag.NotebookTag)
            .Include(item => item.ChecklistItems)
            .FirstOrDefaultAsync(item =>
                item.Id == id &&
                item.OwnerId == ownerId &&
                item.DeletedAtUtc == null,
                ct) ?? throw new KeyNotFoundException();


    private async Task<NotebookItem> LoadOwnedForUpdate(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct)
    {
        var query = _db.NotebookItems
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
            throw new NotebookConcurrencyException(id, expectedVersion, item.Version);
        }

        return item;
    }


    private NotebookItemDetailVm MapDetail(NotebookItem item)
    {
        var detail = ToDetailShell(item, TodayBounds(_clock.UtcNow));
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


    private static NotebookItemDetailVm ToDetailShell(NotebookItem item, (DateTimeOffset StartUtc, DateTimeOffset EndUtc) bounds)
    {
        var list = ToListVm(item, bounds);
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
            Version = list.Version
        };
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
        return allowedColors.Contains(color) ? color! : type == NotebookItemType.Sticky ? "blue" : "white";
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

        var allowedViews = new[] { "home", "today", "reminders", "labels", "archive", "archived", "completed" };
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

    private static NotebookItemListVm ToListVm(NotebookItem item, (DateTimeOffset StartUtc, DateTimeOffset EndUtc) bounds) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Preview = item.BodyMarkdown,
        Type = item.Type,
        Status = item.Status,
        Priority = item.Priority,
        ReminderAtUtc = item.ReminderAtUtc,
        ReminderDisplay = NotebookReminderFormatter.FormatReminder(item.ReminderAtUtc),
        IsPinned = item.IsPinned,
        IsFavorite = item.IsFavorite,
        ColorKey = item.ColorKey ?? "white",
        UpdatedAtUtc = item.UpdatedAtUtc,
        Version = item.Version,
        Tags = item.Tags
            .Select(tag => tag.NotebookTag?.Name ?? string.Empty)
            .Where(tag => tag.Length > 0)
            .ToArray(),
        ChecklistTotal = item.ChecklistItems.Count,
        ChecklistDone = item.ChecklistItems.Count(checklistItem => checklistItem.IsDone),
        ChecklistPreviewItems = item.ChecklistItems
            .OrderBy(checklistItem => checklistItem.SortOrder)
            .Take(3)
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
            .Include(notebookItem => notebookItem.Tags)
            .ThenInclude(itemTag => itemTag.NotebookTag)
            .Include(notebookItem => notebookItem.ChecklistItems)
            .Include(notebookItem => notebookItem.Attachments)
            .FirstOrDefaultAsync(notebookItem =>
                notebookItem.Id == id &&
                notebookItem.OwnerId == ownerId &&
                notebookItem.DeletedAtUtc == null,
                ct);

        if (item is null)
        {
            return null;
        }

        var detail = ToDetailShell(item, bounds);
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
                item.OwnerId == ownerId &&
                item.DeletedAtUtc == null &&
                item.Status == NotebookItemStatus.Active);

        return new NotebookSummaryVm
        {
            TotalActive = await query.CountAsync(item => item.Status == NotebookItemStatus.Active, ct),
            DueToday = await query.CountAsync(item => item.ReminderAtUtc >= bounds.StartUtc && item.ReminderAtUtc < bounds.EndUtc, ct),
            Overdue = await query.CountAsync(item => item.ReminderAtUtc < bounds.StartUtc && item.Status == NotebookItemStatus.Active, ct),
            StickyCount = await query.CountAsync(item => item.Type == NotebookItemType.Sticky, ct),
            PinnedCount = await query.CountAsync(item => item.IsPinned, ct),
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
        var query = _db.NotebookItems
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.DeletedAtUtc == null);

        async Task<int> CountAsync(string view) => view switch
        {
            "home" => await query.CountAsync(item => item.Status == NotebookItemStatus.Active, ct),
            "today" => await query.CountAsync(item =>
                item.Status == NotebookItemStatus.Active &&
                item.ReminderAtUtc != null &&
                item.ReminderAtUtc < bounds.EndUtc,
                ct),
            "reminders" => await ReminderItems(ActiveItems(query)).CountAsync(ct),
            "labels" => await _db.NotebookTags.AsNoTracking().CountAsync(tag => tag.OwnerId == ownerId, ct),
            "archive" or "archived" => await query.CountAsync(item => item.Status == NotebookItemStatus.Archived, ct),
            "completed" => await query.CountAsync(item => item.Status == NotebookItemStatus.Completed, ct),
            _ => 0
        };

        var rows = new[]
        {
            ("home", "All Notes", "bi-journal-text"),
            ("today", "Today", "bi-calendar-check"),
            ("reminders", "Reminders", "bi-bell"),
            ("labels", "Labels", "bi-tags"),
            ("archive", "Archive", "bi-archive"),
            ("completed", "Completed", "bi-check2-circle")
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
            .Where(tag => tag.Count > 0)
            .OrderBy(tag => tag.Name)
            .ToArrayAsync(ct);
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

    private void SyncChecklistItems(NotebookItem item, IReadOnlyList<NotebookChecklistEditRow> rows, DateTimeOffset now)
    {
        var requestedRows = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Text))
            .OrderBy(row => row.SortOrder)
            .ToList();

        var submittedIds = requestedRows.Where(row => row.Id.HasValue).Select(row => row.Id!.Value).ToArray();
        if (submittedIds.Length != submittedIds.Distinct().Count())
        {
            throw new ArgumentException("Duplicate checklist row ids are not allowed.");
        }

        var existingById = item.ChecklistItems.ToDictionary(row => row.Id);
        if (submittedIds.Any(id => !existingById.ContainsKey(id)))
        {
            throw new ArgumentException("Checklist row ids must belong to the notebook item.");
        }

        var requestedIds = submittedIds.ToHashSet();
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

            item.ChecklistItems.Add(new NotebookChecklistItem
            {
                NotebookItemId = item.Id,
                Text = text,
                IsDone = requested.IsDone,
                SortOrder = nextSortOrder++,
                CreatedAtUtc = now,
                CompletedAtUtc = requested.IsDone ? now : null
            });
        }

        foreach (var row in item.ChecklistItems.Where(row => row.Id != 0 && !requestedIds.Contains(row.Id)).ToList())
        {
            item.ChecklistItems.Remove(row);
        }
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

    private static string NormalizeTag(string tag) => tag.ToUpperInvariant();
}
