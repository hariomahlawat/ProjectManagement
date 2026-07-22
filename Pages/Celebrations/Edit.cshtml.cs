using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Pages.Celebrations;

[Authorize(Policy = Policies.Calendar.ManageCelebrations)]
public sealed class EditModel : PageModel
{
    private readonly ICelebrationAdministrationService _celebrations;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuthorizationService _authorization;

    public EditModel(
        ICelebrationAdministrationService celebrations,
        UserManager<ApplicationUser> users,
        IAuthorizationService authorization)
    {
        _celebrations = celebrations ?? throw new ArgumentNullException(nameof(celebrations));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool CanManageBirthdays { get; private set; }
    public bool CanManageAnniversaries { get; private set; }
    public bool CanManageBothTypes => CanManageBirthdays && CanManageAnniversaries;
    public bool IsBirthdayOnlyEditor => CanManageBirthdays && !CanManageAnniversaries;
    public bool IsAnniversaryOnlyEditor => !CanManageBirthdays && CanManageAnniversaries;
    public bool IsRestrictedEditor => IsBirthdayOnlyEditor || IsAnniversaryOnlyEditor;
    public string RestrictedTypeLabel => IsAnniversaryOnlyEditor ? "Anniversary" : "Birthday";
    public string PermissionNotice => IsAnniversaryOnlyEditor
        ? "Your role can maintain anniversary entries only."
        : "Your role can maintain birthday entries only.";
    public string PageTitle => Input.Id.HasValue
        ? (Input.EventType == CelebrationType.Anniversary ? "Edit anniversary" : "Edit birthday")
        : (CanManageBirthdays, CanManageAnniversaries) switch
        {
            (true, true) => "Add celebration",
            (true, false) => "Add birthday",
            (false, true) => "Add anniversary",
            _ => "Add celebration"
        };

    public async Task<IActionResult> OnGetAsync(Guid? id, CancellationToken cancellationToken)
    {
        await LoadPermissionsAsync();
        if (id is null)
        {
            Input.EventType = CanManageBirthdays ? CelebrationType.Birthday : CelebrationType.Anniversary;
            return Page();
        }

        var item = await _celebrations.GetAsync(id.Value, cancellationToken);
        if (item is null) return NotFound();
        if (!await CanManageAsync(item.EventType)) return Forbid();

        Input = new InputModel
        {
            Id = item.Id,
            EventType = item.EventType,
            Name = item.Name,
            SpouseName = item.SpouseName,
            Day = item.Day,
            Month = item.Month,
            Year = item.Year
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadPermissionsAsync();
        if (!Enum.IsDefined(Input.EventType))
        {
            ModelState.AddModelError("Input.EventType", "Select a valid celebration type.");
            return Page();
        }
        if (!await CanManageAsync(Input.EventType)) return Forbid();

        if (Input.Id.HasValue)
        {
            var stored = await _celebrations.GetAsync(Input.Id.Value, cancellationToken);
            if (stored is null) return NotFound();
            if (!await CanManageAsync(stored.EventType)) return Forbid();
        }

        if (!ModelState.IsValid) return Page();
        var actor = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(actor)) return Forbid();

        var result = await _celebrations.SaveAsync(new CelebrationSaveCommand(
            Input.Id,
            Input.EventType,
            Input.Name,
            Input.SpouseName,
            Input.Day,
            Input.Month,
            Input.Year,
            actor), cancellationToken);

        if (!result.Succeeded)
        {
            var key = result.ErrorCode switch
            {
                "InvalidCelebrationDate" => "Input.Day",
                "CelebrationNameRequired" or "CelebrationNameTooLong" => "Input.Name",
                "CelebrationSpouseNameTooLong" => "Input.SpouseName",
                _ => string.Empty
            };
            ModelState.AddModelError(key, result.UserMessage ?? "The celebration could not be saved.");
            return Page();
        }

        TempData[FlashMessageKeys.CelebrationsSuccess] = result.UserMessage;
        return RedirectToPage("Index");
    }

    private async Task LoadPermissionsAsync()
    {
        CanManageBirthdays = (await _authorization.AuthorizeAsync(User, Policies.Calendar.ManageBirthdays)).Succeeded;
        CanManageAnniversaries = (await _authorization.AuthorizeAsync(User, Policies.Calendar.ManageAnniversaries)).Succeeded;
    }

    private async Task<bool> CanManageAsync(CelebrationType eventType) =>
        (await _authorization.AuthorizeAsync(User, Policies.Calendar.PolicyFor(eventType))).Succeeded;

    public sealed class InputModel
    {
        public Guid? Id { get; set; }
        [Required, Display(Name = "Type")] public CelebrationType EventType { get; set; } = CelebrationType.Birthday;
        [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
        [StringLength(120), Display(Name = "Spouse name (optional)")] public string? SpouseName { get; set; }
        [Range(1, 31)] public byte Day { get; set; }
        [Range(1, 12)] public byte Month { get; set; }
        [Range(1, 9999)] public short? Year { get; set; }
    }
}
