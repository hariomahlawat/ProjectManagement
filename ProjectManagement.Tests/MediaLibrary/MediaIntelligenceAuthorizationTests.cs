using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Features.MediaLibrary.Admin;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Services.Admin;
using Xunit;
using ClassificationsModel = ProjectManagement.Pages.Admin.MediaIntelligence.ClassificationsModel;
using MediaIntelligenceIndexModel = ProjectManagement.Pages.Admin.MediaIntelligence.IndexModel;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// Media Intelligence used to check the Admin and HoD roles directly, so the split media
/// capabilities (classification, queue) shown in Access Governance controlled nothing.
/// </summary>
public sealed class MediaIntelligenceAuthorizationTests
{
    [Theory]
    [InlineData(typeof(ClassificationsModel))]
    [InlineData(typeof(MediaIntelligenceIndexModel))]
    public void PagesRequireTheMediaViewPolicyInsteadOfRoles(Type pageType)
    {
        var authorize = pageType.GetCustomAttributes<AuthorizeAttribute>().ToArray();

        var single = Assert.Single(authorize);
        Assert.Equal(AdminPolicies.MediaView, single.Policy);
        Assert.Null(single.Roles);
    }

    [Fact]
    public async Task ClassificationChanges_AreRefusedWithoutTheClassificationPolicy()
    {
        var access = new PolicyAccess(AdminPolicies.MediaView);
        var page = CreateClassificationsPage(access);

        var (result, ran) = await RunHandlerAsync(page, "POST", "ReclassifyStale", nameof(ClassificationsModel.OnPostReclassifyStaleAsync));

        Assert.IsType<ForbidResult>(result);
        Assert.False(ran);
        Assert.Contains(AdminPolicies.MediaClassificationManage, access.Checked);
    }

    [Fact]
    public async Task ClassificationChanges_RunWithTheClassificationPolicy()
    {
        var page = CreateClassificationsPage(new PolicyAccess(AdminPolicies.MediaView, AdminPolicies.MediaClassificationManage));

        var (result, ran) = await RunHandlerAsync(page, "POST", "ReclassifyStale", nameof(ClassificationsModel.OnPostReclassifyStaleAsync));

        Assert.Null(result);
        Assert.True(ran);
    }

    [Fact]
    public async Task Queueing_IsRefusedWithoutTheQueuePolicy_ButReadinessRefreshIsNot()
    {
        var access = new PolicyAccess(AdminPolicies.MediaView);

        var (queueResult, queueRan) = await RunHandlerAsync(
            CreateIndexPage(access), "POST", "Queue", nameof(MediaIntelligenceIndexModel.OnPostQueueAsync));
        var (refreshResult, refreshRan) = await RunHandlerAsync(
            CreateIndexPage(access), "POST", "RefreshReadiness", nameof(MediaIntelligenceIndexModel.OnPostRefreshReadinessAsync));

        Assert.IsType<ForbidResult>(queueResult);
        Assert.False(queueRan);
        Assert.Null(refreshResult);
        Assert.True(refreshRan);
    }

    // A reset image stays "stale" (ClassifierVersion null) until the worker classifies it, so
    // running Reclassify stale again used to re-reset the same first batch. Images already
    // waiting in the classification queue are now left out.
    [Fact]
    public async Task ReclassifyStale_LeavesImagesAlreadyQueuedAlone()
    {
        var db = CreateMediaDb();
        var sourceId = Guid.NewGuid();
        db.Assets.AddRange(
            new ProjectManagement.Features.MediaLibrary.Domain.MediaAsset { Id = 1, SourceId = sourceId, Kind = ProjectManagement.Features.MediaLibrary.Domain.MediaAssetKind.Photo, ClassifierVersion = null },
            new ProjectManagement.Features.MediaLibrary.Domain.MediaAsset { Id = 2, SourceId = sourceId, Kind = ProjectManagement.Features.MediaLibrary.Domain.MediaAssetKind.Photo, ClassifierVersion = null });
        db.ProcessingJobs.AddRange(
            new ProjectManagement.Features.MediaLibrary.Domain.MediaProcessingJob { Id = 1, MediaAssetId = 1, JobType = ProjectManagement.Features.MediaLibrary.Domain.MediaProcessingJobType.ClassifyMedia, Status = ProjectManagement.Features.MediaLibrary.Domain.MediaProcessingJobStatus.Pending },
            new ProjectManagement.Features.MediaLibrary.Domain.MediaProcessingJob { Id = 2, MediaAssetId = 2, JobType = ProjectManagement.Features.MediaLibrary.Domain.MediaProcessingJobType.ClassifyMedia, Status = ProjectManagement.Features.MediaLibrary.Domain.MediaProcessingJobStatus.Pending });
        await db.SaveChangesAsync();

        // The invalidation and audit dependencies throw if called: nothing may be reset again.
        var page = new ClassificationsModel(
            db,
            Unused<ProjectManagement.Features.MediaLibrary.Services.IMediaClassificationOverrideService>(),
            Unused<ProjectManagement.Features.MediaLibrary.Services.IFaceEligibilityPolicy>(),
            Unused<ProjectManagement.Features.MediaLibrary.Services.IMediaContentChangeInvalidationService>(),
            Options.Create(new MediaLibraryOptions()),
            new PolicyAccess(AdminPolicies.MediaView, AdminPolicies.MediaClassificationManage),
            Unused<IAdminAuditService>(),
            NullLogger<ClassificationsModel>.Instance);

        var result = await page.OnPostReclassifyStaleAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("already queued", page.StatusMessage);
    }

