using System.Security.Claims;
using ProjectManagement.Models;

namespace ProjectManagement.Services.IndustryPartners;

public interface IIndustryPartnerService
{
    Task<IndustryPartnerSearchResult> SearchAsync(
        string? query,
        IndustryPartnerDirectoryFilter filter,
        int page,
        int pageSize,
        IndustryPartnerDirectorySort sort = IndustryPartnerDirectorySort.Name,
        CancellationToken cancellationToken = default);

    /// <summary>Every organisation matching the directory search and filter, with all contacts, for export.</summary>
    Task<IReadOnlyList<IndustryPartnerExportRow>> ExportDirectoryAsync(
        string? query,
        IndustryPartnerDirectoryFilter filter,
        IndustryPartnerDirectorySort sort,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndustryPartnerDuplicateSuggestion>> FindDuplicateSuggestionsAsync(
        string? name,
        int take = 5,
        CancellationToken cancellationToken = default);

    Task<IndustryPartnerDto?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> IsOwnerAsync(int id, string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<int>> GetJdpManageableProjectIdsAsync(
        IEnumerable<int> projectIds,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
    Task<IndustryPartnerProjectContextDto?> GetProjectContextAsync(int projectId, CancellationToken cancellationToken = default);
    Task<ProjectMultiJdpProfileDto> GetProjectMultiJdpProfileAsync(int projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectJdpOptionDto>> SearchProjectJdpOptionsAsync(
        int projectId,
        string? query,
        int take = 10,
        CancellationToken cancellationToken = default);

    Task<int> CreateAsync(CreateIndustryPartnerRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, UpdateIndustryPartnerRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task<int> AddContactAsync(int partnerId, ContactRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task UpdateContactAsync(int partnerId, int contactId, ContactRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task DeleteContactAsync(int partnerId, int contactId, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task LinkProjectAsync(int partnerId, int projectId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task UnlinkProjectAsync(int partnerId, int projectId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<ProjectMultiJdpProfileDto> AddProjectJdpAsync(
        int projectId,
        int partnerId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
    Task<ProjectMultiJdpProfileDto> RemoveProjectJdpAsync(
        int projectId,
        int partnerId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
    Task DeletePartnerAsync(int partnerId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

public enum IndustryPartnerDirectoryFilter
{
    All = 0,
    ContactOnly = 1,
    JdpAssociated = 2,
    CurrentJdp = 3,
    PastJdp = 4
}

public enum IndustryPartnerDirectorySort
{
    Name = 0,
    RecentlyUpdated = 1
}

public sealed record IndustryPartnerExportContact(
    string? Name,
    string? Phone,
    string? Email);

// One organisation as exported; contacts are in the order the directory shows them.
public sealed record IndustryPartnerExportRow(
    int Id,
    string Name,
    string? Location,
    string? Remarks,
    int ProjectCount,
    int ActiveProjectCount,
    DateTimeOffset LastUpdatedUtc,
    IReadOnlyList<IndustryPartnerExportContact> Contacts)
{
    public string StatusLabel => JdpStatusRules.PartnerStatusLabel(
        JdpStatusRules.PartnerStatusKey(ProjectCount, ActiveProjectCount));

    public IndustryPartnerExportContact? PrimaryContact => Contacts.Count > 0 ? Contacts[0] : null;
}

public sealed record IndustryPartnerSearchResult(
    IReadOnlyList<IndustryPartnerListItem> Items,
    int Total,
    int Page,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

// ProjectCount and ActiveProjectCount count links to live (not deleted) projects only.
public sealed record IndustryPartnerListItem(
    int Id,
    string Name,
    string? Location,
    string? PrimaryContactName,
    string? ContactPhone,
    string? ContactEmail,
    int ContactCount,
    int AttachmentCount,
    int ProjectCount,
    int ActiveProjectCount,
    DateTimeOffset LastUpdatedUtc)
{
    public string StatusKey => JdpStatusRules.PartnerStatusKey(ProjectCount, ActiveProjectCount);

    public string StatusLabel => JdpStatusRules.PartnerStatusLabel(StatusKey);
}

public sealed record IndustryPartnerDuplicateSuggestion(
    int Id,
    string Name,
    string? Location,
    int ContactCount,
    int ProjectCount);

public sealed record IndustryPartnerContactDto(
    int Id,
    string? Name,
    string? Phone,
    string? Email,
    string? CreatedByUserId,
    DateTimeOffset CreatedUtc,
    string RowVersion)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "General contact" : Name.Trim();
}

public sealed record IndustryPartnerAttachmentDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedUtc);

public sealed record IndustryPartnerProjectDto(
    int ProjectId,
    string ProjectName,
    string? CaseFileNumber,
    ProjectLifecycleStatus LifecycleStatus,
    bool IsArchived,
    bool IsDeleted,
    DateTimeOffset LinkedUtc)
{
    public JdpProjectStatus Status => JdpStatusRules.ForProject(LifecycleStatus, IsArchived, IsDeleted);

    public bool IsCurrent => Status == JdpProjectStatus.Ongoing;

    public string ProjectStatusLabel => JdpStatusRules.Label(Status);
}

public sealed record IndustryPartnerDto(
    int Id,
    string Name,
    string? Location,
    string? Remarks,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastUpdatedUtc,
    string RowVersion,
    IReadOnlyList<IndustryPartnerContactDto> Contacts,
    IReadOnlyList<IndustryPartnerAttachmentDto> Attachments,
    IReadOnlyList<IndustryPartnerProjectDto> LinkedProjects)
{
    public IndustryPartnerContactDto? PrimaryContact => Contacts.FirstOrDefault();

    /// <summary>Links to projects that are not deleted; LinkedProjects also lists deleted ones so they can be removed.</summary>
    public int LiveProjectCount => LinkedProjects.Count(project => project.Status != JdpProjectStatus.Deleted);

    public int ActiveProjectCount => LinkedProjects.Count(project => project.IsCurrent);

    public string StatusKey => JdpStatusRules.PartnerStatusKey(LiveProjectCount, ActiveProjectCount);

    public string StatusLabel => JdpStatusRules.PartnerStatusLabel(StatusKey);
}

public sealed record IndustryPartnerProjectContextDto(
    int Id,
    string Name,
    string? CaseFileNumber,
    ProjectLifecycleStatus LifecycleStatus,
    bool IsArchived);

public sealed record CreateIndustryPartnerRequest(
    string Name,
    string? Location,
    string? Remarks = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    int? ProjectId = null);

public sealed record UpdateIndustryPartnerRequest(
    string Name,
    string? Location,
    string? Remarks,
    string? RowVersion);

public sealed record ContactRequest(
    string? Name,
    string? Phone,
    string? Email,
    string? RowVersion = null);

public sealed record ProjectJdpLinkedProjectDto(
    int ProjectId,
    string ProjectName,
    string? CaseFileNumber,
    ProjectLifecycleStatus LifecycleStatus,
    bool IsArchived,
    bool IsDeleted)
{
    public JdpProjectStatus Status => JdpStatusRules.ForProject(LifecycleStatus, IsArchived, IsDeleted);

    public string StatusLabel => JdpStatusRules.Label(Status);

    public int StatusOrder => (int)Status;
}

public sealed record ProjectJdpOptionDto(
    int Id,
    string Name,
    string? Location,
    int OtherProjectCount,
    int OtherOngoingProjectCount,
    int OtherCompletedProjectCount)
{
    public string UsageSummary => JdpStatusRules.UsageSummary(
        OtherProjectCount,
        OtherOngoingProjectCount,
        OtherCompletedProjectCount);
}
