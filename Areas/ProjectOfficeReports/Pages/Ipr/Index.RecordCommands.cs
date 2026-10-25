using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Configuration;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

public sealed partial class IndexModel
{
    private static readonly IReadOnlyDictionary<IprValidationCode, string[]> ValidationErrorFieldMap =
        new Dictionary<IprValidationCode, string[]>
        {
            [IprValidationCode.FilingNumberRequired] = new[] { nameof(RecordInput.FilingNumber) },
            [IprValidationCode.TitleRequired] = new[] { nameof(RecordInput.Title) },
            [IprValidationCode.DuplicateFilingNumber] = new[] { nameof(RecordInput.FilingNumber) },
            [IprValidationCode.FiledDateRequired] = new[] { nameof(RecordInput.FiledOn) },
            [IprValidationCode.FiledDateInFuture] = new[] { nameof(RecordInput.FiledOn) },
            [IprValidationCode.GrantDateRequired] = new[] { nameof(RecordInput.GrantedOn) },
            [IprValidationCode.GrantDateInFuture] = new[] { nameof(RecordInput.GrantedOn) },
            [IprValidationCode.GrantDateWithoutFilingDate] = new[] { nameof(RecordInput.FiledOn), nameof(RecordInput.GrantedOn) },
            [IprValidationCode.GrantDateBeforeFilingDate] = new[] { nameof(RecordInput.FiledOn), nameof(RecordInput.GrantedOn) },
            [IprValidationCode.ProjectNotAvailable] = new[] { nameof(RecordInput.ProjectId) }
        };

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        Mode = "create";

        var authResult = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        await EvaluateAuthorizationAsync();
        NormalizeFilters();
        RetainModelStateFor(nameof(Input));

        if (!ModelState.IsValid)
        {
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        if (Input.Type is null)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(RecordInput.Type)}", "Select a type.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        if (Input.Status is null)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(RecordInput.Status)}", "Select a status.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        try
        {
            var entity = ToEntity(Input);
            var created = await _writeService.CreateAsync(entity, cancellationToken);
            return await CompleteSaveAsync(created.Id, "IPR record created.", cancellationToken);
        }
        catch (IprValidationException ex)
        {
            AddInputValidationErrors(ex);
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostEditAsync(CancellationToken cancellationToken)
    {
        Mode = "edit";
        if (Input.Id.HasValue)
        {
            Id = Input.Id;
            SelectedRecordId = Input.Id;
        }

        var authResult = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        await EvaluateAuthorizationAsync();
        NormalizeFilters();
        RetainModelStateFor(nameof(Input));

        if (!ModelState.IsValid)
        {
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        if (!Input.Id.HasValue)
        {
            ModelState.AddModelError(string.Empty, "The record could not be found.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        var rowVersion = DecodeRowVersion(Input.RowVersion);
        if (rowVersion is null)
        {
            ModelState.AddModelError(string.Empty, "We could not verify your request. Please reload and try again.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        try
        {
            var entity = ToEntity(Input);
            entity.RowVersion = rowVersion;
            var updated = await _writeService.UpdateAsync(entity, cancellationToken);
            if (updated is null)
            {
                ModelState.AddModelError(string.Empty, "The record could not be found.");
                await LoadPageAsync(cancellationToken, loadRecordInput: false);
                return Page();
            }

            return await CompleteSaveAsync(updated.Id, "IPR record updated.", cancellationToken);
        }
        catch (IprValidationException ex)
        {
            AddInputValidationErrors(ex);
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var authResult = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        NormalizeFilters();
        await EvaluateAuthorizationAsync();

        var rowVersion = DecodeRowVersion(DeleteRequest.RowVersion);
        if (rowVersion is null)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage("./Index", GetRouteValues(includePage: true, includeModeAndId: false));
        }

        try
        {
            var deleted = await _writeService.DeleteAsync(DeleteRequest.Id, rowVersion, cancellationToken);
            if (deleted)
            {
                TempData["ToastMessage"] = "IPR record deleted.";
            }
            else
            {
                TempData["ToastError"] = "The record could not be found.";
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        Mode = null;
        SelectedRecordId = null;
        Id = null;
        return RedirectToPage("./Index", GetRouteValues(includePage: true, includeModeAndId: false));
    }


    // After a successful save: attach the optional evidence PDF, then show the record in its
    // panel. The record is already saved when the PDF fails (not a PDF, too large), so the
    // editor reopens on it with the reason rather than inviting a duplicate create.
    private async Task<IActionResult> CompleteSaveAsync(int recordId, string savedMessage, CancellationToken cancellationToken)
    {
        var evidenceError = await AttachEvidenceAsync(recordId, cancellationToken);
        if (evidenceError is not null)
        {
            TempData["ToastError"] = $"Record saved, but the PDF was not attached: {evidenceError}";
            return RedirectToPage("./Index", GetRouteValues(new { selectedRecordId = recordId, mode = "edit", id = recordId }, includePage: true, includeModeAndId: false));
        }

        TempData["ToastMessage"] = Input.Evidence is { Length: > 0 } ? $"{savedMessage} PDF attached." : savedMessage;

        // A fix made from the Action list returns to the list, so the next item is one click away.
        if (string.Equals(Tab, "followup", StringComparison.Ordinal))
        {
            return RedirectToPage("./Index", GetRouteValues(new { tab = "followup" }, includePage: false, includeModeAndId: false));
        }

        return RedirectToPage("./Index", GetRouteValues(new { tab = "records", selectedRecordId = recordId }, includePage: true, includeModeAndId: false));
    }

    private async Task<string?> AttachEvidenceAsync(int recordId, CancellationToken cancellationToken)
    {
        var file = Input.Evidence;
        if (file is null || file.Length == 0)
        {
            return null;
        }

        var userId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return "your account could not be identified. Sign in again and upload it from the editor.";
        }

        try
        {
            await using var stream = file.OpenReadStream();
            await _writeService.AddAttachmentAsync(recordId, stream, file.FileName, file.ContentType, userId, cancellationToken);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    private static byte[]? DecodeRowVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IprRecord ToEntity(RecordInput input)
    {
        return new IprRecord
        {
            Id = input.Id ?? 0,
            IprFilingNumber = input.FilingNumber?.Trim() ?? string.Empty,
            Title = input.Title?.Trim(),
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            Type = input.Type ?? IprType.Patent,
            Status = input.Status is IprStatus status && IprStatusPolicy.IsSelectable(status) ? status : IprStatus.Filed,
            FiledBy = string.IsNullOrWhiteSpace(input.FiledBy) ? null : input.FiledBy.Trim(),
            FiledAtUtc = input.FiledOn.HasValue
                ? new DateTimeOffset(input.FiledOn.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                : null,
            GrantedAtUtc = input.Status == IprStatus.Granted && input.GrantedOn.HasValue
                ? new DateTimeOffset(input.GrantedOn.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                : null,
            ProjectId = input.ProjectId
        };
    }


    private void RetainModelStateFor(string propertyName)
    {
        var prefix = $"{propertyName}.";
        var unrelatedKeys = ModelState.Keys
            .Where(key =>
                !string.Equals(key, propertyName, StringComparison.OrdinalIgnoreCase) &&
                !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var key in unrelatedKeys)
        {
            ModelState.Remove(key);
        }
    }

    private void AddInputValidationErrors(IprValidationException exception)
    {
        if (!ValidationErrorFieldMap.TryGetValue(exception.Code, out var fields))
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return;
        }

        foreach (var field in fields)
        {
            ModelState.AddModelError($"{nameof(Input)}.{field}", exception.Message);
        }
    }
}
