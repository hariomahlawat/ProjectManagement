using Microsoft.EntityFrameworkCore;
using ProjectManagement.Services.Search;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Services.Admin.MasterData;

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

public interface IMasterDataAdministrationQueryService
{
    Task<MasterDataOverviewSnapshot> GetOverviewAsync(int holidayYear, CancellationToken cancellationToken = default);
}

public sealed class MasterDataAdministrationQueryService : IMasterDataAdministrationQueryService
{
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
            Domain("social-media-types", "Social media event types", "The kind of event a social media post is about.", "bi-megaphone", "ProjectOfficeReports", "/Admin/SocialMediaTypes/Index", socialMediaTypes, AdminPolicies.SocialMediaListsManage,
                group: MasterDataGroups.DocumentsSocialMedia, usedIn: "Social media post form"),
            Domain("social-media-platforms", "Social media platforms", "Where a social media post was published.", "bi-share", "ProjectOfficeReports", "/Admin/SocialMediaTypes/Platforms/Index", socialMediaPlatforms, AdminPolicies.SocialMediaListsManage,
                group: MasterDataGroups.DocumentsSocialMedia, usedIn: "Social media post form"),
            Domain("holidays", $"Holidays · {holidayYear}", "Gazetted and restricted holidays, and which ones the office observes. Stage dates skip them.", "bi-calendar-week", string.Empty, "/Settings/Holidays/Index", holidays, AdminPolicies.HolidaysManage, tracksUsage: false,
                group: MasterDataGroups.Calendar, usedIn: "Shared calendar and stage dates"),
            Domain("celebrations", "Celebrations", "Birthdays and anniversaries shown on the shared calendar.", "bi-stars", string.Empty, "/Celebrations/Index", celebrations, Policies.Calendar.ManageCelebrations, tracksUsage: false,
                group: MasterDataGroups.Calendar, usedIn: "Shared calendar")
        };

        var auditRows = await _db.AuditLogs.AsNoTracking()
            .Where(item => item.Action.StartsWith("MasterData.")
                || item.Action.StartsWith("Holiday")
                || item.Action.StartsWith("GazettedHoliday")
                || item.Action.StartsWith("RestrictedHoliday")
                || item.Action.StartsWith("Celebration"))
            .OrderByDescending(item => item.TimeUtc)
            .ThenByDescending(item => item.Id)
            .Take(8)
            .Select(item => new
            {
                item.Id,
                item.Action,
                item.Level,
                item.UserId,
                item.UserName,
                item.Message,
                item.DataJson,
                item.TimeUtc
            })
            .ToListAsync(cancellationToken);

        // People through the audit log's one name lookup, and the changed item by name
        // (docs/ux/admin-review-4-fixes.md, PR 7).
        var views = await AuditEntryPresenter.DescribeAsync(
            _db,
            auditRows.Select(item => new AuditEntrySource(
                    item.Action,
                    item.Message,
                    item.UserId,
                    item.UserName,
                    item.DataJson,
                    _auditPresentation.Describe(item.Action, item.Level).Label))
                .ToArray(),
            null,
            cancellationToken);

        var recentChanges = auditRows.Select((item, index) =>
        {
            var presentation = _auditPresentation.Describe(item.Action, item.Level);
            var view = views[index];
            var message = (view.Record?.Name, view.Detail) switch
            {
                ({ } name, { } detail) when detail.Contains(name, StringComparison.OrdinalIgnoreCase) => detail,
                ({ } name, { } detail) => $"{name}: {detail}",
                ({ } name, null) => name,
                (null, { } detail) => detail,
                _ => null
            };
            return new MasterDataRecentChange(
                item.Id,
                presentation.Label,
                view.Actor,
                message,
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

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed record CountSnapshot(int Total, int Active, int Inactive, int InUse)
    {
        public static CountSnapshot Empty { get; } = new(0, 0, 0, 0);
    }
}
