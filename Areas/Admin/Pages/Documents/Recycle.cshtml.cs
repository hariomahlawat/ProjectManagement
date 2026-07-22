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
using ProjectManagement.Services.Documents;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.Documents;

[Authorize(Policy = AdminPolicies.RecoveryManage)]
public sealed class RecycleModel : PageModel
{
    private readonly IDocumentRecoveryQueryService _query;
    private readonly IDocumentService _documents;
    private readonly IAdminNavigationUrlBuilder _navigation;
    private readonly IAdminTimeService _time;
    private readonly ILogger<RecycleModel> _logger;
    private readonly AdminRecoveryOptions _options;

    public RecycleModel(
        IDocumentRecoveryQueryService query,
        IDocumentService documents,
        IAdminNavigationUrlBuilder navigation,
        IAdminTimeService time,
        ILogger<RecycleModel> logger,
        IOptions<AdminRecoveryOptions> options)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int? ProjectId { get; set; }
    [BindProperty(SupportsGet = true)] public string? StageCode { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateTime? DeletedFrom { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateTime? DeletedTo { get; set; }
    [BindProperty(SupportsGet = true)] public string? DeletedBy { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty] public DocumentRecoveryCommand Command { get; set; } = new();

    public AdminPageHeaderModel Header { get; private set; } = new();
    public DocumentRecoveryPage Result { get; private set; } =
        new(Array.Empty<DocumentRecoveryRow>(), 0, 0, 1, 25, Array.Empty<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>(), Array.Empty<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>());
    public int MaximumBulkDocuments => _options.MaximumBulkDocuments;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostExecuteAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        var action = Command.Action?.Trim().ToLowerInvariant();
        var ids = ResolveIds(action);
        if (ids.Count == 0)
        {
            SetError("Select at least one recoverable document.");
            return RedirectToPage(RouteValues());
        }

        if (ids.Count > _options.MaximumBulkDocuments)
        {
            SetError($"A maximum of {_options.MaximumBulkDocuments} documents can be processed in one operation.");
            return RedirectToPage(RouteValues());
        }

        var selection = await _query.GetSelectionSummaryAsync(ids, cancellationToken);
        if (selection.EligibleCount == 0)
        {
            SetError("The selected documents are no longer available in the recycle bin.");
            return RedirectToPage(RouteValues());
        }

        if (action is "delete" or "delete-selected")
        {
            if (!Command.Acknowledge || !string.Equals(Command.Confirmation?.Trim(), "DELETE", StringComparison.Ordinal))
            {
                SetError("Type DELETE and acknowledge that permanent deletion cannot be undone.");
                return RedirectToPage(RouteValues());
            }
        }
        else if (action is not ("restore" or "restore-selected"))
        {
            SetError("The requested document recovery operation is not supported.");
            return RedirectToPage(RouteValues());
        }

        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) return Challenge();

        var succeeded = 0;
        var failed = new List<int>();
        foreach (var id in ids)
        {
            try
            {
                if (action is "restore" or "restore-selected")
                    await _documents.RestoreAsync(id, actor, cancellationToken);
                else
                    await _documents.HardDeleteAsync(id, actor, cancellationToken);
                succeeded++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed.Add(id);
                _logger.LogError(exception, "Document recovery operation {Action} failed for document {DocumentId}", action, id);
            }
        }

        var operation = action.StartsWith("restore", StringComparison.Ordinal) ? "restored" : "permanently deleted";
        if (failed.Count == 0)
        {
            TempData[FlashMessageKeys.AdminRecoverySuccess] =
                succeeded == 1 ? $"Document {operation}." : $"{succeeded} documents {operation}.";
        }
        else
        {
            TempData[FlashMessageKeys.AdminRecoveryError] =
                $"{succeeded} document(s) were {operation}; {failed.Count} could not be processed. Review the audit log and retry the failed records.";
        }

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
        ProjectId,
        StageCode,
        DeletedFrom = DeletedFrom?.ToString("yyyy-MM-dd"),
        DeletedTo = DeletedTo?.ToString("yyyy-MM-dd"),
        DeletedBy,
        PageNumber = page,
        PageSize
    }) ?? "#";

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Result = await _query.QueryAsync(new DocumentRecoveryQuery(
            Search,
            ProjectId,
            StageCode,
            DeletedFrom.HasValue ? DateOnly.FromDateTime(DeletedFrom.Value) : null,
            DeletedTo.HasValue ? DateOnly.FromDateTime(DeletedTo.Value) : null,
            DeletedBy,
            PageNumber,
            PageSize), cancellationToken);
        PageNumber = Result.Page;
        PageSize = Result.PageSize;
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Recovery and retention",
            Title = "Document recycle bin",
            Description = "Restore project documents or permanently remove selected records and stored files through a controlled operation.",
            Icon = "bi-recycle",
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

    private IReadOnlyList<int> ResolveIds(string? action)
    {
        IEnumerable<int> source = action is "restore-selected" or "delete-selected"
            ? Command.SelectedIds
            : Command.DocumentId > 0 ? new[] { Command.DocumentId } : Array.Empty<int>();
        return source.Where(id => id > 0).Distinct().Take(_options.MaximumBulkDocuments + 1).ToArray();
    }

    private void NormalizeFilters()
    {
        Search = Normalize(Search, 200);
        StageCode = Normalize(StageCode, 32);
        DeletedBy = Normalize(DeletedBy, 160);
        PageNumber = Math.Max(1, PageNumber);
        PageSize = PageSize is 10 or 25 or 50 or 100 ? PageSize : _options.DefaultPageSize;
        if (DeletedFrom.HasValue) DeletedFrom = DeletedFrom.Value.Date;
        if (DeletedTo.HasValue) DeletedTo = DeletedTo.Value.Date;
        if (DeletedFrom.HasValue && DeletedTo.HasValue && DeletedFrom > DeletedTo)
            (DeletedFrom, DeletedTo) = (DeletedTo, DeletedFrom);
    }

    private object RouteValues() => new
    {
        Search,
        ProjectId,
        StageCode,
        DeletedFrom = DeletedFrom?.ToString("yyyy-MM-dd"),
        DeletedTo = DeletedTo?.ToString("yyyy-MM-dd"),
        DeletedBy,
        PageNumber,
        PageSize
    };

    private void SetError(string message) => TempData[FlashMessageKeys.AdminRecoveryError] = message;
    private static string? Normalize(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    public sealed class DocumentRecoveryCommand
    {
        public string? Action { get; set; }
        public int DocumentId { get; set; }
        public List<int> SelectedIds { get; set; } = new();
        [MaxLength(16)] public string? Confirmation { get; set; }
        public bool Acknowledge { get; set; }
    }
}
