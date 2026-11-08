using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;
using ProjectManagement.Models.Arpp;
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
}

/// <summary>
/// One kind of record that uses a list item, such as projects. An item is in use when any of
/// its list's sources names it. A source gives either a query the database runs
/// (<see cref="ReferencingIds"/>) or, for ids the database cannot read (inside JSON), a loader
/// that reads them in memory (<see cref="LoadReferencingIds"/>). Either way it yields one id per
/// record that uses the item.
/// </summary>
public sealed class ReferenceListUsageSource
{
    public ReferenceListUsageSource(string one, string many, Func<ApplicationDbContext, IQueryable<int>> referencingIds)
    {
        One = one;
        Many = many;
        ReferencingIds = referencingIds ?? throw new ArgumentNullException(nameof(referencingIds));
    }

    public ReferenceListUsageSource(string one, string many, Func<ApplicationDbContext, CancellationToken, Task<IReadOnlyList<int>>> loadReferencingIds)
    {
        One = one;
        Many = many;
        LoadReferencingIds = loadReferencingIds ?? throw new ArgumentNullException(nameof(loadReferencingIds));
    }

    /// <summary>The record in the singular, for "1 project".</summary>
    public string One { get; }

    /// <summary>The record in the plural, for "3 projects".</summary>
    public string Many { get; }

    public Func<ApplicationDbContext, IQueryable<int>>? ReferencingIds { get; }

    public Func<ApplicationDbContext, CancellationToken, Task<IReadOnlyList<int>>>? LoadReferencingIds { get; }
}

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

    /// <summary>The list's name inside a sentence: "project types", but "CFA values" and "DFPDS schedules".</summary>
    public string PluralInSentence => InSentence(PluralLabel);

    /// <summary>Lower-cases the first letter unless the first word is an acronym (CFA, DFPDS).</summary>
    public static string InSentence(string text) =>
        text.Length > 1 && char.IsUpper(text[1]) ? text : text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    public string EntityType { get; }
    public string DeleteAuditAction { get; }
    public string Policy { get; }
    public string AuditOrigin { get; init; } = "Admin.MasterData";
    public IReadOnlyList<ReferenceListUsageSource> UsedBy { get; }

    /// <summary>Columns shown between Name and Order. None for the plain lists.</summary>
    public IReadOnlyList<ReferenceListExtraColumn> ExtraColumns { get; init; } = Array.Empty<ReferenceListExtraColumn>();

    /// <summary>Loads the extra column values for the rows on the page, in column order.</summary>
    public Func<ApplicationDbContext, IReadOnlyCollection<int>, CancellationToken, Task<IReadOnlyDictionary<int, IReadOnlyList<string?>>>>? LoadExtraColumns { get; init; }

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

    internal abstract IQueryable<ReferenceListItemProjection> Items(ApplicationDbContext db);
    internal abstract Task<object?> FindAsync(ApplicationDbContext db, int id, CancellationToken cancellationToken);
    internal abstract Task<IReadOnlyList<object>> LoadOrderedAfterAsync(ApplicationDbContext db, int sortOrder, int? parentId, CancellationToken cancellationToken);
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

    /// <summary>The parent's id, for a list kept as a tree.</summary>
    public int? ParentId { get; init; }

    /// <summary>A short line under the name, such as a description.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// A reference list kept in one table with Id, IsActive and RowVersion columns. By default it
/// also has Name and SortOrder columns; a list whose columns differ (a code instead of a name,
/// no stored order, a description, a parent) gives its own projection.
/// </summary>
public sealed class ReferenceListDefinition<TEntity> : ReferenceListDefinition where TEntity : class
{
    private readonly Func<ApplicationDbContext, DbSet<TEntity>> _set;
    private readonly Expression<Func<TEntity, ReferenceListItemProjection>> _project;

    public ReferenceListDefinition(
        string key,
        string label,
        string pluralLabel,
        string entityType,
        string deleteAuditAction,
        string policy,
        Func<ApplicationDbContext, DbSet<TEntity>> set,
        IReadOnlyList<ReferenceListUsageSource> usedBy,
        Expression<Func<TEntity, ReferenceListItemProjection>>? project = null)
        : base(key, label, pluralLabel, entityType, deleteAuditAction, policy, usedBy)
    {
        _set = set;
        _project = project ?? (item => new ReferenceListItemProjection
        {
            Id = EF.Property<int>(item, "Id"),
            Name = EF.Property<string>(item, "Name"),
            IsActive = EF.Property<bool>(item, "IsActive"),
            SortOrder = EF.Property<int>(item, "SortOrder"),
            RowVersion = EF.Property<byte[]>(item, "RowVersion"),
            ParentId = null,
            Detail = null
        });
    }

    internal override IQueryable<ReferenceListItemProjection> Items(ApplicationDbContext db) =>
        _set(db).AsNoTracking().Select(_project);

    internal override async Task<object?> FindAsync(ApplicationDbContext db, int id, CancellationToken cancellationToken) =>
        await _set(db).SingleOrDefaultAsync(item => EF.Property<int>(item, "Id") == id, cancellationToken);

    internal override async Task<IReadOnlyList<object>> LoadOrderedAfterAsync(ApplicationDbContext db, int sortOrder, int? parentId, CancellationToken cancellationToken)
    {
        var later = _set(db).Where(item => EF.Property<int>(item, "SortOrder") > sortOrder);
        if (ParentProperty is { } parent)
        {
            // The order is kept per parent: only the item's own siblings close up.
            later = parentId is int id
                ? later.Where(item => EF.Property<int?>(item, parent) == id)
                : later.Where(item => EF.Property<int?>(item, parent) == null);
        }

        return await later.ToListAsync(cancellationToken);
    }

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
        item => new ReferenceListItemProjection
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
        item => new ReferenceListItemProjection
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
        item => new ReferenceListItemProjection
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
        item => new ReferenceListItemProjection
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

    public static IReadOnlyList<ReferenceListDefinition> All { get; } = new[]
    {
        ProjectTypes, SponsoringUnits, LineDirectorates,
        ProjectCategories, TechnicalCategories, ActivityTypes,
        ArppCfa, ArppFunds, ArppDfpdsSchedules
    };

    public static ReferenceListDefinition? Find(string key) =>
        All.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
}
