using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services;
using ProjectManagement.Services.Approvals;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Approvals.Pending;

[Authorize(Roles = "Admin,HoD")]
public sealed class IndexModel : PageModel
{
    private const int DefaultPageSize = 40;
    private readonly IApprovalQueueService _queueService;
    private readonly IClock _clock;

    public IndexModel(IApprovalQueueService queueService, IClock clock)
    {
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    [BindProperty(SupportsGet = true)] public string? Type { get; set; }
    [BindProperty(SupportsGet = true)] public string? Module { get; set; }
    [BindProperty(SupportsGet = true)] public string? Readiness { get; set; }
    [BindProperty(SupportsGet = true)] public string? Age { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Success { get; set; }

    public IReadOnlyList<ApprovalQueueGroupVm> ProjectGroups { get; private set; } = Array.Empty<ApprovalQueueGroupVm>();
    public IReadOnlyList<ApprovalQueueGroupVm> StandaloneGroups { get; private set; } = Array.Empty<ApprovalQueueGroupVm>();
    public IReadOnlyList<SelectListItem> TypeOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> ModuleOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> AgeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public int TotalPendingCount { get; private set; }
    public int ReadyCount { get; private set; }
    public int WaitingCount { get; private set; }
    public int BlockedCount { get; private set; }
    public int AttentionCount { get; private set; }
    public int FilteredCount { get; private set; }
    public int TotalPages { get; private set; }
    public int PageStart => FilteredCount == 0 ? 0 : ((PageNumber - 1) * DefaultPageSize) + 1;
    public int PageEnd => Math.Min(PageNumber * DefaultPageSize, FilteredCount);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    public bool HasResults => FilteredCount > 0;
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(Type)
        || !string.IsNullOrWhiteSpace(Module)
        || !string.IsNullOrWhiteSpace(Readiness)
        || !string.IsNullOrWhiteSpace(Age)
        || !string.IsNullOrWhiteSpace(Search);

    public string? SuccessMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        SuccessMessage = TempData["Success"] as string ?? Success?.ToLowerInvariant() switch
        {
            "approved" => "Request approved successfully.",
            "rejected" => "Request rejected successfully.",
            _ => null
        };
        var parsedType = ParseEnum<ApprovalQueueType>(Type);
        var parsedModule = ParseEnum<ApprovalQueueModule>(Module);
        var parsedReadiness = ParseEnum<ApprovalReadiness>(Readiness);
        var now = _clock.UtcNow;

        var items = await _queueService.GetPendingAsync(
            new ApprovalQueueQuery
            {
                Type = parsedType,
                Module = parsedModule,
                // Readiness is applied after the summary is calculated so the
                // KPI strip remains a stable view of the selected module/search/age.
                Readiness = null,
                Search = Search
            },
            User,
            cancellationToken);

        var summaryItems = ApplyAgeFilter(items, Age, now).ToList();
        BuildKpis(summaryItems, now);

        IEnumerable<ApprovalQueueItemVm> readinessFiltered = summaryItems;
        if (parsedReadiness.HasValue)
        {
            readinessFiltered = readinessFiltered.Where(item => item.Readiness == parsedReadiness.Value);
        }

        var filtered = readinessFiltered
            .OrderBy(item => ReadinessOrder(item.Readiness))
            .ThenBy(item => item.ProjectName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.WorkflowOrder ?? int.MaxValue)
            .ThenBy(item => item.RequestedAtUtc)
            .ToList();

        FilteredCount = filtered.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(FilteredCount / (double)DefaultPageSize));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);

        var pageItems = filtered
            .Skip((PageNumber - 1) * DefaultPageSize)
            .Take(DefaultPageSize)
            .Select(AddDetailsUrl)
            .ToArray();

