using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;

namespace ProjectManagement.Areas.Admin.Pages.Lookups;

/// <summary>
/// One of the three lookups (Project types, Sponsoring units, Line directorates): its folder,
/// its list on the shared list page and its commands. The three share one Add and Edit form
/// (<see cref="LookupItemModel"/>) and one in-place switch on the list
/// (docs/ux/admin-review-4-fixes.md, PR 4).
/// </summary>
public sealed record LookupList(
    string Folder,
    ReferenceListDefinition Definition,
    string Icon,
    Func<IAdminMasterDataCommandService, FlatLookupCreateCommand, CancellationToken, Task<AdminOperationResult>> CreateAsync,
    Func<IAdminMasterDataCommandService, FlatLookupUpdateCommand, CancellationToken, Task<AdminOperationResult>> UpdateAsync,
    Func<IAdminMasterDataCommandService, int, bool, byte[], CancellationToken, Task<AdminOperationResult>> SetActiveAsync)
{
    /// <summary>The item in lower case: "project type".</summary>
    public string Label => Definition.Label;

    /// <summary>The item with a capital: "Project type".</summary>
    public string Title => char.ToUpperInvariant(Label[0]) + Label[1..];
}

public static class LookupLists
{
    public static LookupList ProjectTypes { get; } = new(
        "ProjectTypes", ReferenceListCatalog.ProjectTypes, "bi-tags",
        async (commands, command, ct) => Plain(await commands.CreateProjectTypeAsync(command, ct)),
        async (commands, command, ct) => Plain(await commands.UpdateProjectTypeAsync(command, ct)),
        (commands, id, isActive, rowVersion, ct) => commands.SetProjectTypeActiveAsync(id, isActive, rowVersion, ct));

    public static LookupList SponsoringUnits { get; } = new(
        "SponsoringUnits", ReferenceListCatalog.SponsoringUnits, "bi-building",
        async (commands, command, ct) => Plain(await commands.CreateSponsoringUnitAsync(command, ct)),
        async (commands, command, ct) => Plain(await commands.UpdateSponsoringUnitAsync(command, ct)),
        (commands, id, isActive, rowVersion, ct) => commands.SetSponsoringUnitActiveAsync(id, isActive, rowVersion, ct));

    public static LookupList LineDirectorates { get; } = new(
        "LineDirectorates", ReferenceListCatalog.LineDirectorates, "bi-diagram-2",
        async (commands, command, ct) => Plain(await commands.CreateLineDirectorateAsync(command, ct)),
        async (commands, command, ct) => Plain(await commands.UpdateLineDirectorateAsync(command, ct)),
        (commands, id, isActive, rowVersion, ct) => commands.SetLineDirectorateActiveAsync(id, isActive, rowVersion, ct));

    public static IReadOnlyList<LookupList> All { get; } = new[] { ProjectTypes, SponsoringUnits, LineDirectorates };

