using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
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
public class CreateModel : PageModel
{
    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public CreateModel(ApplicationDbContext db, UserManager<ApplicationUser> users, IClock clock, IAuditService audit)
    {
        _db = db;
        _users = users;
        _clock = clock;
        _audit = audit;
    }

    // SECTION: Form state
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

        // SECTION: Optional primary contact details
        [MaxLength(200)]
        public string? ContactName { get; set; }

        [MaxLength(120)]
        public string? ContactDesignation { get; set; }

        [MaxLength(200)]
        public string? ContactEmail { get; set; }

        [MaxLength(40)]
        public string? MobilePhone { get; set; }

        [MaxLength(40)]
        public string? OfficePhone { get; set; }

        [MaxLength(40)]
        public string? WhatsAppPhone { get; set; }

        [MaxLength(1000)]
        public string? ContactNotes { get; set; }
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        // SECTION: Validate model
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var normalizedFirmName = Input.FirmName.Trim().ToUpperInvariant();
        var duplicate = await _db.IndustryPartners
            .AnyAsync(p => p.NormalizedFirmName == normalizedFirmName, ct);

        if (duplicate)
        {
            ModelState.AddModelError(nameof(Input.FirmName), "A partner with this firm name already exists.");
            return Page();
        }

        ValidatePhone(Input.MobilePhone, nameof(Input.MobilePhone));
        ValidatePhone(Input.OfficePhone, nameof(Input.OfficePhone));
        ValidatePhone(Input.WhatsAppPhone, nameof(Input.WhatsAppPhone));

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = _users.GetUserId(User) ?? string.Empty;
        var now = _clock.UtcNow.UtcDateTime;

        // SECTION: Create partner entity
        var partner = new IndustryPartner
        {
            FirmName = Input.FirmName.Trim(),
            NormalizedFirmName = normalizedFirmName,
            PartnerType = string.IsNullOrWhiteSpace(Input.PartnerType) ? null : Input.PartnerType,
            AddressText = string.IsNullOrWhiteSpace(Input.AddressText) ? null : Input.AddressText.Trim(),
            City = string.IsNullOrWhiteSpace(Input.City) ? null : Input.City.Trim(),
            State = string.IsNullOrWhiteSpace(Input.State) ? null : Input.State.Trim(),
            Pincode = string.IsNullOrWhiteSpace(Input.Pincode) ? null : Input.Pincode.Trim(),
            Website = string.IsNullOrWhiteSpace(Input.Website) ? null : Input.Website.Trim(),
            Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
            Status = string.IsNullOrWhiteSpace(Input.Status) ? IndustryPartnerStatuses.Active : Input.Status,
            CreatedAtUtc = now,
            CreatedByUserId = userId
        };

        // SECTION: Optional primary contact
        if (!string.IsNullOrWhiteSpace(Input.ContactName))
        {
            var contact = new IndustryPartnerContact
            {
                Name = Input.ContactName.Trim(),
                Designation = string.IsNullOrWhiteSpace(Input.ContactDesignation) ? null : Input.ContactDesignation.Trim(),
                Email = string.IsNullOrWhiteSpace(Input.ContactEmail) ? null : Input.ContactEmail.Trim(),
                Notes = string.IsNullOrWhiteSpace(Input.ContactNotes) ? null : Input.ContactNotes.Trim(),
                IsPrimary = true,
                CreatedAtUtc = now,
                CreatedByUserId = userId
            };

            foreach (var phone in BuildPhones())
            {
                contact.Phones.Add(phone);
            }

            partner.Contacts.Add(contact);
        }

        _db.IndustryPartners.Add(partner);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "Partners.Created",
            userId: userId,
            data: new Dictionary<string, string?>
            {
                ["PartnerId"] = partner.Id.ToString(),
                ["FirmName"] = partner.FirmName,
                ["Status"] = partner.Status
            });

        return RedirectToPage("/Projects/Partners/Details", new { id = partner.Id });
    }

    // SECTION: Helpers
    private void ValidatePhone(string? phone, string field)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return;
        }

        var normalized = phone.Trim();
        if (!Regex.IsMatch(normalized, "^[0-9+()\\-\\s]{7,}$"))
        {
            ModelState.AddModelError(field, "Enter a valid phone number with at least 7 digits.");
        }
    }

    private IEnumerable<IndustryPartnerContactPhone> BuildPhones()
    {
        return new[]
        {
            (Input.MobilePhone, "Mobile"),
            (Input.OfficePhone, "Office"),
            (Input.WhatsAppPhone, "WhatsApp")
        }
        .Where(pair => !string.IsNullOrWhiteSpace(pair.Item1))
        .Select(pair => new IndustryPartnerContactPhone
        {
            PhoneNumber = pair.Item1!.Trim(),
            Label = pair.Item2
        });
    }
}
