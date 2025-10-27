using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.MiscActivities;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewMiscActivities)]
public sealed class DetailsModel : PageModel
{
    private readonly IMiscActivityViewService _viewService;
    private readonly IMiscActivityService _activityService;
    private readonly IAuthorizationService _authorizationService;

    public DetailsModel(
        IMiscActivityViewService viewService,
        IMiscActivityService activityService,
        IAuthorizationService authorizationService)
    {
        _viewService = viewService ?? throw new ArgumentNullException(nameof(viewService));
        _activityService = activityService ?? throw new ArgumentNullException(nameof(activityService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    public MiscActivityDetailViewModel? Activity { get; private set; }

    public bool CanManage { get; private set; }

    [BindProperty]
    public MiscActivityMediaUploadViewModel Upload { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Activity = await _viewService.GetDetailAsync(id, cancellationToken);
        if (Activity is null)
        {
            return NotFound();
        }

        Upload = Activity.Upload;
        await PopulatePermissionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(Guid id, CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!CanManage)
        {
            return Forbid();
        }

        Activity = await _viewService.GetDetailAsync(id, cancellationToken);
        if (Activity is null)
        {
            return NotFound();
        }

        Upload = ApplyUploadLimits(Upload, Activity.Upload);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Upload.File is null)
        {
            ModelState.AddModelError(nameof(Upload.File), "Select a file to upload.");
            return Page();
        }

        byte[] activityRowVersion;
        try
        {
            activityRowVersion = Convert.FromBase64String(Upload.RowVersion);
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "The activity has changed. Please reload and try again.");
            return Page();
        }

        await using Stream stream = Upload.File.OpenReadStream();
        var request = new ActivityMediaUploadRequest(
            id,
            activityRowVersion,
            stream,
            Upload.File.FileName,
            Upload.File.ContentType,
            Upload.Caption);

        var result = await _activityService.UploadMediaAsync(request, cancellationToken);
        switch (result.Outcome)
        {
            case ActivityMediaUploadOutcome.Success when result.Media is not null && result.ActivityRowVersion is not null:
                TempData["Flash"] = "Attachment uploaded.";
                return RedirectToPage(new { id });
            case ActivityMediaUploadOutcome.ActivityDeleted:
            case ActivityMediaUploadOutcome.ActivityNotFound:
                ModelState.AddModelError(string.Empty, "The activity could not be found or has been removed.");
                break;
            case ActivityMediaUploadOutcome.TooLarge:
            case ActivityMediaUploadOutcome.UnsupportedType:
            case ActivityMediaUploadOutcome.Invalid:
                if (result.Errors.Count > 0)
                {
                    ModelState.AddModelError(nameof(Upload.File), result.Errors[0]);
                }
                else
                {
                    ModelState.AddModelError(nameof(Upload.File), "Unable to upload the file.");
                }
                break;
            case ActivityMediaUploadOutcome.ConcurrencyConflict:
                ModelState.AddModelError(string.Empty, "The activity was updated by someone else. Please reload and try again.");
                break;
            case ActivityMediaUploadOutcome.Unauthorized:
                return Forbid();
            default:
                ModelState.AddModelError(string.Empty, "Unable to upload the file.");
                break;
        }

        // Refresh detail state after a failed upload to ensure metadata is current.
        Activity = await _viewService.GetDetailAsync(id, cancellationToken);
        if (Activity is not null)
        {
            Upload = ApplyUploadLimits(
                new MiscActivityMediaUploadViewModel
                {
                    File = Upload.File,
                    Caption = Upload.Caption,
                    RowVersion = Activity.Upload.RowVersion
                },
                Activity.Upload);
        }

        return Page();
    }

    private static MiscActivityMediaUploadViewModel ApplyUploadLimits(
        MiscActivityMediaUploadViewModel source,
        MiscActivityMediaUploadViewModel limits)
    {
        return new MiscActivityMediaUploadViewModel
        {
            File = source.File,
            Caption = source.Caption,
            RowVersion = source.RowVersion,
            MaxFileSizeBytes = limits.MaxFileSizeBytes,
            AllowedContentTypes = limits.AllowedContentTypes
        };
    }

    private async Task PopulatePermissionsAsync()
    {
        var authorization = await _authorizationService.AuthorizeAsync(
            User,
            null,
            ProjectOfficeReportsPolicies.ManageMiscActivities);
        CanManage = authorization.Succeeded;
    }
}
