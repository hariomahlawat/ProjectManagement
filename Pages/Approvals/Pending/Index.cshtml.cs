using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Services.Approvals;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Approvals.Pending;

[Authorize(Roles = "Admin,HoD")]
public class IndexModel : PageModel
{
    private const int DefaultPageSize = 25;
    private readonly IApprovalQueueService _queueService;

    public IndexModel(IApprovalQueueService queueService)
    {
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
    }

    [BindProperty(SupportsGet = true)] public string? Type { get; set; }
    [BindProperty(SupportsGet = true)] public string? Module { get; set; }
    [BindProperty(SupportsGet = true)] public string? Age { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Success { get; set; }

    public string? SuccessMessage => Success?.ToLowerInvariant() switch
    {
        "approved" => "Request approved successfully.",
        "rejected" => "Request rejected successfully.",
        _ => null
    };

    public IReadOnlyList<ApprovalQueueItemVm> Items { get; private set; } = Array.Empty<ApprovalQueueItemVm>();
    public IReadOnlyList<SelectListItem> TypeOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> ModuleOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> AgeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public int TotalPendingCount { get; private set; }
    public string OldestPendingDisplay { get; private set; } = "—";
    public int ProjectsPendingCount { get; private set; }
    public int ProjectOfficePendingCount { get; private set; }
    public string TopTypeDisplay { get; private set; } = "—";
    public string TopTypeMeta { get; private set; } = "No pending items";
    public int OverdueCount { get; private set; }
    public int FilteredCount { get; private set; }
    public int TotalPages { get; private set; }
    public int PageStart => FilteredCount == 0 ? 0 : ((PageNumber - 1) * DefaultPageSize) + 1;
    public int PageEnd => Math.Min(PageNumber * DefaultPageSize, FilteredCount);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(Type) || !string.IsNullOrWhiteSpace(Module) || !string.IsNullOrWhiteSpace(Age) || !string.IsNullOrWhiteSpace(Search);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var parsedType = ParseEnum<ApprovalQueueType>(Type);
        var all = await _queueService.GetPendingAsync(new ApprovalQueueQuery { Type = parsedType, Search = Search }, User, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var filtered = all.AsEnumerable();
        var parsedModule = ParseEnum<ApprovalQueueModule>(Module);
        if (parsedModule.HasValue)
        {
            filtered = filtered.Where(x => x.Module == parsedModule.Value);
        }

        filtered = ApplyAgeFilter(filtered, Age, now);
        var filteredList = filtered.OrderBy(x => x.RequestedAtUtc).ToList();

        BuildKpis(filteredList, now);
        FilteredCount = filteredList.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(FilteredCount / (double)DefaultPageSize));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);

        Items = filteredList
            .Skip((PageNumber - 1) * DefaultPageSize)
            .Take(DefaultPageSize)
            .Select(item => item with
            {
                DetailsUrl = Url.Page("/Approvals/Pending/Details", new
                {
                    type = item.ApprovalType.ToString(),
                    id = item.RequestId,
                    returnUrl = Request.Path + Request.QueryString
                })
            })
            .ToList();

