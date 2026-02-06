using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.IndustryPartners;

namespace ProjectManagement.Pages.IndustryPartners;

[Authorize(Policy = Policies.IndustryPartners.View)]
public sealed class IndexModel : PageModel
{
    private readonly IIndustryPartnerService _service;
    private readonly IIndustryPartnerAttachmentManager _attachmentManager;
    private readonly IAuthorizationService _authorizationService;

    public IndexModel(IIndustryPartnerService service, IIndustryPartnerAttachmentManager attachmentManager, IAuthorizationService authorizationService)
    {
        _service = service;
        _attachmentManager = attachmentManager;
        _authorizationService = authorizationService;
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IndustryPartnerSearchResult SearchResult { get; private set; } = new(Array.Empty<IndustryPartnerListItem>(), 0, 1, 50);
    public IndustryPartnerDto? SelectedPartner { get; private set; }
    public bool CanManage { get; private set; }
    public bool CanDelete { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // SECTION: Authorization flags
        CanManage = (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.Manage)).Succeeded;
        CanDelete = (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.Delete)).Succeeded;

        // SECTION: Page data loading
        SearchResult = await _service.SearchAsync(Q, 1, 100, cancellationToken);
        if (Id.HasValue)
        {
            SelectedPartner = await _service.GetAsync(Id.Value, cancellationToken);
        }
        else if (SearchResult.Items.Count > 0)
        {
            var firstId = SearchResult.Items[0].Id;
            SelectedPartner = await _service.GetAsync(firstId, cancellationToken);
            Id = firstId;
        }
    }

    public async Task<IActionResult> OnPostCreatePartnerAsync([FromForm] string name, [FromForm] string? location, [FromForm] string? remarks, CancellationToken cancellationToken)
    {
        await EnsureManageAsync();
        try
        {
            var id = await _service.CreateAsync(new CreateIndustryPartnerRequest(name, location, remarks), User, cancellationToken);
            return RedirectToPage(new { id, q = Q });
        }
        catch (IndustryPartnerValidationException ex)
        {
            TempData["Error"] = string.Join(" ", ex.Errors.SelectMany(x => x.Value));
            return RedirectToPage(new { q = Q });
        }
    }

    public async Task<IActionResult> OnPostUpdateFieldAsync([FromForm] int id, [FromForm] string field, [FromForm] string? value, CancellationToken cancellationToken)
    {
        await EnsureManageAsync();
        try
        {
            await _service.UpdateFieldAsync(id, field, value, User, cancellationToken);
            return new JsonResult(new { ok = true });
        }
        catch (IndustryPartnerValidationException ex)
        {
            Response.StatusCode = 400;
            return new JsonResult(new { ok = false, errors = ex.Errors });
        }
    }

    public async Task<IActionResult> OnPostAddContactAsync([FromForm] int partnerId, [FromForm] string? name, [FromForm] string? phone, [FromForm] string? email, CancellationToken cancellationToken)
    {
        await EnsureManageAsync();
        try
        {
            await _service.AddContactAsync(partnerId, new ContactRequest(name, phone, email), User, cancellationToken);
            return RedirectToPage(new { id = partnerId, q = Q });
        }
        catch (IndustryPartnerValidationException ex)
        {
            TempData["Error"] = string.Join(" ", ex.Errors.SelectMany(x => x.Value));
            return RedirectToPage(new { id = partnerId, q = Q });
        }
    }

    public async Task<IActionResult> OnPostUpdateContactAsync([FromForm] int partnerId, [FromForm] int contactId, [FromForm] string? name, [FromForm] string? phone, [FromForm] string? email, CancellationToken cancellationToken)
    {
        await EnsureManageAsync();
        await _service.UpdateContactAsync(partnerId, contactId, new ContactRequest(name, phone, email), User, cancellationToken);
        return RedirectToPage(new { id = partnerId, q = Q });
    }

    public async Task<IActionResult> OnPostDeleteContactAsync([FromForm] int partnerId, [FromForm] int contactId, CancellationToken cancellationToken)
    {
        await EnsureManageAsync();
        await _service.DeleteContactAsync(partnerId, contactId, User, cancellationToken);
        return RedirectToPage(new { id = partnerId, q = Q });
    }

    public async Task<IActionResult> OnPostUploadAttachmentAsync([FromForm] int partnerId, CancellationToken cancellationToken)
    {
        await EnsureManageAsync();
        var file = Request.Form.Files.FirstOrDefault();
        if (file is null)
        {
            TempData["Error"] = "Select a file to upload.";
            return RedirectToPage(new { id = partnerId, q = Q });
        }

        try
        {
            await _attachmentManager.UploadAsync(partnerId, file, User, cancellationToken);
            TempData["Message"] = "Attachment uploaded.";
        }
        catch (IndustryPartnerValidationException ex)
        {
            TempData["Error"] = string.Join(" ", ex.Errors.SelectMany(x => x.Value));
        }

        return RedirectToPage(new { id = partnerId, q = Q });
    }

    public async Task<IActionResult> OnPostDeleteAttachmentAsync([FromForm] int partnerId, [FromForm] Guid attachmentId, CancellationToken cancellationToken)
    {
        await EnsureManageAsync();
        await _attachmentManager.DeleteAsync(partnerId, attachmentId, User, cancellationToken);
        return RedirectToPage(new { id = partnerId, q = Q });
    }

    public async Task<IActionResult> OnGetDownloadAttachmentAsync(int partnerId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var file = await _attachmentManager.DownloadAsync(partnerId, attachmentId, cancellationToken);
        return File(file.Stream, file.ContentType, file.FileName);
    }

    public async Task<IActionResult> OnPostLinkProjectAsync([FromForm] int partnerId, [FromForm] int projectId, CancellationToken cancellationToken)
    {
        await EnsureManageAsync();
        await _service.LinkProjectAsync(partnerId, projectId, User, cancellationToken);
        return RedirectToPage(new { id = partnerId, q = Q });
    }

    public async Task<IActionResult> OnPostUnlinkProjectAsync([FromForm] int partnerId, [FromForm] int projectId, CancellationToken cancellationToken)
    {
        await EnsureManageAsync();
        await _service.UnlinkProjectAsync(partnerId, projectId, User, cancellationToken);
        return RedirectToPage(new { id = partnerId, q = Q });
    }

    public async Task<IActionResult> OnPostDeletePartnerAsync([FromForm] int partnerId, CancellationToken cancellationToken)
    {
        if (!(await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.Delete)).Succeeded)
        {
            return Forbid();
        }

        try
        {
            await _service.DeletePartnerAsync(partnerId, User, cancellationToken);
            TempData["Message"] = "Partner deleted.";
            return RedirectToPage(new { q = Q });
        }
        catch (IndustryPartnerValidationException ex)
        {
            TempData["Error"] = string.Join(" ", ex.Errors.SelectMany(x => x.Value));
            return RedirectToPage(new { id = partnerId, q = Q });
        }
    }

    private async Task EnsureManageAsync()
    {
        if (!(await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.Manage)).Succeeded)
        {
            throw new UnauthorizedAccessException("You are not authorized to manage industry partners.");
        }
    }
}
