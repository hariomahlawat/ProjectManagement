using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Helpers;
using ProjectManagement.Services.IndustryPartners;

namespace ProjectManagement.Pages.IndustryPartners;

[Authorize(Policy = Policies.IndustryPartners.View)]
public class IndexModel : PageModel
{
    private const int PageSize = 25;
    private const string CreateDraftKey = "IndustryPartners.CreateDraft";
    private const string EditDraftKey = "IndustryPartners.EditDraft";
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

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

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
    public IndustryPartnerDirectorySort SelectedSort { get; private set; }
    public bool CanCreateOrganisation { get; private set; }
    public bool CanEditOrganisation { get; private set; }
    public bool CanDeleteOrganisation { get; private set; }
    public bool CanAddContact { get; private set; }
    public bool CanManageAnyContact { get; private set; }
    public string? CurrentUserId { get; private set; }

    // Projects (among the selected organisation's links and the context project) whose
    // JDPs the current user may change. Mirrors the server-side rule in ProjectJdpAccess.
    public IReadOnlySet<int> JdpManageableProjectIds { get; private set; } = new HashSet<int>();

    public bool CanLinkContextProject =>
        ProjectContext is not null && JdpManageableProjectIds.Contains(ProjectContext.Id);

    public bool CanChangeProjectLink(int projectId) => JdpManageableProjectIds.Contains(projectId);

    // What the user typed when a create or edit was refused, so the form can be shown again
    // with their input instead of empty or reverted fields.
    public OrganisationDraft? CreateDraft { get; private set; }
    public OrganisationDraft? EditDraft { get; private set; }

    public sealed record OrganisationDraft(
        int? PartnerId,
        string? Name,
        string? Location,
        string? Remarks,
        string? ContactName = null,
        string? ContactPhone = null,
        string? ContactEmail = null,
        string? RowVersion = null);

    public string ActiveTab => NormalizeTab(Tab, ProjectContext is not null);

    // The organisation page (OrganisationModel) shows one organisation, not the list.
    protected virtual bool LoadsDirectoryList => true;

    // Where links and redirects for one organisation go: the drawer here, the page there.
    protected virtual string OrganisationViewPage => "./Index";

