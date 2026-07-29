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
    Task<bool> IsOwnerAsync(int id, string userId, CancellationToken cancellationToken = default);
    Task<IndustryPartnerProjectContextDto?> GetProjectContextAsync(int projectId, CancellationToken cancellationToken = default);
    Task<ProjectJdpProfileDto> GetProjectJdpProfileAsync(int projectId, CancellationToken cancellationToken = default);
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


public sealed record ProjectJdpLinkedProjectDto(
    int ProjectId,
    string ProjectName,
    string? CaseFileNumber,
    ProjectLifecycleStatus LifecycleStatus,
    bool IsArchived,
    bool IsDeleted)
{
    public string StatusLabel => IsDeleted
        ? "Deleted"
        : IsArchived
            ? "Archived"
            : LifecycleStatus switch
            {
                ProjectLifecycleStatus.Completed => "Completed",
                ProjectLifecycleStatus.Cancelled => "Cancelled",
                _ => "Ongoing"
            };

    public int StatusOrder => StatusLabel switch
    {
        "Ongoing" => 0,
        "Completed" => 1,
        "Cancelled" => 2,
        "Archived" => 3,
        _ => 4
    };
}

public sealed record ProjectJdpProfileDto(
    int ProjectId,
    int? PartnerId,
    string? PartnerName,
    string? PartnerLocation,
    IReadOnlyList<ProjectJdpLinkedProjectDto> OtherProjects,
    bool HasMultipleProjectLinks)
{
    public bool HasJdp => PartnerId.HasValue;

    public int OtherProjectCount => OtherProjects.Count;

    public int OtherOngoingProjectCount => OtherProjects.Count(project => project.StatusLabel == "Ongoing");

    public int OtherCompletedProjectCount => OtherProjects.Count(project => project.StatusLabel == "Completed");

    public int OtherProjectStatusCount => Math.Max(
        0,
        OtherProjectCount - OtherOngoingProjectCount - OtherCompletedProjectCount);

    public string CardTitle => HasJdp
        ? PartnerName ?? "JDP linked"
        : "No JDP linked";

    public string CardSummary
    {
        get
        {
            if (HasMultipleProjectLinks)
            {
                return "Multiple JDP links recorded · correction required";
            }

            if (!HasJdp)
            {
                return "Link an industry partner";
            }

            if (OtherProjectCount == 0)
            {
                return "Not linked to any other project";
            }

            var parts = new List<string>();
            if (OtherOngoingProjectCount > 0)
            {
                parts.Add($"{OtherOngoingProjectCount} ongoing");
            }

            if (OtherCompletedProjectCount > 0)
            {
                parts.Add($"{OtherCompletedProjectCount} completed");
            }

            if (OtherProjectStatusCount > 0)
            {
                parts.Add($"{OtherProjectStatusCount} other");
            }

            return $"Also linked to {OtherProjectCount} other {(OtherProjectCount == 1 ? "project" : "projects")}" +
                   (parts.Count == 0 ? string.Empty : $" · {string.Join(" · ", parts)}");
        }
    }

    public static ProjectJdpProfileDto Empty(int projectId) =>
        new(projectId, null, null, null, Array.Empty<ProjectJdpLinkedProjectDto>(), false);
}

public sealed record ProjectJdpOptionDto(
    int Id,
    string Name,
    string? Location,
    int OtherProjectCount,
    int OtherOngoingProjectCount,
    int OtherCompletedProjectCount,
    bool IsLinkedToProject)
{
    public string UsageSummary
    {
        get
        {
            if (OtherProjectCount == 0)
            {
                return "Not linked to any other project";
            }

            var parts = new List<string>();
            if (OtherOngoingProjectCount > 0)
            {
                parts.Add($"{OtherOngoingProjectCount} ongoing");
            }

            if (OtherCompletedProjectCount > 0)
            {
                parts.Add($"{OtherCompletedProjectCount} completed");
            }

            var otherCount = Math.Max(0, OtherProjectCount - OtherOngoingProjectCount - OtherCompletedProjectCount);
            if (otherCount > 0)
            {
                parts.Add($"{otherCount} other");
            }

            return $"{OtherProjectCount} other {(OtherProjectCount == 1 ? "project" : "projects")}" +
                   (parts.Count == 0 ? string.Empty : $" · {string.Join(" · ", parts)}");
        }
    }
}
