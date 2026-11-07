using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services.Admin;

using ProjectManagement.Models.Scheduling;

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

    /// <summary>
    /// Runs one display-order check on its own, so a list page can say when its own order has
    /// gaps or repeats. Returns null for a key that is not an order check.
    /// </summary>
    async Task<MasterDataIntegrityCheck?> InspectOrderAsync(string checkKey, CancellationToken cancellationToken = default)
    {
        var snapshot = await InspectAsync(cancellationToken);
        return snapshot.Checks.FirstOrDefault(check =>
            string.Equals(check.Key, checkKey?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class AdminMasterDataIntegrityService : IAdminMasterDataIntegrityService
{
    private static readonly HashSet<string> RepairableOrderChecks = new(StringComparer.OrdinalIgnoreCase)
    {
        "project-category-order",
        "technical-category-order",
        "project-type-order",
        "sponsoring-unit-order",
        "line-directorate-order",
        DocumentCategoryOrderKey,
        OfficeCategoryOrderKey
    };

    public const string ProjectTypeOrderKey = "project-type-order";
    public const string SponsoringUnitOrderKey = "sponsoring-unit-order";
    public const string LineDirectorateOrderKey = "line-directorate-order";
    public const string DocumentCategoryOrderKey = "document-category-order";
    public const string OfficeCategoryOrderKey = "office-category-order";

    // The admin lists store positions from 0 and show them from 1. The Document Repository
    // lists store and show the same number, so their first position is 1.
    private static readonly IReadOnlyDictionary<string, FlatOrderDefinition> FlatOrderDefinitions =
        new Dictionary<string, FlatOrderDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [ProjectTypeOrderKey] = new("Project type order", "Project types should be numbered 1, 2, 3… with no gaps or repeats, so dropdowns list them in a fixed order.", "bi-tags", "Project type", 0),
            [SponsoringUnitOrderKey] = new("Sponsoring unit order", "Sponsoring units should be numbered 1, 2, 3… with no gaps or repeats, so dropdowns list them in a fixed order.", "bi-building", "Sponsoring unit", 0),
            [LineDirectorateOrderKey] = new("Line directorate order", "Line directorates should be numbered 1, 2, 3… with no gaps or repeats, so dropdowns list them in a fixed order.", "bi-diagram-2", "Line directorate", 0),
            [DocumentCategoryOrderKey] = new("Document type order", "Document types in the Document Repository should be numbered 1, 2, 3… with no gaps or repeats, so lists show them in a fixed order.", "bi-folder2", "Document type", 1),
            [OfficeCategoryOrderKey] = new("Office order", "Offices in the Document Repository should be numbered 1, 2, 3… with no gaps or repeats, so lists show them in a fixed order.", "bi-building", "Office", 1)
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
        var documentCategories = await LoadDocumentCategoriesAsync(cancellationToken);
        var officeCategories = await LoadOfficeCategoriesAsync(cancellationToken);
        var activityTypes = await _db.ActivityTypes.AsNoTracking()
            .Select(item => new ActivityProjection(item.Id, item.Name, item.IsActive, item.Activities.Any(activity => !activity.IsDeleted)))
            .ToListAsync(cancellationToken);
        var holidays = await _db.Holidays.AsNoTracking()
            .Select(item => new HolidayProjection(item.Id, item.Date, item.Name, item.Type, item.IsObservedAsOfficeHoliday))
            .ToListAsync(cancellationToken);
        var celebrations = await _db.Celebrations.AsNoTracking()
            .Where(item => item.DeletedUtc == null)
            .Select(item => new CelebrationProjection(item.Id, item.Name, item.Day, item.Month, item.Year))
            .ToListAsync(cancellationToken);

        var checks = new List<MasterDataIntegrityCheck>
        {
            BuildOrderCheck("project-category-order", "Project category order", "Categories under the same parent should be numbered 1, 2, 3… with no gaps or repeats, so dropdowns list them in a fixed order.", "bi-diagram-3", CategoryOrderFindings(projectCategories, "Project category")),
            BuildOrderCheck("technical-category-order", "Technical category order", "Categories under the same parent should be numbered 1, 2, 3… with no gaps or repeats, so dropdowns list them in a fixed order.", "bi-cpu", CategoryOrderFindings(technicalCategories, "Technical category")),
            BuildFlatOrderCheck(ProjectTypeOrderKey, projectTypes),
            BuildFlatOrderCheck(SponsoringUnitOrderKey, sponsoringUnits),
            BuildFlatOrderCheck(LineDirectorateOrderKey, lineDirectorates),
            BuildFlatOrderCheck(DocumentCategoryOrderKey, documentCategories),
            BuildFlatOrderCheck(OfficeCategoryOrderKey, officeCategories),
            BuildCheck("project-category-hierarchy", "Project category parents", "Every parent category must exist, and a category cannot sit inside itself.", "bi-bezier2", HierarchyFindings(projectCategories, "Project category"), critical: true),
            BuildCheck("technical-category-hierarchy", "Technical category parents", "Every parent category must exist, and a category cannot sit inside itself.", "bi-bezier2", HierarchyFindings(technicalCategories, "Technical category"), critical: true),
            BuildCheck("active-parent-state", "Categories under deactivated parents", "A category that is still offered should not sit under a parent that was deactivated.", "bi-node-plus", ActiveChildFindings(projectCategories, "Project category").Concat(ActiveChildFindings(technicalCategories, "Technical category")).ToArray()),
            BuildCheck("inactive-references", "Deactivated values still in use", "Old records may keep a deactivated value, but current records should be moved to an active one.", "bi-link-45deg", InactiveReferenceFindings(projectCategories, technicalCategories, projectTypes, sponsoringUnits, lineDirectorates, activityTypes)),
            BuildCheck("normalised-names", "Duplicate names", "No two values in a list should have the same name, ignoring capitals and extra spaces.", "bi-type", DuplicateNameFindings(projectCategories, technicalCategories, projectTypes, sponsoringUnits, lineDirectorates, activityTypes), critical: true),
            BuildCheck("holiday-dates", "Holidays", "No repeated holiday names, at most one gazetted holiday per date, and every gazetted holiday observed by the office.", "bi-calendar-week", HolidayFindings(holidays), critical: true),
            BuildCheck("celebration-dates", "Celebration dates", "Every birthday and anniversary must have a real day and month.", "bi-stars", CelebrationFindings(celebrations), critical: true)
        };

        return new MasterDataIntegritySnapshot(
            checks,
            checks.Count(item => item.Status == MasterDataIntegrityStatus.Healthy),
            checks.Count(item => item.Status == MasterDataIntegrityStatus.Warning),
            checks.Count(item => item.Status == MasterDataIntegrityStatus.Critical),
            checks.Sum(item => item.Findings.Count),
            _time.UtcNow);
    }

    public async Task<MasterDataIntegrityCheck?> InspectOrderAsync(
        string checkKey,
        CancellationToken cancellationToken = default)
    {
        var key = (checkKey?.Trim() ?? string.Empty).ToLowerInvariant();
        if (!RepairableOrderChecks.Contains(key))
        {
            return null;
        }

        if (!FlatOrderDefinitions.ContainsKey(key))
        {
            // Category order is checked per parent; the full inspection already does that.
            var snapshot = await InspectAsync(cancellationToken);
            return snapshot.Checks.FirstOrDefault(check => check.Key == key);
        }

        var items = key switch
        {
            ProjectTypeOrderKey => await _db.ProjectTypes.AsNoTracking()
                .Select(item => new FlatProjection(item.Id, item.Name, item.IsActive, item.SortOrder, false))
                .ToListAsync(cancellationToken),
            SponsoringUnitOrderKey => await _db.SponsoringUnits.AsNoTracking()
                .Select(item => new FlatProjection(item.Id, item.Name, item.IsActive, item.SortOrder, false))
                .ToListAsync(cancellationToken),
            LineDirectorateOrderKey => await _db.LineDirectorates.AsNoTracking()
                .Select(item => new FlatProjection(item.Id, item.Name, item.IsActive, item.SortOrder, false))
                .ToListAsync(cancellationToken),
            DocumentCategoryOrderKey => await LoadDocumentCategoriesAsync(cancellationToken),
            _ => await LoadOfficeCategoriesAsync(cancellationToken)
        };

        return BuildFlatOrderCheck(key, items);
    }

    public async Task<AdminOperationResult> NormaliseOrderAsync(
        string checkKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = (checkKey?.Trim() ?? string.Empty).ToLowerInvariant();
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
                DocumentCategoryOrderKey => await NormaliseFlatOrderAsync(
                    _db.DocumentCategories, item => item.SortOrder, item => item.Name,
                    item => item.SortOrder, (item, value) => item.SortOrder = value, cancellationToken,
                    firstPosition: 1),
                OfficeCategoryOrderKey => await NormaliseFlatOrderAsync(
                    _db.OfficeCategories, item => item.SortOrder, item => item.Name,
                    item => item.SortOrder, (item, value) => item.SortOrder = value, cancellationToken,
                    firstPosition: 1),
                _ => 0
            };

            // Record each row's old and new position, so the repair can be traced and reversed
            // from the audit log (it used to record only the check and a count).
            var moves = _db.ChangeTracker.Entries()
                .Where(entry => entry.State == EntityState.Modified)
                .Select(entry => entry.Property("SortOrder"))
                .Where(property => property.IsModified)
                .Select(property => new
                {
                    Entity = property.EntityEntry.Metadata.ClrType.Name,
                    Id = property.EntityEntry.Property("Id").CurrentValue,
                    From = property.OriginalValue,
                    To = property.CurrentValue
                })
                .ToArray();

            await _db.SaveChangesAsync(cancellationToken);
            await _audit.RecordAsync(new AdminAuditEntry(
                "MasterData.IntegrityOrderNormalised",
                "MasterDataIntegrity",
                normalizedKey,
                Before: new { Check = normalizedKey, Positions = moves.Select(move => new { move.Entity, move.Id, SortOrder = move.From }) },
                After: new { Check = normalizedKey, RecordsChanged = changed, Positions = moves.Select(move => new { move.Entity, move.Id, SortOrder = move.To }) },
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

    private static MasterDataIntegrityCheck BuildFlatOrderCheck(
        string key,
        IReadOnlyList<FlatProjection> items)
    {
        var definition = FlatOrderDefinitions[key];
        return BuildOrderCheck(
            key,
            definition.Title,
            definition.Description,
            definition.Icon,
            FlatOrderFindings(items, definition.Label, definition.FirstPosition));
    }

    private Task<List<FlatProjection>> LoadDocumentCategoriesAsync(CancellationToken cancellationToken) =>
        _db.DocumentCategories.AsNoTracking()
            .Select(item => new FlatProjection(item.Id, item.Name, item.IsActive, item.SortOrder, false))
            .ToListAsync(cancellationToken);

    private Task<List<FlatProjection>> LoadOfficeCategoriesAsync(CancellationToken cancellationToken) =>
        _db.OfficeCategories.AsNoTracking()
            .Select(item => new FlatProjection(item.Id, item.Name, item.IsActive, item.SortOrder, false))
            .ToListAsync(cancellationToken);

    private static MasterDataIntegrityCheck BuildOrderCheck(
        string key,
        string title,
        string description,
        string icon,
        IReadOnlyList<MasterDataIntegrityFinding> findings) =>
        BuildCheck(key, title, description, icon, findings, critical: false, canRepair: findings.Count > 0, repairLabel: "Renumber 1, 2, 3…");

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
        string label,
        int firstPosition = 0)
    {
        var ordered = items.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).ToArray();
        return ordered.Select((item, index) => (item, index))
            .Where(pair => pair.item.SortOrder != pair.index + firstPosition)
            .Select(pair => new MasterDataIntegrityFinding(
                "non-contiguous-order",
                $"{label} '{pair.item.Name}' has display order {pair.item.SortOrder - firstPosition + 1}; expected {pair.index + 1}.",
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
        IReadOnlyList<HolidayProjection> holidays)
    {
        var findings = new List<MasterDataIntegrityFinding>();

        findings.AddRange(holidays
            .GroupBy(item => new
            {
                item.Date,
                item.Type,
                Name = item.Name.Trim().ToUpperInvariant()
            })
            .Where(group => group.Count() > 1)
            .Select(group => new MasterDataIntegrityFinding(
                "duplicate-holiday-entry",
                $"Duplicate {group.Key.Type} holiday entry '{group.First().Name}' is recorded on {group.Key.Date:dd MMM yyyy}.",
                "Holiday")));

        findings.AddRange(holidays
            .Where(item => item.Type == HolidayType.Gazetted)
            .GroupBy(item => item.Date)
            .Where(group => group.Count() > 1)
            .Select(group => new MasterDataIntegrityFinding(
                "multiple-gazetted-holidays",
                $"More than one Gazetted Holiday is recorded on {group.Key:dd MMM yyyy}: {string.Join(", ", group.Select(item => item.Name))}.",
                "Holiday")));

        findings.AddRange(holidays
            .Where(item => item.Type == HolidayType.Gazetted && !item.IsObservedAsOfficeHoliday)
            .Select(item => new MasterDataIntegrityFinding(
                "gazetted-not-observed",
                $"Gazetted Holiday '{item.Name}' is not marked as an office holiday.",
                "Holiday",
                item.Id.ToString(),
                item.Name)));

        return findings;
    }

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
        CancellationToken cancellationToken,
        int firstPosition = 0)
        where T : class
    {
        // Same order the list pages show (position, then name, then Id), so the repair only
        // closes gaps and splits repeats; it never reshuffles what people see.
        var items = await set.OrderBy(sortSelector)
            .ThenBy(nameSelector)
            .ThenBy(item => EF.Property<int>(item, "Id"))
            .ToListAsync(cancellationToken);
        var changed = 0;
        for (var index = 0; index < items.Count; index++)
        {
            var position = index + firstPosition;
            if (getSort(items[index]) != position)
            {
                setSort(items[index], position);
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
    private sealed record FlatOrderDefinition(string Title, string Description, string Icon, string Label, int FirstPosition);
    private sealed record ActivityProjection(int Id, string Name, bool IsActive, bool IsReferenced) : IReferenceProjection;
    private sealed record HolidayProjection(int Id, DateOnly Date, string Name, HolidayType Type, bool IsObservedAsOfficeHoliday);
    private sealed record CelebrationProjection(Guid Id, string Name, byte Day, byte Month, short? Year);
}
