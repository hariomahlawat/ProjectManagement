using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Areas.Admin.Pages.Projects;

[Authorize(Policy = AdminPolicies.RecoveryManage)]
public sealed class TrashModel : PageModel
{
    private readonly IProjectRecoveryQueryService _query;
    private readonly ProjectModerationService _moderation;
    private readonly IAdminAuditService _audit;
    private readonly IAdminNavigationUrlBuilder _navigation;
    private readonly IAdminTimeService _time;
    private readonly ProjectRetentionOptions _retention;

    public TrashModel(
        IProjectRecoveryQueryService query,
        ProjectModerationService moderation,
        IAdminAuditService audit,
        IAdminNavigationUrlBuilder navigation,
        IAdminTimeService time,
        IOptions<ProjectRetentionOptions> retention)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _moderation = moderation ?? throw new ArgumentNullException(nameof(moderation));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _retention = retention?.Value ?? throw new ArgumentNullException(nameof(retention));
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Retention { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty] public ProjectRecoveryCommand Command { get; set; } = new();

    public AdminPageHeaderModel Header { get; private set; } = new();
    public ProjectRecoveryPage<ProjectRecoveryRow> Result { get; private set; } =
        new(Array.Empty<ProjectRecoveryRow>(), 0, 1, 25);
    public int RetentionDays => Math.Max(0, _retention.TrashRetentionDays);
    public bool RemoveAssetsByDefault => _retention.RemoveAssetsOnPurge;
    public int DueCount => Result.Items.Count(row => row.IsPurgeDue);
    public long VisibleStoredBytes => Result.Items.Sum(row => row.StoredBytes);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostExecuteAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        if (Command.ProjectId <= 0 || string.IsNullOrWhiteSpace(Command.Action))
        {
            TempData[FlashMessageKeys.AdminRecoveryError] = "The selected project recovery operation is invalid.";
            return RedirectToPage(RouteValues());
        }

        var preview = await _query.GetPreviewAsync(Command.ProjectId, cancellationToken);
        if (preview is null)
        {
            TempData[FlashMessageKeys.AdminRecoveryError] = "The project is no longer available in Trash.";
            return RedirectToPage(RouteValues());
        }

        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) return Challenge();

        ProjectModerationResult result;
        if (string.Equals(Command.Action, "restore", StringComparison.OrdinalIgnoreCase))
        {
            result = await _moderation.RestoreFromTrashAsync(preview.ProjectId, actor, cancellationToken);
            SetResultMessage(result, $"Restored '{preview.Name}' to the project portfolio.");
            return RedirectToPage(RouteValues());
        }

        if (!string.Equals(Command.Action, "purge", StringComparison.OrdinalIgnoreCase))
        {
            TempData[FlashMessageKeys.AdminRecoveryError] = "The requested project operation is not supported.";
            return RedirectToPage(RouteValues());
        }

        if (preview.PurgeScheduledUtc.HasValue && preview.PurgeScheduledUtc.Value > _time.UtcNow)
        {
            TempData[FlashMessageKeys.AdminRecoveryError] =
                $"Permanent deletion is not available until {_time.FormatIst(preview.PurgeScheduledUtc)}. Restore the project instead or wait for the approved retention period to expire.";
            return RedirectToPage(RouteValues());
        }

        if (!string.Equals(Command.Confirmation?.Trim(), preview.Name, StringComparison.Ordinal))
        {
            TempData[FlashMessageKeys.AdminRecoveryError] = "Type the project name exactly to confirm permanent deletion.";
            return RedirectToPage(RouteValues());
        }
        if (!Command.Acknowledge)
        {
            TempData[FlashMessageKeys.AdminRecoveryError] = "Acknowledge that permanent deletion cannot be undone.";
            return RedirectToPage(RouteValues());
        }
        var reason = Command.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 5)
        {
            TempData[FlashMessageKeys.AdminRecoveryError] = "Record a clear administrative reason for permanent deletion.";
            return RedirectToPage(RouteValues());
        }
        if (reason.Length > 512)
        {
            TempData[FlashMessageKeys.AdminRecoveryError] = "The permanent-deletion reason must be 512 characters or fewer.";
            return RedirectToPage(RouteValues());
        }

        await _audit.RecordAsync(
            new AdminAuditEntry(
                Action: "ProjectPurgeAuthorised",
                EntityType: "Project",
                EntityId: preview.ProjectId.ToString(),
                Before: preview,
                After: new { Command.RemoveAssets, Reason = reason },
                Origin: "Admin.Recovery.ProjectTrash",
                Level: "Warning",
                Message: $"Permanent deletion authorised for project '{preview.Name}'. Reason: {reason}"),
            cancellationToken);

        result = await _moderation.PurgeAsync(preview.ProjectId, actor, Command.RemoveAssets, cancellationToken);
        SetResultMessage(result, $"Permanently deleted '{preview.Name}'.");
        return RedirectToPage(RouteValues());
    }

    public string FormatTime(DateTimeOffset? value) => _time.FormatIst(value);
    public string FormatBytes(long bytes)
    {
        var size = (double)Math.Max(0, bytes);
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var index = 0;
        while (size >= 1024 && index < units.Length - 1) { size /= 1024; index++; }
        return $"{size:0.##} {units[index]}";
    }
    public string PageUrl(int page) => Url.Page(null, new
    {
        Search,
        Retention,
        PageNumber = page,
        PageSize
    }) ?? "#";

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Result = await _query.QueryTrashAsync(
            new ProjectTrashQuery(Search, Retention, PageNumber, PageSize),
            cancellationToken);
        PageNumber = Result.Page;
        PageSize = Result.PageSize;
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Recovery and retention",
            Title = "Project trash",
            Description = $"Restore complete project records or conduct a controlled purge after the {RetentionDays}-day retention period.",
            Icon = "bi-trash3",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Recovery centre",
                    Href = _navigation.GetPath(HttpContext, AdminNavigationKeys.RecoveryCentre),
                    Icon = "bi-arrow-left"
                }
            }
        };
    }

    private void SetResultMessage(ProjectModerationResult result, string success)
    {
        if (result.Status == ProjectModerationStatus.Success)
        {
            TempData[FlashMessageKeys.AdminRecoverySuccess] = success;
            return;
        }
        TempData[FlashMessageKeys.AdminRecoveryError] = result.Error ?? result.Status switch
        {
            ProjectModerationStatus.NotFound => "The project could not be found.",
            ProjectModerationStatus.InvalidState => "The project is no longer in a valid state for this operation.",
            _ => "The project recovery operation could not be completed."
        };
    }

    private void NormalizeFilters()
    {
        Search = Normalize(Search, 160);
        Retention = Normalize(Retention, 32)?.ToLowerInvariant();
        PageNumber = Math.Max(1, PageNumber);
        PageSize = PageSize is 10 or 25 or 50 or 100 ? PageSize : 25;
    }

    private object RouteValues() => new { Search, Retention, PageNumber, PageSize };
    private static string? Normalize(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    public sealed class ProjectRecoveryCommand
    {
        public string? Action { get; set; }
        public int ProjectId { get; set; }
        public string? Confirmation { get; set; }
        [MaxLength(512)] public string? Reason { get; set; }
        public bool RemoveAssets { get; set; }
        public bool Acknowledge { get; set; }
    }
}
