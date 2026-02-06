using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace ProjectManagement.Services.IndustryPartners;

public interface IIndustryPartnerService
{
    Task<IndustryPartnerSearchResult> SearchAsync(string? query, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IndustryPartnerDto?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<int> CreateAsync(CreateIndustryPartnerRequest req, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task UpdateFieldAsync(int id, string field, string? value, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<int> AddContactAsync(int partnerId, ContactRequest req, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task UpdateContactAsync(int partnerId, int contactId, ContactRequest req, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task DeleteContactAsync(int partnerId, int contactId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task LinkProjectAsync(int partnerId, int projectId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task UnlinkProjectAsync(int partnerId, int projectId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task DeletePartnerAsync(int partnerId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

public sealed record IndustryPartnerSearchResult(IReadOnlyList<IndustryPartnerListItem> Items, int Total, int Page, int PageSize);
public sealed record IndustryPartnerListItem(int Id, string Name, string? Location, int ContactCount, int AttachmentCount, int ProjectCount);
public sealed record IndustryPartnerContactDto(int Id, string? Name, string? Phone, string? Email, DateTimeOffset CreatedUtc);
public sealed record IndustryPartnerAttachmentDto(Guid Id, string OriginalFileName, string ContentType, long SizeBytes, DateTimeOffset UploadedUtc);
public sealed record IndustryPartnerProjectDto(int ProjectId, string ProjectName);
public sealed record IndustryPartnerDto(int Id, string Name, string? Location, string? Remarks, IReadOnlyList<IndustryPartnerContactDto> Contacts, IReadOnlyList<IndustryPartnerAttachmentDto> Attachments, IReadOnlyList<IndustryPartnerProjectDto> LinkedProjects);
public sealed record CreateIndustryPartnerRequest(string Name, string? Location, string? Remarks);
public sealed record ContactRequest(string? Name, string? Phone, string? Email);
