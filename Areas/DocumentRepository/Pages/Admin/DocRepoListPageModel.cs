using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin;

/// <summary>
/// The Repository document types and Repository offices pages, on the shared list page inside
/// Administration (docs/ux/admin-review-3-fixes.md, PR 12). Use and Delete follow the shared rule
/// (<see cref="IReferenceListService"/>); adding, renaming, reordering and switching on and off
/// stay in <see cref="IDocRepoListService"/>, which writes the repository's own audit.
/// Routes, handler names (Create, Update, Toggle, Delete, RenumberOrder) and the form fields of
/// the old page (active, version) still work.
/// </summary>
public abstract class DocRepoListPageModel : AdminReferenceListPageModel
{
    public const string AddDialogId = "docRepoListAddModal";
    public const string EditDialogId = "docRepoListEditModal";

    private readonly IDocRepoListService _lists;

    protected DocRepoListPageModel(IDocRepoListService lists, IReferenceListService referenceLists, IListOrderNoticeService order)
        : base(referenceLists, order)
    {
        _lists = lists ?? throw new ArgumentNullException(nameof(lists));
    }

    protected abstract DocRepoListKind Kind { get; }

    /// <summary>"type" or "office", for the page's words.</summary>
    public abstract string Noun { get; }

    /// <summary>True when the Add dialog should open again to show what was wrong.</summary>
    public bool ReopenAddDialog { get; private set; }

    [BindProperty]
    public CreateInput Input { get; set; } = new();

    /// <summary>The old page's Toggle field (the state wanted).</summary>
    [BindProperty(Name = "active")]
    public bool? LegacyActive { get; set; }

    /// <summary>The old page's row version field (PostgreSQL's xmin as a number).</summary>
    [BindProperty(Name = "version")]
    public uint? LegacyVersion { get; set; }

    public class CreateInput
    {
        [Required, MaxLength(DocRepoListService.NameMaxLength)]
        [Display(Name = "Name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Blank puts the new entry at the end of the list.</summary>
        [Range(-999, 9999)]
        [Display(Name = "Order in lists")]
        public int? SortOrder { get; set; }
    }

    protected override string? Note =>
        $"Lower numbers come first in the repository's forms; equal numbers are listed A to Z. A deactivated {Noun} is no longer offered for new documents; documents already filed under it keep it.";

    protected override AdminReferenceListEditDialog? EditDialog => new("#" + EditDialogId, Kind.ToString(), Noun);

    protected override Func<int, bool?, byte[], CancellationToken, Task<AdminOperationResult>>? Toggle => ToggleAsync;

    protected override AdminPageActionModel? PrimaryAction(string folder) => new()
    {
        Text = Presentation.AddText,
        ModalTarget = "#" + AddDialogId,
        Icon = "bi-plus-lg",
        IsPrimary = true
    };

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await ShowAddAgainAsync(cancellationToken);
        }

        var result = await _lists.CreateAsync(Kind, User, Input.Name, Input.SortOrder, cancellationToken);
        if (result.ErrorCode == "Forbidden")
        {
            return Forbid();
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(Input) + "." + nameof(Input.Name), result.Message);
            return await ShowAddAgainAsync(cancellationToken);
        }

        Flash(AdminOperationResult.Success(result.Message));
        return RedirectToPage();
    }

    /// <summary>Saves the name and order from the Edit dialog (rowVersion) or the old page's row (version).</summary>
    public async Task<IActionResult> OnPostUpdateAsync(int id, string? name, int sortOrder, uint? version, string? rowVersion, CancellationToken cancellationToken) =>
        Finish(await _lists.UpdateAsync(Kind, User, id, name, sortOrder, version ?? ToXmin(DecodeRowVersion(rowVersion)), cancellationToken));

    public override Task<IActionResult> OnPostToggleAsync(int id, bool? isActive, string? rowVersion, CancellationToken cancellationToken) =>
        base.OnPostToggleAsync(id, isActive ?? LegacyActive, rowVersion ?? FromLegacyVersion(), cancellationToken);

    public override Task<IActionResult> OnPostDeleteAsync(int id, string? rowVersion, CancellationToken cancellationToken) =>
        base.OnPostDeleteAsync(id, rowVersion ?? FromLegacyVersion(), cancellationToken);

    private async Task<AdminOperationResult> ToggleAsync(int id, bool? isActive, byte[] rowVersion, CancellationToken cancellationToken)
    {
        var result = await _lists.SetActiveAsync(Kind, User, id, isActive, ToXmin(rowVersion), cancellationToken);
        return result.Succeeded
            ? AdminOperationResult.Success(result.Message)
            : AdminOperationResult.Failure(result.Message, result.ErrorCode ?? "Failed");
    }

    private IActionResult Finish(DocRepoListResult result)
    {
        if (result.ErrorCode == "Forbidden")
        {
            return Forbid();
        }

        Flash(result.Succeeded ? AdminOperationResult.Success(result.Message) : AdminOperationResult.Failure(result.Message, result.ErrorCode ?? "Failed"));
        return RedirectToPage();
    }

    private async Task<IActionResult> ShowAddAgainAsync(CancellationToken cancellationToken)
    {
        ReopenAddDialog = true;
        await LoadListAsync(cancellationToken);
        return Page();
    }

    // The list carries xmin as four bytes (base64), like every other list's row version.
    private static uint? ToXmin(byte[] rowVersion) =>
        rowVersion.Length == sizeof(uint) ? BitConverter.ToUInt32(rowVersion) : null;

    private string? FromLegacyVersion() =>
        LegacyVersion is uint version ? Convert.ToBase64String(BitConverter.GetBytes(version)) : null;
}
