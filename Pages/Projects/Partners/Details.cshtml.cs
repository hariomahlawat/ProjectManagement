using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Partners;
using ProjectManagement.Services;
using ProjectManagement.Services.Storage;
using ProjectManagement.ViewModels.Partners;

namespace ProjectManagement.Pages.Projects.Partners;

[Authorize(Policy = Policies.Partners.View)]
public class DetailsModel : PageModel
{
    // SECTION: Constants
    private const long MaxAttachmentSizeBytes = 25 * 1024 * 1024;

    private static readonly HashSet<string> AllowedAttachmentContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png",
        "image/jpeg"
    };

    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly IUploadPathResolver _pathResolver;
    private readonly IProtectedFileUrlBuilder _fileUrlBuilder;

    public DetailsModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IClock clock,
        IAuditService audit,
        IUploadRootProvider uploadRootProvider,
        IUploadPathResolver pathResolver,
        IProtectedFileUrlBuilder fileUrlBuilder)
    {
        _db = db;
        _users = users;
        _clock = clock;
        _audit = audit;
        _uploadRootProvider = uploadRootProvider;
        _pathResolver = pathResolver;
        _fileUrlBuilder = fileUrlBuilder;
    }

    // SECTION: View state
    public IndustryPartner Partner { get; private set; } = null!;

    public IReadOnlyList<ProjectIndustryPartnerVm> ProjectLinks { get; private set; } = Array.Empty<ProjectIndustryPartnerVm>();

    public IReadOnlyList<ProjectOptionVm> ProjectOptions { get; private set; } = Array.Empty<ProjectOptionVm>();

    public IReadOnlyList<AttachmentVm> Attachments { get; private set; } = Array.Empty<AttachmentVm>();

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    // SECTION: Inputs
    [BindProperty]
    public ContactInput Contact { get; set; } = new();

    [BindProperty]
    public AttachmentInput Attachment { get; set; } = new();

    [BindProperty]
    public ProjectLinkInput ProjectLink { get; set; } = new();

    public IReadOnlyList<string> AttachmentTypeOptions => IndustryPartnerAttachmentTypes.All;

    public IReadOnlyList<string> RoleOptions => IndustryPartnerRoles.All;

    public IReadOnlyList<string> StatusOptions => IndustryPartnerAssociationStatuses.All;

    public sealed class ContactInput
    {
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(120)]
        public string? Designation { get; set; }

        [MaxLength(200)]
        public string? Email { get; set; }

        public bool IsPrimary { get; set; }

        [MaxLength(40)]
        public string? MobilePhone { get; set; }

        [MaxLength(40)]
        public string? OfficePhone { get; set; }

        [MaxLength(40)]
        public string? WhatsAppPhone { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }

    public sealed class AttachmentInput
    {
        [Required]
        public IFormFile? File { get; set; }

        [MaxLength(200)]
        public string? Title { get; set; }

        [MaxLength(80)]
        public string? AttachmentType { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }

    public sealed class ProjectLinkInput
    {
        [Required]
        public int ProjectId { get; set; }

        [MaxLength(80)]
        public string Role { get; set; } = IndustryPartnerRoles.JointDevelopmentPartner;

        [MaxLength(40)]
        public string Status { get; set; } = IndustryPartnerAssociationStatuses.Active;

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }

    public sealed record AttachmentVm(
        int Id,
        string Title,
        string AttachmentType,
        string FileName,
        string ContentType,
        long FileSize,
        string DownloadUrl,
        string InlineUrl,
        DateTime UploadedAtUtc,
        string UploadedByUserId);

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var partner = await _db.IndustryPartners
            .Include(p => p.Contacts)
                .ThenInclude(c => c.Phones)
            .Include(p => p.Attachments)
            .Include(p => p.ProjectLinks)
                .ThenInclude(link => link.Project)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (partner is null)
        {
            return NotFound();
        }

        Partner = partner;
        ProjectLinks = partner.ProjectLinks
            .OrderByDescending(link => link.Status == IndustryPartnerAssociationStatuses.Active)
            .ThenBy(link => link.Project != null ? link.Project.Name : string.Empty)
            .Select(link => new ProjectIndustryPartnerVm(
                link.ProjectId,
                link.PartnerId,
                partner.FirmName,
                link.Role,
                link.Status,
                link.FromDate,
                link.ToDate,
                link.Notes))
            .ToList();

        Attachments = partner.Attachments
            .OrderByDescending(a => a.UploadedAtUtc)
            .Select(a => new AttachmentVm(
                a.Id,
                a.Title ?? a.OriginalFileName,
                a.AttachmentType ?? "Other",
                a.OriginalFileName,
                a.ContentType,
                a.FileSize,
                _fileUrlBuilder.CreateDownloadUrl(a.StorageKey, a.OriginalFileName, a.ContentType),
                _fileUrlBuilder.CreateInlineUrl(a.StorageKey, a.OriginalFileName, a.ContentType),
                a.UploadedAtUtc,
                a.UploadedByUserId))
            .ToList();

        ProjectOptions = await _db.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProjectOptionVm(p.Id, p.Name, p.LifecycleStatus.ToString()))
            .ToListAsync(ct);

        return Page();
    }

    [Authorize(Policy = Policies.Partners.Manage)]
    public async Task<IActionResult> OnPostAddContactAsync(int id, CancellationToken ct)
    {
        var partner = await _db.IndustryPartners
            .Include(p => p.Contacts)
                .ThenInclude(c => c.Phones)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (partner is null)
        {
            return NotFound();
        }

        ValidatePhone(Contact.MobilePhone, nameof(Contact.MobilePhone));
        ValidatePhone(Contact.OfficePhone, nameof(Contact.OfficePhone));
        ValidatePhone(Contact.WhatsAppPhone, nameof(Contact.WhatsAppPhone));

        if (!ModelState.IsValid)
        {
            return await ReloadWithErrorsAsync(partner.Id, "Contacts", ct);
        }

        var userId = _users.GetUserId(User) ?? string.Empty;
        var now = _clock.UtcNow.UtcDateTime;

        if (Contact.IsPrimary)
        {
            foreach (var existing in partner.Contacts)
            {
                existing.IsPrimary = false;
            }
        }

        var contact = new IndustryPartnerContact
        {
            PartnerId = partner.Id,
            Name = Contact.Name.Trim(),
            Designation = string.IsNullOrWhiteSpace(Contact.Designation) ? null : Contact.Designation.Trim(),
            Email = string.IsNullOrWhiteSpace(Contact.Email) ? null : Contact.Email.Trim(),
            Notes = string.IsNullOrWhiteSpace(Contact.Notes) ? null : Contact.Notes.Trim(),
            IsPrimary = Contact.IsPrimary,
            CreatedAtUtc = now,
            CreatedByUserId = userId
        };

        foreach (var phone in BuildPhones())
        {
            contact.Phones.Add(phone);
        }

        partner.Contacts.Add(contact);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "Partners.ContactAdded",
            userId: userId,
            data: new Dictionary<string, string?>
            {
                ["PartnerId"] = partner.Id.ToString(),
                ["ContactId"] = contact.Id.ToString(),
                ["ContactName"] = contact.Name
            });

        return RedirectToPage("/Projects/Partners/Details", new { id = partner.Id, tab = "Contacts" });
    }

    [Authorize(Policy = Policies.Partners.Manage)]
    public async Task<IActionResult> OnPostSetPrimaryAsync(int id, int contactId, CancellationToken ct)
    {
        var partner = await _db.IndustryPartners
            .Include(p => p.Contacts)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (partner is null)
        {
            return NotFound();
        }

        var target = partner.Contacts.FirstOrDefault(c => c.Id == contactId);
        if (target is null)
        {
            return NotFound();
        }

        foreach (var contact in partner.Contacts)
        {
            contact.IsPrimary = contact.Id == target.Id;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "Partners.ContactPrimarySet",
            userId: _users.GetUserId(User),
            data: new Dictionary<string, string?>
            {
                ["PartnerId"] = partner.Id.ToString(),
                ["ContactId"] = target.Id.ToString()
            });

        return RedirectToPage("/Projects/Partners/Details", new { id = partner.Id, tab = "Contacts" });
    }

    [Authorize(Policy = Policies.Partners.Delete)]
    public async Task<IActionResult> OnPostDeleteContactAsync(int id, int contactId, CancellationToken ct)
    {
        var contact = await _db.IndustryPartnerContacts
            .Include(c => c.Phones)
            .FirstOrDefaultAsync(c => c.Id == contactId && c.PartnerId == id, ct);

        if (contact is null)
        {
            return NotFound();
        }

        _db.IndustryPartnerContactPhones.RemoveRange(contact.Phones);
        _db.IndustryPartnerContacts.Remove(contact);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "Partners.ContactDeleted",
            userId: _users.GetUserId(User),
            data: new Dictionary<string, string?>
            {
                ["PartnerId"] = id.ToString(),
                ["ContactId"] = contactId.ToString()
            });

        return RedirectToPage("/Projects/Partners/Details", new { id, tab = "Contacts" });
    }

    [Authorize(Policy = Policies.Partners.Manage)]
    public async Task<IActionResult> OnPostAddAttachmentAsync(int id, CancellationToken ct)
    {
        var partner = await _db.IndustryPartners
            .Include(p => p.Attachments)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (partner is null)
        {
            return NotFound();
        }

        ValidateAttachment(Attachment.File, nameof(Attachment.File));
        if (!ModelState.IsValid)
        {
            return await ReloadWithErrorsAsync(partner.Id, "Attachments", ct);
        }

        var userId = _users.GetUserId(User) ?? string.Empty;
        var now = _clock.UtcNow.UtcDateTime;
        var upload = Attachment.File!;
        var storageKey = await SaveAttachmentAsync(partner.Id, upload, ct);

        var attachment = new IndustryPartnerAttachment
        {
            PartnerId = partner.Id,
            StorageKey = storageKey,
            OriginalFileName = Path.GetFileName(upload.FileName),
            ContentType = upload.ContentType,
            FileSize = upload.Length,
            Title = string.IsNullOrWhiteSpace(Attachment.Title) ? null : Attachment.Title.Trim(),
            AttachmentType = string.IsNullOrWhiteSpace(Attachment.AttachmentType) ? null : Attachment.AttachmentType,
            Notes = string.IsNullOrWhiteSpace(Attachment.Notes) ? null : Attachment.Notes.Trim(),
            UploadedAtUtc = now,
            UploadedByUserId = userId
        };

        partner.Attachments.Add(attachment);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "Partners.AttachmentAdded",
            userId: userId,
            data: new Dictionary<string, string?>
            {
                ["PartnerId"] = partner.Id.ToString(),
                ["AttachmentId"] = attachment.Id.ToString(),
                ["AttachmentType"] = attachment.AttachmentType
            });

        return RedirectToPage("/Projects/Partners/Details", new { id = partner.Id, tab = "Attachments" });
    }

    [Authorize(Policy = Policies.Partners.LinkToProject)]
    public async Task<IActionResult> OnPostLinkProjectAsync(int id, CancellationToken ct)
    {
        var partner = await _db.IndustryPartners
            .Include(p => p.ProjectLinks)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (partner is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return await ReloadWithErrorsAsync(partner.Id, "Projects", ct);
        }

        var exists = partner.ProjectLinks.Any(link => link.ProjectId == ProjectLink.ProjectId);
        if (exists)
        {
            ModelState.AddModelError(string.Empty, "This project is already linked.");
            return await ReloadWithErrorsAsync(partner.Id, "Projects", ct);
        }

        var userId = _users.GetUserId(User) ?? string.Empty;
        var now = _clock.UtcNow.UtcDateTime;

        var link = new ProjectIndustryPartner
        {
            PartnerId = partner.Id,
            ProjectId = ProjectLink.ProjectId,
            Role = string.IsNullOrWhiteSpace(ProjectLink.Role) ? IndustryPartnerRoles.JointDevelopmentPartner : ProjectLink.Role,
            Status = string.IsNullOrWhiteSpace(ProjectLink.Status) ? IndustryPartnerAssociationStatuses.Active : ProjectLink.Status,
            Notes = string.IsNullOrWhiteSpace(ProjectLink.Notes) ? null : ProjectLink.Notes.Trim(),
            CreatedAtUtc = now,
            CreatedByUserId = userId
        };

        partner.ProjectLinks.Add(link);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "Partners.ProjectLinked",
            userId: userId,
            data: new Dictionary<string, string?>
            {
                ["PartnerId"] = partner.Id.ToString(),
                ["ProjectId"] = link.ProjectId.ToString(CultureInfo.InvariantCulture)
            });

        return RedirectToPage("/Projects/Partners/Details", new { id = partner.Id, tab = "Projects" });
    }

    [Authorize(Policy = Policies.Partners.LinkToProject)]
    public async Task<IActionResult> OnPostUnlinkProjectAsync(int id, int projectId, CancellationToken ct)
    {
        var link = await _db.ProjectIndustryPartners
            .FirstOrDefaultAsync(p => p.PartnerId == id && p.ProjectId == projectId, ct);

        if (link is null)
        {
            return NotFound();
        }

        _db.ProjectIndustryPartners.Remove(link);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "Partners.ProjectUnlinked",
            userId: _users.GetUserId(User),
            data: new Dictionary<string, string?>
            {
                ["PartnerId"] = id.ToString(),
                ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture)
            });

        return RedirectToPage("/Projects/Partners/Details", new { id, tab = "Projects" });
    }

    [Authorize(Policy = Policies.Partners.LinkToProject)]
    public async Task<IActionResult> OnPostUpdateProjectAsync(int id, int projectId, CancellationToken ct)
    {
        var link = await _db.ProjectIndustryPartners
            .FirstOrDefaultAsync(p => p.PartnerId == id && p.ProjectId == projectId, ct);

        if (link is null)
        {
            return NotFound();
        }

        link.Role = string.IsNullOrWhiteSpace(ProjectLink.Role) ? IndustryPartnerRoles.JointDevelopmentPartner : ProjectLink.Role;
        link.Status = string.IsNullOrWhiteSpace(ProjectLink.Status) ? IndustryPartnerAssociationStatuses.Active : ProjectLink.Status;
        link.Notes = string.IsNullOrWhiteSpace(ProjectLink.Notes) ? null : ProjectLink.Notes.Trim();
        link.UpdatedAtUtc = _clock.UtcNow.UtcDateTime;
        link.UpdatedByUserId = _users.GetUserId(User) ?? string.Empty;

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            "Partners.ProjectAssociationUpdated",
            userId: _users.GetUserId(User),
            data: new Dictionary<string, string?>
            {
                ["PartnerId"] = id.ToString(),
                ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture)
            });

        return RedirectToPage("/Projects/Partners/Details", new { id, tab = "Projects" });
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
            (Contact.MobilePhone, "Mobile"),
            (Contact.OfficePhone, "Office"),
            (Contact.WhatsAppPhone, "WhatsApp")
        }
        .Where(pair => !string.IsNullOrWhiteSpace(pair.Item1))
        .Select(pair => new IndustryPartnerContactPhone
        {
            PhoneNumber = pair.Item1!.Trim(),
            Label = pair.Item2
        });
    }

    private void ValidateAttachment(IFormFile? file, string field)
    {
        if (file is null)
        {
            ModelState.AddModelError(field, "Attachment is required.");
            return;
        }

        if (file.Length == 0 || file.Length > MaxAttachmentSizeBytes)
        {
            ModelState.AddModelError(field, "Attachment size exceeds the 25MB limit.");
            return;
        }

        if (!AllowedAttachmentContentTypes.Contains(file.ContentType))
        {
            ModelState.AddModelError(field, "Only PDF, JPG, and PNG files are allowed.");
        }
    }

    private async Task<string> SaveAttachmentAsync(int partnerId, IFormFile file, CancellationToken ct)
    {
        var safeFileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(safeFileName);
        var partnerFolder = Path.Combine(_uploadRootProvider.RootPath, "partners", partnerId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(partnerFolder);

        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(partnerFolder, storedFileName);

        await using (var stream = new FileStream(absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await file.CopyToAsync(stream, ct);
        }

        return _pathResolver.ToRelative(absolutePath);
    }

    private async Task<IActionResult> ReloadWithErrorsAsync(int partnerId, string tab, CancellationToken ct)
    {
        Tab = tab;
        var result = await OnGetAsync(partnerId, ct);
        return result;
    }
}
