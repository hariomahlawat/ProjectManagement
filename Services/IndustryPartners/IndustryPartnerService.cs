using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.IndustryPartners;
using ProjectManagement.Services.IndustryPartners.Exceptions;
using ProjectManagement.ViewModels.Common;
using ProjectManagement.ViewModels.Projects.IndustryPartners;

namespace ProjectManagement.Services.IndustryPartners
{
    public class IndustryPartnerService : IIndustryPartnerService
    {
        private readonly ApplicationDbContext _dbContext;

        public IndustryPartnerService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // Section: Partner queries
        public async Task<PagedResult<PartnerListItemViewModel>> SearchPartnersAsync(PartnerSearchQuery query, CancellationToken cancellationToken = default)
        {
            query ??= new PartnerSearchQuery();

            var term = (query.Query ?? string.Empty).Trim();
            var type = (query.Type ?? string.Empty).Trim();
            var status = (query.Status ?? string.Empty).Trim();
            var sort = (query.Sort ?? string.Empty).Trim();

            var partnersQuery = _dbContext.IndustryPartners.AsNoTracking();

            // Section: Directory filtering
            if (!string.IsNullOrWhiteSpace(term))
            {
                var likeTerm = $"%{term}%";
                partnersQuery = partnersQuery.Where(partner =>
                    EF.Functions.Like(partner.DisplayName, likeTerm) ||
                    (partner.LegalName != null && EF.Functions.Like(partner.LegalName, likeTerm)));
            }

            if (!string.IsNullOrWhiteSpace(type) && !string.Equals(type, "All", StringComparison.OrdinalIgnoreCase))
            {
                partnersQuery = partnersQuery.Where(partner => partner.PartnerType == type);
            }

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
            {
                var isActive = string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);
                partnersQuery = partnersQuery.Where(partner => partner.IsActive == isActive);
            }

            var totalCount = await partnersQuery.CountAsync(cancellationToken);

            // Section: Sorting
            partnersQuery = sort.ToLowerInvariant() switch
            {
                "projects" => partnersQuery.OrderByDescending(partner =>
                    partner.ProjectAssociations.Count(association => association.IsActive)).ThenBy(partner => partner.DisplayName),
                "updated" => partnersQuery.OrderByDescending(partner => partner.UpdatedUtc).ThenBy(partner => partner.DisplayName),
                _ => partnersQuery.OrderBy(partner => partner.DisplayName)
            };

            var partners = await partnersQuery
                .Select(partner => new
                {
                    partner.Id,
                    partner.DisplayName,
                    partner.PartnerType,
                    partner.IsActive,
                    partner.City,
                    partner.State,
                    partner.Country,
                    ActiveProjectCount = partner.ProjectAssociations.Count(association => association.IsActive)
                })
                .ToListAsync(cancellationToken);

            var items = new List<PartnerListItemViewModel>(partners.Count);
            foreach (var partner in partners)
            {
                var locationSummary = BuildLocationSummary(partner.City, partner.State, partner.Country);
                items.Add(new PartnerListItemViewModel
                {
                    Id = partner.Id,
                    DisplayName = partner.DisplayName,
                    PartnerType = partner.PartnerType,
                    Status = partner.IsActive ? "Active" : "Inactive",
                    LocationSummary = string.Equals(locationSummary, "—", StringComparison.Ordinal) ? null : locationSummary,
                    ActiveProjectCount = partner.ActiveProjectCount
                });
            }

            return new PagedResult<PartnerListItemViewModel>
            {
                Items = items,
                TotalCount = totalCount
            };
        }

        public async Task<PartnerDetailViewModel?> GetPartnerDetailAsync(int partnerId, CancellationToken cancellationToken = default)
        {
            var partner = await _dbContext.IndustryPartners
                .AsNoTracking()
                .Include(item => item.ProjectAssociations)
                .ThenInclude(item => item.Project)
                .FirstOrDefaultAsync(item => item.Id == partnerId, cancellationToken);

            if (partner is null)
            {
                return null;
            }

            var viewModel = new PartnerDetailViewModel
            {
                Id = partner.Id,
                DisplayName = partner.DisplayName,
                LegalName = partner.LegalName,
                PartnerType = partner.PartnerType,
                Status = partner.IsActive ? "Active" : "Inactive",
                RowVersion = partner.RowVersion,
                RegistrationNumber = partner.RegistrationNumber,
                Address = partner.Address,
                City = partner.City,
                State = partner.State,
                Country = partner.Country,
                Website = partner.Website,
                Email = partner.Email,
                Phone = partner.Phone,
                LocationSummary = BuildLocationSummary(partner.City, partner.State, partner.Country),
                ProjectCount = partner.ProjectAssociations.Count(association => association.IsActive),
                ProjectAssociations = partner.ProjectAssociations
                    .OrderByDescending(association => association.IsActive)
                    .ThenBy(association => association.Project?.Name)
                    .Select(association => new ProjectAssociationViewModel
                    {
                        AssociationId = association.Id,
                        ProjectName = association.Project?.Name ?? "(Project missing)",
                        ProjectLink = $"/projects/overview/{association.ProjectId}",
                        AssociationStatus = association.IsActive ? "Active" : "Inactive",
                        IsActive = association.IsActive,
                        Notes = association.Notes
                    })
                    .ToList(),
                Contacts = Array.Empty<PartnerContactViewModel>(),
                Documents = Array.Empty<PartnerDocumentViewModel>(),
                Notes = Array.Empty<PartnerNoteViewModel>()
            };

            return viewModel;
        }