        TypeOptions = BuildTypeOptions(parsedType);
        ModuleOptions = BuildEnumOptions(parsedModule, "All modules", GetModuleLabel);
        AgeOptions = BuildAgeOptions(Age);
    }

    public string GetTypeLabel(ApprovalQueueType type) => type switch
    {
        ApprovalQueueType.StageChange => "Stage change",
        ApprovalQueueType.ProjectMeta => "Project information change",
        ApprovalQueueType.PlanApproval => "Timeline approval",
        ApprovalQueueType.DocRequest => "Document moderation",
        ApprovalQueueType.TotRequest => "Transfer of Technology",
        ApprovalQueueType.ProliferationYearly => "Yearly proliferation",
        ApprovalQueueType.ProliferationGranular => "Unit-wise proliferation",
        _ => SplitEnumLabel(type.ToString())
    };

    public string GetModuleLabel(ApprovalQueueModule module) => module == ApprovalQueueModule.ProjectOfficeReports ? "Project Office" : "Projects";

    public string GetAgeLabel(DateTimeOffset requestedAtUtc)
    {
        var days = Math.Max(0, (int)Math.Floor((DateTimeOffset.UtcNow - requestedAtUtc).TotalDays));
        return days switch
        {
            0 => "Today",
            1 => "1 day",
            _ => $"{days} days"
        };
    }

    public string GetAgeClass(DateTimeOffset requestedAtUtc)
    {
        var days = (DateTimeOffset.UtcNow - requestedAtUtc).TotalDays;
        return days >= 7 ? "approval-age--critical" : days >= 3 ? "approval-age--warning" : "approval-age--normal";
    }

    public object PreviousRouteValues => BuildRouteValues(PageNumber - 1);
    public object NextRouteValues => BuildRouteValues(PageNumber + 1);

    private object BuildRouteValues(int page) => new { Type, Module, Age, Search, PageNumber = page };

    private static IEnumerable<ApprovalQueueItemVm> ApplyAgeFilter(IEnumerable<ApprovalQueueItemVm> source, string? age, DateTimeOffset now)
    {
        return age?.ToLowerInvariant() switch
        {
            "today" => source.Where(x => (now - x.RequestedAtUtc).TotalDays < 1),
            "1-3" => source.Where(x => (now - x.RequestedAtUtc).TotalDays >= 1 && (now - x.RequestedAtUtc).TotalDays < 4),
            "4-7" => source.Where(x => (now - x.RequestedAtUtc).TotalDays >= 4 && (now - x.RequestedAtUtc).TotalDays < 8),
            "over7" => source.Where(x => (now - x.RequestedAtUtc).TotalDays >= 8),
            _ => source
        };
    }

    private static TEnum? ParseEnum<TEnum>(string? value) where TEnum : struct
        => string.IsNullOrWhiteSpace(value) ? null : Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : null;

    private IReadOnlyList<SelectListItem> BuildTypeOptions(ApprovalQueueType? selected)
    {
        var options = new List<SelectListItem> { new() { Value = string.Empty, Text = "All request types", Selected = !selected.HasValue } };
        options.AddRange(Enum.GetValues<ApprovalQueueType>().Select(value => new SelectListItem
        {
            Value = value.ToString(), Text = GetTypeLabel(value), Selected = selected == value
        }));
        return options;
    }

    private static IReadOnlyList<SelectListItem> BuildEnumOptions<TEnum>(TEnum? selected, string placeholder, Func<TEnum, string> label) where TEnum : struct, Enum
    {
        var options = new List<SelectListItem> { new() { Value = string.Empty, Text = placeholder, Selected = !selected.HasValue } };
        options.AddRange(Enum.GetValues<TEnum>().Select(value => new SelectListItem { Value = value.ToString(), Text = label(value), Selected = selected.HasValue && EqualityComparer<TEnum>.Default.Equals(selected.Value, value) }));
        return options;
    }

    private static IReadOnlyList<SelectListItem> BuildAgeOptions(string? selected) => new List<SelectListItem>
    {
        new() { Value = "", Text = "Any age", Selected = string.IsNullOrWhiteSpace(selected) },
        new() { Value = "today", Text = "Today", Selected = selected == "today" },
        new() { Value = "1-3", Text = "1–3 days", Selected = selected == "1-3" },
        new() { Value = "4-7", Text = "4–7 days", Selected = selected == "4-7" },
        new() { Value = "over7", Text = "Over 7 days", Selected = selected == "over7" }
    };

    private static string SplitEnumLabel(string value) => string.Concat(value.Select((ch, index) => index > 0 && char.IsUpper(ch) ? $" {ch}" : ch.ToString()));

    private void BuildKpis(IReadOnlyList<ApprovalQueueItemVm> items, DateTimeOffset now)
    {
        TotalPendingCount = items.Count;
        ProjectsPendingCount = items.Count(x => x.Module == ApprovalQueueModule.Projects);
        ProjectOfficePendingCount = items.Count(x => x.Module == ApprovalQueueModule.ProjectOfficeReports);
        OverdueCount = items.Count(x => (now - x.RequestedAtUtc).TotalDays >= 7);

        if (items.Count == 0) return;
        var oldest = items.Min(x => x.RequestedAtUtc);
        var ageDays = Math.Max(0, (int)Math.Floor((now - oldest).TotalDays));
        OldestPendingDisplay = ageDays == 0 ? "Today" : ageDays == 1 ? "1 day" : $"{ageDays} days";
        var top = items.GroupBy(x => x.ApprovalType).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First();
        TopTypeDisplay = GetTypeLabel(top.Key);
        TopTypeMeta = $"{top.Count()} pending";
    }
}
