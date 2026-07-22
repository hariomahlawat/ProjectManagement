using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Remarks;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records;

[Authorize]
public sealed class DetailsModel : PageModel
{
    private readonly IFfcRecordWorkspaceService _workspaceService;
    private readonly IFfcRecordCommandService _recordCommandService;
    private readonly IFfcProjectCommandService _projectCommandService;
    private readonly IFfcAttachmentCommandService _attachmentCommandService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly FfcAttachmentOptions _attachmentOptions;

    public DetailsModel(
        IFfcRecordWorkspaceService workspaceService,
        IFfcRecordCommandService recordCommandService,
        IFfcProjectCommandService projectCommandService,
        IFfcAttachmentCommandService attachmentCommandService,
        UserManager<ApplicationUser> userManager,
        IOptions<FfcAttachmentOptions> attachmentOptions)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _recordCommandService = recordCommandService ?? throw new ArgumentNullException(nameof(recordCommandService));
        _projectCommandService = projectCommandService ?? throw new ArgumentNullException(nameof(projectCommandService));
        _attachmentCommandService = attachmentCommandService ?? throw new ArgumentNullException(nameof(attachmentCommandService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _attachmentOptions = attachmentOptions?.Value ?? throw new ArgumentNullException(nameof(attachmentOptions));
    }

    public FfcRecordWorkspaceDto Workspace { get; private set; } = default!;
    public IReadOnlyList<FfcCountryOptionDto> Countries { get; private set; } = Array.Empty<FfcCountryOptionDto>();
    public IReadOnlyList<FfcProjectOptionDto> ProjectOptions { get; private set; } = Array.Empty<FfcProjectOptionDto>();

    [BindProperty]
    public FfcRecordEditorInput RecordInput { get; set; } = new();

    [BindProperty]
    public FfcProjectEditorInput ProjectInput { get; set; } = new();

    [BindProperty]
    public FfcAttachmentEditorInput AttachmentInput { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Editor { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? ProjectId { get; set; }

    public bool CanManage => User.IsInRole("Admin") || User.IsInRole("HoD");
    public long MaxFileSizeBytes => _attachmentOptions.MaxFileSizeBytes;
    public string SafeReturnUrl { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadPageAsync(id, initializeInputs: true, cancellationToken);
        if (!loaded)
        {
            return NotFound();
        }

        if (string.Equals(Editor, "project", StringComparison.OrdinalIgnoreCase) && ProjectId.HasValue)
        {
            var project = Workspace.Projects.FirstOrDefault(item => item.Id == ProjectId.Value);
            if (project is null)
            {
                return NotFound();
            }

            ProjectInput = MapProjectInput(project);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUpdateRecordAsync(
        long id,
        CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        RetainModelStateFor(nameof(RecordInput));
        if (!ModelState.IsValid)
        {
            Editor = "record";
            if (!await LoadPageAsync(id, initializeInputs: false, cancellationToken))
            {
                return NotFound();
            }

            return Page();
        }

        var result = await _recordCommandService.UpdateAsync(
            new FfcRecordUpdateCommand(
                RecordId: id,
                CountryId: RecordInput.CountryId,
                Year: RecordInput.Year,
                IpaCompleted: RecordInput.IpaCompleted,
                IpaDate: RecordInput.IpaDate,
                IpaRemarks: RecordInput.IpaRemarks,
                GslCompleted: RecordInput.GslCompleted,
                GslDate: RecordInput.GslDate,
                GslRemarks: RecordInput.GslRemarks,
                OverallRemarks: RecordInput.OverallRemarks,
                RowVersion: RecordInput.RowVersion),
            cancellationToken);

        if (!result.Success)
        {
            ApplyErrors(result, nameof(RecordInput));
            Editor = "record";
            if (!await LoadPageAsync(id, initializeInputs: false, cancellationToken))
            {
                return NotFound();
            }

            if (result.IsConcurrencyConflict)
            {
                RecordInput.RowVersion = Workspace.RowVersion;
            }

            return Page();
        }

        TempData["StatusMessage"] = result.Message ?? "Record updated.";
        return RedirectToWorkspace(id);
    }

    public async Task<IActionResult> OnPostSaveProjectAsync(
        long id,
        CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        if (!ProjectInput.IsLinkedProject)
        {
            ProjectInput.LinkedProjectId = null;
        }

        RetainModelStateFor(nameof(ProjectInput));
        if (!ModelState.IsValid)
        {
            Editor = "project";
            if (!await LoadPageAsync(id, initializeInputs: false, cancellationToken))
            {
                return NotFound();
            }

            return Page();
        }

        var result = await _projectCommandService.SaveAsync(
            new FfcProjectSaveCommand(
                RecordId: id,
                ProjectId: ProjectInput.Id,
                IsLinkedProject: ProjectInput.IsLinkedProject,
                DisplayName: ProjectInput.DisplayName,
                LinkedProjectId: ProjectInput.LinkedProjectId,
                Quantity: ProjectInput.Quantity,
                Position: ProjectInput.Position,
                DeliveredOn: ProjectInput.DeliveredOn,
                InstalledOn: ProjectInput.InstalledOn,
                ProgressText: ProjectInput.ProgressText,
                RowVersion: ProjectInput.RowVersion,
                Actor: BuildRemarkActorContext()),
            cancellationToken);

        if (!result.Success)
        {
            ApplyErrors(result, nameof(ProjectInput));
            Editor = "project";
            if (!await LoadPageAsync(id, initializeInputs: false, cancellationToken))
            {
                return NotFound();
            }

            if (result.IsConcurrencyConflict && ProjectInput.Id.HasValue)
            {
                var latest = Workspace.Projects.FirstOrDefault(project => project.Id == ProjectInput.Id.Value);
                if (latest is not null)
                {
                    ProjectInput.RowVersion = latest.RowVersion;
                }
            }

            return Page();
        }

        TempData["StatusMessage"] = result.Message ?? "Project saved.";
        return RedirectToWorkspace(id, "projects");
    }

    public async Task<IActionResult> OnPostDeleteProjectAsync(
        long id,
        long projectId,
        string? rowVersion,
        CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        var result = await _projectCommandService.DeleteAsync(
            id,
            projectId,
            rowVersion,
            cancellationToken);

        if (!result.Success)
        {
            TempData["StatusMessage"] = result.Message ?? "Unable to remove the project.";
        }
        else
        {
            TempData["StatusMessage"] = result.Message ?? "Project removed.";
        }

        return RedirectToWorkspace(id, "projects");
    }

    public async Task<IActionResult> OnPostUploadAttachmentAsync(
        long id,
        CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        RetainModelStateFor(nameof(AttachmentInput));
        if (!ModelState.IsValid)
        {
            Editor = "attachment";
            if (!await LoadPageAsync(id, initializeInputs: false, cancellationToken))
            {
                return NotFound();
            }

            return Page();
        }

        var result = await _attachmentCommandService.UploadAsync(
            id,
            AttachmentInput.UploadFile,
            AttachmentInput.Caption,
            cancellationToken);

        if (!result.Success)
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                ModelState.AddModelError(string.Empty, result.Message);
            }

            if (result.FieldErrors is not null)
            {
                foreach (var pair in result.FieldErrors)
                {
                    foreach (var message in pair.Value)
                    {
                        ModelState.AddModelError($"{nameof(AttachmentInput)}.{pair.Key}", message);
                    }
                }
            }

            Editor = "attachment";
            if (!await LoadPageAsync(id, initializeInputs: false, cancellationToken))
            {
                return NotFound();
            }

            return Page();
        }

        TempData["StatusMessage"] = string.IsNullOrWhiteSpace(result.Warning)
            ? result.Message ?? "Attachment uploaded."
            : $"{result.Message} {result.Warning}";
        return RedirectToWorkspace(id, "attachments");
    }

    public async Task<IActionResult> OnPostDeleteAttachmentAsync(
        long id,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        var result = await _attachmentCommandService.DeleteAsync(
            id,
            attachmentId,
            cancellationToken);

        TempData["StatusMessage"] = result.Message ??
            (result.Success ? "Attachment removed." : "Unable to remove the attachment.");
        return RedirectToWorkspace(id, "attachments");
    }

    public async Task<IActionResult> OnPostArchiveAsync(
        long id,
        string? rowVersion,
        CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        var result = await _recordCommandService.ArchiveAsync(id, rowVersion, cancellationToken);
        if (!result.Success)
        {
            TempData["StatusMessage"] = result.Message ?? "Unable to archive the record.";
            return RedirectToWorkspace(id);
        }

        TempData["StatusMessage"] = result.Message ?? "FFC record archived.";
        return LocalRedirect(SafeReturnUrlOrDefault());
    }

    private async Task<bool> LoadPageAsync(
        long id,
        bool initializeInputs,
        CancellationToken cancellationToken)
    {
        var workspace = await _workspaceService.GetAsync(id, cancellationToken);
        if (workspace is null)
        {
            return false;
        }

        Workspace = workspace;
        SafeReturnUrl = ResolveSafeReturnUrl();
        ConfigureBreadcrumb();

        if (CanManage)
        {
            Countries = await _workspaceService.GetCountryOptionsAsync(
                workspace.CountryId,
                cancellationToken);
            ProjectOptions = await _workspaceService.GetProjectOptionsAsync(
                workspace.Projects
                    .Where(project => project.LinkedProjectId.HasValue)
                    .Select(project => project.LinkedProjectId!.Value)
                    .ToArray(),
                cancellationToken);
        }
        else
        {
            Countries = Array.Empty<FfcCountryOptionDto>();
            ProjectOptions = Array.Empty<FfcProjectOptionDto>();
        }

        if (initializeInputs)
        {
            RecordInput = new FfcRecordEditorInput
            {
                Id = workspace.RecordId,
                CountryId = workspace.CountryId,
                Year = workspace.Year,
                IpaCompleted = workspace.Ipa.IsCompleted,
                IpaDate = workspace.Ipa.CompletedOn,
                IpaRemarks = workspace.Ipa.Remarks,
                GslCompleted = workspace.Gsl.IsCompleted,
                GslDate = workspace.Gsl.CompletedOn,
                GslRemarks = workspace.Gsl.Remarks,
                OverallRemarks = workspace.OverallRemarks,
                RowVersion = workspace.RowVersion
            };

            ProjectInput = new FfcProjectEditorInput
            {
                IsLinkedProject = true,
                Quantity = 1,
                Position = FfcUnitPosition.Planned
            };
        }

        return true;
    }

    private FfcProjectEditorInput MapProjectInput(FfcWorkspaceProjectDto project)
        => new()
        {
            Id = project.Id,
            IsLinkedProject = project.LinkedProjectId.HasValue,
            DisplayName = project.FfcName,
            LinkedProjectId = project.LinkedProjectId,
            Quantity = project.Quantity,
            Position = project.Position,
            DeliveredOn = project.DeliveredOn,
            InstalledOn = project.InstalledOn,
            ProgressText = project.CurrentProgress,
            RowVersion = project.RowVersion
        };

    private RemarkActorContext? BuildRemarkActorContext()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var roles = new List<RemarkActorRole>();
        if (User.IsInRole("Admin"))
        {
            roles.Add(RemarkActorRole.Administrator);
        }

        if (User.IsInRole("HoD"))
        {
            roles.Add(RemarkActorRole.HeadOfDepartment);
        }

        if (roles.Count == 0)
        {
            return null;
        }

        var primary = roles.Contains(RemarkActorRole.Administrator)
            ? RemarkActorRole.Administrator
            : RemarkActorRole.HeadOfDepartment;

        return new RemarkActorContext(userId, primary, roles);
    }

    private IActionResult RedirectToWorkspace(long id, string? fragment = null)
    {
        var url = Url.Page(
            "/FFC/Records/Details",
            pageHandler: null,
            values: new
            {
                area = "ProjectOfficeReports",
                id,
                returnUrl = SafeReturnUrlOrDefault()
            },
            protocol: null);

        if (!string.IsNullOrWhiteSpace(fragment))
        {
            url = $"{url}#{fragment}";
        }

        return Redirect(url ?? $"/ProjectOfficeReports/FFC/Records/Details/{id}");
    }

    private string SafeReturnUrlOrDefault()
        => !string.IsNullOrWhiteSpace(SafeReturnUrl)
            ? SafeReturnUrl
            : ResolveSafeReturnUrl();

    private string ResolveSafeReturnUrl()
        => !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? ReturnUrl
            : Url.Page("/FFC/Index", new { area = "ProjectOfficeReports" }) ?? "/ProjectOfficeReports/FFC";

    private void ConfigureBreadcrumb()
    {
        FfcBreadcrumbs.Set(
            ViewData,
            ("FFC Proposals", SafeReturnUrl),
            ($"{Workspace.CountryName} – {Workspace.Year}", null));
    }

    private void RetainModelStateFor(string prefix)
    {
        var prefixWithSeparator = $"{prefix}.";
        var unrelatedKeys = ModelState.Keys
            .Where(key =>
                !string.Equals(key, prefix, StringComparison.Ordinal) &&
                !key.StartsWith(prefixWithSeparator, StringComparison.Ordinal))
            .ToArray();

        foreach (var key in unrelatedKeys)
        {
            ModelState.Remove(key);
        }
    }

    private void ApplyErrors(FfcCommandResult result, string prefix)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            ModelState.AddModelError(string.Empty, result.Message);
        }

        if (result.FieldErrors is null)
        {
            return;
        }

        foreach (var pair in result.FieldErrors)
        {
            var key = string.IsNullOrWhiteSpace(pair.Key)
                ? string.Empty
                : $"{prefix}.{pair.Key}";
            foreach (var message in pair.Value)
            {
                ModelState.AddModelError(key, message);
            }
        }
    }
}
