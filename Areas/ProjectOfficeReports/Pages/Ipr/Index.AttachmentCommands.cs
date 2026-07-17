using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Configuration;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

public sealed partial class IndexModel
{
    public async Task<IActionResult> OnPostAttachAsync(CancellationToken cancellationToken)
    {
        Mode = "edit";
        if (UploadInput.RecordId.HasValue)
        {
            Id = UploadInput.RecordId;
        }

        var authResult = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        await EvaluateAuthorizationAsync();
        NormalizeFilters();
        RetainModelStateFor(nameof(UploadInput));

        if (!UploadInput.RecordId.HasValue)
        {
            ModelState.AddModelError(string.Empty, "Select a record before uploading attachments.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        if (UploadInput.File is null || UploadInput.File.Length == 0)
        {
            ModelState.AddModelError($"{nameof(UploadInput)}.{nameof(UploadAttachmentInput.File)}", "Choose a file to upload.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        var userId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        try
        {
            await using var stream = UploadInput.File.OpenReadStream();
            await _writeService.AddAttachmentAsync(
                UploadInput.RecordId.Value,
                stream,
                UploadInput.File.FileName,
                UploadInput.File.ContentType,
                userId,
                cancellationToken);

            TempData["ToastMessage"] = "Attachment uploaded.";
            return RedirectToPage("./Index", GetRouteValues(new { mode = "edit", id = UploadInput.RecordId.Value }, includePage: true, includeModeAndId: false));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveAttachmentAsync(CancellationToken cancellationToken)
    {
        Mode = "edit";
        if (RemoveAttachment.RecordId.HasValue)
        {
            Id = RemoveAttachment.RecordId;
        }

        var authResult = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        await EvaluateAuthorizationAsync();
        NormalizeFilters();

        if (!RemoveAttachment.RecordId.HasValue)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage("./Index", GetRouteValues(includePage: true, includeModeAndId: false));
        }

        var rowVersion = DecodeRowVersion(RemoveAttachment.RowVersion);
        if (rowVersion is null)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage("./Index", GetRouteValues(new { mode = "edit", id = RemoveAttachment.RecordId.Value }, includePage: true, includeModeAndId: false));
        }

        try
        {
            var deleted = await _writeService.DeleteAttachmentAsync(RemoveAttachment.AttachmentId, rowVersion, cancellationToken);
            if (deleted)
            {
                TempData["ToastMessage"] = "Attachment removed.";
            }
            else
            {
                TempData["ToastError"] = "Attachment not found.";
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage("./Index", GetRouteValues(new { mode = "edit", id = RemoveAttachment.RecordId.Value }, includePage: true, includeModeAndId: false));
    }


    private async Task<string?> GetCurrentUserIdAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        return user?.Id;
    }
}