        // Section: Project queries
        public async Task<IReadOnlyList<ProjectSearchItemViewModel>> SearchProjectsAsync(string q, int limit = 20, CancellationToken cancellationToken = default)
        {
            var term = (q ?? string.Empty).Trim();
            if (term.Length < 2)
            {
                return Array.Empty<ProjectSearchItemViewModel>();
            }

            // Section: Limit guardrails
            if (limit <= 0)
            {
                limit = 20;
            }

            if (limit > 20)
            {
                limit = 20;
            }

            // Section: Base query
            var baseQuery = _dbContext.Projects
                .AsNoTracking()
                .Where(project => !project.IsDeleted && !project.IsArchived);

            // Section: Starts-with preference
            var startsWithQuery = baseQuery
                .Where(project =>
                    EF.Functions.Like(project.Name, $"{term}%") ||
                    (project.CaseFileNumber != null && EF.Functions.Like(project.CaseFileNumber, $"{term}%")))
                .Select(project => new ProjectSearchItemViewModel
                {
                    ProjectId = project.Id,
                    ProjectName = project.Name,
                    ProjectCode = project.CaseFileNumber,
                    CategoryName = project.Category != null ? project.Category.Name : null
                });

            // Section: Contains fallback
            var containsQuery = baseQuery
                .Where(project =>
                    EF.Functions.Like(project.Name, $"%{term}%") ||
                    (project.CaseFileNumber != null && EF.Functions.Like(project.CaseFileNumber, $"%{term}%")))
                .Select(project => new ProjectSearchItemViewModel
                {
                    ProjectId = project.Id,
                    ProjectName = project.Name,
                    ProjectCode = project.CaseFileNumber,
                    CategoryName = project.Category != null ? project.Category.Name : null
                });

            // Section: Result shaping
            var results = await startsWithQuery
                .Union(containsQuery)
                .OrderBy(item => item.ProjectName)
                .ThenBy(item => item.ProjectCode)
                .Take(limit)
                .ToListAsync(cancellationToken);

            return results;
        }

