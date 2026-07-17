using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.IndustryPartners;

namespace ProjectManagement.Services.IndustryPartners;

public sealed class IndustryPartnerService : IIndustryPartnerService
{
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex CompanyPunctuationRegex = new(@"[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly string[] CompanySuffixes =
    {
        "private limited", "pvt limited", "pvt ltd", "limited", "ltd", "llp",
        "incorporated", "inc", "corporation", "corp", "company", "co"
    };

    private readonly ApplicationDbContext _db;
    private readonly IIndustryPartnerAttachmentStorage? _attachmentStorage;

    public IndustryPartnerService(
        ApplicationDbContext db,
        IIndustryPartnerAttachmentStorage? attachmentStorage = null)
    {
        _db = db;
        _attachmentStorage = attachmentStorage;
    }

    public async Task<IndustryPartnerSearchResult> SearchAsync(
        string? query,
        IndustryPartnerDirectoryFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);

        var normalizedQuery = Normalize(query);
        var loweredQuery = NullIfEmpty(query)?.ToLowerInvariant();
        var activeStatus = ProjectLifecycleStatus.Active;

        var partners = _db.IndustryPartners.AsNoTracking().AsQueryable();

        partners = filter switch
        {
            IndustryPartnerDirectoryFilter.ContactOnly =>
                partners.Where(partner => !partner.PartnerProjects.Any()),

            IndustryPartnerDirectoryFilter.JdpAssociated =>
                partners.Where(partner => partner.PartnerProjects.Any()),

            IndustryPartnerDirectoryFilter.CurrentJdp =>
                partners.Where(partner => partner.PartnerProjects.Any(link =>
                    !link.Project.IsDeleted &&
                    !link.Project.IsArchived &&
                    link.Project.LifecycleStatus == activeStatus)),

            IndustryPartnerDirectoryFilter.PastJdp =>
                partners.Where(partner => partner.PartnerProjects.Any(link =>
                    link.Project.IsDeleted ||
                    link.Project.IsArchived ||
                    link.Project.LifecycleStatus != activeStatus)),

            _ => partners
        };

        if (!string.IsNullOrWhiteSpace(normalizedQuery) || !string.IsNullOrWhiteSpace(loweredQuery))
        {
            partners = partners.Where(partner =>
                (!string.IsNullOrWhiteSpace(normalizedQuery) &&
                 (partner.NormalizedName.Contains(normalizedQuery) ||
                  (partner.NormalizedLocation ?? string.Empty).Contains(normalizedQuery))) ||
                (!string.IsNullOrWhiteSpace(loweredQuery) &&
                 ((partner.Name ?? string.Empty).ToLower().Contains(loweredQuery) ||
                  (partner.Location ?? string.Empty).ToLower().Contains(loweredQuery) ||
                  (partner.Remarks ?? string.Empty).ToLower().Contains(loweredQuery) ||
                  partner.Contacts.Any(contact =>
                      (contact.Name ?? string.Empty).ToLower().Contains(loweredQuery) ||
                      (contact.Email ?? string.Empty).ToLower().Contains(loweredQuery) ||
                      (contact.Phone ?? string.Empty).ToLower().Contains(loweredQuery)) ||
                  partner.PartnerProjects.Any(link =>
                      (link.Project.Name ?? string.Empty).ToLower().Contains(loweredQuery) ||
                      (link.Project.CaseFileNumber ?? string.Empty).ToLower().Contains(loweredQuery)) ||
                  partner.Attachments.Any(attachment =>
                      (attachment.OriginalFileName ?? string.Empty).ToLower().Contains(loweredQuery)))))
            ;
        }

        var total = await partners.CountAsync(cancellationToken);
        var items = await partners
            .OrderBy(partner => partner.Name)
            .ThenBy(partner => partner.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(partner => new IndustryPartnerListItem(
                partner.Id,
                partner.Name,
                partner.Location,
                partner.Contacts
                    .OrderBy(contact => contact.Id)
                    .Select(contact => contact.Name)
                    .FirstOrDefault(),
                partner.Contacts
                    .OrderBy(contact => contact.Id)
                    .Select(contact => contact.Phone)
                    .FirstOrDefault(),
                partner.Contacts
                    .OrderBy(contact => contact.Id)
                    .Select(contact => contact.Email)
                    .FirstOrDefault(),
                partner.Contacts.Count,
                partner.Attachments.Count,
                partner.PartnerProjects.Count,
                partner.PartnerProjects.Count(link =>
                    !link.Project.IsDeleted &&
                    !link.Project.IsArchived &&
                    link.Project.LifecycleStatus == activeStatus),
                partner.UpdatedUtc ?? partner.CreatedUtc))
            .ToListAsync(cancellationToken);

        return new IndustryPartnerSearchResult(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<IndustryPartnerDuplicateSuggestion>> FindDuplicateSuggestionsAsync(
        string? name,
        int take = 5,
        CancellationToken cancellationToken = default)
    {
        var canonical = CanonicalCompanyName(name);
        if (canonical.Length < 3)
        {
            return Array.Empty<IndustryPartnerDuplicateSuggestion>();
        }

        take = Math.Clamp(take, 1, 10);
        var candidates = await _db.IndustryPartners
            .AsNoTracking()
            .OrderBy(partner => partner.Name)
            .Select(partner => new
            {
                partner.Id,
                partner.Name,
                partner.Location,
                ContactCount = partner.Contacts.Count,
                ProjectCount = partner.PartnerProjects.Count
            })
            .Take(1000)
            .ToListAsync(cancellationToken);

        return candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Canonical = CanonicalCompanyName(candidate.Name)
            })
            .Where(candidate =>
                candidate.Canonical == canonical ||
                candidate.Canonical.Contains(canonical, StringComparison.Ordinal) ||
                canonical.Contains(candidate.Canonical, StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.Canonical == canonical)
            .ThenBy(candidate => Math.Abs(candidate.Canonical.Length - canonical.Length))
            .ThenBy(candidate => candidate.Candidate.Name)
            .Take(take)
            .Select(candidate => new IndustryPartnerDuplicateSuggestion(
                candidate.Candidate.Id,
                candidate.Candidate.Name,
                candidate.Candidate.Location,
                candidate.Candidate.ContactCount,
                candidate.Candidate.ProjectCount))
            .ToList();
    }

    public async Task<IndustryPartnerDto?> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var partner = await _db.IndustryPartners
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Contacts)
            .Include(item => item.Attachments)
            .Include(item => item.PartnerProjects)
                .ThenInclude(link => link.Project)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (partner is null)
        {
            return null;
        }

        var contacts = partner.Contacts
            .OrderBy(contact => contact.Id)
            .Select(contact => new IndustryPartnerContactDto(
                contact.Id,
                contact.Name,
                contact.Phone,
                contact.Email,
                contact.CreatedUtc,
                Convert.ToBase64String(contact.RowVersion)))
            .ToList();

        var attachments = partner.Attachments
            .OrderByDescending(attachment => attachment.UploadedUtc)
            .Select(attachment => new IndustryPartnerAttachmentDto(
                attachment.Id,
                attachment.OriginalFileName,
                attachment.ContentType,
                attachment.SizeBytes,
                attachment.UploadedUtc))
            .ToList();

        var linkedProjects = partner.PartnerProjects
            .OrderByDescending(link =>
                !link.Project.IsDeleted &&
                !link.Project.IsArchived &&
                link.Project.LifecycleStatus == ProjectLifecycleStatus.Active)
            .ThenByDescending(link => link.LinkedUtc)
            .ThenBy(link => link.Project.Name)
            .Select(link => new IndustryPartnerProjectDto(
                link.ProjectId,
                link.Project.Name,
                link.Project.CaseFileNumber,
                link.Project.LifecycleStatus,
                link.Project.IsArchived,
                link.Project.IsDeleted,
                link.LinkedUtc))
            .ToList();

        return new IndustryPartnerDto(
            partner.Id,
            partner.Name,
            partner.Location,
            partner.Remarks,
            partner.CreatedUtc,
            partner.UpdatedUtc ?? partner.CreatedUtc,
            Convert.ToBase64String(partner.RowVersion),
            contacts,
            attachments,
            linkedProjects);
    }