    public virtual async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanCreateOrganisation = (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.Create)).Succeeded;
        CanDeleteOrganisation = (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.Delete)).Succeeded;
        CanAddContact = (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.AddContact)).Succeeded;
        CanManageAnyContact = (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.ManageAnyContact)).Succeeded;
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        SelectedFilter = ParseFilter(Filter);
        Filter = ToFilterKey(SelectedFilter);
        SelectedSort = ParseSort(Sort);
        Sort = ToSortKey(SelectedSort);
        CreateDraft = ReadDraft(CreateDraftKey);
        EditDraft = ReadDraft(EditDraftKey);
        PageNumber = Math.Max(1, PageNumber);

        if (LoadsDirectoryList)
        {
            SearchResult = await _service.SearchAsync(Q, SelectedFilter, PageNumber, PageSize, SelectedSort, cancellationToken);
            if (PageNumber > SearchResult.TotalPages && SearchResult.Total > 0)
            {
                PageNumber = SearchResult.TotalPages;
                SearchResult = await _service.SearchAsync(Q, SelectedFilter, PageNumber, PageSize, SelectedSort, cancellationToken);
            }
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
            else
            {
                CanEditOrganisation = await CanEditOrganisationAsync(SelectedPartner.Id, cancellationToken);
                if (Edit && !CanEditOrganisation)
                {
                    Edit = false;
                }

                if (EditDraft?.PartnerId != SelectedPartner.Id)
                {
                    EditDraft = null;
                }
            }
        }

        var projectIds = (SelectedPartner?.LinkedProjects.Select(link => link.ProjectId) ?? Enumerable.Empty<int>())
            .Concat(ProjectContext is null ? Enumerable.Empty<int>() : new[] { ProjectContext.Id })
            .ToArray();
        if (projectIds.Length > 0)
        {
            JdpManageableProjectIds = await _service.GetJdpManageableProjectIdsAsync(projectIds, User, cancellationToken);
        }
    }

    // Everything the current search, filter and sort show (all pages), as an Excel workbook.
    // Anyone who can view the directory can export what they can already see.
    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        var filter = ParseFilter(Filter);
        var sort = ParseSort(Sort);
        var rows = await _service.ExportDirectoryAsync(Q, filter, sort, cancellationToken);
        var exportedUtc = DateTimeOffset.UtcNow;
        var file = IndustryPartnerDirectoryExcel.Build(rows, DescribeView(Q, filter, sort), exportedUtc);
        var stamp = ProjectManagement.Infrastructure.IstClock.ToIst(exportedUtc)
            .ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
        return File(file, IndustryPartnerDirectoryExcel.ContentType, $"prism-industry-directory-{stamp}.xlsx");
    }

    public static IReadOnlyList<(string Key, string Label)> FilterOptions { get; } = new[]
    {
        ("all", "All organisations"),
        ("contact", "Contact only"),
        ("associated", "All JDPs"),
        ("current", "Current JDPs"),
        ("past", "Past JDPs")
    };

    public static IReadOnlyList<(string Key, string Label)> SortOptions { get; } = new[]
    {
        ("name", "Name A–Z"),
        ("updated", "Recently updated")
    };

    /// <summary>Page numbers to show around the current page (at most five).</summary>
    public IReadOnlyList<int> PageWindow()
    {
        const int width = 5;
        var total = SearchResult.TotalPages;
        var first = Math.Max(1, Math.Min(SearchResult.Page - width / 2, total - width + 1));
        var last = Math.Min(total, first + width - 1);
        return Enumerable.Range(first, last - first + 1).ToList();
    }

    public string BuildPageUrl(int page) =>
        Url.Page("./Index", new { q = Q, filter = Filter, sort = SortRouteValue, p = page, projectId = ContextProjectId })
        ?? "/IndustryPartners";

    public string BuildExportUrl() =>
        Url.Page("./Index", "Export", new { q = Q, filter = Filter, sort = SortRouteValue })
        ?? "/IndustryPartners?handler=Export";

    // The default order is left out of links, so existing URLs keep their exact shape.
    private string? SortRouteValue => ParseSort(Sort) == IndustryPartnerDirectorySort.Name ? null : ToSortKey(ParseSort(Sort));

    private static string DescribeView(string? query, IndustryPartnerDirectoryFilter filter, IndustryPartnerDirectorySort sort)
    {
        var filterLabel = FilterOptions.First(option => option.Key == ToFilterKey(filter)).Label;
        var sortLabel = SortOptions.First(option => option.Key == ToSortKey(sort)).Label;
        var search = string.IsNullOrWhiteSpace(query) ? "no search" : $"search \"{query.Trim()}\"";
        return $"{filterLabel}, {search}, sorted by {sortLabel}";
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
        [FromForm] string? remarks,
        [FromForm] string? contactName,
        [FromForm] string? contactPhone,
        [FromForm] string? contactEmail,
        CancellationToken cancellationToken)
    {
        if (!await CanCreateOrganisationAsync())
        {
            return Forbid();
        }

        try
        {
            var id = await _service.CreateAsync(
                new CreateIndustryPartnerRequest(
                    Name: name,
                    Location: location,
                    Remarks: remarks,
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
            WriteDraft(CreateDraftKey, new OrganisationDraft(null, name, location, remarks, contactName, contactPhone, contactEmail));
            return RedirectToDirectory(null, null);
        }
        catch (ForbiddenException exception)
        {
            TempData["Error"] = exception.Message;
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
        if (!await CanEditOrganisationAsync(partnerId, cancellationToken))
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

            // After a conflict the form must show the other user's saved values, so the
            // user reapplies their change knowingly; other errors keep what they typed.
            if (!exception.Errors.ContainsKey("concurrency"))
            {
                // The submitted record version travels with the draft: if someone else saved
                // the organisation meanwhile, resubmitting still reports the conflict.
                WriteDraft(EditDraftKey, new OrganisationDraft(partnerId, name, location, remarks, RowVersion: rowVersion));
            }

            return RedirectToDirectory(partnerId, "overview", edit: true);
        }
        catch (KeyNotFoundException exception)
        {
            TempData["Error"] = exception.Message;
            return RedirectToDirectory(null, null);
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
        catch (KeyNotFoundException exception)
        {
            TempData["Error"] = exception.Message;
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
        catch (KeyNotFoundException exception)
        {
            TempData["Error"] = exception.Message;
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
        catch (KeyNotFoundException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToDirectory(partnerId, "contacts");
    }

    // Choosing the primary contact is a change to the organisation record, so it follows the
    // organisation edit rule (its creator, or Admin / HoD / Comdt).
    public async Task<IActionResult> OnPostMakePrimaryContactAsync(
        [FromForm] int partnerId,
        [FromForm] int contactId,
        CancellationToken cancellationToken)
    {
        if (!await CanEditOrganisationAsync(partnerId, cancellationToken))
        {
            return Forbid();
        }

        try
        {
            await _service.SetPrimaryContactAsync(partnerId, contactId, User, cancellationToken);
            TempData["Message"] = "Primary contact updated.";
        }
        catch (KeyNotFoundException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToDirectory(partnerId, "contacts");
    }

    // One contact, or every contact of the organisation (contactId omitted), as a vCard file.
    // Anyone who can view the directory can see these details already.
    public async Task<IActionResult> OnGetContactCardAsync(
        int partnerId,
        int? contactId,
        CancellationToken cancellationToken)
    {
        var partner = await _service.GetAsync(partnerId, cancellationToken);
        if (partner is null)
        {
            return NotFound();
        }

        var contacts = contactId.HasValue
            ? partner.Contacts.Where(contact => contact.Id == contactId.Value).ToList()
            : partner.Contacts.ToList();
        if (contacts.Count == 0)
        {
            return NotFound();
        }

        var card = IndustryPartnerVCard.Build(partner.Name, contacts);
        var fileName = IndustryPartnerVCard.FileName(partner.Name, contactId.HasValue ? contacts[0].Name : null);
        return File(System.Text.Encoding.UTF8.GetBytes(card), IndustryPartnerVCard.ContentType, fileName);
    }

    public async Task<IActionResult> OnPostUploadAttachmentAsync(
        [FromForm] int partnerId,
        CancellationToken cancellationToken)
    {
        if (!await CanEditOrganisationAsync(partnerId, cancellationToken))
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
        catch (KeyNotFoundException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToDirectory(partnerId, "files");
    }

    public async Task<IActionResult> OnPostDeleteAttachmentAsync(
        [FromForm] int partnerId,
        [FromForm] Guid attachmentId,
        CancellationToken cancellationToken)
    {
        if (!await CanEditOrganisationAsync(partnerId, cancellationToken))
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
        catch (KeyNotFoundException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToDirectory(partnerId, "files");
    }

    public async Task<IActionResult> OnGetDownloadAttachmentAsync(
        int partnerId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await _attachmentManager.DownloadAsync(partnerId, attachmentId, cancellationToken);
            return File(file.Stream, file.ContentType, file.FileName);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or FileNotFoundException or DirectoryNotFoundException)
        {
            return NotFound();
        }
    }

    public async Task<IActionResult> OnPostLinkProjectAsync(
        [FromForm] int partnerId,
        [FromForm(Name = "associationProjectId")] int projectId,
        CancellationToken cancellationToken)
    {
        if (!await CanEditOrganisationAsync(partnerId, cancellationToken))
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
        catch (ForbiddenException exception)
        {
            TempData["Error"] = exception.Message;
        }
        catch (KeyNotFoundException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToDirectory(partnerId, "projects");
    }

    public async Task<IActionResult> OnPostUnlinkProjectAsync(
        [FromForm] int partnerId,
        [FromForm(Name = "associationProjectId")] int projectId,
        CancellationToken cancellationToken)
    {
        if (!await CanEditOrganisationAsync(partnerId, cancellationToken))
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
        catch (ForbiddenException exception)
        {
            TempData["Error"] = exception.Message;
        }
        catch (KeyNotFoundException exception)
        {
            TempData["Error"] = exception.Message;
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
        catch (KeyNotFoundException exception)
        {
            TempData["Error"] = exception.Message;
            return RedirectToDirectory(null, null);
        }
    }

    /// <summary>The full organisation page, keeping the list's search, filter and page for the way back.</summary>
    public string BuildOrganisationPageUrl(int id, string? section = null) =>
        Url.Page("./Organisation", new
        {
            id,
            q = Q,
            filter = Filter,
            sort = SortRouteValue,
            p = PageNumber,
            tab = section,
            projectId = ContextProjectId
        }) ?? $"/IndustryPartners/{id}";

    /// <summary>The directory list as the user left it.</summary>
    public string BuildListUrl() =>
        Url.Page("./Index", new { q = Q, filter = Filter, sort = SortRouteValue, p = PageNumber, projectId = ContextProjectId })
        ?? "/IndustryPartners";

    public string BuildContactCardUrl(int partnerId, int? contactId = null) =>
        Url.Page("./Index", "ContactCard", new { partnerId, contactId })
        ?? $"/IndustryPartners?handler=ContactCard&partnerId={partnerId}";

    public string BuildDirectoryUrl(int? id = null, string? tab = null, bool edit = false)
    {
        return Url.Page(id is null ? "./Index" : OrganisationViewPage, new
        {
            id,
            q = Q,
            filter = Filter,
            sort = SortRouteValue,
            p = PageNumber,
            tab,
            edit = edit ? true : (bool?)null,
            projectId = ContextProjectId
        }) ?? "/IndustryPartners";
    }

    // With an organisation: back to where the action came from (drawer or organisation page).
    // Without one (for example after a delete): the directory list.
    private RedirectToPageResult RedirectToDirectory(int? id, string? tab, bool edit = false)
    {
        return RedirectToPage(id is null ? "./Index" : OrganisationViewPage, new
        {
            id,
            q = Q,
            filter = Filter,
            sort = SortRouteValue,
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

    private async Task<bool> CanCreateOrganisationAsync() =>
        (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.Create)).Succeeded;

    private async Task<bool> CanEditOrganisationAsync(
        int partnerId,
        CancellationToken cancellationToken)
    {
        if ((await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.EditAny)).Succeeded)
        {
            return true;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrWhiteSpace(userId) &&
               await _service.IsOwnerAsync(partnerId, userId, cancellationToken);
    }

    private async Task<bool> CanAddContactAsync() =>
        (await _authorizationService.AuthorizeAsync(User, Policies.IndustryPartners.AddContact)).Succeeded;

    // TempData lives in a cookie, and the TempData serializer escapes every non-ASCII
    // character as \uXXXX. A draft is kept only if it fits this budget (measured as stored),
    // so a long notes field can never push request headers past the server limit. A draft is
    // all-or-nothing: the edit form treats every restored field as typed by the user, so a
    // partial draft would blank (and on resubmit erase) the fields it left out. When a draft
    // does not fit, none is kept and the form shows the saved values, with the error message.
    internal const int MaxStoredDraftLength = 8 * 1024;

    private void WriteDraft(string key, OrganisationDraft draft)
    {
        var stored = FitDraftForCookie(draft);
        if (stored is not null)
        {
            TempData[key] = stored;
        }
    }

    /// <summary>The draft as stored in TempData, or null when it would exceed <see cref="MaxStoredDraftLength"/>.</summary>
    internal static string? FitDraftForCookie(OrganisationDraft draft)
    {
        var json = JsonSerializer.Serialize(draft);

        // Measure as the TempData serializer will write it: the JSON string re-encoded.
        return JsonSerializer.Serialize(json).Length <= MaxStoredDraftLength ? json : null;
    }

    private OrganisationDraft? ReadDraft(string key)
    {
        if (TempData[key] is not string json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OrganisationDraft>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SetError(IndustryPartnerValidationException exception)
    {
        TempData["Error"] = string.Join(" ", exception.Errors.SelectMany(pair => pair.Value));
    }

    private static IndustryPartnerDirectorySort ParseSort(string? value) =>
        string.Equals(value?.Trim(), "updated", StringComparison.OrdinalIgnoreCase)
            ? IndustryPartnerDirectorySort.RecentlyUpdated
            : IndustryPartnerDirectorySort.Name;

    private static string ToSortKey(IndustryPartnerDirectorySort sort) =>
        sort == IndustryPartnerDirectorySort.RecentlyUpdated ? "updated" : "name";

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
