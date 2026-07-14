using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Services.Admin.MasterData.Integrity;

public enum MasterDataIntegrityStatus
{
    Healthy = 0,
    Warning = 1,
    Critical = 2
}

public sealed record MasterDataIntegrityFinding(
    string Code,
    string Message,
    string? RecordType = null,
    string? RecordId = null,
    string? RecordName = null);

public sealed record MasterDataIntegrityCheck(
    string Key,
    string Title,
    string Description,
    string Icon,
    MasterDataIntegrityStatus Status,
    IReadOnlyList<MasterDataIntegrityFinding> Findings,
    bool CanRepair,
    string? RepairLabel = null);

public sealed record MasterDataIntegritySnapshot(
    IReadOnlyList<MasterDataIntegrityCheck> Checks,
    int Healthy,
    int Warnings,
    int Critical,
    int Findings,
    DateTimeOffset CheckedUtc);

public interface IAdminMasterDataIntegrityService
{
    Task<MasterDataIntegritySnapshot> InspectAsync(CancellationToken cancellationToken = default);
    Task<AdminOperationResult> NormaliseOrderAsync(string checkKey, CancellationToken cancellationToken = default);
}

public sealed class AdminMasterDataIntegrityService : IAdminMasterDataIntegrityService
{
    private static readonly HashSet<string> RepairableOrderChecks = new(StringComparer.OrdinalIgnoreCase)
    {
        "project-category-order",
        "technical-category-order",
        "project-type-order",
        "sponsoring-unit-order",
        "line-directorate-order"
    };

    private readonly ApplicationDbContext _db;
    private readonly IAdminAuditService _audit;
    private readonly IAdminTimeService _time;