        public async Task<ProjectSearchItemViewModel?> GetProjectSearchItemAsync(int projectId, CancellationToken cancellationToken = default)
        {
            if (projectId <= 0)
            {
                return null;
            }

            return await _dbContext.Projects
                .AsNoTracking()
                .Where(project => project.Id == projectId && !project.IsDeleted && !project.IsArchived)
                .Select(project => new ProjectSearchItemViewModel
                {
                    ProjectId = project.Id,
                    ProjectName = project.Name,
                    ProjectCode = project.CaseFileNumber,
                    CategoryName = project.Category != null ? project.Category.Name : null
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Section: Partner commands
        public async Task<bool> ArchivePartnerAsync(int partnerId, CancellationToken cancellationToken = default)
        {
            var partner = await _dbContext.IndustryPartners
                .FirstOrDefaultAsync(item => item.Id == partnerId, cancellationToken);

            if (partner is null || !partner.IsActive)
            {
                return false;
            }

            partner.IsActive = false;
            partner.UpdatedUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<bool> ReactivatePartnerAsync(int partnerId, CancellationToken cancellationToken = default)
        {
            var partner = await _dbContext.IndustryPartners
                .FirstOrDefaultAsync(item => item.Id == partnerId, cancellationToken);

            if (partner is null || partner.IsActive)
            {
                return false;
            }

            partner.IsActive = true;
            partner.UpdatedUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<bool> LinkProjectAsync(LinkProjectRequest request, CancellationToken cancellationToken = default)
        {
            if (request.PartnerId <= 0 || request.ProjectId <= 0)
            {
                return false;
            }

            var partner = await _dbContext.IndustryPartners
                .FirstOrDefaultAsync(item => item.Id == request.PartnerId, cancellationToken);

            if (partner is null || !partner.IsActive)
            {
                throw new IndustryPartnerInactiveException("Partner is inactive.");
            }

            var duplicateExists = await _dbContext.IndustryPartnerProjectAssociations
                .AnyAsync(item =>
                        item.IndustryPartnerId == request.PartnerId &&
                        item.ProjectId == request.ProjectId &&
                        item.IsActive,
                    cancellationToken);

            if (duplicateExists)
            {
                throw new DuplicateAssociationException("Duplicate association detected.");
            }

            var association = new IndustryPartnerProjectAssociation
            {
                IndustryPartnerId = request.PartnerId,
                ProjectId = request.ProjectId,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                LinkedOnUtc = DateTime.UtcNow,
                IsActive = true
            };

            _dbContext.IndustryPartnerProjectAssociations.Add(association);
            partner.UpdatedUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<bool> DeactivateAssociationAsync(int associationId, CancellationToken cancellationToken = default)
        {
            var association = await _dbContext.IndustryPartnerProjectAssociations
                .Include(item => item.IndustryPartner)
                .FirstOrDefaultAsync(item => item.Id == associationId, cancellationToken);

            if (association is null || !association.IsActive)
            {
                return false;
            }

            association.IsActive = false;
            association.DeactivatedUtc = DateTime.UtcNow;
            if (association.IndustryPartner != null)
            {
                association.IndustryPartner.UpdatedUtc = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<PartnerOverviewUpdateResult> UpdateOverviewAsync(UpdatePartnerOverviewRequest request, CancellationToken cancellationToken = default)
        {
            if (request.PartnerId <= 0 ||
                string.IsNullOrWhiteSpace(request.DisplayName) ||
                string.IsNullOrWhiteSpace(request.PartnerType))
            {
                return PartnerOverviewUpdateResult.Missing;
            }

            if (!TryDecodeRowVersion(request.RowVersion, out var rowVersion))
            {
                return PartnerOverviewUpdateResult.ConcurrencyConflict;
            }

            var partner = await _dbContext.IndustryPartners
                .FirstOrDefaultAsync(item => item.Id == request.PartnerId, cancellationToken);

            if (partner is null)
            {
                return PartnerOverviewUpdateResult.Missing;
            }

            _dbContext.Entry(partner).Property(item => item.RowVersion).OriginalValue = rowVersion;

            // Section: Identity
            partner.DisplayName = request.DisplayName.Trim();
            partner.LegalName = string.IsNullOrWhiteSpace(request.LegalName) ? null : request.LegalName.Trim();
            partner.PartnerType = request.PartnerType.Trim();

            // Section: Registration
            partner.RegistrationNumber = string.IsNullOrWhiteSpace(request.RegistrationNumber) ? null : request.RegistrationNumber.Trim();

            // Section: Location
            partner.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
            partner.City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim();
            partner.State = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim();
            partner.Country = string.IsNullOrWhiteSpace(request.Country) ? null : request.Country.Trim();

            // Section: Contact
            partner.Website = string.IsNullOrWhiteSpace(request.Website) ? null : request.Website.Trim();
            partner.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
            partner.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

            partner.UpdatedUtc = DateTime.UtcNow;

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return PartnerOverviewUpdateResult.ConcurrencyConflict;
            }

            return PartnerOverviewUpdateResult.Success;
        }

        public async Task<int> CreatePartnerAsync(CreatePartnerRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.PartnerType))
            {
                return 0;
            }

            var partner = new IndustryPartner
            {
                // Section: Identity
                DisplayName = request.DisplayName.Trim(),
                LegalName = string.IsNullOrWhiteSpace(request.LegalName) ? null : request.LegalName.Trim(),
                PartnerType = request.PartnerType.Trim(),
                IsActive = true,

                // Section: Registration
                RegistrationNumber = string.IsNullOrWhiteSpace(request.RegistrationNumber) ? null : request.RegistrationNumber.Trim(),

                // Section: Location
                Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
                City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(),
                State = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim(),
                Country = string.IsNullOrWhiteSpace(request.Country) ? null : request.Country.Trim(),

                // Section: Contact
                Website = string.IsNullOrWhiteSpace(request.Website) ? null : request.Website.Trim(),
                Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
                Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),

                // Section: Audit
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            _dbContext.IndustryPartners.Add(partner);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return partner.Id;
        }

        // Section: Helpers
        private static string BuildLocationSummary(string? city, string? state, string? country)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(city))
            {
                parts.Add(city.Trim());
            }

            if (!string.IsNullOrWhiteSpace(state))
            {
                parts.Add(state.Trim());
            }

            if (!string.IsNullOrWhiteSpace(country))
            {
                parts.Add(country.Trim());
            }

            return parts.Count == 0 ? "—" : string.Join(", ", parts);
        }

        // Section: Row version helpers
        private static bool TryDecodeRowVersion(string? value, out byte[] rowVersion)
        {
            rowVersion = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                rowVersion = Convert.FromBase64String(value);
                return rowVersion.Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
