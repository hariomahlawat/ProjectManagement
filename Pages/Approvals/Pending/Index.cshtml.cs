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
    // SECTION: Dependencies
    private readonly IApprovalQueueService _queueService;

    public IndexModel(IApprovalQueueService queueService)
    {
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
    }

    // SECTION: Filters
    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    // SECTION: Feedback
    [BindProperty(SupportsGet = true)]
    public string? Success { get; set; }

    public string? SuccessMessage => Success?.ToLowerInvariant() switch
    {
        "approved" => "Approved successfully.",
        "rejected" => "Rejected successfully.",
        _ => null
    };

    // SECTION: View model state
    public IReadOnlyList<ApprovalQueueItemVm> Items { get; private set; } = Array.Empty<ApprovalQueueItemVm>();

    public IReadOnlyList<SelectListItem> TypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    // SECTION: KPI state
    public int TotalPendingCount { get; private set; }

    public string OldestPendingDisplay { get; private set; } = "—";

    public int ProjectsPendingCount { get; private set; }

    public int ProjectOfficePendingCount { get; private set; }

    public string TopTypeDisplay { get; private set; } = "—";

    public string TopTypeMeta { get; private set; } = "No pending items";

    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(Type) || !string.IsNullOrWhiteSpace(Search);

    public string FilterContextLabel => HasActiveFilters ? "Filtered by current criteria" : "All pending approvals";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var parsedType = ParseEnum<ApprovalQueueType>(Type);

        var query = new ApprovalQueueQuery
        {
            Type = parsedType,
            Search = Search
        };

        var items = await _queueService.GetPendingAsync(query, User, cancellationToken);
        Items = items
            .Select(item => item with
            {
                DetailsUrl = Url.Page("/Approvals/Pending/Details", new
                {
                    type = item.ApprovalType.ToString(),
                    id = item.RequestId
                })
            })
            .OrderBy(item => item.RequestedAtUtc)
            .ToList();

        TypeOptions = BuildEnumOptions<ApprovalQueueType>(parsedType, "All types");

        BuildKpis(Items);
    }

    // SECTION: Helpers
    public string GetTypeLabel(ApprovalQueueType type) => SplitEnumLabel(type.ToString());

    public string GetModuleLabel(ApprovalQueueModule module) => module switch
    {
        ApprovalQueueModule.ProjectOfficeReports => "Project office",
        _ => SplitEnumLabel(module.ToString())
    };

    private static TEnum? ParseEnum<TEnum>(string? value) where TEnum : struct
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<SelectListItem> BuildEnumOptions<TEnum>(TEnum? selected, string placeholder)
        where TEnum : struct, Enum
    {
        var options = new List<SelectListItem>
        {
            new()
            {
                Value = string.Empty,
                Text = placeholder,
                Selected = !selected.HasValue
            }
        };

        foreach (var value in Enum.GetValues(typeof(TEnum)).Cast<TEnum>())
        {
            options.Add(new SelectListItem
            {
                Value = value.ToString(),
                Text = SplitEnumLabel(value.ToString()),
                Selected = selected.HasValue && EqualityComparer<TEnum>.Default.Equals(selected.Value, value)
            });
        }

        return options;
    }

    private static string SplitEnumLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return string.Concat(value.Select((ch, index) =>
            index > 0 && char.IsUpper(ch) ? $" {ch}" : ch.ToString()));
    }

    private void BuildKpis(IReadOnlyList<ApprovalQueueItemVm> items)
    {
        TotalPendingCount = items.Count;
        ProjectsPendingCount = items.Count(item => item.Module == ApprovalQueueModule.Projects);
        ProjectOfficePendingCount = items.Count(item => item.Module == ApprovalQueueModule.ProjectOfficeReports);

        if (items.Count == 0)
        {
            OldestPendingDisplay = "—";
            TopTypeDisplay = "—";
            TopTypeMeta = "No pending items";
            return;
        }

        var oldestRequested = items.Min(item => item.RequestedAtUtc);
        var ageDays = (int)Math.Floor((DateTimeOffset.UtcNow - oldestRequested).TotalDays);
        OldestPendingDisplay = ageDays <= 0 ? "Today" : $"{ageDays} days";

        var topType = items
            .GroupBy(item => item.ApprovalType)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.ToString())
            .First();

        TopTypeDisplay = SplitEnumLabel(topType.Key.ToString());
        TopTypeMeta = $"{topType.Count()} pending";
    }
}
