using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
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

    private const int TouchSaveAttempts = 3;
    private const int MaxExportRows = 10000;
    private const string ProjectJdpForbiddenMessage =
        "You are not authorised to change the JDPs for this project.";
    private const string DuplicateOrganisationMessage =
        "An organisation with the same name and location already exists.";

    private readonly ApplicationDbContext _db;
    private readonly IIndustryPartnerAttachmentStorage? _attachmentStorage;
    private readonly ILogger<IndustryPartnerService> _logger;

    public IndustryPartnerService(
        ApplicationDbContext db,
        IIndustryPartnerAttachmentStorage? attachmentStorage = null,
        ILogger<IndustryPartnerService>? logger = null)
    {
        _db = db;
        _attachmentStorage = attachmentStorage;
        _logger = logger ?? NullLogger<IndustryPartnerService>.Instance;
    }

    public async Task<IndustryPartnerSearchResult> SearchAsync(
        string? query,
        IndustryPartnerDirectoryFilter filter,
        int page,
        int pageSize,
        IndustryPartnerDirectorySort sort = IndustryPartnerDirectorySort.Name,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var activeStatus = ProjectLifecycleStatus.Active;
        var partners = BuildDirectoryQuery(query, filter);

        var total = await partners.CountAsync(cancellationToken);
        var items = await ApplySort(partners, sort)
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
                partner.PartnerProjects.Count(link => !link.Project.IsDeleted),
                partner.PartnerProjects.Count(link =>
                    !link.Project.IsDeleted &&
                    !link.Project.IsArchived &&
                    link.Project.LifecycleStatus == activeStatus),
                partner.UpdatedUtc ?? partner.CreatedUtc))
            .ToListAsync(cancellationToken);

        return new IndustryPartnerSearchResult(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<IndustryPartnerExportRow>> ExportDirectoryAsync(
        string? query,
        IndustryPartnerDirectoryFilter filter,
        IndustryPartnerDirectorySort sort,
        CancellationToken cancellationToken = default)
    {
        var activeStatus = ProjectLifecycleStatus.Active;
        var rows = await ApplySort(BuildDirectoryQuery(query, filter), sort)
            .Take(MaxExportRows)
            .Select(partner => new
            {
                partner.Id,
                partner.Name,
                partner.Location,
                partner.Remarks,
                ProjectCount = partner.PartnerProjects.Count(link => !link.Project.IsDeleted),
                ActiveProjectCount = partner.PartnerProjects.Count(link =>
                    !link.Project.IsDeleted &&
                    !link.Project.IsArchived &&
                    link.Project.LifecycleStatus == activeStatus),
                LastUpdatedUtc = partner.UpdatedUtc ?? partner.CreatedUtc
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(row => row.Id).ToArray();
        var contacts = await _db.IndustryPartnerContacts
            .AsNoTracking()
            .Where(contact => ids.Contains(contact.IndustryPartnerId))
            .OrderBy(contact => contact.IndustryPartnerId)
            .ThenBy(contact => contact.Id)
            .Select(contact => new
            {
                contact.IndustryPartnerId,
                Contact = new IndustryPartnerExportContact(contact.Name, contact.Phone, contact.Email)
            })
            .ToListAsync(cancellationToken);

        var contactsByPartner = contacts
            .GroupBy(item => item.IndustryPartnerId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<IndustryPartnerExportContact>)group.Select(item => item.Contact).ToList());

        return rows
            .Select(row => new IndustryPartnerExportRow(
                row.Id,
                row.Name,
                row.Location,
                row.Remarks,
                row.ProjectCount,
                row.ActiveProjectCount,
                row.LastUpdatedUtc,
                contactsByPartner.GetValueOrDefault(row.Id) ?? Array.Empty<IndustryPartnerExportContact>()))
            .ToList();
    }

    // The directory search and filter, shared by the paged list and the export so both
    // always describe the same set of organisations.
    private IQueryable<IndustryPartner> BuildDirectoryQuery(string? query, IndustryPartnerDirectoryFilter filter)
    {
        var normalizedQuery = Normalize(query);
        var loweredQuery = NullIfEmpty(query)?.ToLowerInvariant();
        var activeStatus = ProjectLifecycleStatus.Active;

        var partners = _db.IndustryPartners.AsNoTracking().AsQueryable();

        partners = filter switch
        {
            // Links to soft-deleted projects do not make an organisation a JDP; they stay
            // visible in its JDP history only so they can be removed.
            IndustryPartnerDirectoryFilter.ContactOnly =>
                partners.Where(partner => !partner.PartnerProjects.Any(link => !link.Project.IsDeleted)),

            IndustryPartnerDirectoryFilter.JdpAssociated =>
                partners.Where(partner => partner.PartnerProjects.Any(link => !link.Project.IsDeleted)),

            IndustryPartnerDirectoryFilter.CurrentJdp =>
                partners.Where(partner => partner.PartnerProjects.Any(link =>
                    !link.Project.IsDeleted &&
                    !link.Project.IsArchived &&
                    link.Project.LifecycleStatus == activeStatus)),

            IndustryPartnerDirectoryFilter.PastJdp =>
                partners.Where(partner => partner.PartnerProjects.Any(link =>
                    !link.Project.IsDeleted &&
                    (link.Project.IsArchived ||
                     link.Project.LifecycleStatus != activeStatus))),

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

        return partners;
    }

    private static IOrderedQueryable<IndustryPartner> ApplySort(
        IQueryable<IndustryPartner> partners,
        IndustryPartnerDirectorySort sort) => sort switch
    {
        IndustryPartnerDirectorySort.RecentlyUpdated => partners
            .OrderByDescending(partner => partner.UpdatedUtc ?? partner.CreatedUtc)
            .ThenBy(partner => partner.Name)
            .ThenBy(partner => partner.Id),
        _ => partners
            .OrderBy(partner => partner.Name)
            .ThenBy(partner => partner.Id)
    };

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

        // Canonical names (suffixes and punctuation removed) cannot be compared in SQL, so
        // every organisation's name is read and matched here. Only id and name are loaded;
        // counts are fetched for the few matches.
        var names = await _db.IndustryPartners
            .AsNoTracking()
            .Select(partner => new { partner.Id, partner.Name })
            .ToListAsync(cancellationToken);

        var matchIds = names
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Name,
                Canonical = CanonicalCompanyName(candidate.Name)
            })
            .Where(candidate =>
                candidate.Canonical.Length > 0 &&
                (candidate.Canonical == canonical ||
                 candidate.Canonical.Contains(canonical, StringComparison.Ordinal) ||
                 canonical.Contains(candidate.Canonical, StringComparison.Ordinal)))
            .OrderByDescending(candidate => candidate.Canonical == canonical)
            .ThenBy(candidate => Math.Abs(candidate.Canonical.Length - canonical.Length))
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Id)
            .Take(take)
            .Select(candidate => candidate.Id)
            .ToList();

        if (matchIds.Count == 0)
        {
            return Array.Empty<IndustryPartnerDuplicateSuggestion>();
        }

        var details = await _db.IndustryPartners
            .AsNoTracking()
            .Where(partner => matchIds.Contains(partner.Id))
            .Select(partner => new IndustryPartnerDuplicateSuggestion(
                partner.Id,
                partner.Name,
                partner.Location,
                partner.Contacts.Count,
                partner.PartnerProjects.Count(link => !link.Project.IsDeleted)))
            .ToListAsync(cancellationToken);

        var byId = details.ToDictionary(item => item.Id);
        return matchIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
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
                contact.CreatedByUserId,
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

    public Task<bool> IsOwnerAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(false);
        }

        return _db.IndustryPartners
            .AsNoTracking()
            .AnyAsync(partner =>
                partner.Id == id &&
                partner.CreatedByUserId == userId,
                cancellationToken);
    }

    public async Task<IReadOnlySet<int>> GetJdpManageableProjectIdsAsync(
        IEnumerable<int> projectIds,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var ids = projectIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new HashSet<int>();
        }

        var manageable = await ProjectJdpAccess
            .WhereUserCanManage(_db.Projects.AsNoTracking(), user)
            .Where(project => ids.Contains(project.Id))
            .Select(project => project.Id)
            .ToListAsync(cancellationToken);

        return manageable.ToHashSet();
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


    public async Task<ProjectMultiJdpProfileDto> GetProjectMultiJdpProfileAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var projectExists = await _db.Projects
            .AsNoTracking()
            .AnyAsync(project => project.Id == projectId && !project.IsDeleted, cancellationToken);

        if (!projectExists)
        {
            return ProjectMultiJdpProfileDto.Empty(projectId);
        }

        var linkedPartners = await _db.IndustryPartnerProjects
            .AsNoTracking()
            .Where(link => link.ProjectId == projectId)
            .OrderBy(link => link.IndustryPartner.Name)
            .ThenBy(link => link.IndustryPartnerId)
            .Select(link => new
            {
                Id = link.IndustryPartnerId,
                link.IndustryPartner.Name,
                link.IndustryPartner.Location
            })
            .ToListAsync(cancellationToken);

        if (linkedPartners.Count == 0)
        {
            return ProjectMultiJdpProfileDto.Empty(projectId);
        }

        var partnerIds = linkedPartners.Select(partner => partner.Id).ToArray();
        var otherProjectRows = await _db.IndustryPartnerProjects
            .AsNoTracking()
            .Where(link =>
                partnerIds.Contains(link.IndustryPartnerId) &&
                link.ProjectId != projectId &&
                !link.Project.IsDeleted)
            .Select(link => new
            {
                link.IndustryPartnerId,
                Project = new ProjectJdpLinkedProjectDto(
                    link.ProjectId,
                    link.Project.Name,
                    link.Project.CaseFileNumber,
                    link.Project.LifecycleStatus,
                    link.Project.IsArchived,
                    link.Project.IsDeleted)
            })
            .ToListAsync(cancellationToken);

        var projectsByPartner = otherProjectRows
            .GroupBy(row => row.IndustryPartnerId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ProjectJdpLinkedProjectDto>)group
                    .Select(row => row.Project)
                    .OrderBy(project => project.StatusOrder)
                    .ThenBy(project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(project => project.ProjectId)
                    .ToList());

        var partners = linkedPartners
            .Select(partner => new ProjectJdpPartnerProfileDto(
                partner.Id,
                partner.Name,
                partner.Location,
                projectsByPartner.GetValueOrDefault(partner.Id) ?? Array.Empty<ProjectJdpLinkedProjectDto>()))
            .ToList();

        return new ProjectMultiJdpProfileDto(projectId, partners);
    }

    public async Task<IReadOnlyList<ProjectJdpOptionDto>> SearchProjectJdpOptionsAsync(
        int projectId,
        string? query,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 20);
        var normalizedQuery = Normalize(query);
        var loweredQuery = NullIfEmpty(query)?.ToLowerInvariant();
        var activeStatus = ProjectLifecycleStatus.Active;
        var completedStatus = ProjectLifecycleStatus.Completed;

        var partners = _db.IndustryPartners.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(normalizedQuery) || !string.IsNullOrWhiteSpace(loweredQuery))
        {
            partners = partners.Where(partner =>
                (!string.IsNullOrWhiteSpace(normalizedQuery) &&
                 (partner.NormalizedName.Contains(normalizedQuery) ||
                  (partner.NormalizedLocation ?? string.Empty).Contains(normalizedQuery))) ||
                (!string.IsNullOrWhiteSpace(loweredQuery) &&
                 ((partner.Name ?? string.Empty).ToLower().Contains(loweredQuery) ||
                  (partner.Location ?? string.Empty).ToLower().Contains(loweredQuery) ||
                  partner.Contacts.Any(contact =>
                      (contact.Name ?? string.Empty).ToLower().Contains(loweredQuery) ||
                      (contact.Email ?? string.Empty).ToLower().Contains(loweredQuery) ||
                      (contact.Phone ?? string.Empty).ToLower().Contains(loweredQuery)))))
            ;
        }

        partners = partners.Where(partner => !partner.PartnerProjects.Any(link => link.ProjectId == projectId));

        return await partners
            .OrderBy(partner => partner.Name)
            .ThenBy(partner => partner.Id)
            .Take(take)
            .Select(partner => new ProjectJdpOptionDto(
                partner.Id,
                partner.Name,
                partner.Location,
                partner.PartnerProjects.Count(link =>
                    link.ProjectId != projectId &&
                    !link.Project.IsDeleted),
                partner.PartnerProjects.Count(link =>
                    link.ProjectId != projectId &&
                    !link.Project.IsDeleted &&
                    !link.Project.IsArchived &&
                    link.Project.LifecycleStatus == activeStatus),
                partner.PartnerProjects.Count(link =>
                    link.ProjectId != projectId &&
                    !link.Project.IsDeleted &&
                    !link.Project.IsArchived &&
                    link.Project.LifecycleStatus == completedStatus)))
            .ToListAsync(cancellationToken);
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
            await EnsureProjectCanBeLinkedAsync(request.ProjectId.Value, user, cancellationToken);
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
                CreatedByUserId = userId,
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
        await SaveWithValidationAsync(0, name, location, cancellationToken);
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

        await SaveWithConcurrencyAsync(id, name, location, cancellationToken);
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
            CreatedByUserId = GetUserId(user),
            CreatedUtc = DateTimeOffset.UtcNow
        };

        _db.IndustryPartnerContacts.Add(contact);
        Touch(partner, user);
        await SaveTouchingPartnerAsync(partner, user, cancellationToken);
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

        EnsureCanModifyContact(contact, user);
        ApplyConcurrencyToken(contact, request.RowVersion);
        var contactValue = ValidateContact(request);

        contact.Name = contactValue.Name;
        contact.Phone = contactValue.Phone;
        contact.Email = contactValue.Email;
        Touch(partner, user);

        try
        {
            await SaveTouchingPartnerAsync(partner, user, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw ConcurrencyError(exception);
        }
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

        EnsureCanModifyContact(contact, user);
        _db.IndustryPartnerContacts.Remove(contact);
        Touch(partner, user);
        try
        {
            await SaveTouchingPartnerAsync(partner, user, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw ConcurrencyError(exception);
        }
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

        await EnsureProjectCanBeLinkedAsync(projectId, user, cancellationToken);

        var existingLink = await _db.IndustryPartnerProjects
            .AsNoTracking()
            .AnyAsync(link => link.ProjectId == projectId && link.IndustryPartnerId == partnerId, cancellationToken);

        if (existingLink)
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
        await SaveProjectLinkAsync(partner, user, partnerId, projectId, cancellationToken);
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

        // Links to soft-deleted projects stay removable, so an organisation can be
        // cleaned up before deletion; the project-level permission still applies.
        await EnsureCanChangeProjectJdpAsync(projectId, user, includeDeletedProject: true, cancellationToken);

        _db.IndustryPartnerProjects.Remove(link);
        Touch(partner, user);
        await SaveTouchingPartnerAsync(partner, user, cancellationToken);
    }


    public async Task<ProjectMultiJdpProfileDto> AddProjectJdpAsync(
        int projectId,
        int partnerId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectCanBeLinkedAsync(projectId, user, cancellationToken);

        var partner = await _db.IndustryPartners
            .FirstOrDefaultAsync(item => item.Id == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Industry organisation not found.");

        var alreadyLinked = await _db.IndustryPartnerProjects
            .AsNoTracking()
            .AnyAsync(link => link.ProjectId == projectId && link.IndustryPartnerId == partnerId, cancellationToken);
        if (alreadyLinked)
        {
            throw Error("project", "This organisation is already linked to the project as a JDP.");
        }

        _db.IndustryPartnerProjects.Add(new IndustryPartnerProject
        {
            IndustryPartnerId = partnerId,
            ProjectId = projectId,
            LinkedByUserId = GetUserId(user),
            LinkedUtc = DateTimeOffset.UtcNow
        });

        Touch(partner, user);
        await SaveProjectLinkAsync(partner, user, partnerId, projectId, cancellationToken);

        return await GetProjectMultiJdpProfileAsync(projectId, cancellationToken);
    }

    public async Task<ProjectMultiJdpProfileDto> RemoveProjectJdpAsync(
        int projectId,
        int partnerId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectCanBeLinkedAsync(projectId, user, cancellationToken);

        var link = await _db.IndustryPartnerProjects
            .FirstOrDefaultAsync(item =>
                item.ProjectId == projectId &&
                item.IndustryPartnerId == partnerId, cancellationToken);
        if (link is null)
        {
            throw new KeyNotFoundException("The selected JDP is no longer linked to this project.");
        }

        var partner = await _db.IndustryPartners
            .FirstOrDefaultAsync(item => item.Id == partnerId, cancellationToken);

        _db.IndustryPartnerProjects.Remove(link);
        if (partner is not null)
        {
            Touch(partner, user);
            await SaveTouchingPartnerAsync(partner, user, cancellationToken);
        }
        else
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetProjectMultiJdpProfileAsync(projectId, cancellationToken);
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

        // The organisation is already gone; a file that cannot be removed now must not
        // turn a completed delete into an error for the user.
        foreach (var storageKey in storageKeys)
        {
            try
            {
                await _attachmentStorage.DeleteAsync(storageKey, CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.LogWarning(
                    exception,
                    "Industry partner {PartnerId} was deleted but attachment file {StorageKey} could not be removed.",
                    partnerId,
                    storageKey);
            }
        }
    }

    private static void EnsureCanModifyContact(IndustryPartnerContact contact, ClaimsPrincipal user)
    {
        var currentUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var canManageAny = Policies.IndustryPartners.ContactOverrideRoles.Any(user.IsInRole);
        var isAuthor = !string.IsNullOrWhiteSpace(currentUserId) &&
                       !string.IsNullOrWhiteSpace(contact.CreatedByUserId) &&
                       string.Equals(contact.CreatedByUserId, currentUserId, StringComparison.Ordinal);

        if (!canManageAny && !isAuthor)
        {
            throw new ForbiddenException("You can edit or remove only contacts that you created.");
        }
    }

    private async Task EnsureProjectCanBeLinkedAsync(
        int projectId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var exists = await _db.Projects
            .AsNoTracking()
            .AnyAsync(project => project.Id == projectId && !project.IsDeleted, cancellationToken);

        if (!exists)
        {
            throw Error("project", "Selected project was not found.");
        }

        await EnsureCanChangeProjectJdpAsync(projectId, user, includeDeletedProject: false, cancellationToken);
    }

    private async Task EnsureCanChangeProjectJdpAsync(
        int projectId,
        ClaimsPrincipal user,
        bool includeDeletedProject,
        CancellationToken cancellationToken)
    {
        var projects = _db.Projects.AsNoTracking().Where(project => project.Id == projectId);
        if (!includeDeletedProject)
        {
            projects = projects.Where(project => !project.IsDeleted);
        }

        var allowed = await ProjectJdpAccess
            .WhereUserCanManage(projects, user)
            .AnyAsync(cancellationToken);

        if (!allowed)
        {
            throw new ForbiddenException(ProjectJdpForbiddenMessage);
        }
    }

    // Adding or removing a contact or project link also stamps the organisation's
    // "last updated" fields, which changes its row version. Another user's unrelated
    // change to the same organisation must not fail this save, so a conflict that is
    // only on the organisation row is resolved by reloading it and stamping it again.
    private async Task SaveTouchingPartnerAsync(
        IndustryPartner partner,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException exception)
                when (attempt < TouchSaveAttempts &&
                      exception.Entries.Count > 0 &&
                      exception.Entries.All(entry => ReferenceEquals(entry.Entity, partner)))
            {
                var entry = _db.Entry(partner);
                await entry.ReloadAsync(cancellationToken);
                if (entry.State == EntityState.Detached)
                {
                    throw new KeyNotFoundException("Industry organisation not found.");
                }

                Touch(partner, user);
            }
        }
    }

    private async Task SaveProjectLinkAsync(
        IndustryPartner partner,
        ClaimsPrincipal user,
        int partnerId,
        int projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            await SaveTouchingPartnerAsync(partner, user, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            var duplicate = await _db.IndustryPartnerProjects
                .AsNoTracking()
                .AnyAsync(link => link.ProjectId == projectId && link.IndustryPartnerId == partnerId, cancellationToken);
            if (duplicate)
            {
                throw Error("project", "This organisation is already linked to the project as a JDP.");
            }

            throw new IndustryPartnerValidationException(
                new Dictionary<string, List<string>>
                {
                    ["project"] = new() { "Unable to add the JDP. The record may have changed; refresh and try again." }
                },
                exception);
        }
    }

    private async Task EnsureUniqueAsync(
        int id,
        string name,
        string? location,
        CancellationToken cancellationToken)
    {
        if (await DuplicateExistsAsync(id, name, location, cancellationToken))
        {
            throw Error("name", DuplicateOrganisationMessage);
        }
    }

    private Task<bool> DuplicateExistsAsync(
        int id,
        string name,
        string? location,
        CancellationToken cancellationToken)
    {
        var normalizedName = Normalize(name)!;
        var normalizedLocation = Normalize(location);
        return _db.IndustryPartners.AsNoTracking().AnyAsync(partner =>
            partner.Id != id &&
            partner.NormalizedName == normalizedName &&
            partner.NormalizedLocation == normalizedLocation,
            cancellationToken);
    }

    private async Task SaveWithValidationAsync(
        int id,
        string name,
        string? location,
        CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException)
        {
            throw await TranslateSaveFailureAsync(exception, id, name, location, cancellationToken);
        }
    }

    private async Task SaveWithConcurrencyAsync(
        int id,
        string name,
        string? location,
        CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw ConcurrencyError(exception);
        }
        catch (DbUpdateException exception)
        {
            throw await TranslateSaveFailureAsync(exception, id, name, location, cancellationToken);
        }
    }

    // Only report a duplicate when one really exists (for example, another user saved
    // the same name and location a moment earlier). Any other database failure, such
    // as a project deleted mid-request, gets a neutral message.
    private async Task<IndustryPartnerValidationException> TranslateSaveFailureAsync(
        DbUpdateException exception,
        int id,
        string name,
        string? location,
        CancellationToken cancellationToken)
    {
        if (await DuplicateExistsAsync(id, name, location, cancellationToken))
        {
            return new IndustryPartnerValidationException(
                new Dictionary<string, List<string>> { ["name"] = new() { DuplicateOrganisationMessage } },
                exception);
        }

        _logger.LogWarning(exception, "Saving industry partner {PartnerId} failed.", id);
        return new IndustryPartnerValidationException(
            new Dictionary<string, List<string>>
            {
                ["save"] = new() { "The organisation could not be saved. Refresh the page and try again." }
            },
            exception);
    }

    private static IndustryPartnerValidationException ConcurrencyError(DbUpdateConcurrencyException exception) =>
        new(
            new Dictionary<string, List<string>>
            {
                ["concurrency"] = new() { "This record was changed by another user. Reload it and apply your changes again." }
            },
            exception);

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
