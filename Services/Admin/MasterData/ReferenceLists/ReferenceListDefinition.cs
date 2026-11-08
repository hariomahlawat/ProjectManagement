using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Admin.MasterData.ReferenceLists;

/// <summary>
/// The keys of the reference lists on the shared list page (docs/ux/admin-review-3-fixes.md,
/// owner decision 1, PRs 10-12).
/// </summary>
public static class ReferenceListKeys
{
    public const string ProjectTypes = "project-types";
    public const string SponsoringUnits = "sponsoring-units";
    public const string LineDirectorates = "line-directorates";
}

/// <summary>
/// One kind of record that uses a list item, such as projects. An item is in use when any of
/// its list's sources names it.
/// </summary>
/// <param name="One">The record in the singular, for "Used by 1 project".</param>
/// <param name="Many">The record in the plural, for "Used by 3 projects".</param>
/// <param name="ReferencingIds">The list item ids the records point at, one per record.</param>
public sealed record ReferenceListUsageSource(
    string One,
    string Many,
    Func<ApplicationDbContext, IQueryable<int>> ReferencingIds);

/// <summary>
/// A column a list shows between Name and Order, such as a code or a parent.
/// </summary>
public sealed record ReferenceListExtraColumn(string Header);

/// <summary>
/// What the shared list page needs to know about one reference list. The rule for use and
/// Delete lives in <see cref="ReferenceListService"/>; a definition only says which table the
/// list is and which records use it.
/// </summary>
public abstract class ReferenceListDefinition
{
    protected ReferenceListDefinition(
        string key,
        string label,
        string pluralLabel,
        string entityType,
        string deleteAuditAction,
        string policy,
        IReadOnlyList<ReferenceListUsageSource> usedBy)
    {
        if (usedBy.Count == 0)
        {
            throw new ArgumentException("A reference list needs at least one usage source.", nameof(usedBy));
        }

        Key = key;
        Label = label;
        PluralLabel = pluralLabel;
        EntityType = entityType;
        DeleteAuditAction = deleteAuditAction;
        Policy = policy;
        UsedBy = usedBy;
    }

    public string Key { get; }

    /// <summary>The item in lower case, for sentences: "project type".</summary>
    public string Label { get; }

    /// <summary>The list's name: "Project types".</summary>
    public string PluralLabel { get; }

    public string EntityType { get; }
    public string DeleteAuditAction { get; }
    public string Policy { get; }
    public string AuditOrigin { get; init; } = "Admin.MasterData";
    public IReadOnlyList<ReferenceListUsageSource> UsedBy { get; }

    /// <summary>Columns shown between Name and Order. None for the plain lists.</summary>
    public IReadOnlyList<ReferenceListExtraColumn> ExtraColumns { get; init; } = Array.Empty<ReferenceListExtraColumn>();

    /// <summary>Loads the extra column values for the rows on the page, in column order.</summary>
    public Func<ApplicationDbContext, IReadOnlyCollection<int>, CancellationToken, Task<IReadOnlyDictionary<int, IReadOnlyList<string?>>>>? LoadExtraColumns { get; init; }

    internal abstract IQueryable<ReferenceListItemProjection> Items(ApplicationDbContext db);
    internal abstract Task<object?> FindAsync(ApplicationDbContext db, int id, CancellationToken cancellationToken);
    internal abstract Task<IReadOnlyList<object>> LoadOrderedAfterAsync(ApplicationDbContext db, int sortOrder, CancellationToken cancellationToken);
    internal abstract void Remove(ApplicationDbContext db, object entity);
}

