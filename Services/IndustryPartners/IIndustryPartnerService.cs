using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.ViewModels.Projects.IndustryPartners;

namespace ProjectManagement.Services.IndustryPartners
{
    public interface IIndustryPartnerService
    {
        // Section: Partner queries
        Task<IReadOnlyList<PartnerDetailViewModel>> SearchPartnersAsync(PartnerSearchQuery query, CancellationToken cancellationToken = default);
        Task<PartnerDetailViewModel?> GetPartnerDetailAsync(int partnerId, CancellationToken cancellationToken = default);

        // Section: Partner commands
        Task<bool> ArchivePartnerAsync(int partnerId, CancellationToken cancellationToken = default);
        Task<bool> ReactivatePartnerAsync(int partnerId, CancellationToken cancellationToken = default);
        Task<bool> LinkProjectAsync(LinkProjectRequest request, CancellationToken cancellationToken = default);
        Task<bool> DeactivateAssociationAsync(int associationId, CancellationToken cancellationToken = default);
        Task<bool> UpdateOverviewAsync(UpdatePartnerOverviewRequest request, CancellationToken cancellationToken = default);
        Task<int> CreatePartnerAsync(CreatePartnerRequest request, CancellationToken cancellationToken = default);
    }
}
