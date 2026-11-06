using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Services.Admin.MasterData;

public enum MasterDataCategoryKind
{
    Project = 0,
    Technical = 1
}

public enum MasterDataFlatLookupKind
{
    ProjectType = 0,
    SponsoringUnit = 1,
    LineDirectorate = 2
}

public sealed record MasterDataDomainSummary(
    string Key,
    string Title,
    string Description,
    string Icon,
    string Area,
    string Page,
    int Total,
    int Active,
    int Inactive,
    int InUse,
    string Policy,
    bool TracksUsage = true,
    string Group = "",
    string UsedIn = "");

/// <summary>The groups of "Lists used in forms", in page order (docs/ux/admin-module.md, PR 6).</summary>
public static class MasterDataGroups
{
    public const string Projects = "Projects";
    public const string ActivitiesArpp = "Activities and ARPP";
    public const string DocumentsSocialMedia = "Documents and social media";
    public const string Calendar = "Calendar";

    public static IReadOnlyList<string> Ordered { get; } = new[] { Projects, ActivitiesArpp, DocumentsSocialMedia, Calendar };
}

public sealed record MasterDataRecentChange(
    long AuditId,
    string Title,
    string? Actor,
    string? Message,
    DateTimeOffset WhenUtc,
    string Icon,
    string Tone);

public sealed record MasterDataOverviewSnapshot(
    IReadOnlyList<MasterDataDomainSummary> Domains,
    IReadOnlyList<MasterDataRecentChange> RecentChanges,
    int TotalRecords,
    int ActiveRecords,
    int InactiveRecords,
    int RecordsInUse);

public sealed record CategoryDirectoryRequest(
    string? Search,
    string? Status);

public sealed record CategoryAdminRow(
    int Id,
    string Name,
    int? ParentId,
    string Path,
    int Depth,
    bool IsActive,
    int SortOrder,
    int DirectUsageCount,
    int DirectChildCount,
    int DescendantCount,
    string RowVersion,
    bool HasChildren,
    bool CanMoveUp,
    bool CanMoveDown,
    bool IsContextOnly);

public sealed record CategoryDirectoryResult(
    MasterDataCategoryKind Kind,
    IReadOnlyList<CategoryAdminRow> Rows,
    int Total,
    int Active,
    int Inactive,
    int InUse,
    int RootCount,
    string Search,
    string Status);

public sealed record FlatLookupDirectoryRequest(
    string? Search,
    string? Status,
    int Page,
    int PageSize);

public sealed record FlatLookupAdminRow(
    int Id,
    string Name,
    bool IsActive,
    int SortOrder,
    int UsageCount,
    string RowVersion,
    DateTimeOffset? UpdatedUtc,
    bool CanMoveUp,
    bool CanMoveDown);

public sealed record FlatLookupDirectoryResult(
    MasterDataFlatLookupKind Kind,
    IReadOnlyList<FlatLookupAdminRow> Rows,
    int Total,
    int Active,
    int Inactive,
    int InUse,
    int FilteredCount,
    int Page,
    int PageSize,
    int TotalPages,
    string Search,
    string Status);

public sealed record ActivityTypeDirectoryRequest(
    string? Search,
    string? Status,
    int Page,
    int PageSize);

public sealed record ActivityTypeAdminRow(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    int UsageCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? ChangedBy,
    string RowVersion);

public sealed record ActivityTypeDirectoryResult(
    IReadOnlyList<ActivityTypeAdminRow> Rows,
    int Total,
    int Active,
    int Inactive,
    int InUse,
    int FilteredCount,
    int Page,
    int PageSize,
    int TotalPages,
    string Search,
    string Status);

public sealed record CategoryImpact(
    int Id,
    string Name,
    string? ParentName,
    int DirectUsageCount,
    int DirectChildCount,
    int DescendantCount,
    bool IsActive,
    string RowVersion,
    int OtherReferenceCount = 0);

public interface IMasterDataAdministrationQueryService
{
    Task<MasterDataOverviewSnapshot> GetOverviewAsync(int holidayYear, CancellationToken cancellationToken = default);
    Task<CategoryDirectoryResult> GetCategoriesAsync(MasterDataCategoryKind kind, CategoryDirectoryRequest request, CancellationToken cancellationToken = default);
    Task<CategoryImpact?> GetCategoryImpactAsync(MasterDataCategoryKind kind, int id, CancellationToken cancellationToken = default);
    Task<FlatLookupDirectoryResult> GetFlatLookupAsync(MasterDataFlatLookupKind kind, FlatLookupDirectoryRequest request, CancellationToken cancellationToken = default);
    Task<ActivityTypeDirectoryResult> GetActivityTypesAsync(ActivityTypeDirectoryRequest request, CancellationToken cancellationToken = default);
}