    public AdminMasterDataIntegrityService(
        ApplicationDbContext db,
        IAdminAuditService audit,
        IAdminTimeService time)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task<MasterDataIntegritySnapshot> InspectAsync(CancellationToken cancellationToken = default)
    {
        var projectCategories = await _db.ProjectCategories.AsNoTracking()
            .Select(item => new CategoryProjection(item.Id, item.Name, item.ParentId, item.IsActive, item.SortOrder, item.Projects.Any()))
            .ToListAsync(cancellationToken);
        var technicalCategories = await _db.TechnicalCategories.AsNoTracking()
            .Select(item => new CategoryProjection(item.Id, item.Name, item.ParentId, item.IsActive, item.SortOrder, item.Projects.Any()))
            .ToListAsync(cancellationToken);
        var projectTypes = await _db.ProjectTypes.AsNoTracking()
            .Select(item => new FlatProjection(item.Id, item.Name, item.IsActive, item.SortOrder, item.Projects.Any()))
            .ToListAsync(cancellationToken);
        var sponsoringUnits = await _db.SponsoringUnits.AsNoTracking()
            .Select(item => new FlatProjection(item.Id, item.Name, item.IsActive, item.SortOrder, item.Projects.Any()))
            .ToListAsync(cancellationToken);
        var lineDirectorates = await _db.LineDirectorates.AsNoTracking()
            .Select(item => new FlatProjection(item.Id, item.Name, item.IsActive, item.SortOrder, item.Projects.Any()))
            .ToListAsync(cancellationToken);
        var activityTypes = await _db.ActivityTypes.AsNoTracking()
            .Select(item => new ActivityProjection(item.Id, item.Name, item.IsActive, item.Activities.Any(activity => !activity.IsDeleted)))
            .ToListAsync(cancellationToken);
        var holidays = await _db.Holidays.AsNoTracking()
            .Select(item => new HolidayProjection(item.Id, item.Date, item.Name))
            .ToListAsync(cancellationToken);
        var celebrations = await _db.Celebrations.AsNoTracking()
            .Where(item => item.DeletedUtc == null)
            .Select(item => new CelebrationProjection(item.Id, item.Name, item.Day, item.Month, item.Year))
            .ToListAsync(cancellationToken);

        var checks = new List<MasterDataIntegrityCheck>
        {
            BuildOrderCheck("project-category-order", "Project category display order", "Sibling categories must have deterministic, contiguous display positions.", "bi-diagram-3", CategoryOrderFindings(projectCategories, "Project category")),
            BuildOrderCheck("technical-category-order", "Technical category display order", "Sibling categories must have deterministic, contiguous display positions.", "bi-cpu", CategoryOrderFindings(technicalCategories, "Technical category")),
            BuildOrderCheck("project-type-order", "Project type display order", "Project types must have unique, contiguous display positions.", "bi-tags", FlatOrderFindings(projectTypes, "Project type")),
            BuildOrderCheck("sponsoring-unit-order", "Sponsoring unit display order", "Sponsoring units must have unique, contiguous display positions.", "bi-building", FlatOrderFindings(sponsoringUnits, "Sponsoring unit")),
            BuildOrderCheck("line-directorate-order", "Line directorate display order", "Line directorates must have unique, contiguous display positions.", "bi-diagram-2", FlatOrderFindings(lineDirectorates, "Line directorate")),
            BuildCheck("project-category-hierarchy", "Project category hierarchy", "Parent references must exist and the hierarchy must remain acyclic.", "bi-bezier2", HierarchyFindings(projectCategories, "Project category"), critical: true),
            BuildCheck("technical-category-hierarchy", "Technical category hierarchy", "Parent references must exist and the hierarchy must remain acyclic.", "bi-bezier2", HierarchyFindings(technicalCategories, "Technical category"), critical: true),
            BuildCheck("active-parent-state", "Category activation consistency", "An active child should not remain below an inactive parent.", "bi-node-plus", ActiveChildFindings(projectCategories, "Project category").Concat(ActiveChildFindings(technicalCategories, "Technical category")).ToArray()),
            BuildCheck("inactive-references", "Inactive records in use", "Inactive values may remain for history, but active operational records should not depend on them without review.", "bi-link-45deg", InactiveReferenceFindings(projectCategories, technicalCategories, projectTypes, sponsoringUnits, lineDirectorates, activityTypes)),
            BuildCheck("normalised-names", "Normalised name uniqueness", "Names must remain unique after trimming and case normalisation.", "bi-type", DuplicateNameFindings(projectCategories, technicalCategories, projectTypes, sponsoringUnits, lineDirectorates, activityTypes), critical: true),
            BuildCheck("holiday-dates", "Holiday dates", "Only one official holiday may be recorded for each calendar date.", "bi-calendar-week", HolidayFindings(holidays), critical: true),
            BuildCheck("celebration-dates", "Celebration annual dates", "Annual calendar records must contain a valid day and month combination.", "bi-stars", CelebrationFindings(celebrations), critical: true)
        };

        return new MasterDataIntegritySnapshot(
            checks,
            checks.Count(item => item.Status == MasterDataIntegrityStatus.Healthy),
            checks.Count(item => item.Status == MasterDataIntegrityStatus.Warning),
            checks.Count(item => item.Status == MasterDataIntegrityStatus.Critical),
            checks.Sum(item => item.Findings.Count),
            _time.UtcNow);
    }

