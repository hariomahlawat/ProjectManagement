using System.Globalization;
using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Projects;

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
    public const string ProjectCategories = "project-categories";
    public const string TechnicalCategories = "technical-categories";
    public const string ActivityTypes = "activity-types";
    public const string ArppCfa = "arpp-cfa";
    public const string ArppFunds = "arpp-funds";
    public const string ArppDfpdsSchedules = "arpp-dfpds-schedules";
    public const string DocumentTypes = "document-types";
    public const string DocumentOffices = "document-offices";
    public const string SocialMediaEventTypes = "social-media-event-types";
    public const string SocialMediaPlatforms = "social-media-platforms";
}

/// <summary>
/// One kind of record that uses a list item, such as projects. An item is in use when any of
/// its list's sources names it. A source gives either a query the database runs
/// (<see cref="ReferencingIds"/>) or, for ids the database cannot read (inside JSON, or in the
/// application's settings), a loader that reads them in memory (<see cref="LoadReferencingIds"/>).
/// Either way it yields one id per record that uses the item.
/// </summary>
public class ReferenceListUsageSource<TKey> where TKey : struct
{
    public ReferenceListUsageSource(string one, string many, Func<ApplicationDbContext, IQueryable<TKey>> referencingIds)
    {
        One = one;
        Many = many;
        ReferencingIds = referencingIds ?? throw new ArgumentNullException(nameof(referencingIds));
    }

    public ReferenceListUsageSource(string one, string many, Func<ApplicationDbContext, CancellationToken, Task<IReadOnlyList<TKey>>> loadReferencingIds)
    {
        ArgumentNullException.ThrowIfNull(loadReferencingIds);
        One = one;
        Many = many;
        LoadReferencingIds = (db, _, cancellationToken) => loadReferencingIds(db, cancellationToken);
    }

    /// <summary>A loader that also reads the application's services (the repository's settings, for example).</summary>
    public ReferenceListUsageSource(string one, string many, Func<ApplicationDbContext, IServiceProvider?, CancellationToken, Task<IReadOnlyList<TKey>>> loadReferencingIds)
    {
        One = one;
        Many = many;
        LoadReferencingIds = loadReferencingIds ?? throw new ArgumentNullException(nameof(loadReferencingIds));
    }

    /// <summary>The record in the singular, for "1 project".</summary>
    public string One { get; }

    /// <summary>The record in the plural, for "3 projects".</summary>
    public string Many { get; }

    public Func<ApplicationDbContext, IQueryable<TKey>>? ReferencingIds { get; }

    public Func<ApplicationDbContext, IServiceProvider?, CancellationToken, Task<IReadOnlyList<TKey>>>? LoadReferencingIds { get; }
}

/// <summary>A usage source of a list keyed by whole numbers (most lists).</summary>
public sealed class ReferenceListUsageSource : ReferenceListUsageSource<int>
{
    public ReferenceListUsageSource(string one, string many, Func<ApplicationDbContext, IQueryable<int>> referencingIds)
        : base(one, many, referencingIds)
    {
    }

    public ReferenceListUsageSource(string one, string many, Func<ApplicationDbContext, CancellationToken, Task<IReadOnlyList<int>>> loadReferencingIds)
        : base(one, many, loadReferencingIds)
    {
    }

    public ReferenceListUsageSource(string one, string many, Func<ApplicationDbContext, IServiceProvider?, CancellationToken, Task<IReadOnlyList<int>>> loadReferencingIds)
        : base(one, many, loadReferencingIds)
    {
    }
}

/// <summary>
/// A column a list shows between Name and Order, such as a code or a parent.
/// </summary>
public sealed record ReferenceListExtraColumn(string Header);

