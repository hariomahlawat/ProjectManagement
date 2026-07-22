using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Helpers;
using ProjectManagement.Services.IndustryPartners;

namespace ProjectManagement.Pages.IndustryPartners;

[Authorize(Policy = Policies.IndustryPartners.View)]
public sealed class IndexModel : PageModel
{
    private const int PageSize = 25;
    private readonly IIndustryPartnerService _service;
    private readonly IIndustryPartnerAttachmentManager _attachmentManager;
    private readonly IAuthorizationService _authorizationService;

    public IndexModel(
        IIndustryPartnerService service,
        IIndustryPartnerAttachmentManager attachmentManager,
        IAuthorizationService authorizationService)
    {
        _service = service;
        _attachmentManager = attachmentManager;
        _authorizationService = authorizationService;
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Filter { get; set; } = "all";

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Edit { get; set; }

    [BindProperty(SupportsGet = true, Name = "projectId")]
    public int? ContextProjectId { get; set; }

    public IndustryPartnerSearchResult SearchResult { get; private set; } =
        new(Array.Empty<IndustryPartnerListItem>(), 0, 1, PageSize);

    public IndustryPartnerDto? SelectedPartner { get; private set; }
    public IndustryPartnerProjectContextDto? ProjectContext { get; private set; }
    public IndustryPartnerDirectoryFilter SelectedFilter { get; private set; }
    public bool CanManageOrganisation { get; private set; }
    public bool CanDeleteOrganisation { get; private set; }
    public bool CanAddContact { get; private set; }
    public bool CanManageAnyContact { get; private set; }
    public string? CurrentUserId { get; private set; }

    public string ActiveTab => NormalizeTab(Tab, ProjectContext is not null);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanManageOrganisation = (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.Manage)).Succeeded;
        CanDeleteOrganisation = (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.Delete)).Succeeded;
        CanAddContact = (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.AddContact)).Succeeded;
        CanManageAnyContact = (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.ManageAnyContact)).Succeeded;
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        SelectedFilter = ParseFilter(Filter);
        Filter = ToFilterKey(SelectedFilter);
        PageNumber = Math.Max(1, PageNumber);

        SearchResult = await _service.SearchAsync(Q, SelectedFilter, PageNumber, PageSize, cancellationToken);
        if (PageNumber > SearchResult.TotalPages && SearchResult.Total > 0)
        {
            PageNumber = SearchResult.TotalPages;
            SearchResult = await _service.SearchAsync(Q, SelectedFilter, PageNumber, PageSize, cancellationToken);
        }

        if (ContextProjectId.HasValue)
        {
            ProjectContext = await _service.GetProjectContextAsync(ContextProjectId.Value, cancellationToken);
            if (ProjectContext is null)
            {
                ContextProjectId = null;
            }
        }

        if (Id.HasValue)
        {
            SelectedPartner = await _service.GetAsync(Id.Value, cancellationToken);
            if (SelectedPartner is null)
            {
                Id = null;
            }
        }
    }

    public async Task<IActionResult> OnGetDuplicateSuggestionsAsync(
        string? name,
        CancellationToken cancellationToken)
    {
        var items = await _service.FindDuplicateSuggestionsAsync(name, 5, cancellationToken);
        return new JsonResult(new { items });
    }

    public async Task<IActionResult> OnPostCreatePartnerAsync(
        [FromForm] string name,
        [FromForm] string? location,
        [FromForm] string? contactName,
        [FromForm] string? contactPhone,
        [FromForm] string? contactEmail,
        CancellationToken cancellationToken)
    {
        if (!await CanManageOrganisationAsync())
        {
            return Forbid();
        }

        try
        {
            var id = await _service.CreateAsync(
                new CreateIndustryPartnerRequest(
                    Name: name,
                    Location: location,
                    ContactName: contactName,
                    ContactPhone: contactPhone,
                    ContactEmail: contactEmail,
                    ProjectId: ContextProjectId),
                User,
                cancellationToken);

            TempData["Message"] = ContextProjectId.HasValue
                ? "Organisation added and linked to the project as JDP."
                : "Organisation added to the directory.";
            return RedirectToDirectory(id, ContextProjectId.HasValue ? "projects" : "overview");
        }
        catch (IndustryPartnerValidationException exception)
        {
            SetError(exception);
            return RedirectToDirectory(null, null);
        }
    }

    public async Task<IActionResult> OnPostUpdatePartnerAsync(
        [FromForm] int partnerId,
        [FromForm] string name,
        [FromForm] string? location,
        [FromForm] string? remarks,
        [FromForm] string? rowVersion,
        CancellationToken cancellationToken)
    {
        if (!await CanManageOrganisationAsync())
        {
            return Forbid();
        }

        try
        {
            await _service.UpdateAsync(
                partnerId,
                new UpdateIndustryPartnerRequest(name, location, remarks, rowVersion),
                User,
                cancellationToken);

            TempData["Message"] = "Organisation details updated.";
            return RedirectToDirectory(partnerId, "overview");
        }
        catch (IndustryPartnerValidationException exception)
        {
            SetError(exception);
            return RedirectToDirectory(partnerId, "overview", edit: true);
        }
    }

    public async Task<IActionResult> OnPostAddContactAsync(
        [FromForm] int partnerId,
        [FromForm] string? name,
        [FromForm] string? phone,
        [FromForm] string? email,
        CancellationToken cancellationToken)
    {
        if (!await CanAddContactAsync())
        {
            return Forbid();
        }

        try
        {
            await _service.AddContactAsync(
                partnerId,
                new ContactRequest(name, phone, email),
                User,
                cancellationToken);
            TempData["Message"] = "Contact added.";
        }
        catch (IndustryPartnerValidationException exception)
        {
            SetError(exception);
        }

        return RedirectToDirectory(partnerId, "contacts");
    }

    public async Task<IActionResult> OnPostUpdateContactAsync(
        [FromForm] int partnerId,
        [FromForm] int contactId,
        [FromForm] string? name,
        [FromForm] string? phone,
        [FromForm] string? email,
        [FromForm] string? rowVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await _service.UpdateContactAsync(
                partnerId,
                contactId,
                new ContactRequest(name, phone, email, rowVersion),
                User,
                cancellationToken);
            TempData["Message"] = "Contact updated.";
        }
        catch (ForbiddenException)
        {
            return Forbid();
        }
        catch (IndustryPartnerValidationException exception)
        {
            SetError(exception);
        }

        return RedirectToDirectory(partnerId, "contacts");
    }

    public async Task<IActionResult> OnPostDeleteContactAsync(
        [FromForm] int partnerId,
        [FromForm] int contactId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeleteContactAsync(partnerId, contactId, User, cancellationToken);
            TempData["Message"] = "Contact removed.";
        }
        catch (ForbiddenException)
        {
            return Forbid();
        }
        catch (IndustryPartnerValidationException exception)
        {
            SetError(exception);
        }

        return RedirectToDirectory(partnerId, "contacts");
    }

    public async Task<IActionResult> OnPostUploadAttachmentAsync(
        [FromForm] int partnerId,
        CancellationToken cancellationToken)
    {
        if (!await CanManageOrganisationAsync())
        {
            return Forbid();
        }

        var file = Request.Form.Files.FirstOrDefault();
        if (file is null)
        {
            TempData["Error"] = "Select a file to upload.";
            return RedirectToDirectory(partnerId, "files");
        }

        try
        {
            await _attachmentManager.UploadAsync(partnerId, file, User, cancellationToken);
            TempData["Message"] = "File added.";
        }
        catch (IndustryPartnerValidationException exception)
        {
            SetError(exception);
        }

        return RedirectToDirectory(partnerId, "files");
    }

    public async Task<IActionResult> OnPostDeleteAttachmentAsync(
        [FromForm] int partnerId,
        [FromForm] Guid attachmentId,
        CancellationToken cancellationToken)
    {
        if (!await CanManageOrganisationAsync())
        {
            return Forbid();
        }

        try
        {
            await _attachmentManager.DeleteAsync(partnerId, attachmentId, User, cancellationToken);
            TempData["Message"] = "File removed.";
        }
        catch (IndustryPartnerValidationException exception)
        {
            SetError(exception);
        }

        return RedirectToDirectory(partnerId, "files");
    }

    public async Task<IActionResult> OnGetDownloadAttachmentAsync(
        int partnerId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var file = await _attachmentManager.DownloadAsync(partnerId, attachmentId, cancellationToken);
        return File(file.Stream, file.ContentType, file.FileName);
    }

    public async Task<IActionResult> OnPostLinkProjectAsync(
        [FromForm] int partnerId,
        [FromForm(Name = "associationProjectId")] int projectId,
        CancellationToken cancellationToken)
    {
        if (!await CanManageOrganisationAsync())
        {
            return Forbid();
        }

        try
        {
            await _service.LinkProjectAsync(partnerId, projectId, User, cancellationToken);
            TempData["Message"] = "JDP association added.";
        }
        catch (IndustryPartnerValidationException exception)
        {
            SetError(exception);
        }

        return RedirectToDirectory(partnerId, "projects");
    }

    public async Task<IActionResult> OnPostUnlinkProjectAsync(
        [FromForm] int partnerId,
        [FromForm(Name = "associationProjectId")] int projectId,
        CancellationToken cancellationToken)
    {
        if (!await CanManageOrganisationAsync())
        {
            return Forbid();
        }

        try
        {
            await _service.UnlinkProjectAsync(partnerId, projectId, User, cancellationToken);
            TempData["Message"] = "JDP association removed.";
        }
        catch (IndustryPartnerValidationException exception)
        {
            SetError(exception);
        }

        return RedirectToDirectory(partnerId, "projects");
    }

    public async Task<IActionResult> OnPostDeletePartnerAsync(
        [FromForm] int partnerId,
        CancellationToken cancellationToken)
    {
        if (!(await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.Delete)).Succeeded)
        {
            return Forbid();
        }

        try
        {
            await _service.DeletePartnerAsync(partnerId, User, cancellationToken);
            TempData["Message"] = "Organisation permanently deleted.";
            return RedirectToDirectory(null, null);
        }
        catch (IndustryPartnerValidationException exception)
        {
            SetError(exception);
            return RedirectToDirectory(partnerId, "overview");
        }
    }

    public string BuildDirectoryUrl(int? id = null, string? tab = null, bool edit = false)
    {
        return Url.Page("./Index", new
        {
            id,
            q = Q,
            filter = Filter,
            p = PageNumber,
            tab,
            edit = edit ? true : (bool?)null,
            projectId = ContextProjectId
        }) ?? "/IndustryPartners";
    }

    private RedirectToPageResult RedirectToDirectory(int? id, string? tab, bool edit = false)
    {
        return RedirectToPage(new
        {
            id,
            q = Q,
            filter = Filter,
            p = PageNumber,
            tab,
            edit = edit ? true : (bool?)null,
            projectId = ContextProjectId
        });
    }

    public bool CanModifyContact(IndustryPartnerContactDto contact)
    {
        if (CanManageAnyContact)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(CurrentUserId) &&
               !string.IsNullOrWhiteSpace(contact.CreatedByUserId) &&
               string.Equals(contact.CreatedByUserId, CurrentUserId, StringComparison.Ordinal);
    }

    private async Task<bool> CanManageOrganisationAsync() =>
        (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.Manage)).Succeeded;

    private async Task<bool> CanAddContactAsync() =>
        (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.AddContact)).Succeeded;

    private void SetError(IndustryPartnerValidationException exception)
    {
        TempData["Error"] = string.Join(" ", exception.Errors.SelectMany(pair => pair.Value));
    }

    private static IndustryPartnerDirectoryFilter ParseFilter(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "contact" => IndustryPartnerDirectoryFilter.ContactOnly,
            "associated" => IndustryPartnerDirectoryFilter.JdpAssociated,
            "current" => IndustryPartnerDirectoryFilter.CurrentJdp,
            "past" => IndustryPartnerDirectoryFilter.PastJdp,
            _ => IndustryPartnerDirectoryFilter.All
        };

    private static string ToFilterKey(IndustryPartnerDirectoryFilter filter) => filter switch
    {
        IndustryPartnerDirectoryFilter.ContactOnly => "contact",
        IndustryPartnerDirectoryFilter.JdpAssociated => "associated",
        IndustryPartnerDirectoryFilter.CurrentJdp => "current",
        IndustryPartnerDirectoryFilter.PastJdp => "past",
        _ => "all"
    };

    private static string NormalizeTab(string? tab, bool hasProjectContext) =>
        tab?.Trim().ToLowerInvariant() switch
        {
            "contacts" => "contacts",
            "projects" => "projects",
            "files" => "files",
            _ => hasProjectContext ? "projects" : "overview"
        };
}
