using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Partners;
using ProjectManagement.Services;

namespace ProjectManagement.Pages.Projects.Partners;

[Authorize(Policy = Policies.Partners.Manage)]
public class EditModel : PageModel
{
    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public EditModel(ApplicationDbContext db, UserManager<ApplicationUser> users, IClock clock, IAuditService audit)
    {
        _db = db;
        _users = users;
        _clock = clock;
        _audit = audit;
    }

    // SECTION: View state
    public IndustryPartner Partner { get; private set; } = null!;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<string> StatusOptions => IndustryPartnerStatuses.All;

    public IReadOnlyList<string> PartnerTypeOptions => IndustryPartnerTypes.All;

    public sealed class InputModel
    {
        [Required]
        [MaxLength(256)]
        public string FirmName { get; set; } = string.Empty;

        [MaxLength(80)]
        public string? PartnerType { get; set; }

        [MaxLength(512)]
        public string? AddressText { get; set; }

        [MaxLength(120)]
        public string? City { get; set; }

        [MaxLength(120)]
        public string? State { get; set; }

        [MaxLength(20)]
        public string? Pincode { get; set; }

        [MaxLength(256)]
        public string? Website { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }

        [Required]
        [MaxLength(40)]
        public string Status { get; set; } = IndustryPartnerStatuses.Active;
    }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var partner = await _db.IndustryPartners.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (partner is null)
        {
            return NotFound();
        }

        Partner = partner;
        Input = new InputModel
        {
            FirmName = partner.FirmName,
            PartnerType = partner.PartnerType,
            AddressText = partner.AddressText,
            City = partner.City,
            State = partner.State,
            Pincode = partner.Pincode,
            Website = partner.Website,
            Notes = partner.Notes,
            Status = partner.Status
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
    {
        var partner = await _db.IndustryPartners.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (partner is null)
        {
            return NotFound();
        }

        Partner = partner;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var normalizedFirmName = Input.FirmName.Trim().ToUpperInvariant();
        var duplicate = await _db.IndustryPartners
            .AnyAsync(p => p.Id != partner.Id && p.NormalizedFirmName == normalizedFirmName, ct);

        if (duplicate)
        {
            ModelState.AddModelError(nameof(Input.FirmName), "A partner with this firm name already exists.");
            return Page();
        }

        var userId = _users.GetUserId(User) ?? string.Empty;
        var now = _clock.UtcNow.UtcDateTime;

        // SECTION: Update entity
        partner.FirmName = Input.FirmName.Trim();
        partner.NormalizedFirmName = normalizedFirmName;
        partner.PartnerType = string.IsNullOrWhiteSpace(Input.PartnerType) ? null : Input.PartnerType;
        partner.AddressText = string.IsNullOrWhiteSpace(Input.AddressText) ? null : Input.AddressText.Trim();
        partner.City = string.IsNullOrWhiteSpace(Input.City) ? null : Input.City.Trim();
        partner.State = string.IsNullOrWhiteSpace(Input.State) ? null : Input.State.Trim();
        partner.Pincode = string.IsNullOrWhiteSpace(Input.Pincode) ? null : Input.Pincode.Trim();
        partner.Website = string.IsNullOrWhiteSpace(Input.Website) ? null : Input.Website.Trim();
        partner.Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim();
        partner.Status = string.IsNullOrWhiteSpace(Input.Status) ? IndustryPartnerStatuses.Active : Input.Status;
        partner.UpdatedAtUtc = now;
        partner.UpdatedByUserId = userId;

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "Partners.Updated",
            userId: userId,
            data: new Dictionary<string, string?>
            {
                ["PartnerId"] = partner.Id.ToString(),
                ["FirmName"] = partner.FirmName,
                ["Status"] = partner.Status
            });

        return RedirectToPage("/Projects/Partners/Details", new { id = partner.Id });
    }
}
