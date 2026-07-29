using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Configuration;
using ProjectManagement.Pages.IndustryPartners;
using ProjectManagement.Services.IndustryPartners;
using Xunit;

namespace ProjectManagement.Tests.IndustryPartners;

public sealed class IndustryPartnerOrganisationAuthorizationTests
{
    [Fact]
    public void CreateRoles_ContainApprovedOperationalRoles()
    {
        var expected = new[]
        {
            RoleNames.Admin,
            RoleNames.HoD,
            RoleNames.Comdt,
            RoleNames.ProjectOfficer,
            RoleNames.ProjectOffice,
            RoleNames.ProjectOfficeAlternate,
            RoleNames.Mco,
            RoleNames.Ta,
            RoleNames.Ito
        };

        Assert.Equal(
            expected.OrderBy(role => role, StringComparer.Ordinal),
            Policies.IndustryPartners.CreateAllowedRoles.OrderBy(role => role, StringComparer.Ordinal));
    }

    [Fact]
    public void EditAnyRoles_AreLimitedToAdminHodAndComdt()
    {
        var expected = new[]
        {
            RoleNames.Admin,
            RoleNames.HoD,
            RoleNames.Comdt
        };

        Assert.Equal(
            expected.OrderBy(role => role, StringComparer.Ordinal),
            Policies.IndustryPartners.EditAnyAllowedRoles.OrderBy(role => role, StringComparer.Ordinal));
    }

    [Fact]
    public async Task UpdateOrganisation_OwnerCanEdit_WhenUserHasNoOverrideRole()
    {
        var service = new RecordingIndustryPartnerService(isOwner: true);
        var page = CreatePage(
            service,
            new PolicyAuthorizationService(Policies.IndustryPartners.EditAny, isAllowed: false),
            userId: "owner-1",
            role: RoleNames.ProjectOfficer);

        var result = await page.OnPostUpdatePartnerAsync(
            partnerId: 42,
            name: "Updated organisation",
            location: "Delhi",
            remarks: "Owner update",
            rowVersion: null,
            CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(42, service.UpdatedPartnerId);
        Assert.Equal("Updated organisation", service.UpdateRequest?.Name);
    }

    [Fact]
    public async Task UpdateOrganisation_NonOwnerWithoutOverride_IsForbidden()
    {
        var service = new RecordingIndustryPartnerService(isOwner: false);
        var page = CreatePage(
            service,
            new PolicyAuthorizationService(Policies.IndustryPartners.EditAny, isAllowed: false),
            userId: "other-user",
            role: RoleNames.Mco);

        var result = await page.OnPostUpdatePartnerAsync(
            partnerId: 42,
            name: "Blocked update",
            location: null,
            remarks: null,
            rowVersion: null,
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.Null(service.UpdatedPartnerId);
    }

    [Theory]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.HoD)]
    [InlineData(RoleNames.Comdt)]
    public async Task UpdateOrganisation_OverrideRolesCanEditAnyRecord(string role)
    {
        var service = new RecordingIndustryPartnerService(isOwner: false);
        var page = CreatePage(
            service,
            new PolicyAuthorizationService(Policies.IndustryPartners.EditAny, isAllowed: true),
            userId: "command-user",
            role: role);

        var result = await page.OnPostUpdatePartnerAsync(
            partnerId: 42,
            name: "Command update",
            location: null,
            remarks: null,
            rowVersion: null,
            CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(42, service.UpdatedPartnerId);
    }

    private static IndexModel CreatePage(
        RecordingIndustryPartnerService service,
        IAuthorizationService authorizationService,
        string userId,
        string role)
    {
        var page = new IndexModel(service, new StubAttachmentManager(), authorizationService);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role)
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role));
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());

        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
        return page;
    }

    private sealed class PolicyAuthorizationService : IAuthorizationService
    {
        private readonly string _policyName;
        private readonly bool _isAllowed;

        public PolicyAuthorizationService(string policyName, bool isAllowed)
        {
            _policyName = policyName;
            _isAllowed = isAllowed;
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(
                string.Equals(policyName, _policyName, StringComparison.Ordinal) && _isAllowed
                    ? AuthorizationResult.Success()
                    : AuthorizationResult.Failed());
    }

    private sealed class RecordingIndustryPartnerService : IIndustryPartnerService
    {
        private readonly bool _isOwner;

        public RecordingIndustryPartnerService(bool isOwner) => _isOwner = isOwner;

        public int? UpdatedPartnerId { get; private set; }
        public UpdateIndustryPartnerRequest? UpdateRequest { get; private set; }

        public Task<bool> IsOwnerAsync(int id, string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_isOwner);

        public Task UpdateAsync(
            int id,
            UpdateIndustryPartnerRequest request,
            ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            UpdatedPartnerId = id;
            UpdateRequest = request;
            return Task.CompletedTask;
        }

        public Task<IndustryPartnerSearchResult> SearchAsync(string? query, IndustryPartnerDirectoryFilter filter, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IndustryPartnerDuplicateSuggestion>> FindDuplicateSuggestionsAsync(string? name, int take = 5, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IndustryPartnerDto?> GetAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IndustryPartnerProjectContextDto?> GetProjectContextAsync(int projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProjectJdpProfileDto> GetProjectJdpProfileAsync(int projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProjectMultiJdpProfileDto> GetProjectMultiJdpProfileAsync(int projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectJdpOptionDto>> SearchProjectJdpOptionsAsync(int projectId, string? query, int take = 10, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CreateAsync(CreateIndustryPartnerRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> AddContactAsync(int partnerId, ContactRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateContactAsync(int partnerId, int contactId, ContactRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteContactAsync(int partnerId, int contactId, ClaimsPrincipal user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LinkProjectAsync(int partnerId, int projectId, ClaimsPrincipal user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnlinkProjectAsync(int partnerId, int projectId, ClaimsPrincipal user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProjectMultiJdpProfileDto> AddProjectJdpAsync(int projectId, int partnerId, ClaimsPrincipal user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProjectMultiJdpProfileDto> RemoveProjectJdpAsync(int projectId, int partnerId, ClaimsPrincipal user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePartnerAsync(int partnerId, ClaimsPrincipal user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubAttachmentManager : IIndustryPartnerAttachmentManager
    {
        public Task<Guid> UploadAsync(int partnerId, IFormFile file, ClaimsPrincipal user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(Stream Stream, string FileName, string ContentType)> DownloadAsync(int partnerId, Guid attachmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(int partnerId, Guid attachmentId, ClaimsPrincipal user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }
}