/// <summary>A list item as the list reads it, before use is counted.</summary>
public sealed class ReferenceListItemProjection
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public byte[] RowVersion { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// A reference list kept in one table with Id, Name, IsActive, SortOrder and RowVersion
/// columns.
/// </summary>
public sealed class ReferenceListDefinition<TEntity> : ReferenceListDefinition where TEntity : class
{
    private readonly Func<ApplicationDbContext, DbSet<TEntity>> _set;

    public ReferenceListDefinition(
        string key,
        string label,
        string pluralLabel,
        string entityType,
        string deleteAuditAction,
        string policy,
        Func<ApplicationDbContext, DbSet<TEntity>> set,
        IReadOnlyList<ReferenceListUsageSource> usedBy)
        : base(key, label, pluralLabel, entityType, deleteAuditAction, policy, usedBy)
    {
        _set = set;
    }

    internal override IQueryable<ReferenceListItemProjection> Items(ApplicationDbContext db) =>
        _set(db).AsNoTracking().Select(item => new ReferenceListItemProjection
        {
            Id = EF.Property<int>(item, "Id"),
            Name = EF.Property<string>(item, "Name"),
            IsActive = EF.Property<bool>(item, "IsActive"),
            SortOrder = EF.Property<int>(item, "SortOrder"),
            RowVersion = EF.Property<byte[]>(item, "RowVersion")
        });

    internal override async Task<object?> FindAsync(ApplicationDbContext db, int id, CancellationToken cancellationToken) =>
        await _set(db).SingleOrDefaultAsync(item => EF.Property<int>(item, "Id") == id, cancellationToken);

    internal override async Task<IReadOnlyList<object>> LoadOrderedAfterAsync(ApplicationDbContext db, int sortOrder, CancellationToken cancellationToken) =>
        await _set(db).Where(item => EF.Property<int>(item, "SortOrder") > sortOrder).ToListAsync(cancellationToken);

    internal override void Remove(ApplicationDbContext db, object entity) => _set(db).Remove((TEntity)entity);
}

/// <summary>The reference lists on the shared list page.</summary>
public static class ReferenceListCatalog
{
    private static readonly ReferenceListUsageSource ProjectsByType = new(
        "project", "projects",
        db => db.Projects.Where(project => project.ProjectTypeId != null).Select(project => project.ProjectTypeId!.Value));

    private static readonly ReferenceListUsageSource ProjectsBySponsoringUnit = new(
        "project", "projects",
        db => db.Projects.Where(project => project.SponsoringUnitId != null).Select(project => project.SponsoringUnitId!.Value));

    private static readonly ReferenceListUsageSource ProjectsByLineDirectorate = new(
        "project", "projects",
        db => db.Projects.Where(project => project.SponsoringLineDirectorateId != null).Select(project => project.SponsoringLineDirectorateId!.Value));

    // Every project row counts, archived and deleted ones too: the database's foreign key
    // refuses a delete while any project row points at the item.
    public static ReferenceListDefinition ProjectTypes { get; } = new ReferenceListDefinition<ProjectType>(
        ReferenceListKeys.ProjectTypes, "project type", "Project types", "ProjectType",
        "MasterData.ProjectTypeDeleted", AdminPolicies.MasterDataManage,
        db => db.ProjectTypes, new[] { ProjectsByType });

    public static ReferenceListDefinition SponsoringUnits { get; } = new ReferenceListDefinition<SponsoringUnit>(
        ReferenceListKeys.SponsoringUnits, "sponsoring unit", "Sponsoring units", "SponsoringUnit",
        "MasterData.SponsoringUnitDeleted", AdminPolicies.MasterDataManage,
        db => db.SponsoringUnits, new[] { ProjectsBySponsoringUnit });

    public static ReferenceListDefinition LineDirectorates { get; } = new ReferenceListDefinition<LineDirectorate>(
        ReferenceListKeys.LineDirectorates, "line directorate", "Line directorates", "LineDirectorate",
        "MasterData.LineDirectorateDeleted", AdminPolicies.MasterDataManage,
        db => db.LineDirectorates, new[] { ProjectsByLineDirectorate });

    public static IReadOnlyList<ReferenceListDefinition> All { get; } = new[] { ProjectTypes, SponsoringUnits, LineDirectorates };

    public static ReferenceListDefinition? Find(string key) =>
        All.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
}