    public async Task<AdminOperationResult> NormaliseOrderAsync(
        string checkKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = checkKey?.Trim() ?? string.Empty;
        if (!RepairableOrderChecks.Contains(normalizedKey))
        {
            return AdminOperationResult.Failure("The selected integrity check does not support an automatic repair.", "unsupported-repair");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var changed = normalizedKey switch
            {
                "project-category-order" => await NormaliseCategoryOrderAsync(
                    _db.ProjectCategories,
                    item => item.ParentId,
                    item => item.SortOrder,
                    item => item.Name,
                    item => item.ParentId,
                    item => item.SortOrder,
                    (item, value) => item.SortOrder = value,
                    cancellationToken),
                "technical-category-order" => await NormaliseCategoryOrderAsync(
                    _db.TechnicalCategories,
                    item => item.ParentId,
                    item => item.SortOrder,
                    item => item.Name,
                    item => item.ParentId,
                    item => item.SortOrder,
                    (item, value) => item.SortOrder = value,
                    cancellationToken),
                "project-type-order" => await NormaliseFlatOrderAsync(
                    _db.ProjectTypes, item => item.SortOrder, item => item.Name,
                    item => item.SortOrder, (item, value) => item.SortOrder = value, cancellationToken),
                "sponsoring-unit-order" => await NormaliseFlatOrderAsync(
                    _db.SponsoringUnits, item => item.SortOrder, item => item.Name,
                    item => item.SortOrder, (item, value) => item.SortOrder = value, cancellationToken),
                "line-directorate-order" => await NormaliseFlatOrderAsync(
                    _db.LineDirectorates, item => item.SortOrder, item => item.Name,
                    item => item.SortOrder, (item, value) => item.SortOrder = value, cancellationToken),
                _ => 0
            };

            await _db.SaveChangesAsync(cancellationToken);
            await _audit.RecordAsync(new AdminAuditEntry(
                "MasterData.IntegrityOrderNormalised",
                "MasterDataIntegrity",
                normalizedKey,
                Before: new { Check = normalizedKey },
                After: new { Check = normalizedKey, RecordsChanged = changed },
                Reason: "Deterministic display-order repair",
                Message: changed == 0
                    ? "Display order was already normalised."
                    : $"Normalised display order for {changed} record(s)."), cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return AdminOperationResult.Success(
                changed == 0
                    ? "No display-order changes were required."
                    : $"Normalised display order for {changed} record(s).");
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AdminOperationResult.Failure("The data changed during the repair. Refresh the integrity review and try again.", "concurrency");
        }
    }

    private static MasterDataIntegrityCheck BuildOrderCheck(
        string key,
        string title,
        string description,
        string icon,
        IReadOnlyList<MasterDataIntegrityFinding> findings) =>
        BuildCheck(key, title, description, icon, findings, critical: false, canRepair: findings.Count > 0, repairLabel: "Normalise order");

    private static MasterDataIntegrityCheck BuildCheck(
        string key,
        string title,
        string description,
        string icon,
        IReadOnlyList<MasterDataIntegrityFinding> findings,
        bool critical = false,
        bool canRepair = false,
        string? repairLabel = null) =>
        new(
            key,
            title,
            description,
            icon,
            findings.Count == 0
                ? MasterDataIntegrityStatus.Healthy
                : critical ? MasterDataIntegrityStatus.Critical : MasterDataIntegrityStatus.Warning,
            findings,
            canRepair,
            repairLabel);

    private static IReadOnlyList<MasterDataIntegrityFinding> CategoryOrderFindings(
        IReadOnlyList<CategoryProjection> items,
        string label)
    {
        var findings = new List<MasterDataIntegrityFinding>();
        foreach (var group in items.GroupBy(item => item.ParentId))
        {
            var ordered = group.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                if (ordered[index].SortOrder != index)
                {
                    findings.Add(new MasterDataIntegrityFinding(
                        "non-contiguous-order",
                        $"{label} '{ordered[index].Name}' has display order {ordered[index].SortOrder + 1}; expected {index + 1} within its sibling group.",
                        label,
                        ordered[index].Id.ToString(),
                        ordered[index].Name));
                }
            }
        }
        return findings;
    }

