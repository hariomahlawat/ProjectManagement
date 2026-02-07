using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.IndustryPartners;

namespace ProjectManagement.Services.IndustryPartners;

public sealed class IndustryPartnerService : IIndustryPartnerService
{
    private readonly ApplicationDbContext _db;
    public IndustryPartnerService(ApplicationDbContext db) => _db = db;

    public async Task<IndustryPartnerSearchResult> SearchAsync(string? query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // SECTION: Search input normalization
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedQuery = Normalize(query);
        var rawQuery = query?.Trim();
        var loweredRawQuery = rawQuery?.ToLower();

        // SECTION: Base search query
        var partners = _db.IndustryPartners.AsNoTracking().AsQueryable();

        // SECTION: Comprehensive partner search filters
        if (!string.IsNullOrWhiteSpace(normalizedQuery) || !string.IsNullOrWhiteSpace(loweredRawQuery))
        {
            partners = partners.Where(x =>
                (!string.IsNullOrWhiteSpace(normalizedQuery) &&
                    (
                        x.NormalizedName.Contains(normalizedQuery!) ||
                        (x.NormalizedLocation ?? string.Empty).Contains(normalizedQuery!)
                    )) ||
                (!string.IsNullOrWhiteSpace(loweredRawQuery) &&
                    (
                        (x.Name ?? string.Empty).ToLower().Contains(loweredRawQuery!) ||
                        (x.Location ?? string.Empty).ToLower().Contains(loweredRawQuery!) ||
                        (x.Remarks ?? string.Empty).ToLower().Contains(loweredRawQuery!) ||
                        x.Contacts.Any(c =>
                            (c.Name ?? string.Empty).ToLower().Contains(loweredRawQuery!) ||
                            (c.Email ?? string.Empty).ToLower().Contains(loweredRawQuery!) ||
                            (c.Phone ?? string.Empty).ToLower().Contains(loweredRawQuery!)) ||
                        x.PartnerProjects.Any(p =>
                            (p.Project.Name ?? string.Empty).ToLower().Contains(loweredRawQuery!)) ||
                        x.Attachments.Any(a =>
                            (a.OriginalFileName ?? string.Empty).ToLower().Contains(loweredRawQuery!))
                    )));
        }

        // SECTION: Result shaping
        var total = await partners.CountAsync(cancellationToken);
        var items = await partners
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new IndustryPartnerListItem(
                x.Id,
                x.Name,
                x.Location,
                x.Contacts.Count,
                x.Attachments.Count,
                x.PartnerProjects.Count))
            .ToListAsync(cancellationToken);
        return new IndustryPartnerSearchResult(items, total, page, pageSize);
    }

    public async Task<IndustryPartnerDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var row = await _db.IndustryPartners.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new IndustryPartnerDto(
                x.Id,
                x.Name,
                x.Location,
                x.Remarks,
                x.Contacts.OrderBy(c => c.Id).Select(c => new IndustryPartnerContactDto(c.Id, c.Name, c.Phone, c.Email, c.CreatedUtc)).ToList(),
                x.Attachments.OrderByDescending(a => a.UploadedUtc).Select(a => new IndustryPartnerAttachmentDto(a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes, a.UploadedUtc)).ToList(),
                x.PartnerProjects.OrderBy(p => p.Project.Name).Select(p => new IndustryPartnerProjectDto(p.ProjectId, p.Project.Name)).ToList()))
            .FirstOrDefaultAsync(cancellationToken);
        return row;
    }

    public async Task<int> CreateAsync(CreateIndustryPartnerRequest req, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var name = req.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) throw Error(nameof(req.Name), "Name is required.");
        await EnsureUniqueAsync(0, name, req.Location, cancellationToken);
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        var entity = new IndustryPartner
        {
            Name = name,
            NormalizedName = Normalize(name)!,
            Location = NullIfEmpty(req.Location),
            NormalizedLocation = Normalize(req.Location),
            Remarks = NullIfEmpty(req.Remarks),
            CreatedByUserId = id,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        _db.IndustryPartners.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateFieldAsync(int id, string field, string? value, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var partner = await _db.IndustryPartners.FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        switch ((field ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "name":
                partner.Name = value?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(partner.Name)) throw Error("name", "Name is required.");
                partner.NormalizedName = Normalize(partner.Name)!;
                await EnsureUniqueAsync(id, partner.Name, partner.Location, cancellationToken);
                break;
            case "location":
                partner.Location = NullIfEmpty(value);
                partner.NormalizedLocation = Normalize(partner.Location);
                await EnsureUniqueAsync(id, partner.Name, partner.Location, cancellationToken);
                break;
            case "remarks":
                partner.Remarks = NullIfEmpty(value);
                break;
            default:
                throw Error("field", "Unsupported field.");
        }

        partner.UpdatedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        partner.UpdatedUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> AddContactAsync(int partnerId, ContactRequest req, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        _ = user;
        await EnsureContactValid(req, cancellationToken);
        var contact = new IndustryPartnerContact
        {
            IndustryPartnerId = partnerId,
            Name = NullIfEmpty(req.Name),
            Phone = NullIfEmpty(req.Phone),
            Email = NullIfEmpty(req.Email),
            CreatedUtc = DateTimeOffset.UtcNow
        };
        _db.IndustryPartnerContacts.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return contact.Id;
    }

    public async Task UpdateContactAsync(int partnerId, int contactId, ContactRequest req, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        _ = user;
        await EnsureContactValid(req, cancellationToken);
        var contact = await _db.IndustryPartnerContacts.FirstOrDefaultAsync(x => x.Id == contactId && x.IndustryPartnerId == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException();
        contact.Name = NullIfEmpty(req.Name);
        contact.Phone = NullIfEmpty(req.Phone);
        contact.Email = NullIfEmpty(req.Email);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteContactAsync(int partnerId, int contactId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        _ = user;
        var contact = await _db.IndustryPartnerContacts.FirstOrDefaultAsync(x => x.Id == contactId && x.IndustryPartnerId == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException();
        _db.IndustryPartnerContacts.Remove(contact);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task LinkProjectAsync(int partnerId, int projectId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var exists = await _db.IndustryPartnerProjects.AnyAsync(x => x.IndustryPartnerId == partnerId && x.ProjectId == projectId, cancellationToken);
        if (exists) return;
        _db.IndustryPartnerProjects.Add(new IndustryPartnerProject
        {
            IndustryPartnerId = partnerId,
            ProjectId = projectId,
            LinkedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system",
            LinkedUtc = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlinkProjectAsync(int partnerId, int projectId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        _ = user;
        var entity = await _db.IndustryPartnerProjects.FirstOrDefaultAsync(x => x.IndustryPartnerId == partnerId && x.ProjectId == projectId, cancellationToken);
        if (entity is null) return;
        _db.IndustryPartnerProjects.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeletePartnerAsync(int partnerId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        _ = user;
        var partner = await _db.IndustryPartners.FirstOrDefaultAsync(x => x.Id == partnerId, cancellationToken) ?? throw new KeyNotFoundException();
        var linked = await _db.IndustryPartnerProjects.AnyAsync(x => x.IndustryPartnerId == partnerId, cancellationToken);
        if (linked) throw Error("delete", "Cannot delete partner while linked projects exist.");
        _db.IndustryPartners.Remove(partner);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureUniqueAsync(int id, string name, string? location, CancellationToken ct)
    {
        var nn = Normalize(name)!;
        var nl = Normalize(location);
        var exists = await _db.IndustryPartners.AnyAsync(x => x.Id != id && x.NormalizedName == nn && x.NormalizedLocation == nl, ct);
        if (exists) throw Error("name", "A partner with the same name and location already exists.");
    }

    private static async Task EnsureContactValid(ContactRequest req, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        var phone = NullIfEmpty(req.Phone);
        var email = NullIfEmpty(req.Email);
        if (phone is null && email is null) throw Error("contact", "Either phone or email is required.");
        if (email is not null && !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) throw Error("email", "Email format is invalid.");
    }

    private static IndustryPartnerValidationException Error(string key, string message)
    {
        return new IndustryPartnerValidationException(new Dictionary<string, List<string>> { [key] = new() { message } });
    }

    private static string? Normalize(string? value)
    {
        var trimmed = NullIfEmpty(value);
        return trimmed?.ToLowerInvariant();
    }

    private static string? NullIfEmpty(string? value)
    {
        var v = value?.Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
