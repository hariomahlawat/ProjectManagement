using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Arpp;

public sealed class ArppReferenceDataService : IArppReferenceDataService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public ArppReferenceDataService(ApplicationDbContext db, IClock clock, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<ArppReferenceDataSet> GetWorkspaceOptionsAsync(
        IReadOnlyCollection<int> selectedCfaIds,
        IReadOnlyCollection<int> selectedFundIds,
        IReadOnlyCollection<int> selectedDfpdsIds,
        CancellationToken cancellationToken = default)
    {
        selectedCfaIds ??= Array.Empty<int>();
        selectedFundIds ??= Array.Empty<int>();
        selectedDfpdsIds ??= Array.Empty<int>();

        var cfaRows = await _db.ArppCfaOptions.AsNoTracking()
            .Where(item => item.IsActive || selectedCfaIds.Contains(item.Id))
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                Value = item.Name,
                Description = (string?)null,
                item.IsActive,
                item.SortOrder,
                UsageCount = item.Entries.Count,
                item.RowVersion
            })
            .ToListAsync(cancellationToken);

        var fundRows = await _db.ArppFundOptions.AsNoTracking()
            .Where(item => item.IsActive || selectedFundIds.Contains(item.Id))
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                Value = item.Name,
                Description = (string?)null,
                item.IsActive,
                item.SortOrder,
                UsageCount = item.Entries.Count,
                item.RowVersion
            })
            .ToListAsync(cancellationToken);

        var scheduleRows = await _db.ArppDfpdsSchedules.AsNoTracking()
            .Where(item => item.IsActive || selectedDfpdsIds.Contains(item.Id))
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Code)
            .Select(item => new
            {
                item.Id,
                Value = item.Code,
                item.Description,
                item.IsActive,
                item.SortOrder,
                UsageCount = item.Entries.Count,
                item.RowVersion
            })
            .ToListAsync(cancellationToken);

        return new ArppReferenceDataSet(
            cfaRows.Select(item => ToOption(item.Id, item.Value, item.Description, item.IsActive, item.SortOrder, item.UsageCount, item.RowVersion)).ToArray(),
            fundRows.Select(item => ToOption(item.Id, item.Value, item.Description, item.IsActive, item.SortOrder, item.UsageCount, item.RowVersion)).ToArray(),
            scheduleRows.Select(item => ToOption(item.Id, item.Value, item.Description, item.IsActive, item.SortOrder, item.UsageCount, item.RowVersion)).ToArray());
    }

    public async Task<ArppReferenceDataAdminSnapshot> GetAdminSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var cfaRows = await _db.ArppCfaOptions.AsNoTracking()
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                Value = item.Name,
                Description = (string?)null,
                item.IsActive,
                item.SortOrder,
                UsageCount = item.Entries.Count,
                item.RowVersion
            })
            .ToListAsync(cancellationToken);

        var fundRows = await _db.ArppFundOptions.AsNoTracking()
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                Value = item.Name,
                Description = (string?)null,
                item.IsActive,
                item.SortOrder,
                UsageCount = item.Entries.Count,
                item.RowVersion
            })
            .ToListAsync(cancellationToken);

        var scheduleRows = await _db.ArppDfpdsSchedules.AsNoTracking()
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Code)
            .Select(item => new
            {
                item.Id,
                Value = item.Code,
                item.Description,
                item.IsActive,
                item.SortOrder,
                UsageCount = item.Entries.Count,
                item.RowVersion
            })
            .ToListAsync(cancellationToken);

        return new ArppReferenceDataAdminSnapshot(
            cfaRows.Select(item => ToOption(item.Id, item.Value, item.Description, item.IsActive, item.SortOrder, item.UsageCount, item.RowVersion)).ToArray(),
            fundRows.Select(item => ToOption(item.Id, item.Value, item.Description, item.IsActive, item.SortOrder, item.UsageCount, item.RowVersion)).ToArray(),
            scheduleRows.Select(item => ToOption(item.Id, item.Value, item.Description, item.IsActive, item.SortOrder, item.UsageCount, item.RowVersion)).ToArray());
    }

    public async Task<ArppReferenceDataCommandResult> SaveAsync(
        ArppReferenceDataSaveCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = Validate(command);
        if (errors.Count > 0)
        {
            return ArppReferenceDataCommandResult.Failed("Review the highlighted reference-data fields.", errors);
        }

        var value = command.Value.Trim();
        var normalized = Normalize(value);
        var now = _clock.UtcNow.ToUniversalTime();

        switch (command.Kind)
        {
            case ArppReferenceDataKind.Cfa:
                return await SaveCfaAsync(command, value, normalized, now, cancellationToken);
            case ArppReferenceDataKind.Fund:
                return await SaveFundAsync(command, value, normalized, now, cancellationToken);
            case ArppReferenceDataKind.DfpdsSchedule:
                return await SaveDfpdsAsync(command, value, normalized, now, cancellationToken);
            default:
                return ArppReferenceDataCommandResult.Failed("Select a valid ARPP reference-data type.");
        }
    }

    public async Task<ArppReferenceDataCommandResult> SetActiveAsync(
        ArppReferenceDataActivationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Id <= 0 || string.IsNullOrWhiteSpace(command.UserId))
        {
            return ArppReferenceDataCommandResult.Failed("The reference value could not be updated.");
        }

        var rowVersion = ParseRowVersion(command.RowVersion);
        if (rowVersion is null)
        {
            return ArppReferenceDataCommandResult.Failed("The reference value is stale. Reload the page and try again.");
        }

        var now = _clock.UtcNow.ToUniversalTime();
        string value;

        switch (command.Kind)
        {
            case ArppReferenceDataKind.Cfa:
            {
                var item = await _db.ArppCfaOptions.SingleOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
                if (item is null) return ArppReferenceDataCommandResult.Failed("The CFA value was not found.");
                _db.Entry(item).Property(x => x.RowVersion).OriginalValue = rowVersion;
                item.IsActive = command.IsActive;
                item.UpdatedAtUtc = now;
                item.UpdatedByUserId = command.UserId;
                value = item.Name;
                break;
            }
            case ArppReferenceDataKind.Fund:
            {
                var item = await _db.ArppFundOptions.SingleOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
                if (item is null) return ArppReferenceDataCommandResult.Failed("The Fund value was not found.");
                _db.Entry(item).Property(x => x.RowVersion).OriginalValue = rowVersion;
                item.IsActive = command.IsActive;
                item.UpdatedAtUtc = now;
                item.UpdatedByUserId = command.UserId;
                value = item.Name;
                break;
            }
            case ArppReferenceDataKind.DfpdsSchedule:
            {
                var item = await _db.ArppDfpdsSchedules.SingleOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
                if (item is null) return ArppReferenceDataCommandResult.Failed("The DFPDS schedule was not found.");
                _db.Entry(item).Property(x => x.RowVersion).OriginalValue = rowVersion;
                item.IsActive = command.IsActive;
                item.UpdatedAtUtc = now;
                item.UpdatedByUserId = command.UserId;
                value = item.Code;
                break;
            }
            default:
                return ArppReferenceDataCommandResult.Failed("Select a valid ARPP reference-data type.");
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ArppReferenceDataCommandResult.Failed("This value was changed by another administrator. Reload the page before trying again.");
        }

        await _audit.LogAsync(
            action: command.IsActive ? "MasterData.ArppReferenceActivated" : "MasterData.ArppReferenceDeactivated",
            message: $"{(command.IsActive ? "Activated" : "Deactivated")} ARPP {DisplayKind(command.Kind)} value '{value}'.",
            userId: command.UserId,
            userName: command.UserName,
            data: new Dictionary<string, string?>
            {
                ["Kind"] = command.Kind.ToString(),
                ["Id"] = command.Id.ToString(),
                ["Value"] = value
            });

        return ArppReferenceDataCommandResult.Succeeded($"{DisplayKind(command.Kind)} value {(command.IsActive ? "activated" : "deactivated")}.");
    }

    private async Task<ArppReferenceDataCommandResult> SaveCfaAsync(
        ArppReferenceDataSaveCommand command, string value, string normalized, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await _db.ArppCfaOptions.AsNoTracking().AnyAsync(x => x.NormalizedName == normalized && x.Id != command.Id, cancellationToken))
            return Duplicate("CFA");

        ArppCfaOption item;
        var isNew = !command.Id.HasValue;
        if (isNew)
        {
            item = new ArppCfaOption { CreatedAtUtc = now, CreatedByUserId = command.UserId };
            _db.ArppCfaOptions.Add(item);
        }
        else
        {
            item = await _db.ArppCfaOptions.SingleOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            if (item is null) return ArppReferenceDataCommandResult.Failed("The CFA value no longer exists.");
            if (!TrySetOriginalRowVersion(item, command.RowVersion)) return ArppReferenceDataCommandResult.Failed("The CFA value is stale. Reload the page.");
        }

        item.Name = value;
        item.NormalizedName = normalized;
        item.SortOrder = command.SortOrder;
        item.UpdatedAtUtc = now;
        item.UpdatedByUserId = command.UserId;
        if (isNew) item.IsActive = true;
        return await SaveAndAuditAsync(command, value, isNew, cancellationToken);
    }

    private async Task<ArppReferenceDataCommandResult> SaveFundAsync(
        ArppReferenceDataSaveCommand command, string value, string normalized, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await _db.ArppFundOptions.AsNoTracking().AnyAsync(x => x.NormalizedName == normalized && x.Id != command.Id, cancellationToken))
            return Duplicate("Fund");

        ArppFundOption item;
        var isNew = !command.Id.HasValue;
        if (isNew)
        {
            item = new ArppFundOption { CreatedAtUtc = now, CreatedByUserId = command.UserId };
            _db.ArppFundOptions.Add(item);
        }
        else
        {
            item = await _db.ArppFundOptions.SingleOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            if (item is null) return ArppReferenceDataCommandResult.Failed("The Fund value no longer exists.");
            if (!TrySetOriginalRowVersion(item, command.RowVersion)) return ArppReferenceDataCommandResult.Failed("The Fund value is stale. Reload the page.");
        }

        item.Name = value;
        item.NormalizedName = normalized;
        item.SortOrder = command.SortOrder;
        item.UpdatedAtUtc = now;
        item.UpdatedByUserId = command.UserId;
        if (isNew) item.IsActive = true;
        return await SaveAndAuditAsync(command, value, isNew, cancellationToken);
    }

    private async Task<ArppReferenceDataCommandResult> SaveDfpdsAsync(
        ArppReferenceDataSaveCommand command, string value, string normalized, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await _db.ArppDfpdsSchedules.AsNoTracking().AnyAsync(x => x.NormalizedCode == normalized && x.Id != command.Id, cancellationToken))
            return Duplicate("DFPDS schedule");

        ArppDfpdsSchedule item;
        var isNew = !command.Id.HasValue;
        if (isNew)
        {
            item = new ArppDfpdsSchedule { CreatedAtUtc = now, CreatedByUserId = command.UserId };
            _db.ArppDfpdsSchedules.Add(item);
        }
        else
        {
            item = await _db.ArppDfpdsSchedules.SingleOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            if (item is null) return ArppReferenceDataCommandResult.Failed("The DFPDS schedule no longer exists.");
            if (!TrySetOriginalRowVersion(item, command.RowVersion)) return ArppReferenceDataCommandResult.Failed("The DFPDS schedule is stale. Reload the page.");
        }

        item.Code = value;
        item.NormalizedCode = normalized;
        item.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        item.SortOrder = command.SortOrder;
        item.UpdatedAtUtc = now;
        item.UpdatedByUserId = command.UserId;
        if (isNew) item.IsActive = true;
        return await SaveAndAuditAsync(command, value, isNew, cancellationToken);
    }

    private async Task<ArppReferenceDataCommandResult> SaveAndAuditAsync(
        ArppReferenceDataSaveCommand command, string value, bool isNew, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ArppReferenceDataCommandResult.Failed("This value was changed by another administrator. Reload the page before saving again.");
        }
        catch (DbUpdateException exception) when (IsLikelyUniqueConstraintViolation(exception))
        {
            return Duplicate(DisplayKind(command.Kind));
        }

        await _audit.LogAsync(
            action: isNew ? "MasterData.ArppReferenceAdded" : "MasterData.ArppReferenceUpdated",
            message: $"{(isNew ? "Added" : "Updated")} ARPP {DisplayKind(command.Kind)} value '{value}'.",
            userId: command.UserId,
            userName: command.UserName,
            data: new Dictionary<string, string?>
            {
                ["Kind"] = command.Kind.ToString(),
                ["Value"] = value,
                ["SortOrder"] = command.SortOrder.ToString()
            });

        return ArppReferenceDataCommandResult.Succeeded($"{DisplayKind(command.Kind)} value {(isNew ? "added" : "updated")}.");
    }

    private bool TrySetOriginalRowVersion<TEntity>(TEntity entity, string? rowVersion) where TEntity : class
    {
        var parsed = ParseRowVersion(rowVersion);
        if (parsed is null) return false;
        _db.Entry(entity).Property("RowVersion").OriginalValue = parsed;
        return true;
    }

    private static Dictionary<string, IReadOnlyList<string>> Validate(ArppReferenceDataSaveCommand command)
    {
        var errors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var max = command.Kind == ArppReferenceDataKind.Cfa ? 200 : 120;
        var value = command.Value?.Trim() ?? string.Empty;
        if (value.Length == 0) Add(errors, nameof(command.Value), "Enter the reference value.");
        else if (value.Length > max) Add(errors, nameof(command.Value), $"The value cannot exceed {max} characters.");
        if ((command.Description?.Trim().Length ?? 0) > 300) Add(errors, nameof(command.Description), "The description cannot exceed 300 characters.");
        if (command.SortOrder < 0) Add(errors, nameof(command.SortOrder), "Sort order cannot be negative.");
        if (string.IsNullOrWhiteSpace(command.UserId)) Add(errors, nameof(command.UserId), "The current user could not be identified.");
        if (command.Id.HasValue && ParseRowVersion(command.RowVersion) is null) Add(errors, nameof(command.RowVersion), "The value is stale. Reload the page.");
        return errors;
    }

    private static string Normalize(string value)
        => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static byte[]? ParseRowVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { return null; }
    }

    private static void Add(IDictionary<string, IReadOnlyList<string>> errors, string key, string message)
    {
        if (errors.TryGetValue(key, out var existing)) errors[key] = existing.Concat(new[] { message }).ToArray();
        else errors[key] = new[] { message };
    }

    private static ArppReferenceDataCommandResult Duplicate(string label)
        => ArppReferenceDataCommandResult.Failed($"A {label} value with the same wording already exists.");

    private static bool IsLikelyUniqueConstraintViolation(DbUpdateException exception)
        => exception.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
           || exception.Message.Contains("unique", StringComparison.OrdinalIgnoreCase);

    private static ArppReferenceOption ToOption(
        int id,
        string value,
        string? description,
        bool isActive,
        int sortOrder,
        int usageCount,
        byte[] rowVersion)
        => new(
            id,
            value,
            description,
            isActive,
            sortOrder,
            usageCount,
            Convert.ToBase64String(rowVersion));

    private static string DisplayKind(ArppReferenceDataKind kind) => kind switch
    {
        ArppReferenceDataKind.Cfa => "CFA",
        ArppReferenceDataKind.Fund => "Fund",
        ArppReferenceDataKind.DfpdsSchedule => "DFPDS schedule",
        _ => "reference"
    };
}
