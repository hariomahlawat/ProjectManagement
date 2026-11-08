using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Pages.Logs;

[Authorize(Policy = AdminPolicies.LogsView)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : PageModel
{
    private const int MaximumExportRows = 50_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAdminLogQueryService _queries;
    private readonly IAuditActionPresentationCatalog _presentations;
    private readonly IAdminClientDescriptorService _clients;
    private readonly IAdminAuditEntityLinkResolver _entityLinks;
    private readonly ISafeCsvWriter _csv;
    private readonly IAdminTimeService _time;
    private readonly IAdminRoleDescriptorCatalog _roles;

    public IndexModel(
        IAdminLogQueryService queries,
        IAuditActionPresentationCatalog presentations,
        IAdminClientDescriptorService clients,
        IAdminAuditEntityLinkResolver entityLinks,
        ISafeCsvWriter csv,
        IAdminTimeService time,
        IAdminRoleDescriptorCatalog roles)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _presentations = presentations ?? throw new ArgumentNullException(nameof(presentations));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _entityLinks = entityLinks ?? throw new ArgumentNullException(nameof(entityLinks));
        _csv = csv ?? throw new ArgumentNullException(nameof(csv));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    [BindProperty(SupportsGet = true)] public string? Level { get; set; }
    [BindProperty(SupportsGet = true)] public string? Action { get; set; }
    [BindProperty(SupportsGet = true)] public string? Category { get; set; }
    [BindProperty(SupportsGet = true)] public string? EntityType { get; set; }
    [BindProperty(SupportsGet = true, Name = "User")] public string? UserName { get; set; }
    [BindProperty(SupportsGet = true)] public string? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ip { get; set; }
    [BindProperty(SupportsGet = true)] public string? Contains { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "Time";
    [BindProperty(SupportsGet = true)] public string Dir { get; set; } = "desc";

    /// <summary>True to see the whole history instead of the default recent window.</summary>
    [BindProperty(SupportsGet = true)] public bool All { get; set; }

    public const int DefaultWindowDays = 30;

    // With no dates and nothing that narrows the log to specific records, the page used to
    // count, group and chart the entire audit history on every load (one chart label per day
    // since go-live). It now shows the last 30 days unless the administrator asks for all.
    // Picking one person, record or action narrows the log, so those keep their full history.
    // Typed searches (text, a name, an IP) are "contains" scans of every row, so they keep the
    // 30-day window too, with "Show all history" one click away
    // (docs/ux/admin-review-2-fixes.md, PR 11).
    public bool UsesDefaultWindow => !All
        && !From.HasValue
        && !To.HasValue
        && string.IsNullOrWhiteSpace(Action)
        && string.IsNullOrWhiteSpace(EntityType)
        && string.IsNullOrWhiteSpace(UserId);

    public string AllHistoryUrl()
    {
        var values = QueryValues(1);
        values[nameof(All)] = true;
        return Url.Page("./Index", values) ?? "#";
    }

    public string RecentWindowUrl()
    {
        var values = QueryValues(1);
        values[nameof(All)] = null;
        return Url.Page("./Index", values) ?? "#";
    }

    public AdminLogResult Result { get; private set; } = new(
        Array.Empty<AdminLogRow>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<int>(),
        0,
        1,
        25,
        new AdminLogSummary(0, 0, 0, 0, 0, 0));

    public AdminPageHeaderModel Header { get; private set; } = new();

    public IReadOnlyList<AdminMonitoringMetricModel> Metrics { get; private set; } =
        Array.Empty<AdminMonitoringMetricModel>();

    public bool HasFilters => !string.IsNullOrWhiteSpace(Level)
        || !string.IsNullOrWhiteSpace(Action)
        || !string.IsNullOrWhiteSpace(Category)
        || !string.IsNullOrWhiteSpace(EntityType)
        || !string.IsNullOrWhiteSpace(UserName)
        || !string.IsNullOrWhiteSpace(UserId)
        || !string.IsNullOrWhiteSpace(Ip)
        || !string.IsNullOrWhiteSpace(Contains)
        || From.HasValue
        || To.HasValue;

    public int FirstRow => Result.Total == 0 ? 0 : ((Result.Page - 1) * Result.PageSize) + 1;
    public int LastRow => Math.Min(Result.Page * Result.PageSize, Result.Total);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await _queries.GetAsync(BuildQuery(), cancellationToken);
        PageNo = Result.Page;
        PageSize = Result.PageSize;
        BuildPresentation();
        return Page();
    }

    public async Task<FileResult> OnGetExportCsvAsync(
        [FromServices] IAdminExportAuditService exportAudit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exportAudit);
        var query = BuildQuery();
        var rows = await _queries.GetForExportAsync(query, MaximumExportRows + 1, cancellationToken);
        var wasTruncated = rows.Count > MaximumExportRows;
        var exported = rows.Take(MaximumExportRows).ToArray();

        var csvRows = exported.Select(row =>
        {
            var presentation = Presentation(row);
            var payload = row.Payload;
            return (IReadOnlyList<object?>)new object?[]
            {
                FormatIst(row.TimeUtc),
                row.Level,
                presentation.Label,
                row.Action,
                presentation.Category,
                row.Actor,
                payload?.ActorUserId ?? row.UserId,
                payload?.EntityType,
                payload?.EntityId,
                row.Ip,
                Client(row).Summary,
                row.Message,
                payload?.Reason,
                payload?.Outcome,
                payload?.TraceId,
                payload?.Origin,
                payload?.BeforeJson,
                payload?.AfterJson,
                payload?.RawPrettyJson ?? row.DataJson
            };
        });

        if (wasTruncated)
        {
            // The header stays for scripts; the last row tells the person who opens the file.
            csvRows = csvRows.Append(AdminExportNotice.TruncatedRow(exported.Length));
        }

        var bytes = _csv.Write(
            new[]
            {
                "TimeIST", "Severity", "Event", "RawAction", "Category", "Actor", "ActorUserId",
                "AffectedEntityType", "AffectedEntityId", "SourceIP", "Client", "Message", "Reason",
                "Outcome", "TraceId", "Origin", "Before", "After", "RawJson"
            },
            csvRows);

        await exportAudit.RecordAsync(
            new AdminExportRecord(
                AdminExportKind.AuditLog,
                exported.Length,
                new Dictionary<string, string?>
                {
                    ["Severity"] = query.Level,
                    ["Action"] = query.Action,
                    ["Category"] = query.Category,
                    ["EntityType"] = query.EntityType,
                    ["User"] = query.UserName,
                    ["UserId"] = query.UserId,
                    ["SourceIP"] = query.Ip,
                    ["Contains"] = query.Contains,
                    ["From"] = query.From?.ToString("yyyy-MM-dd"),
                    ["To"] = query.To?.ToString("yyyy-MM-dd")
                },
                wasTruncated),
            cancellationToken);

        if (wasTruncated)
        {
            Response.Headers["X-PRISM-Export-Truncated"] = "true";
        }

        return File(
            bytes,
            "text/csv; charset=utf-8",
            $"audit_logs_{_time.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    public string FormatIst(DateTime utc) => _time.FormatIst(utc);

    public AuditActionPresentation Presentation(AdminLogRow row) =>
        _presentations.Describe(row.Action, row.Level);

    /// <summary>
    /// The actor's roles in full ("Administrator, Head of Department"), from the role names
    /// stored on the row ("Admin,HoD"). Null when the row kept no roles.
    /// </summary>
    public string? ActorRoles(AdminLogRow row)
    {
        var stored = row.Payload?.ActorRoles;
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        var names = _roles
            .DescribeMany(stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(role => role.DisplayName)
            .ToArray();
        return names.Length == 0 ? null : string.Join(", ", names);
    }

    public AdminClientDescriptor Client(AdminLogRow row) => _clients.Describe(row.UserAgent);

    public AdminAuditEntityLink? EntityLink(AdminLogRow row)
    {
        if (row.Payload is { EntityType: not null, EntityId: not null } stored)
        {
            return _entityLinks.Resolve(HttpContext, stored);
        }

        // Entries that name their record only in their own details (a project id, a person's
        // account) link the same way as entries written with an entity type and id.
        var entityType = row.RecordRef?.Kind switch
        {
            AuditRecordKinds.Project => "Project",
            AuditRecordKinds.Person => "ApplicationUser",
            { } kind when AuditRecordKinds.ListItemKinds.Contains(kind) => kind,
            _ => null
        };
        return entityType is null
            ? null
            : _entityLinks.Resolve(
                HttpContext,
                new AdminAuditPayload(entityType, row.RecordRef!.Key, null, null, null, null, null, null, null, null, null, null));
    }

    /// <summary>
    /// The Category filter's words, matching the sidebar. The query values ("master-data",
    /// "pdf-ingestion") stay as they were, so saved links keep working.
    /// </summary>
    public static string CategoryLabel(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "authentication" => "Authentication",
        "access" or "access-security" => "Access & security",
        "projects" => "Projects",
        "documents" => "Documents",
        "calendar" => "Calendar",
        "recovery" => "Recovery",
        "master-data" or "master data" => AuditActionPresentationCatalog.ListsCategory,
        "pdf-ingestion" => AuditActionPresentationCatalog.PdfCopyCategory,
        "legacy-import" => AuditActionPresentationCatalog.LegacyImportCategory,
        null or "" => "All categories",
        _ => value!.Replace('-', ' ')
    };

    public string DetailJson(AdminLogRow row)
    {
        var presentation = Presentation(row);
        var client = Client(row);
        var payload = row.Payload;
        var entityLink = EntityLink(row);

        return JsonSerializer.Serialize(new AuditDetailDocument(
            row.Id,
            presentation.Label,
            row.Action,
            presentation.Category,
            presentation.Icon,
            presentation.Tone,
            row.Level,
            FormatIst(row.TimeUtc),
            row.Actor,
            payload?.ActorUserId ?? row.UserId,
            ActorRoles(row),
            payload?.EntityType,
            payload?.EntityId,
            row.Record?.ToString() ?? payload?.AffectedRecord,
            entityLink?.Text,
            entityLink?.Href,
            row.Ip,
            client.Summary,
            client.RawUserAgent,
            row.Detail,
            payload?.Reason,
            payload?.Outcome,
            payload?.TraceId,
            payload?.Origin,
            payload?.BeforeJson,
            payload?.AfterJson,
            payload?.RawPrettyJson ?? row.DataJson), JsonOptions);
    }

    public string PageUrl(int page) => Url.Page("./Index", QueryValues(page)) ?? "#";

    public string SortUrl(string sort)
    {
        var nextDirection = string.Equals(Sort, sort, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(Dir, "asc", StringComparison.OrdinalIgnoreCase)
            ? "desc"
            : "asc";
        var values = QueryValues(1);
        values[nameof(Sort)] = sort;
        values[nameof(Dir)] = nextDirection;
        return Url.Page("./Index", values) ?? "#";
    }

    public string FilterUrl(string? level = null, string? category = null)
    {
        var values = QueryValues(1);
        values[nameof(Level)] = level;
        values[nameof(Category)] = category;
        return Url.Page("./Index", values) ?? "#";
    }

    private AdminLogQuery BuildQuery() => new(
        Level,
        Action,
        UserName,
        UserId,
        Ip,
        Contains,
        From.HasValue
            ? DateOnly.FromDateTime(From.Value)
            : UsesDefaultWindow ? _time.TodayIst.AddDays(-(DefaultWindowDays - 1)) : null,
        To.HasValue ? DateOnly.FromDateTime(To.Value) : null,
        PageNo,
        PageSize,
        Sort,
        Dir,
        Category,
        EntityType);

    private Dictionary<string, object?> QueryValues(int page) => new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Level)] = Level,
        [nameof(Action)] = Action,
        [nameof(Category)] = Category,
        [nameof(EntityType)] = EntityType,
        ["User"] = UserName,
        [nameof(UserId)] = UserId,
        [nameof(Ip)] = Ip,
        [nameof(Contains)] = Contains,
        [nameof(From)] = From?.ToString("yyyy-MM-dd"),
        [nameof(To)] = To?.ToString("yyyy-MM-dd"),
        [nameof(PageNo)] = page,
        [nameof(PageSize)] = PageSize,
        [nameof(Sort)] = Sort,
        [nameof(Dir)] = Dir,
        [nameof(All)] = All ? true : null
    };

    private void BuildPresentation()
    {
        Header = new AdminPageHeaderModel
        {
            Title = "Audit log",
            Description = ProjectManagement.Services.Navigation.ModuleNav.AdminNavigationCatalog.GetEntry(ProjectManagement.Services.Navigation.ModuleNav.AdminNavigationKeys.Logs).Description,
            Icon = "bi-journal-text",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Sign-in activity",
                    Href = Url.Page("/Analytics/Logins", new { area = "Admin" }),
                    Icon = "bi-graph-up-arrow"
                },
                new AdminPageActionModel
                {
                    Text = "Export CSV",
                    Href = Url.Page("./Index", "ExportCsv", QueryValues(1)),
                    Icon = "bi-download",
                    IsPrimary = true
                }
            }
        };

        var summary = Result.Summary ?? new AdminLogSummary(Result.Total, 0, 0, 0, 0, 0);
        Metrics = new[]
        {
            new AdminMonitoringMetricModel
            {
                Label = "Events",
                Value = summary.Total.ToString("N0"),
                Detail = "Matching the filters",
                Icon = "bi-activity",
                Tone = "neutral",
                Href = FilterUrl(),
                IsActive = string.IsNullOrWhiteSpace(Level)
            },
            new AdminMonitoringMetricModel
            {
                Label = "Information",
                Value = summary.Information.ToString("N0"),
                Detail = "Everyday changes",
                Icon = "bi-info-circle",
                Tone = "neutral",
                Href = FilterUrl("Info"),
                IsActive = string.Equals(Level, "Info", StringComparison.OrdinalIgnoreCase)
            },
            new AdminMonitoringMetricModel
            {
                Label = "Warnings",
                Value = summary.Warnings.ToString("N0"),
                Detail = "Worth a look",
                Icon = "bi-exclamation-triangle",
                Tone = summary.Warnings > 0 ? "warning" : "neutral",
                Href = FilterUrl("Warning"),
                IsActive = string.Equals(Level, "Warning", StringComparison.OrdinalIgnoreCase)
            },
            new AdminMonitoringMetricModel
            {
                Label = "Errors",
                Value = summary.Errors.ToString("N0"),
                Detail = "Things that failed",
                Icon = "bi-x-octagon",
                Tone = summary.Errors > 0 ? "danger" : "neutral",
                Href = FilterUrl("Error"),
                IsActive = string.Equals(Level, "Error", StringComparison.OrdinalIgnoreCase)
            },
            new AdminMonitoringMetricModel
            {
                Label = summary.Actors == 1 ? "Person" : "People",
                Value = summary.Actors.ToString("N0"),
                Detail = summary.AffectedRecords == 1 ? "1 record changed" : $"{summary.AffectedRecords:N0} records changed",
                Icon = "bi-person-check",
                Tone = "neutral"
            }
        };
    }

    private sealed record AuditDetailDocument(
        long Id,
        string Title,
        string RawAction,
        string Category,
        string Icon,
        string Tone,
        string Severity,
        string Time,
        string Actor,
        string? ActorUserId,
        string? ActorRoles,
        string? EntityType,
        string? EntityId,
        string? AffectedRecord,
        string? EntityLinkText,
        string? EntityLinkHref,
        string? Ip,
        string Client,
        string? RawUserAgent,
        string? Message,
        string? Reason,
        string? Outcome,
        string? TraceId,
        string? Origin,
        string? Before,
        string? After,
        string? RawJson);
}