    private static async Task<(IActionResult? Result, bool HandlerRan)> RunHandlerAsync(
        PageModel page,
        string method,
        string handlerName,
        string methodName)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        var pageContext = new PageContext(new ActionContext(http, new RouteData(), new CompiledPageActionDescriptor()));
        page.PageContext = pageContext;
        var handler = new HandlerMethodDescriptor
        {
            Name = handlerName,
            HttpMethod = method,
            MethodInfo = page.GetType().GetMethod(methodName)!
        };
        var context = new PageHandlerExecutingContext(
            pageContext,
            Array.Empty<IFilterMetadata>(),
            handler,
            new Dictionary<string, object?>(),
            page);

        var ran = false;
        await page.OnPageHandlerExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new PageHandlerExecutedContext(pageContext, Array.Empty<IFilterMetadata>(), handler, page));
        });

        return (context.Result, ran);
    }

    private static ClassificationsModel CreateClassificationsPage(IMediaAdminAccessService access) =>
        new(
            CreateMediaDb(),
            Unused<ProjectManagement.Features.MediaLibrary.Services.IMediaClassificationOverrideService>(),
            Unused<ProjectManagement.Features.MediaLibrary.Services.IFaceEligibilityPolicy>(),
            Unused<ProjectManagement.Features.MediaLibrary.Services.IMediaContentChangeInvalidationService>(),
            Options.Create(new MediaLibraryOptions()),
            access,
            Unused<IAdminAuditService>(),
            NullLogger<ClassificationsModel>.Instance);

    private static MediaIntelligenceIndexModel CreateIndexPage(IMediaAdminAccessService access) =>
        new(
            CreateMediaDb(),
            Unused<ProjectManagement.Features.MediaLibrary.Services.IMediaLibrarySchemaService>(),
            Unused<ProjectManagement.Features.MediaLibrary.Services.IFaceModelReadinessService>(),
            Unused<ProjectManagement.Features.MediaLibrary.Services.IFaceQueueService>(),
            Unused<ProjectManagement.Features.MediaLibrary.Services.IMediaProcessingRuntimeState>(),
            Unused<ProjectManagement.Features.MediaLibrary.Services.IFaceEligibilityPolicy>(),
            Unused<ProjectManagement.Features.MediaLibrary.Services.IFaceCandidateRefreshQueueService>(),
            Unused<ProjectManagement.Features.MediaLibrary.Services.IFaceCandidateRefreshRuntimeState>(),
            Options.Create(new MediaLibraryOptions()),
            NullLogger<MediaIntelligenceIndexModel>.Instance,
            access);

    private static MediaLibraryDbContext CreateMediaDb() =>
        new(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"media-authz-{Guid.NewGuid():N}")
            .Options);

    // The filter decides before any handler runs, so these dependencies are never called.
    private static T Unused<T>() where T : class => DispatchProxy.Create<T, UnusedProxy>();

    public class UnusedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"{targetMethod?.Name} should not be called by the authorization filter.");
    }

    private sealed class PolicyAccess : IMediaAdminAccessService
    {
        private readonly HashSet<string> _granted;

        public PolicyAccess(params string[] granted) => _granted = new HashSet<string>(granted, StringComparer.Ordinal);

        public List<string> Checked { get; } = new();

        public Task<bool> IsAuthorizedAsync(string policy, CancellationToken cancellationToken = default)
        {
            Checked.Add(policy);
            return Task.FromResult(_granted.Contains(policy));
        }
    }
}
