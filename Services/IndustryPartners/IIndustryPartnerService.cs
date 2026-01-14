using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.ViewModels.Common;
using ProjectManagement.ViewModels.Projects.IndustryPartners;

namespace ProjectManagement.Services.IndustryPartners
{
    public interface IIndustryPartnerService
    {
        // Section: Partner queries
        Task<PagedResult<PartnerListItemViewModel>> SearchPartnersAsync(PartnerSearchQuery query, CancellationToken cancellationToken = default);
        Task<PartnerDetailViewModel?> GetPartnerDetailAsync(int partnerId, CancellationToken cancellationToken = default);

        // Section: Project queries
        Task<IReadOnlyList<ProjectSearchItemViewModel>> SearchProjectsAsync(string q, int limit = 20, CancellationToken cancellationToken = default);
        Task<ProjectSearchItemViewModel?> GetProjectSearchItemAsync(int projectId, CancellationToken cancellationToken = default);

        // Section: Partner commands
        Task<bool> ArchivePartnerAsync(int partnerId, CancellationToken cancellationToken = default);
        Task<bool> ReactivatePartnerAsync(int partnerId, CancellationToken cancellationToken = default);
        Task<bool> LinkProjectAsync(LinkProjectRequest request, CancellationToken cancellationToken = default);
        Task<bool> DeactivateAssociationAsync(int associationId, CancellationToken cancellationToken = default);
        Task<PartnerOverviewUpdateResult> UpdateOverviewAsync(UpdatePartnerOverviewRequest request, CancellationToken cancellationToken = default);
        Task<int> CreatePartnerAsync(CreatePartnerRequest request, CancellationToken cancellationToken = default);
    }
}