    public async Task<IndustryPartnerProjectContextDto?> GetProjectContextAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Projects
            .AsNoTracking()
            .Where(project => project.Id == projectId && !project.IsDeleted)
            .Select(project => new IndustryPartnerProjectContextDto(
                project.Id,
                project.Name,
                project.CaseFileNumber,
                project.LifecycleStatus,
                project.IsArchived))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> CreateAsync(
        CreateIndustryPartnerRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var name = Require(request.Name, "name", "Organisation name is required.", 200);
        var location = Optional(request.Location, "location", 2000);
        var remarks = Optional(request.Remarks, "remarks", 4000);
        var contact = ValidateOptionalContact(request.ContactName, request.ContactPhone, request.ContactEmail);

        await EnsureUniqueAsync(0, name, location, cancellationToken);
        if (request.ProjectId.HasValue)
        {
            await EnsureProjectCanBeLinkedAsync(request.ProjectId.Value, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var userId = GetUserId(user);
        var partner = new IndustryPartner
        {
            Name = name,
            NormalizedName = Normalize(name)!,
            Location = location,
            NormalizedLocation = Normalize(location),
            Remarks = remarks,
            CreatedByUserId = userId,
            CreatedUtc = now,
            UpdatedByUserId = contact is not null || request.ProjectId.HasValue ? userId : null,
            UpdatedUtc = contact is not null || request.ProjectId.HasValue ? now : null
        };

        if (contact is not null)
        {
            partner.Contacts.Add(new IndustryPartnerContact
            {
                Name = contact.Value.Name,
                Phone = contact.Value.Phone,
                Email = contact.Value.Email,
                CreatedUtc = now
            });
        }

        if (request.ProjectId.HasValue)
        {
            partner.PartnerProjects.Add(new IndustryPartnerProject
            {
                ProjectId = request.ProjectId.Value,
                LinkedByUserId = userId,
                LinkedUtc = now
            });
        }

        _db.IndustryPartners.Add(partner);
        await SaveWithValidationAsync(cancellationToken);
        return partner.Id;
    }

    public async Task UpdateAsync(
        int id,
        UpdateIndustryPartnerRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var partner = await _db.IndustryPartners
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Industry organisation not found.");

        ApplyConcurrencyToken(partner, request.RowVersion);

        var name = Require(request.Name, "name", "Organisation name is required.", 200);
        var location = Optional(request.Location, "location", 2000);
        var remarks = Optional(request.Remarks, "remarks", 4000);

        await EnsureUniqueAsync(id, name, location, cancellationToken);

        partner.Name = name;
        partner.NormalizedName = Normalize(name)!;
        partner.Location = location;
        partner.NormalizedLocation = Normalize(location);
        partner.Remarks = remarks;
        Touch(partner, user);

        await SaveWithConcurrencyAsync(cancellationToken);
    }

    public async Task<int> AddContactAsync(
        int partnerId,
        ContactRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var partner = await _db.IndustryPartners
            .FirstOrDefaultAsync(item => item.Id == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Industry organisation not found.");

        var contactValue = ValidateContact(request);
        var contact = new IndustryPartnerContact
        {
            IndustryPartnerId = partnerId,
            Name = contactValue.Name,
            Phone = contactValue.Phone,
            Email = contactValue.Email,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        _db.IndustryPartnerContacts.Add(contact);
        Touch(partner, user);
        await _db.SaveChangesAsync(cancellationToken);
        return contact.Id;
    }

    public async Task UpdateContactAsync(
        int partnerId,
        int contactId,
        ContactRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var partner = await _db.IndustryPartners
            .FirstOrDefaultAsync(item => item.Id == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Industry organisation not found.");

        var contact = await _db.IndustryPartnerContacts
            .FirstOrDefaultAsync(item => item.Id == contactId && item.IndustryPartnerId == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Contact not found.");

        ApplyConcurrencyToken(contact, request.RowVersion);
        var contactValue = ValidateContact(request);

        contact.Name = contactValue.Name;
        contact.Phone = contactValue.Phone;
        contact.Email = contactValue.Email;
        Touch(partner, user);

        await SaveWithConcurrencyAsync(cancellationToken);
    }

    public async Task DeleteContactAsync(
        int partnerId,
        int contactId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var partner = await _db.IndustryPartners
            .FirstOrDefaultAsync(item => item.Id == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Industry organisation not found.");

        var contact = await _db.IndustryPartnerContacts
            .FirstOrDefaultAsync(item => item.Id == contactId && item.IndustryPartnerId == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Contact not found.");

        _db.IndustryPartnerContacts.Remove(contact);
        Touch(partner, user);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task LinkProjectAsync(
        int partnerId,
        int projectId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var partner = await _db.IndustryPartners
            .FirstOrDefaultAsync(item => item.Id == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Industry organisation not found.");

        await EnsureProjectCanBeLinkedAsync(projectId, cancellationToken);

        var alreadyLinked = await _db.IndustryPartnerProjects.AnyAsync(
            link => link.IndustryPartnerId == partnerId && link.ProjectId == projectId,
            cancellationToken);

        if (alreadyLinked)
        {
            throw Error("project", "This organisation is already linked to the selected project as JDP.");
        }

        _db.IndustryPartnerProjects.Add(new IndustryPartnerProject
        {
            IndustryPartnerId = partnerId,
            ProjectId = projectId,
            LinkedByUserId = GetUserId(user),
            LinkedUtc = DateTimeOffset.UtcNow
        });

        Touch(partner, user);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlinkProjectAsync(
        int partnerId,
        int projectId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var partner = await _db.IndustryPartners
            .FirstOrDefaultAsync(item => item.Id == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Industry organisation not found.");

        var link = await _db.IndustryPartnerProjects
            .FirstOrDefaultAsync(item => item.IndustryPartnerId == partnerId && item.ProjectId == projectId, cancellationToken);

        if (link is null)
        {
            return;
        }

        _db.IndustryPartnerProjects.Remove(link);
        Touch(partner, user);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeletePartnerAsync(
        int partnerId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        _ = user;
        var partner = await _db.IndustryPartners
            .Include(item => item.Attachments)
            .FirstOrDefaultAsync(item => item.Id == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Industry organisation not found.");

        var hasLinks = await _db.IndustryPartnerProjects
            .AnyAsync(link => link.IndustryPartnerId == partnerId, cancellationToken);
        if (hasLinks)
        {
            throw Error("delete", "Remove the JDP project links before permanently deleting this organisation.");
        }

        var storageKeys = partner.Attachments.Select(attachment => attachment.StorageKey).ToArray();
        _db.IndustryPartners.Remove(partner);
        await _db.SaveChangesAsync(cancellationToken);

        if (_attachmentStorage is null)
        {
            return;
        }

        foreach (var storageKey in storageKeys)
        {
            await _attachmentStorage.DeleteAsync(storageKey, cancellationToken);
        }
    }

    private async Task EnsureProjectCanBeLinkedAsync(int projectId, CancellationToken cancellationToken)
    {
        var exists = await _db.Projects
            .AsNoTracking()
            .AnyAsync(project => project.Id == projectId && !project.IsDeleted, cancellationToken);

        if (!exists)
        {
            throw Error("project", "Selected project was not found.");
        }
    }

    private async Task EnsureUniqueAsync(
        int id,
        string name,
        string? location,
        CancellationToken cancellationToken)
    {
        var normalizedName = Normalize(name)!;
        var normalizedLocation = Normalize(location);
        var exists = await _db.IndustryPartners.AnyAsync(partner =>
            partner.Id != id &&
            partner.NormalizedName == normalizedName &&
            partner.NormalizedLocation == normalizedLocation,
            cancellationToken);

        if (exists)
        {
            throw Error("name", "An organisation with the same name and location already exists.");
        }
    }

    private async Task SaveWithValidationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new IndustryPartnerValidationException(
                new Dictionary<string, List<string>>
                {
                    ["name"] = new() { "An organisation with the same name and location already exists." }
                },
                exception);
        }
    }

    private async Task SaveWithConcurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new IndustryPartnerValidationException(
                new Dictionary<string, List<string>>
                {
                    ["concurrency"] = new() { "This record was changed by another user. Reload it and apply your changes again." }
                },
                exception);
        }
        catch (DbUpdateException exception)
        {
            throw new IndustryPartnerValidationException(
                new Dictionary<string, List<string>>
                {
                    ["name"] = new() { "An organisation with the same name and location already exists." }
                },
                exception);
        }
    }

    private void ApplyConcurrencyToken<TEntity>(TEntity entity, string? token)
        where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw Error("concurrency", "The record version is missing. Reload the page and try again.");
        }

        try
        {
            _db.Entry(entity).Property("RowVersion").OriginalValue = Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            throw Error("concurrency", "The record version is invalid. Reload the page and try again.");
        }
    }

    private static (string? Name, string? Phone, string? Email) ValidateContact(ContactRequest request)
    {
        var name = Optional(request.Name, "contactName", 200);
        var phone = Optional(request.Phone, "phone", 64);
        var email = OptionalEmail(request.Email, "email");

        if (phone is null && email is null)
        {
            throw Error("contact", "Add at least a phone number or an email address.");
        }

        return (name, phone, email);
    }

    private static (string? Name, string? Phone, string? Email)? ValidateOptionalContact(
        string? name,
        string? phone,
        string? email)
    {
        var hasAnyValue = !string.IsNullOrWhiteSpace(name) ||
                          !string.IsNullOrWhiteSpace(phone) ||
                          !string.IsNullOrWhiteSpace(email);
        if (!hasAnyValue)
        {
            return null;
        }

        var validated = ValidateContact(new ContactRequest(name, phone, email));
        return validated;
    }

    private static string Require(string? value, string key, string message, int maxLength)
    {
        var normalized = NullIfEmpty(value);
        if (normalized is null)
        {
            throw Error(key, message);
        }

        if (normalized.Length > maxLength)
        {
            throw Error(key, $"Maximum length is {maxLength} characters.");
        }

        return normalized;
    }

    private static string? Optional(string? value, string key, int maxLength)
    {
        var normalized = NullIfEmpty(value);
        if (normalized is not null && normalized.Length > maxLength)
        {
            throw Error(key, $"Maximum length is {maxLength} characters.");
        }

        return normalized;
    }

    private static string? OptionalEmail(string? value, string key)
    {
        var email = Optional(value, key, 256);
        if (email is not null && !new EmailAddressAttribute().IsValid(email))
        {
            throw Error(key, "Enter a valid email address.");
        }

        return email;
    }

    private static void Touch(IndustryPartner partner, ClaimsPrincipal user)
    {
        partner.UpdatedUtc = DateTimeOffset.UtcNow;
        partner.UpdatedByUserId = GetUserId(user);
    }

    private static string GetUserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";

    private static string? Normalize(string? value)
    {
        var normalized = NullIfEmpty(value);
        return normalized is null
            ? null
            : MultiSpaceRegex.Replace(normalized, " ").ToLowerInvariant();
    }

    private static string CanonicalCompanyName(string? value)
    {
        var normalized = Normalize(value) ?? string.Empty;
        normalized = Regex.Replace(normalized, @"^m\s*/?\s*s\.?\s+", string.Empty, RegexOptions.IgnoreCase);
        normalized = CompanyPunctuationRegex.Replace(normalized, " ").Trim();

        var changed = true;
        while (changed && normalized.Length > 0)
        {
            changed = false;
            foreach (var suffix in CompanySuffixes)
            {
                var suffixPattern = $@"(?:^|\s){Regex.Escape(suffix)}$";
                var withoutSuffix = Regex.Replace(normalized, suffixPattern, string.Empty, RegexOptions.IgnoreCase).Trim();
                if (withoutSuffix.Length == normalized.Length)
                {
                    continue;
                }

                normalized = withoutSuffix;
                changed = true;
                break;
            }
        }

        return CompanyPunctuationRegex.Replace(normalized, string.Empty);
    }

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static IndustryPartnerValidationException Error(string key, string message) =>
        new(new Dictionary<string, List<string>> { [key] = new() { message } });
}
