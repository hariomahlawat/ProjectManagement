using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Helpers;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.ARPP;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewArpp)]
public sealed class DetailsModel : PageModel
{
    private readonly IArppReadService _readService;
    private readonly IArppCommandService _commandService;
    private readonly IArppAttachmentService _attachmentService;
    private readonly IArppExportService _exportService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly ArppAttachmentOptions _attachmentOptions;

    public DetailsModel(
        IArppReadService readService,
        IArppCommandService commandService,
        IArppAttachmentService attachmentService,
        IArppExportService exportService,
        IAuthorizationService authorizationService,
        IClock clock,
        IOptions<ArppAttachmentOptions> attachmentOptions)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        _attachmentService = attachmentService ?? throw new ArgumentNullException(nameof(attachmentService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _attachmentOptions = attachmentOptions?.Value ?? throw new ArgumentNullException(nameof(attachmentOptions));
    }

    public ArppIssueDetails Issue { get; private set; } = default!;
    public bool CanManage { get; private set; }
    public bool CanVerify { get; private set; }
    public bool CanUnlock { get; private set; }

    public string MaxAttachmentSizeLabel => FileSizeFormatter.FormatFileSize(
        _attachmentOptions.MaxFileSizeBytes > 0
            ? _attachmentOptions.MaxFileSizeBytes
            : 100L * 1024L * 1024L);

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    [BindProperty]
    public string? VerificationNote { get; set; }

    [BindProperty]
    public string UnlockReason { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
        => await LoadPageAsync(id, cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnGetExcelAsync(long id, CancellationToken cancellationToken)
    {
        var issue = await _readService.GetIssueAsync(id, cancellationToken);
        if (issue is null) return NotFound();
        var export = _exportService.BuildExcel(issue, _clock.UtcNow.ToUniversalTime());
        return File(export.Content, export.ContentType, export.FileName);
    }

    public async Task<IActionResult> OnGetAttachmentAsync(long id, CancellationToken cancellationToken)
    {
        var download = await _attachmentService.OpenDownloadAsync(id, cancellationToken);
        if (download is null) return NotFound();

        var fileResult = File(download.Content, download.ContentType, download.DownloadFileName);
        fileResult.EnableRangeProcessing = true;
        return fileResult;
    }

    public async Task<IActionResult> OnPostUploadPdfAsync(long id, CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync(ProjectOfficeReportsPolicies.ManageArpp)) return Forbid();

        var result = await _attachmentService.UploadOrReplaceAsync(
            id,
            UploadFile,
            CurrentUserId(),
            User.Identity?.Name,
            cancellationToken);

        if (!result.Success)
        {
            ApplyAttachmentErrors(result);
            return await LoadPageAsync(id, cancellationToken) ? Page() : NotFound();
        }

        TempData["StatusMessage"] = result.Message;
        if (!string.IsNullOrWhiteSpace(result.Warning)) TempData["ArppWarningMessage"] = result.Warning;
        return RedirectToPage("/ARPP/Details", new { area = "ProjectOfficeReports", id });
    }

    public async Task<IActionResult> OnPostDeletePdfAsync(long id, long attachmentId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync(ProjectOfficeReportsPolicies.ManageArpp)) return Forbid();

        var result = await _attachmentService.DeleteAsync(
            id,
            attachmentId,
            CurrentUserId(),
            User.Identity?.Name,
            cancellationToken);

        TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Message;
        return RedirectToPage("/ARPP/Details", new { area = "ProjectOfficeReports", id });
    }

    public async Task<IActionResult> OnPostVerifyAsync(long id, string issueRowVersion, CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync(ProjectOfficeReportsPolicies.VerifyArpp)) return Forbid();

        var result = await _commandService.VerifyAsync(
            new ArppVerifyCommand(
                id,
                issueRowVersion,
                VerificationNote,
                CurrentUserId(),
                User.Identity?.Name),
            cancellationToken);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = BuildErrorMessage(result);
        }
        else
        {
            TempData["StatusMessage"] = result.Message;
        }

        return RedirectToPage("/ARPP/Details", new { area = "ProjectOfficeReports", id });
    }

    public async Task<IActionResult> OnPostUnlockAsync(long id, string issueRowVersion, CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync(ProjectOfficeReportsPolicies.UnlockArpp)) return Forbid();

        var result = await _commandService.UnlockAsync(
            new ArppUnlockCommand(
                id,
                issueRowVersion,
                UnlockReason,
                CurrentUserId(),
                User.Identity?.Name),
            cancellationToken);

        TempData[result.Success ? "StatusMessage" : "ErrorMessage"] = result.Success
            ? result.Message
            : BuildErrorMessage(result);
        return RedirectToPage("/ARPP/Details", new { area = "ProjectOfficeReports", id });
    }

    private async Task<bool> LoadPageAsync(long id, CancellationToken cancellationToken)
    {
        var issue = await _readService.GetIssueAsync(id, cancellationToken);
        if (issue is null) return false;

        Issue = issue;
        CanManage = await IsAuthorisedAsync(ProjectOfficeReportsPolicies.ManageArpp);
        CanVerify = await IsAuthorisedAsync(ProjectOfficeReportsPolicies.VerifyArpp);
        CanUnlock = await IsAuthorisedAsync(ProjectOfficeReportsPolicies.UnlockArpp);
        return true;
    }

    private async Task<bool> IsAuthorisedAsync(string policy)
        => (await _authorizationService.AuthorizeAsync(User, resource: null, policy)).Succeeded;

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private static string BuildErrorMessage(ArppCommandResult result)
    {
        var messages = result.FieldErrors.Values.SelectMany(values => values).Distinct().ToArray();
        return messages.Length == 0
            ? result.Message ?? "The operation could not be completed."
            : $"{result.Message} {string.Join(" ", messages)}";
    }

    private void ApplyAttachmentErrors(ArppAttachmentCommandResult result)
    {
        ModelState.AddModelError(string.Empty, result.Message);
        if (result.FieldErrors is null) return;

        foreach (var pair in result.FieldErrors)
        {
            var fieldName = string.Equals(pair.Key, "UploadFile", StringComparison.OrdinalIgnoreCase)
                ? nameof(UploadFile)
                : string.Empty;
            foreach (var error in pair.Value) ModelState.AddModelError(fieldName, error);
        }
    }
}
