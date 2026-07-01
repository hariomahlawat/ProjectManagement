using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Pages.Celebrations;

[Authorize(Policy = Policies.Calendar.ManageCelebrations)]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuthorizationService _authorization;

    public EditModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IAuthorizationService authorization)
    {
        _db = db;
        _users = users;
        _authorization = authorization;
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

    public string PageTitle
    {
        get
        {
            if (Input.Id is not null)
            {
                return Input.EventType == CelebrationType.Anniversary
                    ? "Edit anniversary"
                    : "Edit birthday";
            }

            return (CanManageBirthdays, CanManageAnniversaries) switch
            {
                (true, true) => "Add celebration",
                (true, false) => "Add birthday",
                (false, true) => "Add anniversary",
                _ => "Add celebration"
            };
        }
    }

    public class InputModel
    {
        public Guid? Id { get; set; }

        [Required]
        [Display(Name = "Type")]
        public CelebrationType EventType { get; set; } = CelebrationType.Birthday;

        [Required]
        [StringLength(120)]
        public string Name { get; set; } = string.Empty;

        [StringLength(120)]
        [Display(Name = "Spouse name (optional)")]
        public string? SpouseName { get; set; }

        [Range(1, 31)]
        public byte Day { get; set; }

        [Range(1, 12)]
        public byte Month { get; set; }

        [Range(1, 9999)]
        public short? Year { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        await LoadPermissionsAsync();

        if (id is null)
        {
            Input.EventType = CanManageBirthdays
                ? CelebrationType.Birthday
                : CelebrationType.Anniversary;
            return Page();
        }

        var celebration = await _db.Celebrations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.DeletedUtc == null);

        if (celebration is null)
        {
            return NotFound();
        }

        if (!await CanManageAsync(celebration.EventType))
        {
            return Forbid();
        }

        Input = new InputModel
        {
            Id = celebration.Id,
            EventType = celebration.EventType,
            Name = celebration.Name,
            SpouseName = celebration.SpouseName,
            Day = celebration.Day,
            Month = celebration.Month,
            Year = celebration.Year
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadPermissionsAsync();

        if (!Enum.IsDefined(Input.EventType))
        {
            ModelState.AddModelError(nameof(Input.EventType), "Select a valid celebration type.");
            return Page();
        }

        if (!await CanManageAsync(Input.EventType))
        {
            return Forbid();
        }

        Celebration? celebration = null;
        if (Input.Id is not null)
        {
            celebration = await _db.Celebrations
                .FirstOrDefaultAsync(x => x.Id == Input.Id && x.DeletedUtc == null);

            if (celebration is null)
            {
                return NotFound();
            }

            // Authorize against the stored type as well as the submitted type. This prevents
            // a type-restricted editor from changing an entry by forging the form payload.
            if (!await CanManageAsync(celebration.EventType))
            {
                return Forbid();
            }
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var maxDay = DateTime.DaysInMonth(Input.Year ?? 2000, Input.Month);
        if (Input.Day > maxDay)
        {
            ModelState.AddModelError(nameof(Input.Day), "The selected date is invalid for this month.");
            return Page();
        }

        if (celebration is null)
        {
            var userId = _users.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Forbid();
            }

            celebration = new Celebration
            {
                Id = Guid.NewGuid(),
                CreatedById = userId,
                CreatedUtc = DateTimeOffset.UtcNow
            };
            _db.Celebrations.Add(celebration);
        }

        celebration.EventType = Input.EventType;
        celebration.Name = Input.Name.Trim();
        celebration.SpouseName = string.IsNullOrWhiteSpace(Input.SpouseName)
            ? null
            : Input.SpouseName.Trim();
        celebration.Day = Input.Day;
        celebration.Month = Input.Month;
        celebration.Year = Input.Year;
        celebration.UpdatedUtc = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync();

        TempData["ok"] = Input.Id is null
            ? (celebration.EventType == CelebrationType.Birthday ? "Birthday added." : "Anniversary added.")
            : (celebration.EventType == CelebrationType.Birthday ? "Birthday updated." : "Anniversary updated.");

        return RedirectToPage("Index");
    }

    private async Task LoadPermissionsAsync()
    {
        CanManageBirthdays = (await _authorization.AuthorizeAsync(
            User,
            Policies.Calendar.ManageBirthdays)).Succeeded;

        CanManageAnniversaries = (await _authorization.AuthorizeAsync(
            User,
            Policies.Calendar.ManageAnniversaries)).Succeeded;
    }

    private async Task<bool> CanManageAsync(CelebrationType eventType)
    {
        var result = await _authorization.AuthorizeAsync(
            User,
            Policies.Calendar.PolicyFor(eventType));

        return result.Succeeded;
    }
}
