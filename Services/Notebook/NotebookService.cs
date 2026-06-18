using Microsoft.EntityFrameworkCore;
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

    public NotebookService(ApplicationDbContext db, IAuditService audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    // SECTION: Read model composition
    public async Task<NotebookIndexVm> GetIndexAsync(
        string ownerId,
        string view,
        string? query,
        Guid? selectedId,
        CancellationToken ct = default)
    {
        view = NormalizeView(view);
        var bounds = TodayBounds(_clock.UtcNow);

        var baseQuery = _db.NotebookItems
            .AsNoTracking()
            .Include(item => item.Tags)
            .ThenInclude(itemTag => itemTag.NotebookTag)
            .Include(item => item.ChecklistItems)
            .Where(item => item.OwnerId == ownerId && item.DeletedAtUtc == null);

        var activeQuery = VisibleItems(baseQuery);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var search = query.Trim();
            activeQuery = activeQuery.Where(item =>
                EF.Functions.ILike(item.Title, $"%{search}%") ||
                (item.BodyMarkdown != null && EF.Functions.ILike(item.BodyMarkdown, $"%{search}%")) ||
                item.Tags.Any(tag => EF.Functions.ILike(tag.NotebookTag!.Name, $"%{search}%")));
        }

        var activeStatusQuery = ActiveItems(activeQuery);
        var remindersQuery = ReminderItems(activeStatusQuery);

        var filteredQuery = view switch
        {
            "today" => activeQuery.Where(item =>
                item.Status == NotebookItemStatus.Active &&
                item.ReminderAtUtc != null &&
                item.ReminderAtUtc < bounds.EndUtc),
            "sticky" => activeQuery.Where(item =>
                item.Type == NotebookItemType.Sticky &&
                item.Status == NotebookItemStatus.Active),
            "notes" => activeQuery.Where(item => item.Type == NotebookItemType.Note),
            "checklists" => activeQuery.Where(item => item.Type == NotebookItemType.Checklist),
            "reminders" => remindersQuery,
            "archived" => baseQuery.Where(item => item.Status == NotebookItemStatus.Archived),
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
                .Where(item => item.IsPinned)
                .OrderByDescending(item => item.UpdatedAtUtc)
                .Take(8)
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
                .Take(10)
                .ToListAsync(ct))
            .Select(item => ToListVm(item, bounds))
            .ToArray();

        var selected = await LoadSelectedAsync(ownerId, selectedId ?? items.FirstOrDefault()?.Id, bounds, ct);

        return new NotebookIndexVm
        {
            View = view,
            Query = query,
            Items = items,
            PinnedItems = pinned,
            StickyItems = sticky,
            DueItems = due,
            SelectedItem = selected,
            Summary = await BuildSummary(ownerId, bounds, ct),
            RailItems = await BuildRail(ownerId, view, bounds, ct),
            Tags = await BuildTags(ownerId, ct)
        };
    }

    // SECTION: Mutations
    public Task<Guid> QuickCaptureAsync(
        string ownerId,
        string input,
        NotebookItemType? forcedType = null,
        CancellationToken ct = default)
    {
        var parsed = NotebookQuickCaptureParser.Parse(input, _clock.UtcNow, forcedType);
        return CreateAsync(ownerId, new NotebookEditInput
        {
            Title = parsed.Title,
            Type = parsed.Type,
            Priority = parsed.Priority,
            ReminderAtUtc = parsed.ReminderAtUtc,
            Tags = parsed.Tags,
            ColorKey = parsed.Type == NotebookItemType.Sticky ? "blue" : null
        }, ct);
    }

    public async Task<Guid> CreateAsync(string ownerId, NotebookEditInput input, CancellationToken ct = default)
    {
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
            UpdatedAtUtc = now
        };

        ApplyChecklist(item, input.ChecklistItems, now);
        _db.NotebookItems.Add(item);
        await SyncTags(item, ownerId, input.Tags, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Notebook.Create", userId: ownerId, data: new Dictionary<string, string?> { ["Id"] = item.Id.ToString() });
        return item.Id;
    }

    public async Task UpdateAsync(string ownerId, Guid id, NotebookEditInput input, CancellationToken ct = default)
    {
        var item = await LoadOwned(ownerId, id, ct);
        item.Title = CleanTitle(input.Title);
        item.BodyMarkdown = input.BodyMarkdown;
        item.Type = input.Type;
        item.Priority = input.Priority;
        item.ReminderAtUtc = input.ReminderAtUtc;
        item.IsPinned = input.IsPinned;
        item.IsFavorite = input.IsFavorite;
        item.ColorKey = CleanColor(input.ColorKey, input.Type);
        item.UpdatedAtUtc = _clock.UtcNow;

        SyncChecklistItems(item, input.ChecklistItems, item.UpdatedAtUtc);
        await SyncTags(item, ownerId, input.Tags, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Notebook.Update", userId: ownerId, data: new Dictionary<string, string?> { ["Id"] = id.ToString() });
    }

    public async Task ArchiveAsync(string ownerId, Guid id, CancellationToken ct = default)
    {
        var item = await LoadOwned(ownerId, id, ct);
        item.Status = NotebookItemStatus.Archived;
        item.ArchivedAtUtc = _clock.UtcNow;
        item.UpdatedAtUtc = item.ArchivedAtUtc.Value;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Notebook.Archive", userId: ownerId);
    }

    public async Task RestoreAsync(string ownerId, Guid id, CancellationToken ct = default)
    {
        var item = await LoadOwned(ownerId, id, ct);
        item.Status = NotebookItemStatus.Active;
        item.ArchivedAtUtc = null;
        item.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string ownerId, Guid id, CancellationToken ct = default)
    {
        var item = await LoadOwned(ownerId, id, ct);
        item.DeletedAtUtc = _clock.UtcNow;
        item.UpdatedAtUtc = item.DeletedAtUtc.Value;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Notebook.Delete", userId: ownerId);
    }

    public async Task TogglePinAsync(string ownerId, Guid id, CancellationToken ct = default)
    {
        var item = await LoadOwned(ownerId, id, ct);
        item.IsPinned = !item.IsPinned;
        item.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ToggleFavoriteAsync(string ownerId, Guid id, CancellationToken ct = default)
    {
        var item = await LoadOwned(ownerId, id, ct);
        item.IsFavorite = !item.IsFavorite;
        item.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(string ownerId, Guid id, bool isComplete, CancellationToken ct = default)
    {
        var item = await LoadOwned(ownerId, id, ct);
        item.Status = isComplete ? NotebookItemStatus.Completed : NotebookItemStatus.Active;
        item.CompletedAtUtc = isComplete ? _clock.UtcNow : null;
        item.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ConvertTypeAsync(string ownerId, Guid id, NotebookItemType newType, CancellationToken ct = default)
    {
        var item = await LoadOwned(ownerId, id, ct);
        item.Type = newType;
        item.ColorKey = CleanColor(item.ColorKey, newType);
        item.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ToggleChecklistItemAsync(string ownerId, int checklistItemId, bool isDone, CancellationToken ct = default)
    {
        var checklistItem = await _db.NotebookChecklistItems
            .Include(item => item.NotebookItem)
            .FirstOrDefaultAsync(item =>
                item.Id == checklistItemId &&
                item.NotebookItem!.OwnerId == ownerId &&
                item.NotebookItem.DeletedAtUtc == null,
                ct) ?? throw new KeyNotFoundException();

        checklistItem.IsDone = isDone;
        checklistItem.CompletedAtUtc = isDone ? _clock.UtcNow : null;
        await _db.SaveChangesAsync(ct);
    }

    // SECTION: Helpers
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

    private static string CleanTitle(string title)
    {
        var trimmed = title.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "Untitled" : trimmed[..Math.Min(trimmed.Length, 220)];
    }

    private static string CleanColor(string? color, NotebookItemType type)
    {
        var allowedColors = new[] { "blue", "amber", "green", "rose", "slate" };
        return allowedColors.Contains(color) ? color! : type == NotebookItemType.Sticky ? "blue" : "slate";
    }

    private static string NormalizeView(string? view)
    {
        var allowedViews = new[] { "home", "today", "sticky", "notes", "checklists", "reminders", "archived" };
        return allowedViews.Contains(view) ? view! : "home";
    }

    private static IQueryable<NotebookItem> VisibleItems(IQueryable<NotebookItem> query)
    {
        return query.Where(item => item.Status != NotebookItemStatus.Archived);
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
        ColorKey = item.ColorKey ?? "blue",
        UpdatedAtUtc = item.UpdatedAtUtc,
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

        var detail = new NotebookItemDetailVm();
        var list = ToListVm(item, bounds);
        foreach (var property in typeof(NotebookItemListVm).GetProperties())
        {
            property.SetValue(detail, property.GetValue(list));
        }

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
                item.Status != NotebookItemStatus.Archived);

        return new NotebookSummaryVm
        {
            TotalActive = await query.CountAsync(item => item.Status == NotebookItemStatus.Active, ct),
            DueToday = await query.CountAsync(item => item.ReminderAtUtc >= bounds.StartUtc && item.ReminderAtUtc < bounds.EndUtc, ct),
            Overdue = await query.CountAsync(item => item.ReminderAtUtc < bounds.StartUtc && item.Status == NotebookItemStatus.Active, ct),
            StickyCount = await query.CountAsync(item => item.Type == NotebookItemType.Sticky, ct),
            PinnedCount = await query.CountAsync(item => item.IsPinned, ct),
            ChecklistCount = await query.CountAsync(item => item.Type == NotebookItemType.Checklist, ct)
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
            "home" => await query.CountAsync(item => item.Status != NotebookItemStatus.Archived, ct),
            "today" => await query.CountAsync(item =>
                item.Status == NotebookItemStatus.Active &&
                item.ReminderAtUtc != null &&
                item.ReminderAtUtc < bounds.EndUtc,
                ct),
            "sticky" => await query.CountAsync(item =>
                item.Status == NotebookItemStatus.Active &&
                item.Type == NotebookItemType.Sticky,
                ct),
            "notes" => await query.CountAsync(item =>
                item.Status == NotebookItemStatus.Active &&
                item.Type == NotebookItemType.Note,
                ct),
            "checklists" => await query.CountAsync(item =>
                item.Status == NotebookItemStatus.Active &&
                item.Type == NotebookItemType.Checklist,
                ct),
            "reminders" => await ReminderItems(ActiveItems(query)).CountAsync(ct),
            "archived" => await query.CountAsync(item => item.Status == NotebookItemStatus.Archived, ct),
            _ => 0
        };

        var rows = new[]
        {
            ("home", "Home", "bi-house"),
            ("today", "Today", "bi-calendar-check"),
            ("sticky", "Sticky Board", "bi-sticky"),
            ("notes", "Notes", "bi-journal-text"),
            ("checklists", "Checklists", "bi-check2-square"),
            ("reminders", "Reminders", "bi-bell"),
            ("archived", "Archived", "bi-archive")
        };

        var list = new List<NotebookRailItemVm>();
        foreach (var row in rows)
        {
            list.Add(new NotebookRailItemVm
            {
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

    private static void ApplyChecklist(NotebookItem item, IReadOnlyList<string> lines, DateTimeOffset now)
    {
        var sortOrder = 0;
        foreach (var line in lines.Select(line => line.Trim()).Where(line => line.Length > 0))
        {
            item.ChecklistItems.Add(new NotebookChecklistItem
            {
                Text = line[..Math.Min(line.Length, 300)],
                SortOrder = sortOrder++,
                CreatedAtUtc = now
            });
        }
    }

    private void SyncChecklistItems(NotebookItem item, IReadOnlyList<string> lines, DateTimeOffset now)
    {
        var desiredLines = lines
            .Select(line => line?.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line![..Math.Min(line.Length, 300)])
            .ToList();

        var existingItems = item.ChecklistItems
            .OrderBy(row => row.SortOrder)
            .ToList();

        var usedExistingIds = new HashSet<int>();
        var nextSortOrder = 0;

        foreach (var desiredText in desiredLines)
        {
            var existing = existingItems.FirstOrDefault(row =>
                !usedExistingIds.Contains(row.Id) &&
                string.Equals(row.Text.Trim(), desiredText, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.Text = desiredText;
                existing.SortOrder = nextSortOrder++;
                usedExistingIds.Add(existing.Id);
                continue;
            }

            item.ChecklistItems.Add(new NotebookChecklistItem
            {
                NotebookItemId = item.Id,
                Text = desiredText,
                IsDone = false,
                SortOrder = nextSortOrder++,
                CreatedAtUtc = now
            });
        }

        foreach (var row in existingItems.Where(row => !usedExistingIds.Contains(row.Id)))
        {
            item.ChecklistItems.Remove(row);
        }
    }

    private async Task SyncTags(NotebookItem item, string ownerId, IReadOnlyList<string> tags, CancellationToken ct)
    {
        var requestedNames = tags
            .Select(tag => tag.Trim().TrimStart('#'))
            .Where(tag => tag.Length > 0)
            .Select(tag => tag[..Math.Min(tag.Length, 64)])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
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
