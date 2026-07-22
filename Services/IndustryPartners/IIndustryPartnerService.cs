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
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndustryPartnerDuplicateSuggestion>> FindDuplicateSuggestionsAsync(
        string? name,
        int take = 5,
        CancellationToken cancellationToken = default);

    Task<IndustryPartnerDto?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<IndustryPartnerProjectContextDto?> GetProjectContextAsync(int projectId, CancellationToken cancellationToken = default);

    Task<int> CreateAsync(CreateIndustryPartnerRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, UpdateIndustryPartnerRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task<int> AddContactAsync(int partnerId, ContactRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task UpdateContactAsync(int partnerId, int contactId, ContactRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task DeleteContactAsync(int partnerId, int contactId, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task LinkProjectAsync(int partnerId, int projectId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task UnlinkProjectAsync(int partnerId, int projectId, ClaimsPrincipal user, CancellationToken cancellationToken = default);

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
    public string StatusKey => ProjectCount == 0
        ? "contact"
        : ActiveProjectCount > 0
            ? "current"
            : "past";

    public string StatusLabel => StatusKey switch
    {
        "current" => "Current JDP",
        "past" => "Past JDP",
        _ => "Contact only"
    };
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
    public string ProjectStatusLabel => IsDeleted
        ? "Deleted project"
        : IsArchived
            ? "Archived"
            : LifecycleStatus switch
            {
                ProjectLifecycleStatus.Completed => "Completed",
                ProjectLifecycleStatus.Cancelled => "Cancelled",
                _ => "Ongoing"
            };
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

    public int ActiveProjectCount => LinkedProjects.Count(project =>
        !project.IsDeleted &&
        !project.IsArchived &&
        project.LifecycleStatus == ProjectLifecycleStatus.Active);

    public string StatusLabel => LinkedProjects.Count == 0
        ? "Contact only"
        : ActiveProjectCount > 0
            ? "Current JDP"
            : "Past JDP";
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