        ProjectGroups = pageItems
            .Where(item => item.ProjectId.HasValue)
            .GroupBy(item => new { item.ProjectId, item.ProjectName, item.WorkflowVersion })
            .Select(group => BuildGroup(
                group.Key.ProjectId,
                group.Key.ProjectName ?? $"Project #{group.Key.ProjectId}",
                group.Count() == 1 ? GetTypeLabel(group.First().ApprovalType) : $"{group.Count()} pending requests",
                group.Key.WorkflowVersion,
                group.OrderBy(item => item.WorkflowOrder ?? int.MaxValue).ThenBy(item => item.RequestedAtUtc).ToArray()))
            .OrderBy(group => GroupReadinessOrder(group))
            .ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        StandaloneGroups = pageItems
            .Where(item => !item.ProjectId.HasValue)
            .GroupBy(item => item.Module)
            .Select(group => BuildGroup(
                null,
                GetModuleLabel(group.Key),
                $"{group.Count()} pending request{(group.Count() == 1 ? string.Empty : "s")}",
                null,
                group.OrderBy(item => ReadinessOrder(item.Readiness)).ThenBy(item => item.RequestedAtUtc).ToArray()))
            .OrderBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        TypeOptions = BuildTypeOptions(parsedType);
        ModuleOptions = BuildModuleOptions(parsedModule);
        AgeOptions = BuildAgeOptions(Age);
    }

    public string GetTypeLabel(ApprovalQueueType type) => type switch
    {
        ApprovalQueueType.StageChange => "Stage update",
        ApprovalQueueType.ProjectMeta => "Project information",
        ApprovalQueueType.PlanApproval => "Timeline plan",
        ApprovalQueueType.DocRequest => "Project document",
        ApprovalQueueType.TotRequest => "Transfer of Technology",
        ApprovalQueueType.ProliferationYearly => "Yearly proliferation",
        ApprovalQueueType.ProliferationGranular => "Unit-wise proliferation",
        ApprovalQueueType.ActivityDelete => "Activity deletion",
        ApprovalQueueType.TrainingDelete => "Training deletion",
        ApprovalQueueType.RepositoryDocumentDelete => "Repository document deletion",
        _ => SplitEnumLabel(type.ToString())
    };

    public string GetModuleLabel(ApprovalQueueModule module) => module switch
    {
        ApprovalQueueModule.Projects => "Projects",
        ApprovalQueueModule.ProjectOfficeReports => "Project Office Reports",
        ApprovalQueueModule.Activities => "Institutional Activities",
        ApprovalQueueModule.DocumentRepository => "Document Repository",
        _ => SplitEnumLabel(module.ToString())
    };

    public string GetReadinessLabel(ApprovalReadiness readiness) => readiness switch
    {
        ApprovalReadiness.Ready => "Ready",
        ApprovalReadiness.Waiting => "Waiting",
        ApprovalReadiness.Blocked => "Blocked",
        ApprovalReadiness.Superseded => "Superseded",
        ApprovalReadiness.Stale => "Needs review",
        _ => readiness.ToString()
    };

    public string GetReadinessIcon(ApprovalReadiness readiness) => readiness switch
    {
        ApprovalReadiness.Ready => "bi-check2-circle",
        ApprovalReadiness.Waiting => "bi-hourglass-split",
        ApprovalReadiness.Blocked => "bi-exclamation-octagon",
        ApprovalReadiness.Superseded => "bi-files",
        ApprovalReadiness.Stale => "bi-arrow-repeat",
        _ => "bi-circle"
    };

    public string GetTypeIcon(ApprovalQueueType type) => type switch
    {
        ApprovalQueueType.StageChange => "bi-signpost-split",
        ApprovalQueueType.ProjectMeta => "bi-card-checklist",
        ApprovalQueueType.PlanApproval => "bi-calendar-range",
        ApprovalQueueType.DocRequest => "bi-file-earmark-check",
        ApprovalQueueType.TotRequest => "bi-diagram-3",
        ApprovalQueueType.ProliferationYearly or ApprovalQueueType.ProliferationGranular => "bi-globe2",
        ApprovalQueueType.ActivityDelete => "bi-activity",
        ApprovalQueueType.TrainingDelete => "bi-mortarboard",
        ApprovalQueueType.RepositoryDocumentDelete => "bi-folder-x",
        _ => "bi-check2-square"
    };

    public string FormatRequestedAt(DateTimeOffset value)
        => IstClock.ToIst(value).ToString("dd MMM yyyy, h:mm tt", CultureInfo.InvariantCulture);

    public string GetAgeLabel(DateTimeOffset requestedAtUtc)
    {
        var days = GetAgeDays(requestedAtUtc, _clock.UtcNow);
        return days switch
        {
            0 => "Today",
            1 => "1 day",
            _ => $"{days} days"
        };
    }

    public string GetAgeClass(DateTimeOffset requestedAtUtc)
    {
        var days = GetAgeDays(requestedAtUtc, _clock.UtcNow);
        return days >= 8 ? "critical" : days >= 4 ? "warning" : "normal";
    }

    public object PreviousRouteValues => BuildRouteValues(PageNumber - 1);
    public object NextRouteValues => BuildRouteValues(PageNumber + 1);

    private ApprovalQueueItemVm AddDetailsUrl(ApprovalQueueItemVm item)
        => item with
        {
            DetailsUrl = Url.Page(
                "/Approvals/Pending/Details",
                new
                {
                    type = item.ApprovalType.ToString(),
                    id = item.RequestId,
                    returnUrl = Request.Path + Request.QueryString
                })
        };

    private static ApprovalQueueGroupVm BuildGroup(
        int? projectId,
        string title,
        string? subtitle,
        string? workflowVersion,
        IReadOnlyList<ApprovalQueueItemVm> items)
        => new(
            projectId,
            title,
            subtitle,
            workflowVersion,
            items,
            items.Count(item => item.Readiness == ApprovalReadiness.Ready),
            items.Count(item => item.Readiness == ApprovalReadiness.Waiting),
            items.Count(item => item.Readiness is ApprovalReadiness.Blocked or ApprovalReadiness.Stale));

    private object BuildRouteValues(int page)
        => new { Type, Module, Readiness, Age, Search, PageNumber = page };

    private static IEnumerable<ApprovalQueueItemVm> ApplyAgeFilter(
        IEnumerable<ApprovalQueueItemVm> source,
        string? age,
        DateTimeOffset now)
        => age?.ToLowerInvariant() switch
        {
            "today" => source.Where(item => GetAgeDays(item.RequestedAtUtc, now) == 0),
            "1-3" => source.Where(item => GetAgeDays(item.RequestedAtUtc, now) is >= 1 and <= 3),
            "4-7" => source.Where(item => GetAgeDays(item.RequestedAtUtc, now) is >= 4 and <= 7),
            "over7" => source.Where(item => GetAgeDays(item.RequestedAtUtc, now) >= 8),
            _ => source
        };

    private static int GetAgeDays(DateTimeOffset requestedAtUtc, DateTimeOffset nowUtc)
    {
        var requestedDate = DateOnly.FromDateTime(IstClock.ToIst(requestedAtUtc).DateTime);
        var currentDate = DateOnly.FromDateTime(IstClock.ToIst(nowUtc).DateTime);
        return Math.Max(0, currentDate.DayNumber - requestedDate.DayNumber);
    }

    private void BuildKpis(IReadOnlyList<ApprovalQueueItemVm> items, DateTimeOffset now)
    {
        TotalPendingCount = items.Count;
        ReadyCount = items.Count(item => item.Readiness == ApprovalReadiness.Ready);
        WaitingCount = items.Count(item => item.Readiness == ApprovalReadiness.Waiting);
        BlockedCount = items.Count(item => item.Readiness == ApprovalReadiness.Blocked);
        AttentionCount = items.Count(item => GetAgeDays(item.RequestedAtUtc, now) >= 8 || item.Readiness is ApprovalReadiness.Blocked or ApprovalReadiness.Stale);
    }

    private IReadOnlyList<SelectListItem> BuildTypeOptions(ApprovalQueueType? selected)
    {
        var values = new List<SelectListItem>
        {
            new() { Value = string.Empty, Text = "All request types", Selected = !selected.HasValue }
        };
        values.AddRange(Enum.GetValues<ApprovalQueueType>().Select(value => new SelectListItem
        {
            Value = value.ToString(),
            Text = GetTypeLabel(value),
            Selected = selected == value
        }));
        return values;
    }

    private IReadOnlyList<SelectListItem> BuildModuleOptions(ApprovalQueueModule? selected)
    {
        var values = new List<SelectListItem>
        {
            new() { Value = string.Empty, Text = "All modules", Selected = !selected.HasValue }
        };
        values.AddRange(Enum.GetValues<ApprovalQueueModule>().Select(value => new SelectListItem
        {
            Value = value.ToString(),
            Text = GetModuleLabel(value),
            Selected = selected == value
        }));
        return values;
    }

    private static IReadOnlyList<SelectListItem> BuildAgeOptions(string? selected) => new List<SelectListItem>
    {
        new() { Value = string.Empty, Text = "Any age", Selected = string.IsNullOrWhiteSpace(selected) },
        new() { Value = "today", Text = "Today", Selected = selected == "today" },
        new() { Value = "1-3", Text = "1–3 days", Selected = selected == "1-3" },
        new() { Value = "4-7", Text = "4–7 days", Selected = selected == "4-7" },
        new() { Value = "over7", Text = "Over 7 days", Selected = selected == "over7" }
    };

    private static TEnum? ParseEnum<TEnum>(string? value) where TEnum : struct
        => string.IsNullOrWhiteSpace(value)
            ? null
            : Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : null;

    private static int ReadinessOrder(ApprovalReadiness readiness) => readiness switch
    {
        ApprovalReadiness.Ready => 0,
        ApprovalReadiness.Waiting => 1,
        ApprovalReadiness.Blocked => 2,
        ApprovalReadiness.Stale => 3,
        ApprovalReadiness.Superseded => 4,
        _ => 5
    };

    private static int GroupReadinessOrder(ApprovalQueueGroupVm group)
        => group.ReadyCount > 0 ? 0 : group.WaitingCount > 0 ? 1 : 2;

    private static string SplitEnumLabel(string value)
        => string.Concat(value.Select((character, index) => index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));
}