public sealed class MasterDataAdministrationQueryService : IMasterDataAdministrationQueryService
{
    private const int MaximumPageSize = 100;

    private readonly ApplicationDbContext _db;
    private readonly IAuditActionPresentationCatalog _auditPresentation;

    public MasterDataAdministrationQueryService(
        ApplicationDbContext db,
        IAuditActionPresentationCatalog auditPresentation)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _auditPresentation = auditPresentation ?? throw new ArgumentNullException(nameof(auditPresentation));
    }

    public async Task<MasterDataOverviewSnapshot> GetOverviewAsync(
        int holidayYear,
        CancellationToken cancellationToken = default)
    {
        var projectCategories = await _db.ProjectCategories.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new CountSnapshot(
                group.Count(),
                group.Count(item => item.IsActive),
                group.Count(item => !item.IsActive),
                group.Count(item => item.Projects.Any())))
            .SingleOrDefaultAsync(cancellationToken) ?? CountSnapshot.Empty;

        var technicalCategories = await _db.TechnicalCategories.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new CountSnapshot(
                group.Count(),
                group.Count(item => item.IsActive),
                group.Count(item => !item.IsActive),
                group.Count(item => item.Projects.Any())))
            .SingleOrDefaultAsync(cancellationToken) ?? CountSnapshot.Empty;

        var projectTypes = await _db.ProjectTypes.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new CountSnapshot(
                group.Count(),
                group.Count(item => item.IsActive),
                group.Count(item => !item.IsActive),
                group.Count(item => item.Projects.Any())))
            .SingleOrDefaultAsync(cancellationToken) ?? CountSnapshot.Empty;

        var sponsoringUnits = await _db.SponsoringUnits.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new CountSnapshot(
                group.Count(),
                group.Count(item => item.IsActive),
                group.Count(item => !item.IsActive),
                group.Count(item => item.Projects.Any())))
            .SingleOrDefaultAsync(cancellationToken) ?? CountSnapshot.Empty;

        var lineDirectorates = await _db.LineDirectorates.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new CountSnapshot(
                group.Count(),
                group.Count(item => item.IsActive),
                group.Count(item => !item.IsActive),
                group.Count(item => item.Projects.Any())))
            .SingleOrDefaultAsync(cancellationToken) ?? CountSnapshot.Empty;

        var activityTypes = await _db.ActivityTypes.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new CountSnapshot(
                group.Count(),
                group.Count(item => item.IsActive),
                group.Count(item => !item.IsActive),
                group.Count(item => item.Activities.Any(activity => !activity.IsDeleted))))
            .SingleOrDefaultAsync(cancellationToken) ?? CountSnapshot.Empty;

        var arppCfa = await _db.ArppCfaOptions.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new CountSnapshot(
                group.Count(),
                group.Count(item => item.IsActive),
                group.Count(item => !item.IsActive),
                group.Count(item => item.Entries.Any())))
            .SingleOrDefaultAsync(cancellationToken) ?? CountSnapshot.Empty;

        var arppFunds = await _db.ArppFundOptions.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new CountSnapshot(
                group.Count(),
                group.Count(item => item.IsActive),
                group.Count(item => !item.IsActive),
                group.Count(item => item.Entries.Any())))
            .SingleOrDefaultAsync(cancellationToken) ?? CountSnapshot.Empty;

        var arppDfpds = await _db.ArppDfpdsSchedules.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new CountSnapshot(
                group.Count(),
                group.Count(item => item.IsActive),
                group.Count(item => !item.IsActive),
                group.Count(item => item.Entries.Any())))
            .SingleOrDefaultAsync(cancellationToken) ?? CountSnapshot.Empty;

        var arppReferenceData = new CountSnapshot(
            arppCfa.Total + arppFunds.Total + arppDfpds.Total,
            arppCfa.Active + arppFunds.Active + arppDfpds.Active,
            arppCfa.Inactive + arppFunds.Inactive + arppDfpds.Inactive,
            arppCfa.InUse + arppFunds.InUse + arppDfpds.InUse);

        var holidayStart = new DateOnly(holidayYear, 1, 1);
        var holidayEnd = holidayStart.AddYears(1);
        var holidayTotal = await _db.Holidays.AsNoTracking()
            .CountAsync(item => item.Date >= holidayStart && item.Date < holidayEnd, cancellationToken);
        // Holidays and celebrations are not referenced by other records, so "in use" does
        // not apply; they used to report every row as in use and inflate the total.
        var holidays = new CountSnapshot(holidayTotal, holidayTotal, 0, 0);

        var celebrationsTotal = await _db.Celebrations.AsNoTracking()
            .CountAsync(item => item.DeletedUtc == null, cancellationToken);
        var celebrations = new CountSnapshot(celebrationsTotal, celebrationsTotal, 0, 0);

        // Lists that belong to other modules but are still choices in PRISM's forms.
        var documentTypes = await CountListAsync(
            _db.DocumentCategories.AsNoTracking().Select(item => item.IsActive),
            _db.Documents.AsNoTracking().Select(item => item.DocumentCategoryId).Distinct(),
            cancellationToken);
        var offices = await CountListAsync(
            _db.OfficeCategories.AsNoTracking().Select(item => item.IsActive),
            _db.Documents.AsNoTracking().Select(item => item.OfficeCategoryId).Distinct(),
            cancellationToken);
        var socialMediaTypes = await CountListAsync(
            _db.SocialMediaEventTypes.AsNoTracking().Select(item => item.IsActive),
            _db.SocialMediaEvents.AsNoTracking().Select(item => item.SocialMediaEventTypeId).Distinct(),
            cancellationToken);
        var socialMediaPlatforms = await CountListAsync(
            _db.SocialMediaPlatforms.AsNoTracking().Select(item => item.IsActive),
            _db.SocialMediaEvents.AsNoTracking().Select(item => item.SocialMediaPlatformId).Distinct(),
            cancellationToken);

        var domains = new[]
        {
            Domain("project-categories", "Project categories", "Groups projects in dashboards and reports. A category can have sub-categories.", "bi-diagram-3", "Admin", "/Categories/Index", projectCategories, AdminPolicies.MasterDataManage,
                group: MasterDataGroups.Projects, usedIn: "Project form"),
            Domain("technical-categories", "Technical categories", "The technology area of a project.", "bi-cpu", "Admin", "/TechnicalCategories/Index", technicalCategories, AdminPolicies.MasterDataManage,
                group: MasterDataGroups.Projects, usedIn: "Project form"),
            Domain("project-types", "Project types", "The kind of project, such as build or procurement.", "bi-tags", "Admin", "/Lookups/ProjectTypes/Index", projectTypes, AdminPolicies.MasterDataManage,
                group: MasterDataGroups.Projects, usedIn: "Project form"),
            Domain("sponsoring-units", "Sponsoring units", "Units and formations that sponsor a project.", "bi-building", "Admin", "/Lookups/SponsoringUnits/Index", sponsoringUnits, AdminPolicies.MasterDataManage,
                group: MasterDataGroups.Projects, usedIn: "Project form"),
            Domain("line-directorates", "Line directorates", "The directorate linked to a project's sponsor.", "bi-diagram-2", "Admin", "/Lookups/LineDirectorates/Index", lineDirectorates, AdminPolicies.MasterDataManage,
                group: MasterDataGroups.Projects, usedIn: "Project form"),
            Domain("activity-types", "Activity types", "Kinds of activity, used in planning and reports.", "bi-list-task", "Admin", "/ActivityTypes/Index", activityTypes, AdminPolicies.ActivityTypesManage,
                group: MasterDataGroups.ActivitiesArpp, usedIn: "Activity form"),
            Domain("arpp-reference-data", "ARPP values", "The CFA, Fund and DFPDS choices offered when entering ARPP data.", "bi-list-check", "Admin", "/MasterData/ArppReferences/Index", arppReferenceData, AdminPolicies.MasterDataManage,
                group: MasterDataGroups.ActivitiesArpp, usedIn: "ARPP entry"),
            Domain("document-types", "Repository document types", "The kind of document filed in the Document Repository.", "bi-folder2", "DocumentRepository", "/Admin/DocumentCategories/Index", documentTypes, Policies.Documents.ManageCategories,
                group: MasterDataGroups.DocumentsSocialMedia, usedIn: "Document Repository upload"),
            Domain("document-offices", "Repository offices", "The office a Document Repository document belongs to.", "bi-building-gear", "DocumentRepository", "/Admin/OfficeCategories/Index", offices, Policies.Documents.ManageCategories,
                group: MasterDataGroups.DocumentsSocialMedia, usedIn: "Document Repository upload"),
            Domain("social-media-types", "Social media event types", "The kind of event a social media post is about.", "bi-megaphone", "ProjectOfficeReports", "/Admin/SocialMediaTypes/Index", socialMediaTypes, AdminPolicies.MasterDataManage,
                group: MasterDataGroups.DocumentsSocialMedia, usedIn: "Social media post form"),
            Domain("social-media-platforms", "Social media platforms", "Where a social media post was published.", "bi-share", "ProjectOfficeReports", "/Admin/SocialMediaTypes/Platforms/Index", socialMediaPlatforms, AdminPolicies.MasterDataManage,
                group: MasterDataGroups.DocumentsSocialMedia, usedIn: "Social media post form"),
            Domain("holidays", $"Holidays · {holidayYear}", "Gazetted and restricted holidays, and which ones the office observes. Stage dates skip them.", "bi-calendar-week", string.Empty, "/Settings/Holidays/Index", holidays, AdminPolicies.HolidaysManage, tracksUsage: false,
                group: MasterDataGroups.Calendar, usedIn: "Shared calendar and stage dates"),
            Domain("celebrations", "Celebrations", "Birthdays and anniversaries shown on the shared calendar.", "bi-stars", string.Empty, "/Celebrations/Index", celebrations, Policies.Calendar.ManageCelebrations, tracksUsage: false,
                group: MasterDataGroups.Calendar, usedIn: "Shared calendar")
        };

        var auditRows = await (
                from item in _db.AuditLogs.AsNoTracking()
                where item.Action.StartsWith("MasterData.")
                    || item.Action.StartsWith("Holiday")
                    || item.Action.StartsWith("GazettedHoliday")
                    || item.Action.StartsWith("RestrictedHoliday")
                    || item.Action.StartsWith("Celebration")
                join user in _db.Users on item.UserId equals user.Id into users
                from user in users.DefaultIfEmpty()
                orderby item.TimeUtc descending, item.Id descending
                select new
                {
                    item.Id,
                    item.Action,
                    item.Level,
                    // The person's name when the log kept only their id.
                    Actor = item.UserName ?? (user == null ? item.UserId : (user.FullName ?? user.UserName)),
                    item.Message,
                    item.TimeUtc
                })
            .Take(8)
            .ToListAsync(cancellationToken);

        var recentChanges = auditRows.Select(item =>
        {
            var presentation = _auditPresentation.Describe(item.Action, item.Level);
            return new MasterDataRecentChange(
                item.Id,
                presentation.Label,
                item.Actor,
                item.Message,
                new DateTimeOffset(EnsureUtc(item.TimeUtc)),
                presentation.Icon,
                presentation.Tone);
        }).ToArray();

        return new MasterDataOverviewSnapshot(
            domains,
            recentChanges,
            domains.Sum(item => item.Total),
            domains.Sum(item => item.Active),
            domains.Sum(item => item.Inactive),
            domains.Where(item => item.TracksUsage).Sum(item => item.InUse));
    }

    public async Task<CategoryDirectoryResult> GetCategoriesAsync(
        MasterDataCategoryKind kind,
        CategoryDirectoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var raw = kind == MasterDataCategoryKind.Project
            ? await LoadProjectCategoriesAsync(cancellationToken)
            : await LoadTechnicalCategoriesAsync(cancellationToken);

        return BuildCategoryResult(kind, raw, request);
    }

    public async Task<CategoryImpact?> GetCategoryImpactAsync(
        MasterDataCategoryKind kind,
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await GetCategoriesAsync(kind, new CategoryDirectoryRequest(null, "all"), cancellationToken);
        var row = result.Rows.FirstOrDefault(item => item.Id == id);
        if (row is null) return null;

        var parentName = row.ParentId.HasValue
            ? result.Rows.FirstOrDefault(item => item.Id == row.ParentId.Value)?.Name
            : null;

        // Records other than projects that the database will not let a delete orphan.
        var otherReferences = kind == MasterDataCategoryKind.Technical
            ? await _db.ProjectLegacyImports.AsNoTracking().CountAsync(import => import.TechnicalCategoryId == id, cancellationToken)
              + await _db.ProjectMetaChangeRequests.AsNoTracking().CountAsync(request => request.TechnicalCategoryId == id, cancellationToken)
            : await _db.ProjectLegacyImports.AsNoTracking().CountAsync(import => import.ProjectCategoryId == id, cancellationToken);

        return new CategoryImpact(
            row.Id,
            row.Name,
            parentName,
            row.DirectUsageCount,
            row.DirectChildCount,
            row.DescendantCount,
            row.IsActive,
            row.RowVersion,
            otherReferences);
    }

    public Task<FlatLookupDirectoryResult> GetFlatLookupAsync(
        MasterDataFlatLookupKind kind,
        FlatLookupDirectoryRequest request,
        CancellationToken cancellationToken = default) => kind switch
        {
            MasterDataFlatLookupKind.ProjectType => GetProjectTypesAsync(request, cancellationToken),
            MasterDataFlatLookupKind.SponsoringUnit => GetSponsoringUnitsAsync(request, cancellationToken),
            MasterDataFlatLookupKind.LineDirectorate => GetLineDirectoratesAsync(request, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    public async Task<ActivityTypeDirectoryResult> GetActivityTypesAsync(
        ActivityTypeDirectoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var search = NormalizeSearch(request.Search);
        var status = NormalizeStatus(request.Status);
        var pageSize = NormalizePageSize(request.PageSize);
        var page = Math.Max(1, request.Page);

        var all = _db.ActivityTypes.AsNoTracking();
        var total = await all.CountAsync(cancellationToken);
        var active = await all.CountAsync(item => item.IsActive, cancellationToken);
        var inUse = await all.CountAsync(item => item.Activities.Any(activity => !activity.IsDeleted), cancellationToken);

        IQueryable<ActivityType> filtered = all;
        filtered = ApplyStatus(filtered, status, item => item.IsActive);
        if (search.Length > 0)
        {
            filtered = filtered.Where(item =>
                EF.Functions.ILike(item.Name, $"%{search}%")
                || (item.Description != null && EF.Functions.ILike(item.Description, $"%{search}%")));
        }

        var filteredCount = await filtered.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rawRows = await filtered
            .OrderBy(item => item.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Description,
                item.IsActive,
                UsageCount = item.Activities.Count(activity => !activity.IsDeleted),
                item.CreatedAtUtc,
                item.LastModifiedAtUtc,
                ChangedBy = item.LastModifiedByUser != null
                    ? item.LastModifiedByUser.UserName
                    : item.CreatedByUser != null
                        ? item.CreatedByUser.UserName
                        : item.LastModifiedByUserId ?? item.CreatedByUserId,
                item.RowVersion
            })
            .ToListAsync(cancellationToken);

        var rows = rawRows.Select(item => new ActivityTypeAdminRow(
            item.Id,
            item.Name,
            item.Description,
            item.IsActive,
            item.UsageCount,
            item.CreatedAtUtc,
            item.LastModifiedAtUtc,
            item.ChangedBy,
            Convert.ToBase64String(item.RowVersion))).ToArray();

        return new ActivityTypeDirectoryResult(
            rows,
            total,
            active,
            total - active,
            inUse,
            filteredCount,
            page,
            pageSize,
            totalPages,
            search,
            status);
    }

    private async Task<FlatLookupDirectoryResult> GetProjectTypesAsync(
        FlatLookupDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        var all = _db.ProjectTypes.AsNoTracking();
        var total = await all.CountAsync(cancellationToken);
        var active = await all.CountAsync(item => item.IsActive, cancellationToken);
        var inUse = await all.CountAsync(item => item.Projects.Any(), cancellationToken);
        var search = NormalizeSearch(request.Search);
        var status = NormalizeStatus(request.Status);
        var pageSize = NormalizePageSize(request.PageSize);
        var page = Math.Max(1, request.Page);

        IQueryable<ProjectType> filtered = ApplyStatus(all, status, item => item.IsActive);
        if (search.Length > 0) filtered = filtered.Where(item => EF.Functions.ILike(item.Name, $"%{search}%"));
        var filteredCount = await filtered.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Min(page, totalPages);
        var ordered = filtered.OrderBy(item => item.SortOrder).ThenBy(item => item.Name);
        var allOrderedIds = await all.OrderBy(item => item.SortOrder).ThenBy(item => item.Name).Select(item => item.Id).ToListAsync(cancellationToken);
        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new { item.Id, item.Name, item.IsActive, item.SortOrder, Usage = item.Projects.Count, item.RowVersion })
            .ToListAsync(cancellationToken);

        return BuildFlatResult(MasterDataFlatLookupKind.ProjectType, rows.Select(item => new FlatLookupAdminRow(
            item.Id, item.Name, item.IsActive, item.SortOrder, item.Usage, Convert.ToBase64String(item.RowVersion), null,
            CanMoveUp(allOrderedIds, item.Id), CanMoveDown(allOrderedIds, item.Id))).ToArray(), total, active, inUse,
            filteredCount, page, pageSize, totalPages, search, status);
    }

    private async Task<FlatLookupDirectoryResult> GetSponsoringUnitsAsync(
        FlatLookupDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        var all = _db.SponsoringUnits.AsNoTracking();
        var total = await all.CountAsync(cancellationToken);
        var active = await all.CountAsync(item => item.IsActive, cancellationToken);
        var inUse = await all.CountAsync(item => item.Projects.Any(), cancellationToken);
        var search = NormalizeSearch(request.Search);
        var status = NormalizeStatus(request.Status);
        var pageSize = NormalizePageSize(request.PageSize);
        var page = Math.Max(1, request.Page);

        IQueryable<SponsoringUnit> filtered = ApplyStatus(all, status, item => item.IsActive);
        if (search.Length > 0) filtered = filtered.Where(item => EF.Functions.ILike(item.Name, $"%{search}%"));
        var filteredCount = await filtered.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Min(page, totalPages);
        var allOrderedIds = await all.OrderBy(item => item.SortOrder).ThenBy(item => item.Name).Select(item => item.Id).ToListAsync(cancellationToken);
        var rows = await filtered.OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new { item.Id, item.Name, item.IsActive, item.SortOrder, Usage = item.Projects.Count, item.RowVersion, item.UpdatedUtc })
            .ToListAsync(cancellationToken);

        return BuildFlatResult(MasterDataFlatLookupKind.SponsoringUnit, rows.Select(item => new FlatLookupAdminRow(
            item.Id, item.Name, item.IsActive, item.SortOrder, item.Usage, Convert.ToBase64String(item.RowVersion),
            new DateTimeOffset(EnsureUtc(item.UpdatedUtc)), CanMoveUp(allOrderedIds, item.Id), CanMoveDown(allOrderedIds, item.Id))).ToArray(),
            total, active, inUse, filteredCount, page, pageSize, totalPages, search, status);
    }

    private async Task<FlatLookupDirectoryResult> GetLineDirectoratesAsync(
        FlatLookupDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        var all = _db.LineDirectorates.AsNoTracking();
        var total = await all.CountAsync(cancellationToken);
        var active = await all.CountAsync(item => item.IsActive, cancellationToken);
        var inUse = await all.CountAsync(item => item.Projects.Any(), cancellationToken);
        var search = NormalizeSearch(request.Search);
        var status = NormalizeStatus(request.Status);
        var pageSize = NormalizePageSize(request.PageSize);
        var page = Math.Max(1, request.Page);

        IQueryable<LineDirectorate> filtered = ApplyStatus(all, status, item => item.IsActive);
        if (search.Length > 0) filtered = filtered.Where(item => EF.Functions.ILike(item.Name, $"%{search}%"));
        var filteredCount = await filtered.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Min(page, totalPages);
        var allOrderedIds = await all.OrderBy(item => item.SortOrder).ThenBy(item => item.Name).Select(item => item.Id).ToListAsync(cancellationToken);
        var rows = await filtered.OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new { item.Id, item.Name, item.IsActive, item.SortOrder, Usage = item.Projects.Count, item.RowVersion, item.UpdatedUtc })
            .ToListAsync(cancellationToken);

        return BuildFlatResult(MasterDataFlatLookupKind.LineDirectorate, rows.Select(item => new FlatLookupAdminRow(
            item.Id, item.Name, item.IsActive, item.SortOrder, item.Usage, Convert.ToBase64String(item.RowVersion),
            new DateTimeOffset(EnsureUtc(item.UpdatedUtc)), CanMoveUp(allOrderedIds, item.Id), CanMoveDown(allOrderedIds, item.Id))).ToArray(),
            total, active, inUse, filteredCount, page, pageSize, totalPages, search, status);
    }

    private async Task<IReadOnlyList<RawCategoryRow>> LoadProjectCategoriesAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.ProjectCategories.AsNoTracking()
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.ParentId,
                item.IsActive,
                item.SortOrder,
                UsageCount = item.Projects.Count,
                item.RowVersion
            })
            .ToListAsync(cancellationToken);

        return rows.Select(item => new RawCategoryRow(
            item.Id, item.Name, item.ParentId, item.IsActive, item.SortOrder,
            item.UsageCount, Convert.ToBase64String(item.RowVersion))).ToArray();
    }

    private async Task<IReadOnlyList<RawCategoryRow>> LoadTechnicalCategoriesAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.TechnicalCategories.AsNoTracking()
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.ParentId,
                item.IsActive,
                item.SortOrder,
                UsageCount = item.Projects.Count,
                item.RowVersion
            })
            .ToListAsync(cancellationToken);

        return rows.Select(item => new RawCategoryRow(
            item.Id, item.Name, item.ParentId, item.IsActive, item.SortOrder,
            item.UsageCount, Convert.ToBase64String(item.RowVersion))).ToArray();
    }

    private static CategoryDirectoryResult BuildCategoryResult(
        MasterDataCategoryKind kind,
        IReadOnlyList<RawCategoryRow> raw,
        CategoryDirectoryRequest request)
    {
        var byId = raw.ToDictionary(item => item.Id);
        var children = raw.GroupBy(item => item.ParentId ?? 0)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList());

        var flattened = new List<CategoryAdminRow>(raw.Count);
        var pathById = new Dictionary<int, string>();
        var visiting = new HashSet<int>();
        var visited = new HashSet<int>();

        void Visit(RawCategoryRow item, int depth, string parentPath)
        {
            if (!visiting.Add(item.Id)) return;
            var path = string.IsNullOrWhiteSpace(parentPath) ? item.Name : $"{parentPath} / {item.Name}";
            pathById[item.Id] = path;
            var siblings = children.TryGetValue(item.ParentId ?? 0, out var siblingList) ? siblingList : new List<RawCategoryRow>();
            var siblingIndex = siblings.FindIndex(candidate => candidate.Id == item.Id);
            var directChildren = children.TryGetValue(item.Id, out var childList) ? childList : new List<RawCategoryRow>();
            var descendantCount = CountDescendants(item.Id, children, new HashSet<int>());

            flattened.Add(new CategoryAdminRow(
                item.Id,
                item.Name,
                item.ParentId,
                path,
                depth,
                item.IsActive,
                item.SortOrder,
                item.UsageCount,
                directChildren.Count,
                descendantCount,
                item.RowVersion,
                directChildren.Count > 0,
                siblingIndex > 0,
                siblingIndex >= 0 && siblingIndex < siblings.Count - 1,
                false));

            foreach (var child in directChildren)
            {
                Visit(child, depth + 1, path);
            }

            visiting.Remove(item.Id);
            visited.Add(item.Id);
        }

        if (children.TryGetValue(0, out var roots))
        {
            foreach (var root in roots) Visit(root, 0, string.Empty);
        }

        // Preserve access to malformed legacy rows while preventing a cycle from failing the page.
        foreach (var orphan in raw.Where(item => !visited.Contains(item.Id)).OrderBy(item => item.SortOrder).ThenBy(item => item.Name))
        {
            Visit(orphan, 0, "Unlinked");
        }

        var search = NormalizeSearch(request.Search);
        var status = NormalizeStatus(request.Status);
        var matching = flattened.Where(row =>
            StatusMatches(row.IsActive, status)
            && (search.Length == 0
                || row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Path.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .Select(row => row.Id)
            .ToHashSet();

        var included = new HashSet<int>(matching);
        foreach (var id in matching.ToArray())
        {
            var current = byId.TryGetValue(id, out var item) ? item : null;
            var guard = new HashSet<int>();
            while (current?.ParentId is int parentId && guard.Add(parentId) && byId.TryGetValue(parentId, out var parent))
            {
                included.Add(parentId);
                current = parent;
            }
        }

        var rows = flattened
            .Where(row => included.Contains(row.Id))
            .Select(row => row with { IsContextOnly = !matching.Contains(row.Id) })
            .ToArray();

        return new CategoryDirectoryResult(
            kind,
            rows,
            raw.Count,
            raw.Count(item => item.IsActive),
            raw.Count(item => !item.IsActive),
            raw.Count(item => item.UsageCount > 0),
            raw.Count(item => item.ParentId == null),
            search,
            status);
    }

    private static int CountDescendants(
        int id,
        IReadOnlyDictionary<int, List<RawCategoryRow>> children,
        ISet<int> visited)
    {
        if (!visited.Add(id) || !children.TryGetValue(id, out var rows)) return 0;
        return rows.Count + rows.Sum(child => CountDescendants(child.Id, children, visited));
    }

    private static MasterDataDomainSummary Domain(
        string key,
        string title,
        string description,
        string icon,
        string area,
        string page,
        CountSnapshot counts,
        string policy,
        bool tracksUsage = true,
        string group = "",
        string usedIn = "") => new(
            key,
            title,
            description,
            icon,
            area,
            page,
            counts.Total,
            counts.Active,
            counts.Total - counts.Active,
            counts.InUse,
            policy,
            tracksUsage,
            group,
            usedIn);

    // Total and active values of a list, and how many of its values records use.
    private static async Task<CountSnapshot> CountListAsync(
        IQueryable<bool> isActive,
        IQueryable<int> usedIds,
        CancellationToken cancellationToken)
    {
        var total = await isActive.CountAsync(cancellationToken);
        var active = await isActive.CountAsync(value => value, cancellationToken);
        return new CountSnapshot(total, active, total - active, await usedIds.CountAsync(cancellationToken));
    }

    private static async Task<CountSnapshot> CountListAsync(
        IQueryable<bool> isActive,
        IQueryable<Guid> usedIds,
        CancellationToken cancellationToken)
    {
        var total = await isActive.CountAsync(cancellationToken);
        var active = await isActive.CountAsync(value => value, cancellationToken);
        return new CountSnapshot(total, active, total - active, await usedIds.CountAsync(cancellationToken));
    }

    private static IQueryable<T> ApplyStatus<T>(
        IQueryable<T> query,
        string status,
        System.Linq.Expressions.Expression<Func<T, bool>> isActive) => status switch
        {
            "active" => query.Where(isActive),
            "inactive" => query.Where(Negate(isActive)),
            _ => query
        };

    private static System.Linq.Expressions.Expression<Func<T, bool>> Negate<T>(
        System.Linq.Expressions.Expression<Func<T, bool>> expression)
    {
        var body = System.Linq.Expressions.Expression.Not(expression.Body);
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, expression.Parameters);
    }

    private static FlatLookupDirectoryResult BuildFlatResult(
        MasterDataFlatLookupKind kind,
        IReadOnlyList<FlatLookupAdminRow> rows,
        int total,
        int active,
        int inUse,
        int filteredCount,
        int page,
        int pageSize,
        int totalPages,
        string search,
        string status) => new(
            kind,
            rows,
            total,
            active,
            total - active,
            inUse,
            filteredCount,
            page,
            pageSize,
            totalPages,
            search,
            status);

    private static bool CanMoveUp(IReadOnlyList<int> orderedIds, int id) => orderedIds.IndexOf(id) > 0;
    private static bool CanMoveDown(IReadOnlyList<int> orderedIds, int id)
    {
        var index = orderedIds.IndexOf(id);
        return index >= 0 && index < orderedIds.Count - 1;
    }

    private static string NormalizeSearch(string? value) => value?.Trim() ?? string.Empty;
    private static string NormalizeStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "inactive" => "inactive",
        "all" => "all",
        _ => "active"
    };
    private static int NormalizePageSize(int value) => value switch
    {
        < 10 => 25,
        > MaximumPageSize => MaximumPageSize,
        _ => value
    };
    private static bool StatusMatches(bool isActive, string status) => status switch
    {
        "active" => isActive,
        "inactive" => !isActive,
        _ => true
    };
    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed record RawCategoryRow(
        int Id,
        string Name,
        int? ParentId,
        bool IsActive,
        int SortOrder,
        int UsageCount,
        string RowVersion);

    private sealed record CountSnapshot(int Total, int Active, int Inactive, int InUse)
    {
        public static CountSnapshot Empty { get; } = new(0, 0, 0, 0);
    }
}

internal static class MasterDataListExtensions
{
    public static int IndexOf(this IReadOnlyList<int> items, int value)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] == value) return index;
        }
        return -1;
    }
}
