using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.MasterData.ArppReferences;

// The three ARPP value lists on the shared list page (docs/ux/admin-review-3-fixes.md, owner
// decision 1, PR 11), one at a time under a tab strip (?list=cfa|funds|dfpds). Add and Edit stay
// in the dialog on this page (Save); the order is the number people type.
[Authorize(Policy = AdminPolicies.MasterDataManage)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : AdminReferenceListPageModel
{
    public sealed record ArppList(string Key, ArppReferenceDataKind Kind, ReferenceListDefinition Definition, string Label, string KindLabel);

    public static IReadOnlyList<ArppList> AllLists { get; } = new[]
    {
        new ArppList("cfa", ArppReferenceDataKind.Cfa, ReferenceListCatalog.ArppCfa, "CFA", "CFA"),
        new ArppList("funds", ArppReferenceDataKind.Fund, ReferenceListCatalog.ArppFunds, "Funds", "Fund"),
        new ArppList("dfpds", ArppReferenceDataKind.DfpdsSchedule, ReferenceListCatalog.ArppDfpdsSchedules, "DFPDS schedules", "DFPDS schedule")
    };

    private static readonly AdminReferenceListPresentation Shown = new(
        AdminNavigationKeys.ArppReferenceData, "bi-list-check", "Add value", "ARPP entries", "/MasterData/ArppReferences");

    private readonly IArppReferenceDataService _referenceData;

    public IndexModel(IReferenceListService lists, IListOrderNoticeService order, IArppReferenceDataService referenceData)
        : base(lists, order)
    {
        _referenceData = referenceData ?? throw new ArgumentNullException(nameof(referenceData));
    }

    /// <summary>Which list is open: cfa (the default), funds or dfpds.</summary>
    [BindProperty(SupportsGet = true, Name = "list")]
    public string? List { get; set; }

    public ArppList Current => AllLists.FirstOrDefault(item => string.Equals(item.Key, List, StringComparison.OrdinalIgnoreCase)) ?? AllLists[0];

    /// <summary>How many values each list has, for the tab strip.</summary>
    public IReadOnlyDictionary<string, int> Counts { get; private set; } = new Dictionary<string, int>();

    [BindProperty]
    public ReferenceInputModel Input { get; set; } = new();

    protected override ReferenceListDefinition Definition => Current.Definition;
    protected override string? OrderCheckKey => null;
    protected override AdminReferenceListPresentation Presentation => Shown;
    protected override string Title => "ARPP values";
    protected override string? Note => "Lower numbers come first in the ARPP forms; equal numbers are listed A to Z.";
    protected override IReadOnlyDictionary<string, string> ListRouteValues => new Dictionary<string, string> { ["list"] = Current.Key };
    protected override AdminReferenceListEditDialog? EditDialog => new("#arppReferenceModal", ((int)Current.Kind).ToString(), Current.KindLabel);
    protected override Func<int, bool?, byte[], CancellationToken, Task<AdminOperationResult>>? Toggle => SetActiveFromListAsync;

    protected override AdminPageActionModel? PrimaryAction(string folder) => new()
    {
        Text = "Open ARPP register",
        Href = Url.Page("/ARPP/Index", new { area = "ProjectOfficeReports" }),
        Icon = "bi-box-arrow-up-right",
        IsPrimary = true
    };

    protected override async Task OnListLoadedAsync(CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>();
        foreach (var item in AllLists)
        {
            counts[item.Key] = (await Lists.GetTilesAsync(item.Definition.Key, cancellationToken)).All;
        }

        Counts = counts;
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        List = AllLists.FirstOrDefault(item => item.Kind == Input.Kind)?.Key ?? List;
        if (!ModelState.IsValid)
        {
            return await ShowAgainAsync(cancellationToken);
        }

        var result = await _referenceData.SaveAsync(
            new ArppReferenceDataSaveCommand(
                Input.Kind,
                Input.Id,
                Input.Value,
                Input.Description,
                Input.SortOrder,
                Input.RowVersion,
                CurrentUserId(),
                User.Identity?.Name),
            cancellationToken);

        if (!result.Success)
        {
            foreach (var pair in result.FieldErrors)
            {
                var key = pair.Key switch
                {
                    nameof(ArppReferenceDataSaveCommand.Value) => "Input.Value",
                    nameof(ArppReferenceDataSaveCommand.Description) => "Input.Description",
                    nameof(ArppReferenceDataSaveCommand.SortOrder) => "Input.SortOrder",
                    _ => string.Empty
                };
                foreach (var message in pair.Value) ModelState.AddModelError(key, message);
            }
            if (result.FieldErrors.Count == 0) ModelState.AddModelError(string.Empty, result.Message);
            return await ShowAgainAsync(cancellationToken);
        }

        // Admin pages render the namespaced admin keys only. The generic StatusMessage /
        // ErrorMessage keys were never shown here and surfaced later on ARPP and FFC pages.
        TempData[FlashMessageKeys.AdminMasterDataSuccess] = result.Message;
        return RedirectToPage(new { list = Current.Key });
    }

    // Kept for forms from before the shared list page; the list's own Deactivate posts Toggle.
    public async Task<IActionResult> OnPostSetActiveAsync(
        ArppReferenceDataKind kind,
        int id,
        bool isActive,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var result = await _referenceData.SetActiveAsync(
            new ArppReferenceDataActivationCommand(kind, id, isActive, rowVersion, CurrentUserId(), User.Identity?.Name),
            cancellationToken);

        TempData[result.Success ? FlashMessageKeys.AdminMasterDataSuccess : FlashMessageKeys.AdminMasterDataError] = result.Message;
        return RedirectToPage(new { list = AllLists.FirstOrDefault(item => item.Kind == kind)?.Key });
    }

    private async Task<AdminOperationResult> SetActiveFromListAsync(int id, bool? isActive, byte[] rowVersion, CancellationToken cancellationToken)
    {
        if (isActive is not bool target)
        {
            return AdminOperationResult.Failure("Reload the page and try again.", "BadRequest");
        }

        var result = await _referenceData.SetActiveAsync(
            new ArppReferenceDataActivationCommand(Current.Kind, id, target, Convert.ToBase64String(rowVersion), CurrentUserId(), User.Identity?.Name),
            cancellationToken);
        return result.Success ? AdminOperationResult.Success(result.Message) : AdminOperationResult.Failure(result.Message, "Failed");
    }

    private async Task<IActionResult> ShowAgainAsync(CancellationToken cancellationToken)
    {
        await LoadListAsync(cancellationToken);
        ViewData["ReopenReferenceModal"] = true;
        return Page();
    }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    public sealed class ReferenceInputModel
    {
        [Required]
        public ArppReferenceDataKind Kind { get; set; }

        public int? Id { get; set; }

        [Required(ErrorMessage = "Enter the reference value.")]
        [StringLength(200)]
        public string Value { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Description { get; set; }

        [Range(0, 9999)]
        public int SortOrder { get; set; }

        public string? RowVersion { get; set; }
    }
}