/// <summary>What a list's delete hooks get: the item being deleted and who is deleting it.</summary>
public sealed record ReferenceListDeleteContext(
    ApplicationDbContext Db,
    IServiceProvider? Services,
    ClaimsPrincipal User,
    string Id,
    string Name,
    int SortOrder);

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
        string policy)
    {
        Key = key;
        Label = label;
        PluralLabel = pluralLabel;
        EntityType = entityType;
        DeleteAuditAction = deleteAuditAction;
        Policy = policy;
    }

    public string Key { get; }

    /// <summary>The item in lower case, for sentences: "project type".</summary>
    public string Label { get; }

    /// <summary>The list's name: "Project types".</summary>
    public string PluralLabel { get; }

    /// <summary>The list's name inside a sentence: "project types", but "CFA values" and "DFPDS schedules".</summary>
    public string PluralInSentence => InSentence(PluralLabel);

    /// <summary>Lower-cases the first letter unless the first word is an acronym (CFA, DFPDS).</summary>
    public static string InSentence(string text) =>
        text.Length > 1 && char.IsUpper(text[1]) ? text : text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    public string EntityType { get; }
    public string DeleteAuditAction { get; }
    public string Policy { get; }
    public string AuditOrigin { get; init; } = "Admin.MasterData";

    /// <summary>
    /// False for a list whose module keeps its own audit trail (the Document Repository); its
    /// <see cref="KeyedReferenceListDefinition{TKey}.OnDeletingAsync"/> writes the entry instead.
    /// </summary>
    public bool WritesAdminAudit { get; init; } = true;

    /// <summary>Columns shown between Name and Order. None for the plain lists.</summary>
    public IReadOnlyList<ReferenceListExtraColumn> ExtraColumns { get; init; } = Array.Empty<ReferenceListExtraColumn>();

    /// <summary>
    /// The column that holds an item's parent, for a list kept as a tree (categories). The page
    /// then lists parents before their children, indented, and the order is kept per parent.
    /// </summary>
    public string? ParentProperty { get; init; }

    public bool IsTree => ParentProperty is not null;

    /// <summary>False for a list with no stored order (activity types are listed A to Z).</summary>
    public bool HasOrder { get; init; } = true;

    /// <summary>
    /// True when the order is a position kept 0, 1, 2… (shown from 1) and closed up after a
    /// delete; false when people type the order number themselves (the ARPP values).
    /// </summary>
    public bool OrderIsPosition { get; init; } = true;

    /// <summary>The usage-and-delete rule for this list, typed by the list's key.</summary>
    internal abstract IReferenceListEngine CreateEngine(ReferenceListServices services);
}

/// <summary>A reference list whose items are keyed by <typeparamref name="TKey"/> (a number or a Guid).</summary>
public abstract class KeyedReferenceListDefinition<TKey> : ReferenceListDefinition where TKey : struct
{
    protected KeyedReferenceListDefinition(
        string key,
        string label,
        string pluralLabel,
        string entityType,
        string deleteAuditAction,
        string policy,
        IReadOnlyList<ReferenceListUsageSource<TKey>> usedBy)
        : base(key, label, pluralLabel, entityType, deleteAuditAction, policy)
    {
        if (usedBy.Count == 0)
        {
            throw new ArgumentException("A reference list needs at least one usage source.", nameof(usedBy));
        }

        UsedBy = usedBy;
    }

    public IReadOnlyList<ReferenceListUsageSource<TKey>> UsedBy { get; }

    /// <summary>Loads the extra column values for the rows on the page, in column order.</summary>
    public Func<ApplicationDbContext, IReadOnlyCollection<TKey>, CancellationToken, Task<IReadOnlyDictionary<TKey, IReadOnlyList<string?>>>>? LoadExtraColumns { get; init; }

    internal override IReferenceListEngine CreateEngine(ReferenceListServices services) =>
        new ReferenceListEngine<TKey>(this, services);

    internal abstract IQueryable<ReferenceListItemProjection<TKey>> Items(ApplicationDbContext db);
    internal abstract Task<object?> FindAsync(ApplicationDbContext db, TKey id, CancellationToken cancellationToken);
    internal abstract Task<IReadOnlyList<object>> LoadOrderedAfterAsync(ApplicationDbContext db, int sortOrder, TKey? parentId, CancellationToken cancellationToken);
    internal abstract void Remove(ApplicationDbContext db, object entity);

    /// <summary>Locks the item's row (PostgreSQL FOR UPDATE) inside the caller's transaction.</summary>
    internal abstract Task LockAsync(ApplicationDbContext db, TKey id, CancellationToken cancellationToken);

    /// <summary>Whether a delete must carry the row's version (false where the database keeps none).</summary>
    internal virtual bool RequiresRowVersion(ApplicationDbContext db) => true;

    /// <summary>
    /// Makes the delete conditional on the version the page showed. False when the page was out
    /// of date (the row has changed since).
    /// </summary>
    internal virtual bool ApplyRowVersion(EntityEntry entry, byte[] rowVersion)
    {
        entry.Property("RowVersion").OriginalValue = rowVersion;
        return true;
    }

