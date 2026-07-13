using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Pages.Analytics;

[Authorize(Policy = AdminPolicies.SecurityView)]
[ResponseCache(NoStore = true)]
public sealed class LoginsModel : PageModel
{
    private const int MaximumExportRows = 5_000;

    private readonly IAdminLoginMonitoringService _monitoring;
    private readonly IAdminClientDescriptorService _clients;
    private readonly IAdminTimeService _time;
    private readonly ISafeCsvWriter _csv;
    private readonly int _defaultDays;
    private readonly bool _defaultMarkWeekends;

    public LoginsModel(
        IAdminLoginMonitoringService monitoring,
        IAdminClientDescriptorService clients,
        IAdminTimeService time,
        ISafeCsvWriter csv,
        IOptions<AdminLoginMonitoringOptions> options)
    {
        _monitoring = monitoring ?? throw new ArgumentNullException(nameof(monitoring));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _csv = csv ?? throw new ArgumentNullException(nameof(csv));
        var configured = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _defaultDays = configured.DefaultLookbackDays;
        _defaultMarkWeekends = configured.MarkWeekendsForReview;
        Days = _defaultDays;
        MarkWeekends = _defaultMarkWeekends;
    }

    [BindProperty(SupportsGet = true)]
    public int Days { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Outcome { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool ReviewOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool MarkWeekends { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNo { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    public AdminLoginMonitoringSnapshot Snapshot { get; private set; } = EmptySnapshot();

    public AdminPageHeaderModel Header { get; private set; } = new();

    public IReadOnlyList<AdminMonitoringMetricModel> Metrics { get; private set; } =
        Array.Empty<AdminMonitoringMetricModel>();

    public bool HasFilters => !string.IsNullOrWhiteSpace(UserId)
        || !string.IsNullOrWhiteSpace(Outcome)
        || ReviewOnly
        || MarkWeekends != _defaultMarkWeekends
        || Days != _defaultDays;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _monitoring.GetAsync(BuildRequest(), cancellationToken);
        Days = Snapshot.Days;
        PageNo = Snapshot.ReviewPage;
        PageSize = Snapshot.ReviewPageSize;
        MarkWeekends = Snapshot.MarkWeekendsForReview;
        BuildPresentation();
        return Page();
    }

    public async Task<FileResult> OnGetExportCsvAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _monitoring.GetAsync(
            BuildRequest() with { Page = 1, PageSize = 100 },
            cancellationToken);

        var rows = snapshot.PatternPoints
            .OrderByDescending(row => row.WhenUtc)
            .Take(MaximumExportRows)
            .ToArray();

        var bytes = _csv.Write(
            new[]
            {
                "WhenIST", "Outcome", "DisplayName", "LoginName", "UserId", "SourceIP",
                "Client", "RequiresReview", "ReviewReason"
            },
            rows.Select(row => (IReadOnlyList<object?>)new object?[]
            {
                FormatIst(row.WhenUtc),
                OutcomeLabel(row.Outcome),
                row.DisplayName,
                row.LoginName,
                row.UserId,
                row.Ip,
                ClientDescriptor(row.UserAgent).Summary,
                row.RequiresReview,
                row.ReviewReason
            }));

        if (snapshot.IsTruncated || snapshot.PatternPoints.Count > MaximumExportRows)
        {
            Response.Headers["X-PRISM-Export-Truncated"] = "true";
        }

        return File(
            bytes,
            "text/csv; charset=utf-8",
            $"login_activity_{_time.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    public string FormatIst(DateTimeOffset utc) => _time.FormatIst(utc);

    public AdminClientDescriptor ClientDescriptor(string? userAgent) => _clients.Describe(userAgent);

    public string OutcomeLabel(AdminLoginOutcome outcome) => outcome switch
    {
        AdminLoginOutcome.Successful => "Successful",
        AdminLoginOutcome.Failed => "Failed",
        AdminLoginOutcome.LockedOut => "Locked out",
        _ => "Unknown"
    };

    public string OutcomeTone(AdminLoginOutcome outcome) => outcome switch
    {
        AdminLoginOutcome.Successful => "success",
        AdminLoginOutcome.Failed => "warning",
        AdminLoginOutcome.LockedOut => "danger",
        _ => "neutral"
    };

    public string OutcomeIcon(AdminLoginOutcome outcome) => outcome switch
    {
        AdminLoginOutcome.Successful => "bi-box-arrow-in-right",
        AdminLoginOutcome.Failed => "bi-shield-exclamation",
        AdminLoginOutcome.LockedOut => "bi-lock",
        _ => "bi-question-circle"
    };

    public string FormatMinutes(int minutes)
    {
        if (minutes <= 0) return "—";
        var normalized = minutes % (24 * 60);
        return $"{normalized / 60:00}:{normalized % 60:00}";
    }

    private AdminLoginMonitoringRequest BuildRequest() => new(
        Days,
        UserId,
        Outcome,
        ReviewOnly,
        MarkWeekends,
        PageNo,
        PageSize);

    private void BuildPresentation()
    {
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Monitoring",
            Title = "Login activity",
            Description = "Review authentication volume, time patterns and sign-ins requiring administrator attention.",
            Icon = "bi-graph-up-arrow",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Audit logs",
                    Href = Url.Page("/Logs/Index", new
                    {
                        area = "Admin",
                        Category = "authentication",
                        From = Snapshot.FromDate.ToString("yyyy-MM-dd"),
                        To = Snapshot.ToDate.ToString("yyyy-MM-dd")
                    }),
                    Icon = "bi-journal-text"
                },
                new AdminPageActionModel
                {
                    Text = "Export CSV",
                    Href = Url.Page("./Logins", "ExportCsv", new
                    {
                        Days,
                        UserId,
                        Outcome,
                        ReviewOnly,
                        MarkWeekends
                    }),
                    Icon = "bi-download",
                    IsPrimary = true
                }
            }
        };

        Metrics = new[]
        {
            new AdminMonitoringMetricModel
            {
                Label = "Successful sign-ins",
                Value = Snapshot.Successful.ToString("N0"),
                Detail = $"Last {Snapshot.Days} days",
                Icon = "bi-check-circle",
                Tone = "success",
                Href = FilterUrl("successful", false),
                IsActive = string.Equals(Outcome, "successful", StringComparison.OrdinalIgnoreCase) && !ReviewOnly
            },
            new AdminMonitoringMetricModel
            {
                Label = "Failed sign-ins",
                Value = Snapshot.Failed.ToString("N0"),
                Detail = "Authentication rejected",
                Icon = "bi-shield-exclamation",
                Tone = "warning",
                Href = FilterUrl("failed", false),
                IsActive = string.Equals(Outcome, "failed", StringComparison.OrdinalIgnoreCase) && !ReviewOnly
            },
            new AdminMonitoringMetricModel
            {
                Label = "Locked out",
                Value = Snapshot.LockedOut.ToString("N0"),
                Detail = "Account lockout events",
                Icon = "bi-lock",
                Tone = "danger",
                Href = FilterUrl("locked-out", false),
                IsActive = string.Equals(Outcome, "locked-out", StringComparison.OrdinalIgnoreCase) && !ReviewOnly
            },
            new AdminMonitoringMetricModel
            {
                Label = "Unique users",
                Value = Snapshot.UniqueUsers.ToString("N0"),
                Detail = $"{Snapshot.UniqueSourceIps:N0} source IPs",
                Icon = "bi-people",
                Tone = "neutral"
            },
            new AdminMonitoringMetricModel
            {
                Label = "Requires review",
                Value = Snapshot.ReviewSignals.ToString("N0"),
                Detail = "Based on configured rules",
                Icon = "bi-eye",
                Tone = Snapshot.ReviewSignals > 0 ? "warning" : "success",
                Href = FilterUrl(Outcome, true),
                IsActive = ReviewOnly
            }
        };
    }

    private string? FilterUrl(string? outcome, bool reviewOnly) => Url.Page("./Logins", new
    {
        Days,
        UserId,
        Outcome = outcome,
        ReviewOnly = reviewOnly,
        MarkWeekends,
        PageNo = 1,
        PageSize
    });

    private static AdminLoginMonitoringSnapshot EmptySnapshot() => new(
        30,
        DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-29)),
        DateOnly.FromDateTime(DateTime.UtcNow.Date),
        TimeSpan.FromHours(8),
        TimeSpan.FromHours(18),
        true,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        false,
        Array.Empty<AdminLoginUserOption>(),
        Array.Empty<AdminLoginTrendPoint>(),
        Array.Empty<AdminLoginEventRow>(),
        Array.Empty<AdminLoginEventRow>(),
        Array.Empty<AdminLoginEventRow>(),
        0,
        1,
        25);
}
