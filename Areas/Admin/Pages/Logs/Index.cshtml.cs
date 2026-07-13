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

    public IndexModel(
        IAdminLogQueryService queries,
        IAuditActionPresentationCatalog presentations,
        IAdminClientDescriptorService clients,
        IAdminAuditEntityLinkResolver entityLinks,
        ISafeCsvWriter csv,
        IAdminTimeService time)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _presentations = presentations ?? throw new ArgumentNullException(nameof(presentations));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _entityLinks = entityLinks ?? throw new ArgumentNullException(nameof(entityLinks));
        _csv = csv ?? throw new ArgumentNullException(nameof(csv));
        _time = time ?? throw new ArgumentNullException(nameof(time));
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

    public async Task<FileResult> OnGetExportCsvAsync(CancellationToken cancellationToken)
    {
        var rows = await _queries.GetForExportAsync(BuildQuery(), MaximumExportRows + 1, cancellationToken);
        var wasTruncated = rows.Count > MaximumExportRows;
        var exported = rows.Take(MaximumExportRows).ToArray();

        var bytes = _csv.Write(
            new[]
            {
                "TimeIST", "Severity", "Event", "RawAction", "Category", "Actor", "ActorUserId",
                "AffectedEntityType", "AffectedEntityId", "SourceIP", "Client", "Message", "Reason",
                "Outcome", "TraceId", "Origin", "Before", "After", "RawJson"
            },
            exported.Select(row =>
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
                    payload?.ActorName ?? row.UserName,
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
            }));

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

    public AdminClientDescriptor Client(AdminLogRow row) => _clients.Describe(row.UserAgent);

    public AdminAuditEntityLink? EntityLink(AdminLogRow row) => row.Payload is null
        ? null
        : _entityLinks.Resolve(HttpContext, row.Payload);

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
            payload?.ActorName ?? row.UserName ?? "System",
            payload?.ActorUserId ?? row.UserId,
            payload?.ActorRoles,
            payload?.EntityType,
            payload?.EntityId,
            payload?.AffectedRecord,
            entityLink?.Text,
            entityLink?.Href,
            row.Ip,
            client.Summary,
            client.RawUserAgent,
            row.Message,
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
        From.HasValue ? DateOnly.FromDateTime(From.Value) : null,
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
        [nameof(Dir)] = Dir
    };

    private void BuildPresentation()
    {
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Monitoring",
            Title = "Audit logs",
            Description = "Trace administrative actions, affected records and security-relevant system events.",
            Icon = "bi-journal-text",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Login activity",
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
                Detail = "Current filter scope",
                Icon = "bi-activity",
                Tone = "neutral",
                Href = FilterUrl(),
                IsActive = string.IsNullOrWhiteSpace(Level)
            },
            new AdminMonitoringMetricModel
            {
                Label = "Information",
                Value = summary.Information.ToString("N0"),
                Detail = "Routine operations",
                Icon = "bi-info-circle",
                Tone = "neutral",
                Href = FilterUrl("Info"),
                IsActive = string.Equals(Level, "Info", StringComparison.OrdinalIgnoreCase)
            },
            new AdminMonitoringMetricModel
            {
                Label = "Warnings",
                Value = summary.Warnings.ToString("N0"),
                Detail = "Review recommended",
                Icon = "bi-exclamation-triangle",
                Tone = "warning",
                Href = FilterUrl("Warning"),
                IsActive = string.Equals(Level, "Warning", StringComparison.OrdinalIgnoreCase)
            },
            new AdminMonitoringMetricModel
            {
                Label = "Errors",
                Value = summary.Errors.ToString("N0"),
                Detail = "Immediate investigation",
                Icon = "bi-x-octagon",
                Tone = "danger",
                Href = FilterUrl("Error"),
                IsActive = string.Equals(Level, "Error", StringComparison.OrdinalIgnoreCase)
            },
            new AdminMonitoringMetricModel
            {
                Label = "Actors",
                Value = summary.Actors.ToString("N0"),
                Detail = $"{summary.AffectedRecords:N0} affected records",
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