    /// <summary>Runs in the delete's transaction just before it is saved, for a module's own audit entry.</summary>
    internal virtual Task OnDeletingAsync(ReferenceListDeleteContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The key as a form posts it.</summary>
    internal static string Format(TKey id) => Convert.ToString(id, CultureInfo.InvariantCulture)!;

    internal static bool TryParse(string? value, out TKey id)
    {
        id = default;
        if (typeof(TKey) == typeof(int) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            id = (TKey)(object)number;
            return true;
        }

        if (typeof(TKey) == typeof(Guid) && Guid.TryParse(value, out var guid))
        {
            id = (TKey)(object)guid;
            return true;
        }

        return false;
    }

    /// <summary>item =&gt; item.Id == id, with the id sent as a query parameter.</summary>
    internal static Expression<Func<ReferenceListItemProjection<TKey>, bool>> IdIs(TKey id)
    {
        var item = Expression.Parameter(typeof(ReferenceListItemProjection<TKey>), "item");
        var holder = new KeyHolder(id);
        var body = Expression.Equal(
            Expression.Property(item, nameof(ReferenceListItemProjection<TKey>.Id)),
            Expression.Property(Expression.Constant(holder), nameof(KeyHolder.Value)));
        return Expression.Lambda<Func<ReferenceListItemProjection<TKey>, bool>>(body, item);
    }

    private sealed class KeyHolder
    {
        public KeyHolder(TKey value) => Value = value;

        public TKey Value { get; }
    }
}

/// <summary>A list item as the list reads it, before use is counted.</summary>
public class ReferenceListItemProjection<TKey> where TKey : struct
{
    public TKey Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public byte[] RowVersion { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// The row's version where the table has no RowVersion column and PostgreSQL's built-in xmin
    /// stands in for it (the Document Repository lists). Null elsewhere.
    /// </summary>
    public uint? Xmin { get; init; }

    /// <summary>The parent's id, for a list kept as a tree.</summary>
    public TKey? ParentId { get; init; }

    /// <summary>A short line under the name, such as a description.</summary>
    public string? Detail { get; init; }

    /// <summary>The version as the page carries it (base64).</summary>
    internal string RowVersionText => Convert.ToBase64String(Xmin is uint xmin ? BitConverter.GetBytes(xmin) : RowVersion ?? Array.Empty<byte>());
}

/// <summary>A list item keyed by a whole number (most lists).</summary>
public sealed class ReferenceListItemProjection : ReferenceListItemProjection<int>
{
}

/// <summary>
/// A reference list kept in one table with Id, IsActive and RowVersion columns. By default it
/// also has Name and SortOrder columns; a list whose columns differ (a code instead of a name,
/// no stored order, a description, a parent) gives its own projection.
/// </summary>
public class ReferenceListDefinition<TEntity, TKey> : KeyedReferenceListDefinition<TKey>
    where TEntity : class
    where TKey : struct
{
    private readonly Func<ApplicationDbContext, DbSet<TEntity>> _set;
    private readonly Expression<Func<TEntity, ReferenceListItemProjection<TKey>>> _project;

    public ReferenceListDefinition(
        string key,
        string label,
        string pluralLabel,
        string entityType,
        string deleteAuditAction,
        string policy,
        Func<ApplicationDbContext, DbSet<TEntity>> set,
        IReadOnlyList<ReferenceListUsageSource<TKey>> usedBy,
        Expression<Func<TEntity, ReferenceListItemProjection<TKey>>>? project = null)
        : base(key, label, pluralLabel, entityType, deleteAuditAction, policy, usedBy)
    {
        _set = set;
        _project = project ?? (item => new ReferenceListItemProjection<TKey>
        {
            Id = EF.Property<TKey>(item, "Id"),
            Name = EF.Property<string>(item, "Name"),
            IsActive = EF.Property<bool>(item, "IsActive"),
            SortOrder = EF.Property<int>(item, "SortOrder"),
            RowVersion = EF.Property<byte[]>(item, "RowVersion"),
            ParentId = null,
            Detail = null
        });
    }

    protected DbSet<TEntity> Set(ApplicationDbContext db) => _set(db);

    internal override IQueryable<ReferenceListItemProjection<TKey>> Items(ApplicationDbContext db) =>
        _set(db).AsNoTracking().Select(_project);

    internal override async Task<object?> FindAsync(ApplicationDbContext db, TKey id, CancellationToken cancellationToken) =>
        await _set(db).FindAsync(new object[] { id }, cancellationToken);

    internal override async Task<IReadOnlyList<object>> LoadOrderedAfterAsync(ApplicationDbContext db, int sortOrder, TKey? parentId, CancellationToken cancellationToken)
    {
        var later = _set(db).Where(item => EF.Property<int>(item, "SortOrder") > sortOrder);
        if (ParentProperty is { } parent)
        {
            // The order is kept per parent: only the item's own siblings close up.
            var item = Expression.Parameter(typeof(TEntity), "item");
            var column = Expression.Call(typeof(EF), nameof(EF.Property), new[] { typeof(TKey?) }, item, Expression.Constant(parent));
            var sameParent = Expression.Equal(column, Expression.Constant(parentId, typeof(TKey?)));
            later = later.Where(Expression.Lambda<Func<TEntity, bool>>(sameParent, item));
        }

        return await later.ToListAsync(cancellationToken);
    }

    internal override void Remove(ApplicationDbContext db, object entity) => _set(db).Remove((TEntity)entity);

    internal override async Task LockAsync(ApplicationDbContext db, TKey id, CancellationToken cancellationToken)
    {
        // Names come from the EF model, never from input; the id is a parameter.
        var entityType = db.Model.FindEntityType(typeof(TEntity))!;
        var table = entityType.GetTableName()!;
        var schema = entityType.GetSchema();
        var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, schema);
        var idColumn = entityType.FindProperty("Id")!.GetColumnName(store)!;
        var qualified = schema is null ? Quote(table) : $"{Quote(schema)}.{Quote(table)}";
        await db.Database.ExecuteSqlRawAsync(
            $"SELECT 1 FROM {qualified} WHERE {Quote(idColumn)} = {{0}} FOR UPDATE",
            new object[] { id },
            cancellationToken);
    }

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
}

/// <summary>A reference list keyed by whole numbers (most lists).</summary>
public sealed class ReferenceListDefinition<TEntity> : ReferenceListDefinition<TEntity, int> where TEntity : class
{
    public ReferenceListDefinition(
        string key,
        string label,
        string pluralLabel,
        string entityType,
        string deleteAuditAction,
        string policy,
        Func<ApplicationDbContext, DbSet<TEntity>> set,
        IReadOnlyList<ReferenceListUsageSource<int>> usedBy,
        Expression<Func<TEntity, ReferenceListItemProjection<int>>>? project = null)
        : base(key, label, pluralLabel, entityType, deleteAuditAction, policy, set, usedBy, project)
    {
    }
}

/// <summary>
/// The Document Repository's lists (document types and offices). They have no RowVersion
/// column: on PostgreSQL the row's built-in xmin is the version (no migration). They keep their
/// own audit trail (DocRepoAudits), as their add, rename and switch-off already do.
/// </summary>
public sealed class DocRepoReferenceListDefinition<TEntity> : ReferenceListDefinition<TEntity, int>
    where TEntity : class, IDocRepoListEntry
{
    private readonly string _auditKind;

    public DocRepoReferenceListDefinition(
        string key,
        string label,
        string pluralLabel,
        string entityType,
        string auditKind,
        Func<ApplicationDbContext, DbSet<TEntity>> set,
        IReadOnlyList<ReferenceListUsageSource<int>> usedBy)
        : base(key, label, pluralLabel, entityType, "DocRepo.CategoryChanged", Policies.Documents.ManageCategories, set, usedBy)
    {
        _auditKind = auditKind;
        WritesAdminAudit = false;

        // People type the order number (-999 to 9999); it is shown as typed, and a delete leaves
        // the rest alone. Data checks offers Renumber for gaps and repeats.
        OrderIsPosition = false;
        AuditOrigin = "DocumentRepository.Admin";
    }

    private static bool HasXmin(ApplicationDbContext db) =>
        db.Model.FindEntityType(typeof(TEntity))?.FindProperty(DocRepoListService.VersionProperty) is not null;

    internal override IQueryable<ReferenceListItemProjection<int>> Items(ApplicationDbContext db)
    {
        var items = Set(db).AsNoTracking();
        return HasXmin(db)
            ? items.Select(item => new ReferenceListItemProjection<int>
            {
                Id = item.Id,
                Name = item.Name,
                IsActive = item.IsActive,
                SortOrder = item.SortOrder,
                Xmin = EF.Property<uint>(item, DocRepoListService.VersionProperty),
                // Bound explicitly so a search on Detail translates (Codex review on #3019).
                ParentId = null,
                Detail = null
            })
            : items.Select(item => new ReferenceListItemProjection<int>
            {
                Id = item.Id,
                Name = item.Name,
                IsActive = item.IsActive,
                SortOrder = item.SortOrder,
                ParentId = null,
                Detail = null
            });
    }

    // Databases without xmin (the test databases) keep no version, as before.
    internal override bool RequiresRowVersion(ApplicationDbContext db) => HasXmin(db);

    internal override bool ApplyRowVersion(EntityEntry entry, byte[] rowVersion)
    {
        if (entry.Metadata.FindProperty(DocRepoListService.VersionProperty) is null)
        {
            return true;
        }

        if (rowVersion.Length != sizeof(uint))
        {
            return false;
        }

        var expected = BitConverter.ToUInt32(rowVersion);
        var property = entry.Property(DocRepoListService.VersionProperty);
        if (property.CurrentValue is uint current && current != expected)
        {
            return false;
        }

        property.OriginalValue = expected;
        return true;
    }

    internal override Task OnDeletingAsync(ReferenceListDeleteContext context, CancellationToken cancellationToken)
    {
        var audit = context.Services?.GetService(typeof(IDocRepoAuditService)) as IDocRepoAuditService
            ?? throw new InvalidOperationException("The repository audit service is not available.");
        var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.Identity?.Name ?? "system";
        audit.Stage(null, actor, DocRepoAuditEventTypes.CategoryChanged,
            new { kind = _auditKind, action = "Deleted", Id = int.Parse(context.Id, CultureInfo.InvariantCulture), context.Name, context.SortOrder });
        return Task.CompletedTask;
    }
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

    // Project change requests keep the item they started from in a column and the item they
    // propose inside their JSON payload; neither has a foreign key. Every request counts, decided
    // ones too, because their history shows the names.
    private static ReferenceListUsageSource ChangeRequests(
        Func<ChangeRequestIds, int?> original,
        Func<ProjectMetaChangeRequestPayload, int?> proposed,
        Func<ChangeRequestIds, int?>? proposedColumn = null) => new(
        "change request", "change requests",
        async (db, cancellationToken) =>
        {
            var rows = await db.ProjectMetaChangeRequests.AsNoTracking()
                .Select(request => new ChangeRequestIds
                {
                    ProjectTypeId = request.OriginalProjectTypeId,
                    SponsoringUnitId = request.OriginalSponsoringUnitId,
                    LineDirectorateId = request.OriginalSponsoringLineDirectorateId,
                    CategoryId = request.OriginalCategoryId,
                    TechnicalCategoryId = request.OriginalTechnicalCategoryId,
                    ProposedTechnicalCategoryId = request.TechnicalCategoryId,
                    Payload = request.Payload
                })
                .ToListAsync(cancellationToken);
            var ids = new List<int>();
            foreach (var row in rows)
            {
                var named = new HashSet<int>();
                if (original(row) is int start) named.Add(start);
                if (proposedColumn?.Invoke(row) is int column) named.Add(column);
                if (ReadPayload(row.Payload) is { } payload && proposed(payload) is int target) named.Add(target);
                ids.AddRange(named);
            }

            return ids;
        });

    private sealed class ChangeRequestIds
    {
        public int? ProjectTypeId { get; init; }
        public int? SponsoringUnitId { get; init; }
        public int? LineDirectorateId { get; init; }
        public int? CategoryId { get; init; }
        public int? TechnicalCategoryId { get; init; }

        /// <summary>A request also keeps the technical category it proposes in a column, with a foreign key.</summary>
        public int? ProposedTechnicalCategoryId { get; init; }

        public string? Payload { get; init; }
    }

    private static ProjectMetaChangeRequestPayload? ReadPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ProjectMetaChangeRequestPayload>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static readonly ReferenceListUsageSource ChangeRequestsByType =
        ChangeRequests(row => row.ProjectTypeId, payload => payload.ProjectTypeId);

    private static readonly ReferenceListUsageSource ChangeRequestsBySponsoringUnit =
        ChangeRequests(row => row.SponsoringUnitId, payload => payload.SponsoringUnitId);

    private static readonly ReferenceListUsageSource ChangeRequestsByLineDirectorate =
        ChangeRequests(row => row.LineDirectorateId, payload => payload.SponsoringLineDirectorateId);

    // Every project row counts, archived and deleted ones too: the database's foreign key
    // refuses a delete while any project row points at the item.
    public static ReferenceListDefinition ProjectTypes { get; } = new ReferenceListDefinition<ProjectType>(
        ReferenceListKeys.ProjectTypes, "project type", "Project types", "ProjectType",
        "MasterData.ProjectTypeDeleted", AdminPolicies.MasterDataManage,
        db => db.ProjectTypes, new[] { ProjectsByType, ChangeRequestsByType });

    public static ReferenceListDefinition SponsoringUnits { get; } = new ReferenceListDefinition<SponsoringUnit>(
        ReferenceListKeys.SponsoringUnits, "sponsoring unit", "Sponsoring units", "SponsoringUnit",
        "MasterData.SponsoringUnitDeleted", AdminPolicies.MasterDataManage,
        db => db.SponsoringUnits, new[] { ProjectsBySponsoringUnit, ChangeRequestsBySponsoringUnit });

    public static ReferenceListDefinition LineDirectorates { get; } = new ReferenceListDefinition<LineDirectorate>(
        ReferenceListKeys.LineDirectorates, "line directorate", "Line directorates", "LineDirectorate",
        "MasterData.LineDirectorateDeleted", AdminPolicies.MasterDataManage,
        db => db.LineDirectorates, new[] { ProjectsByLineDirectorate, ChangeRequestsByLineDirectorate });

    // PR 11: categories (kept as trees), activity types and the ARPP values.

    private static readonly ReferenceListUsageSource ProjectsByCategory = new(
        "project", "projects",
        db => db.Projects.Where(project => project.CategoryId != null).Select(project => project.CategoryId!.Value));

    private static readonly ReferenceListUsageSource ProjectsByTechnicalCategory = new(
        "project", "projects",
        db => db.Projects.Where(project => project.TechnicalCategoryId != null).Select(project => project.TechnicalCategoryId!.Value));

    // A category with sub-categories cannot go: the database's foreign key keeps the children.
    private static readonly ReferenceListUsageSource ProjectSubCategories = new(
        "sub-category", "sub-categories",
        db => db.ProjectCategories.Where(category => category.ParentId != null).Select(category => category.ParentId!.Value));

    private static readonly ReferenceListUsageSource TechnicalSubCategories = new(
        "sub-category", "sub-categories",
        db => db.TechnicalCategories.Where(category => category.ParentId != null).Select(category => category.ParentId!.Value));

    private static readonly ReferenceListUsageSource LegacyImportsByCategory = new(
        "legacy import", "legacy imports",
        db => db.ProjectLegacyImports.Select(import => import.ProjectCategoryId));

    private static readonly ReferenceListUsageSource LegacyImportsByTechnicalCategory = new(
        "legacy import", "legacy imports",
        db => db.ProjectLegacyImports.Select(import => import.TechnicalCategoryId));

    private static readonly ReferenceListUsageSource ChangeRequestsByCategory =
        ChangeRequests(row => row.CategoryId, payload => payload.CategoryId);

    private static readonly ReferenceListUsageSource ChangeRequestsByTechnicalCategory =
        ChangeRequests(row => row.TechnicalCategoryId, payload => payload.TechnicalCategoryId, row => row.ProposedTechnicalCategoryId);

    // Every activity row counts, deleted ones too: the foreign key refuses the delete.
    private static readonly ReferenceListUsageSource ActivitiesByType = new(
        "activity", "activities",
        db => db.Activities.Select(activity => activity.ActivityTypeId));

    // An ARPP entry's link is emptied when its value is deleted, so the database would not stop
    // the delete: this count is the only guard. Every entry counts.
    private static readonly ReferenceListUsageSource ArppEntriesByCfa = new(
        "ARPP entry", "ARPP entries",
        db => db.ArppEntries.Where(entry => entry.CfaOptionId != null).Select(entry => entry.CfaOptionId!.Value));

    private static readonly ReferenceListUsageSource ArppEntriesByFund = new(
        "ARPP entry", "ARPP entries",
        db => db.ArppEntries.Where(entry => entry.FundOptionId != null).Select(entry => entry.FundOptionId!.Value));

    private static readonly ReferenceListUsageSource ArppEntriesBySchedule = new(
        "ARPP entry", "ARPP entries",
        db => db.ArppEntries.Where(entry => entry.DfpdsScheduleId != null).Select(entry => entry.DfpdsScheduleId!.Value));

    public static ReferenceListDefinition ProjectCategories { get; } = new ReferenceListDefinition<ProjectCategory>(
        ReferenceListKeys.ProjectCategories, "project category", "Project categories", "ProjectCategory",
        "MasterData.ProjectCategoryDeleted", AdminPolicies.MasterDataManage,
        db => db.ProjectCategories,
        new[] { ProjectsByCategory, ProjectSubCategories, LegacyImportsByCategory, ChangeRequestsByCategory },
        item => new ReferenceListItemProjection<int>
        {
            Id = item.Id,
            Name = item.Name,
            IsActive = item.IsActive,
            SortOrder = item.SortOrder,
            RowVersion = item.RowVersion,
            ParentId = item.ParentId
        })
    {
        ParentProperty = nameof(ProjectCategory.ParentId)
    };

    public static ReferenceListDefinition TechnicalCategories { get; } = new ReferenceListDefinition<TechnicalCategory>(
        ReferenceListKeys.TechnicalCategories, "technical category", "Technical categories", "TechnicalCategory",
        "MasterData.TechnicalCategoryDeleted", AdminPolicies.MasterDataManage,
        db => db.TechnicalCategories,
        new[] { ProjectsByTechnicalCategory, TechnicalSubCategories, LegacyImportsByTechnicalCategory, ChangeRequestsByTechnicalCategory },
        item => new ReferenceListItemProjection<int>
        {
            Id = item.Id,
            Name = item.Name,
            IsActive = item.IsActive,
            SortOrder = item.SortOrder,
            RowVersion = item.RowVersion,
            ParentId = item.ParentId
        })
    {
        ParentProperty = nameof(TechnicalCategory.ParentId)
    };

    public static ReferenceListDefinition ActivityTypes { get; } = new ReferenceListDefinition<ActivityType>(
        ReferenceListKeys.ActivityTypes, "activity type", "Activity types", "ActivityType",
        "MasterData.ActivityTypeDeleted", AdminPolicies.ActivityTypesManage,
        db => db.ActivityTypes,
        new[] { ActivitiesByType },
        item => new ReferenceListItemProjection<int>
        {
            Id = item.Id,
            Name = item.Name,
            IsActive = item.IsActive,
            SortOrder = 0,
            RowVersion = item.RowVersion,
            Detail = item.Description
        })
    {
        HasOrder = false
    };

    public static ReferenceListDefinition ArppCfa { get; } = new ReferenceListDefinition<ArppCfaOption>(
        ReferenceListKeys.ArppCfa, "CFA", "CFA values", "ArppCfaOption",
        "MasterData.ArppReferenceDeleted", AdminPolicies.MasterDataManage,
        db => db.ArppCfaOptions,
        new[] { ArppEntriesByCfa })
    {
        OrderIsPosition = false
    };

    public static ReferenceListDefinition ArppFunds { get; } = new ReferenceListDefinition<ArppFundOption>(
        ReferenceListKeys.ArppFunds, "fund", "Funds", "ArppFundOption",
        "MasterData.ArppReferenceDeleted", AdminPolicies.MasterDataManage,
        db => db.ArppFundOptions,
        new[] { ArppEntriesByFund })
    {
        OrderIsPosition = false
    };

    public static ReferenceListDefinition ArppDfpdsSchedules { get; } = new ReferenceListDefinition<ArppDfpdsSchedule>(
        ReferenceListKeys.ArppDfpdsSchedules, "DFPDS schedule", "DFPDS schedules", "ArppDfpdsSchedule",
        "MasterData.ArppReferenceDeleted", AdminPolicies.MasterDataManage,
        db => db.ArppDfpdsSchedules,
        new[] { ArppEntriesBySchedule },
        item => new ReferenceListItemProjection<int>
        {
            Id = item.Id,
            Name = item.Code,
            IsActive = item.IsActive,
            SortOrder = item.SortOrder,
            RowVersion = item.RowVersion,
            Detail = item.Description
        })
    {
        OrderIsPosition = false
    };

    // PR 12: the Document Repository's lists and the social media lists.

    // Every document counts, trashed ones too: they still point at the value, can be restored,
    // and the database would delete them with it (a cascading foreign key).
    private static readonly ReferenceListUsageSource DocumentsByType = new(
        "document", "documents",
        db => db.Documents.Select(document => document.DocumentCategoryId));

    private static readonly ReferenceListUsageSource DocumentsByOffice = new(
        "document", "documents",
        db => db.Documents.Select(document => document.OfficeCategoryId));

    // Automatic ingestion (uploads from activities, ARPP and FFC) files new PDFs under the
    // configured type and office by id; deleting either would make every later import fail.
    private static ReferenceListUsageSource IngestionSetting(Func<DocRepoOptions, int?> configured) => new(
        "setting for imported PDFs", "settings for imported PDFs",
        (db, services, cancellationToken) =>
        {
            var options = (services?.GetService(typeof(Microsoft.Extensions.Options.IOptions<DocRepoOptions>))
                as Microsoft.Extensions.Options.IOptions<DocRepoOptions>)?.Value;
            IReadOnlyList<int> ids = options is not null && configured(options) is int id ? new[] { id } : Array.Empty<int>();
            return Task.FromResult(ids);
        });

    public static ReferenceListDefinition DocumentTypes { get; } = new DocRepoReferenceListDefinition<DocumentCategory>(
        ReferenceListKeys.DocumentTypes, "document type", "Repository document types", "DocumentCategory", "document",
        db => db.DocumentCategories,
        new[] { DocumentsByType, IngestionSetting(options => options.IngestionDocumentCategoryId) });

    public static ReferenceListDefinition DocumentOffices { get; } = new DocRepoReferenceListDefinition<OfficeCategory>(
        ReferenceListKeys.DocumentOffices, "office", "Repository offices", "OfficeCategory", "office",
        db => db.OfficeCategories,
        new[] { DocumentsByOffice, IngestionSetting(options => options.IngestionOfficeCategoryId) });

    // Every event counts, deleted ones too: the foreign key refuses the delete.
    private static readonly ReferenceListUsageSource<Guid> EventsByType = new(
        "event", "events",
        db => db.SocialMediaEvents.Select(item => item.SocialMediaEventTypeId));

    private static readonly ReferenceListUsageSource<Guid> EventsByPlatform = new(
        "event", "events",
        db => db.SocialMediaEvents.Select(item => item.SocialMediaPlatformId));

    // The social media lists have no stored order (listed A to Z) and keep a description.
    public static ReferenceListDefinition SocialMediaEventTypes { get; } = new ReferenceListDefinition<SocialMediaEventType, Guid>(
        ReferenceListKeys.SocialMediaEventTypes, "event type", "Social media event types", "SocialMediaEventType",
        "ProjectOfficeReports.SocialMediaEventTypeDeleted", AdminPolicies.SocialMediaListsManage,
        db => db.SocialMediaEventTypes,
        new[] { EventsByType },
        item => new ReferenceListItemProjection<Guid>
        {
            Id = item.Id,
            Name = item.Name,
            IsActive = item.IsActive,
            SortOrder = 0,
            RowVersion = item.RowVersion,
            Detail = item.Description
        })
    {
        HasOrder = false,
        AuditOrigin = "Admin.SocialMediaLists"
    };

    public static ReferenceListDefinition SocialMediaPlatforms { get; } = new ReferenceListDefinition<SocialMediaPlatform, Guid>(
        ReferenceListKeys.SocialMediaPlatforms, "platform", "Social media platforms", "SocialMediaPlatform",
        "ProjectOfficeReports.SocialMediaPlatformDeleted", AdminPolicies.SocialMediaListsManage,
        db => db.SocialMediaPlatforms,
        new[] { EventsByPlatform },
        item => new ReferenceListItemProjection<Guid>
        {
            Id = item.Id,
            Name = item.Name,
            IsActive = item.IsActive,
            SortOrder = 0,
            RowVersion = item.RowVersion,
            Detail = item.Description
        })
    {
        HasOrder = false,
        AuditOrigin = "Admin.SocialMediaLists"
    };

    public static IReadOnlyList<ReferenceListDefinition> All { get; } = new[]
    {
        ProjectTypes, SponsoringUnits, LineDirectorates,
        ProjectCategories, TechnicalCategories, ActivityTypes,
        ArppCfa, ArppFunds, ArppDfpdsSchedules,
        DocumentTypes, DocumentOffices,
        SocialMediaEventTypes, SocialMediaPlatforms
    };

    public static ReferenceListDefinition? Find(string key) =>
        All.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
}