    /// <summary>
    /// The list a page belongs to, from its path ("/Lookups/ProjectTypes/Edit"). The pages keep
    /// their own files and routes; only the folder tells them apart.
    /// </summary>
    public static LookupList? FromPagePath(string? viewEnginePath)
    {
        var parts = (viewEnginePath ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "Lookups"
            ? All.FirstOrDefault(list => string.Equals(list.Folder, parts[1], StringComparison.Ordinal))
            : null;
    }

    /// <summary>The list of the page being run: its path, or the "page" route value.</summary>
    public static LookupList? FromPage(PageContext? context) =>
        FromPagePath(context?.ActionDescriptor?.ViewEnginePath ?? context?.RouteData?.Values["page"] as string);

    /// <summary>
    /// The in-place switch on the list: the state asked for and the row version shown. A form
    /// without a state (never sent by the list) is refused rather than flipped.
    /// </summary>
    public static Func<int, bool?, byte[], CancellationToken, Task<AdminOperationResult>> Toggle(
        LookupList list,
        IAdminMasterDataCommandService commands) =>
        (id, isActive, rowVersion, cancellationToken) => isActive is bool target
            ? list.SetActiveAsync(commands, id, target, rowVersion, cancellationToken)
            : Task.FromResult(AdminOperationResult.Failure("Reload the page and try again.", "BadRequest"));

    private static AdminOperationResult Plain<T>(AdminOperationResult<T> result) =>
        new(result.Succeeded, result.UserMessage, result.ErrorCode, result.TraceId);
}

/// <summary>
/// The one Add and Edit form of the three lookups. Create.cshtml and Edit.cshtml in each folder
/// use this model; the list comes from the folder. Edit reads the item through the shared list
/// service, and both save through the master-data commands (which check the policy, refuse a
/// duplicate name or an out-of-date version, and audit).
/// </summary>
[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class LookupItemModel : PageModel
{
    private readonly IAdminMasterDataCommandService _commands;
    private readonly IReferenceListService _lists;

    public LookupItemModel(IAdminMasterDataCommandService commands, IReferenceListService lists)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _lists = lists ?? throw new ArgumentNullException(nameof(lists));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public LookupList List { get; private set; } = LookupLists.ProjectTypes;

    public bool IsEdit { get; private set; }

    /// <summary>The page's heading: "Add project type" or "Edit Radar".</summary>
    public string Heading { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken cancellationToken)
    {
        if (!Resolve(id))
        {
            return NotFound();
        }

        if (id is int existing)
        {
            var item = await _lists.GetItemAsync(List.Definition.Key, existing, cancellationToken);
            if (item is null)
            {
                return NotFound();
            }

            Input = new InputModel
            {
                Id = existing,
                Name = item.Name,
                SortOrder = item.SortOrder,
                RowVersion = item.RowVersion
            };
        }

        SetHeading();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken cancellationToken)
    {
        if (!Resolve(id))
        {
            return NotFound();
        }

        SetHeading();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = id is int existing
            ? await List.UpdateAsync(
                _commands,
                new FlatLookupUpdateCommand(existing, Input.Name, Input.SortOrder, DecodeRowVersion(Input.RowVersion)),
                cancellationToken)
            : await List.CreateAsync(_commands, new FlatLookupCreateCommand(Input.Name, 0), cancellationToken);

        if (!result.Succeeded)
        {
            if (result.ErrorCode == "NotFound")
            {
                return NotFound();
            }

            ModelState.AddModelError(
                result.ErrorCode == "DuplicateName" || !IsEdit ? "Input.Name" : string.Empty,
                result.UserMessage ?? $"The {List.Label} could not be saved.");
            return Page();
        }

        TempData[FlashMessageKeys.AdminMasterDataSuccess] = result.UserMessage;
        return RedirectToPage("./Index");
    }

    private bool Resolve(int? id)
    {
        if (LookupLists.FromPage(PageContext) is not { } list)
        {
            return false;
        }

        List = list;
        IsEdit = id.HasValue;
        return true;
    }

    private void SetHeading() =>
        Heading = IsEdit ? $"Edit {Input.Name}" : $"Add {List.Label}";

    internal static byte[] DecodeRowVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<byte>();
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }

    public sealed class InputModel
    {
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Kept as it is on Edit; the order changes only on the list (Move up and down).</summary>
        [Range(0, 1000)]
        public int SortOrder { get; set; }

        /// <summary>The row version the form showed (base64).</summary>
        public string? RowVersion { get; set; }
    }
}

/// <summary>
/// The old Deactivate pages of the three lookups. The list now switches an item off and on in
/// place, so opening the old address goes to the list. A form posted from a Deactivate page
/// left open before the change still does what it said (with the version it showed), then
/// returns to the list.
/// </summary>
[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class LookupDeactivateModel : PageModel
{
    private readonly IAdminMasterDataCommandService _commands;

    public LookupDeactivateModel(IAdminMasterDataCommandService commands) =>
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Restore { get; set; }

    [BindProperty]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public IActionResult OnGet() => RedirectToPage("./Index");

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (LookupLists.FromPage(PageContext) is not { } list)
        {
            return NotFound();
        }

        var result = await list.SetActiveAsync(_commands, Id, Restore, RowVersion ?? Array.Empty<byte>(), cancellationToken);
        TempData[result.Succeeded ? FlashMessageKeys.AdminMasterDataSuccess : FlashMessageKeys.AdminMasterDataError] =
            result.UserMessage ?? $"The {list.Label} could not be changed.";
        return RedirectToPage("./Index");
    }
}