    private static IReadOnlyList<MasterDataIntegrityFinding> FlatOrderFindings(
        IReadOnlyList<FlatProjection> items,
        string label)
    {
        var ordered = items.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).ToArray();
        return ordered.Select((item, index) => (item, index))
            .Where(pair => pair.item.SortOrder != pair.index)
            .Select(pair => new MasterDataIntegrityFinding(
                "non-contiguous-order",
                $"{label} '{pair.item.Name}' has display order {pair.item.SortOrder + 1}; expected {pair.index + 1}.",
                label,
                pair.item.Id.ToString(),
                pair.item.Name))
            .ToArray();
    }

    private static IReadOnlyList<MasterDataIntegrityFinding> HierarchyFindings(
        IReadOnlyList<CategoryProjection> items,
        string label)
    {
        var findings = new List<MasterDataIntegrityFinding>();
        var byId = items.ToDictionary(item => item.Id);

        foreach (var item in items)
        {
            if (item.ParentId is int parentId && !byId.ContainsKey(parentId))
            {
                findings.Add(new MasterDataIntegrityFinding(
                    "missing-parent",
                    $"{label} '{item.Name}' references missing parent ID {parentId}.",
                    label,
                    item.Id.ToString(),
                    item.Name));
                continue;
            }

            var path = new HashSet<int>();
            var current = item;
            while (current.ParentId is int currentParent && byId.TryGetValue(currentParent, out var parent))
            {
                if (!path.Add(parent.Id))
                {
                    findings.Add(new MasterDataIntegrityFinding(
                        "hierarchy-cycle",
                        $"{label} '{item.Name}' participates in a hierarchy cycle.",
                        label,
                        item.Id.ToString(),
                        item.Name));
                    break;
                }
                current = parent;
            }
        }

        return findings.DistinctBy(item => $"{item.Code}:{item.RecordId}").ToArray();
    }

    private static IReadOnlyList<MasterDataIntegrityFinding> ActiveChildFindings(
        IReadOnlyList<CategoryProjection> items,
        string label)
    {
        var byId = items.ToDictionary(item => item.Id);
        return items.Where(item => item.IsActive
                && item.ParentId is int parentId
                && byId.TryGetValue(parentId, out var parent)
                && !parent.IsActive)
            .Select(item => new MasterDataIntegrityFinding(
                "active-child-inactive-parent",
                $"Active {label.ToLowerInvariant()} '{item.Name}' is below an inactive parent.",
                label,
                item.Id.ToString(),
                item.Name))
            .ToArray();
    }

    private static IReadOnlyList<MasterDataIntegrityFinding> InactiveReferenceFindings(
        IReadOnlyList<CategoryProjection> projectCategories,
        IReadOnlyList<CategoryProjection> technicalCategories,
        IReadOnlyList<FlatProjection> projectTypes,
        IReadOnlyList<FlatProjection> sponsoringUnits,
        IReadOnlyList<FlatProjection> lineDirectorates,
        IReadOnlyList<ActivityProjection> activityTypes)
    {
        var findings = new List<MasterDataIntegrityFinding>();
        findings.AddRange(InactiveReferences(projectCategories, "Project category"));
        findings.AddRange(InactiveReferences(technicalCategories, "Technical category"));
        findings.AddRange(InactiveReferences(projectTypes, "Project type"));
        findings.AddRange(InactiveReferences(sponsoringUnits, "Sponsoring unit"));
        findings.AddRange(InactiveReferences(lineDirectorates, "Line directorate"));
        findings.AddRange(activityTypes.Where(item => !item.IsActive && item.IsReferenced)
            .Select(item => new MasterDataIntegrityFinding(
                "inactive-referenced",
                $"Inactive activity type '{item.Name}' remains referenced by operational activity records.",
                "Activity type",
                item.Id.ToString(),
                item.Name)));
        return findings;
    }

    private static IEnumerable<MasterDataIntegrityFinding> InactiveReferences<T>(IEnumerable<T> items, string label)
        where T : IReferenceProjection =>
        items.Where(item => !item.IsActive && item.IsReferenced)
            .Select(item => new MasterDataIntegrityFinding(
                "inactive-referenced",
                $"Inactive {label.ToLowerInvariant()} '{item.Name}' remains referenced.",
                label,
                item.Id.ToString(),
                item.Name));

    private static IReadOnlyList<MasterDataIntegrityFinding> DuplicateNameFindings(
        IReadOnlyList<CategoryProjection> projectCategories,
        IReadOnlyList<CategoryProjection> technicalCategories,
        IReadOnlyList<FlatProjection> projectTypes,
        IReadOnlyList<FlatProjection> sponsoringUnits,
        IReadOnlyList<FlatProjection> lineDirectorates,
        IReadOnlyList<ActivityProjection> activityTypes)
    {
        var findings = new List<MasterDataIntegrityFinding>();
        findings.AddRange(DuplicateCategoryNames(projectCategories, "Project category"));
        findings.AddRange(DuplicateCategoryNames(technicalCategories, "Technical category"));
        findings.AddRange(DuplicateFlatNames(projectTypes, "Project type"));
        findings.AddRange(DuplicateFlatNames(sponsoringUnits, "Sponsoring unit"));
        findings.AddRange(DuplicateFlatNames(lineDirectorates, "Line directorate"));
        findings.AddRange(activityTypes.GroupBy(item => NormalizeName(item.Name), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 && group.Count() > 1)
            .Select(group => new MasterDataIntegrityFinding(
                "duplicate-normalised-name",
                $"Activity type names normalise to the same value: {string.Join(", ", group.Select(item => item.Name))}.",
                "Activity type")));
        return findings;
    }

    private static IEnumerable<MasterDataIntegrityFinding> DuplicateCategoryNames(
        IEnumerable<CategoryProjection> items,
        string label) =>
        items.GroupBy(item => new { item.ParentId, Name = NormalizeName(item.Name) })
            .Where(group => group.Key.Name.Length > 0 && group.Count() > 1)
            .Select(group => new MasterDataIntegrityFinding(
                "duplicate-normalised-name",
                $"{label} names under the same parent normalise to the same value: {string.Join(", ", group.Select(item => item.Name))}.",
                label));

    private static IEnumerable<MasterDataIntegrityFinding> DuplicateFlatNames(
        IEnumerable<FlatProjection> items,
        string label) =>
        items.GroupBy(item => NormalizeName(item.Name), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 && group.Count() > 1)
            .Select(group => new MasterDataIntegrityFinding(
                "duplicate-normalised-name",
                $"{label} names normalise to the same value: {string.Join(", ", group.Select(item => item.Name))}.",
                label));

    private static IReadOnlyList<MasterDataIntegrityFinding> HolidayFindings(
        IReadOnlyList<HolidayProjection> holidays) =>
        holidays.GroupBy(item => item.Date)
            .Where(group => group.Count() > 1)
            .Select(group => new MasterDataIntegrityFinding(
                "duplicate-holiday-date",
                $"Multiple holidays are recorded on {group.Key:dd MMM yyyy}: {string.Join(", ", group.Select(item => item.Name))}.",
                "Holiday"))
            .ToArray();

    private static IReadOnlyList<MasterDataIntegrityFinding> CelebrationFindings(
        IReadOnlyList<CelebrationProjection> celebrations)
    {
        var findings = new List<MasterDataIntegrityFinding>();
        foreach (var item in celebrations)
        {
            var year = item.Year is >= 1 and <= 9999 ? item.Year.Value : (short)2000;
            if (item.Month is < 1 or > 12 || item.Day < 1 || item.Day > DateTime.DaysInMonth(year, item.Month))
            {
                findings.Add(new MasterDataIntegrityFinding(
                    "invalid-celebration-date",
                    $"Celebration '{item.Name}' has invalid annual date {item.Day}/{item.Month}.",
                    "Celebration",
                    item.Id.ToString(),
                    item.Name));
            }
        }
        return findings;
    }

    private async Task<int> NormaliseCategoryOrderAsync<T>(
        DbSet<T> set,
        System.Linq.Expressions.Expression<Func<T, int?>> parentSelector,
        System.Linq.Expressions.Expression<Func<T, int>> sortSelector,
        System.Linq.Expressions.Expression<Func<T, string>> nameSelector,
        Func<T, int?> getParent,
        Func<T, int> getSort,
        Action<T, int> setSort,
        CancellationToken cancellationToken)
        where T : class
    {
        var items = await set.OrderBy(parentSelector)
            .ThenBy(sortSelector)
            .ThenBy(nameSelector)
            .ThenBy(item => EF.Property<int>(item, "Id"))
            .ToListAsync(cancellationToken);

        var changed = 0;
        foreach (var group in items.GroupBy(getParent))
        {
            var position = 0;
            foreach (var item in group)
            {
                if (getSort(item) != position)
                {
                    setSort(item, position);
                    changed++;
                }
                position++;
            }
        }
        return changed;
    }

    private async Task<int> NormaliseFlatOrderAsync<T>(
        DbSet<T> set,
        System.Linq.Expressions.Expression<Func<T, int>> sortSelector,
        System.Linq.Expressions.Expression<Func<T, string>> nameSelector,
        Func<T, int> getSort,
        Action<T, int> setSort,
        CancellationToken cancellationToken)
        where T : class
    {
        var items = await set.OrderBy(sortSelector)
            .ThenBy(nameSelector)
            .ThenBy(item => EF.Property<int>(item, "Id"))
            .ToListAsync(cancellationToken);
        var changed = 0;
        for (var index = 0; index < items.Count; index++)
        {
            if (getSort(items[index]) != index)
            {
                setSort(items[index], index);
                changed++;
            }
        }
        return changed;
    }

    private static string NormalizeName(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private interface IReferenceProjection
    {
        int Id { get; }
        string Name { get; }
        bool IsActive { get; }
        bool IsReferenced { get; }
    }

    private sealed record CategoryProjection(int Id, string Name, int? ParentId, bool IsActive, int SortOrder, bool IsReferenced) : IReferenceProjection;
    private sealed record FlatProjection(int Id, string Name, bool IsActive, int SortOrder, bool IsReferenced) : IReferenceProjection;
    private sealed record ActivityProjection(int Id, string Name, bool IsActive, bool IsReferenced) : IReferenceProjection;
    private sealed record HolidayProjection(int Id, DateOnly Date, string Name);
    private sealed record CelebrationProjection(Guid Id, string Name, byte Day, byte Month, short? Year);
}
