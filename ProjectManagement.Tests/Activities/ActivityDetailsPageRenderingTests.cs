using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models.Activities;
using ProjectManagement.Pages.Activities;
using ProjectManagement.Services.Activities;
using Xunit;

namespace ProjectManagement.Tests.Activities;

public sealed class ActivityDetailsPageRenderingTests
{
    private static readonly IServiceProvider Services = BuildServiceProvider();

    [Fact]
    public async Task DetailsPage_PhotoTriggersTargetMatchingModalIds()
    {
        // SECTION: Arrange activity details with multiple photo attachments.
        var activity = new Activity
        {
            Id = 42,
            Title = "Stakeholder Workshop",
            Description = "Workshop summary",
            ActivityType = new ActivityType { Id = 7, Name = "Workshop", CreatedByUserId = "seed" },
            ActivityTypeId = 7,
            CreatedByUserId = "owner",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var attachments = new[]
        {
            new ActivityAttachmentMetadata(123, "opening.jpg", "image/jpeg", 2048, "/files/opening.jpg", "/files/opening.jpg", "activities/42/opening.jpg", DateTimeOffset.UtcNow, "owner"),
            new ActivityAttachmentMetadata(456, "closing.png", "image/png", 4096, "/files/closing.png", "/files/closing.png", "activities/42/closing.png", DateTimeOffset.UtcNow, "owner")
        };

        var page = new DetailsModel(
            new StubActivityService(activity, attachments),
            new StubActivityAttachmentManager(),
            NullLogger<DetailsModel>.Instance);

        var html = await RenderDetailsPageAsync(page, activity.Id);

        // SECTION: Assert every photo trigger targets its generated modal id.
        Assert.Contains("data-bs-target=\"#photo-modal-123\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"photo-modal-123\"", html, StringComparison.Ordinal);
        Assert.Contains("data-bs-target=\"#photo-modal-456\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"photo-modal-456\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-bs-target=\"#{modalId}\"", html, StringComparison.Ordinal);
    }

    private static async Task<string> RenderDetailsPageAsync(DetailsModel page, int activityId)
    {
        // SECTION: Populate the Razor PageModel before rendering the details view.
        using var scope = Services.CreateScope();
        var provider = scope.ServiceProvider;
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "owner")
        }, "Test"));

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        page.PageContext = new PageContext(actionContext);
        page.TempData = new TempDataDictionary(httpContext, provider.GetRequiredService<ITempDataProvider>());

        var result = await page.OnGetAsync(activityId, CancellationToken.None);
        Assert.IsType<PageResult>(result);

        var viewEngine = provider.GetRequiredService<IRazorViewEngine>();
        var viewResult = viewEngine.GetView(executingFilePath: null, viewPath: "/Pages/Activities/Details.cshtml", isMainPage: true);
        if (!viewResult.Success)
        {
            throw new InvalidOperationException("Unable to locate Activities details page view.");
        }

        await using var writer = new StringWriter();
        var viewData = new ViewDataDictionary<DetailsModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = page
        };
        var viewContext = new ViewContext(actionContext, viewResult.View, viewData, page.TempData, writer, new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private static IServiceProvider BuildServiceProvider()
    {
        // SECTION: Minimal MVC services required to render Razor Pages in tests.
        var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var webRoot = Path.Combine(contentRoot, "wwwroot");
        var environment = new TestWebHostEnvironment
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name!,
            ContentRootPath = contentRoot,
            WebRootPath = webRoot,
            ContentRootFileProvider = new PhysicalFileProvider(contentRoot),
            WebRootFileProvider = Directory.Exists(webRoot) ? new PhysicalFileProvider(webRoot) : new NullFileProvider(),
            EnvironmentName = Environments.Development
        };

        var services = new ServiceCollection();
        var diagnosticListener = new DiagnosticListener("Microsoft.AspNetCore");
        services.AddSingleton<DiagnosticListener>(diagnosticListener);
        services.AddSingleton<DiagnosticSource>(diagnosticListener);
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddLogging();
        services.AddRouting();
        services.AddRazorPages();
        services.AddControllersWithViews();
        return services.BuildServiceProvider();
    }

    private sealed class StubActivityService : IActivityService
    {
        private readonly Activity _activity;
        private readonly IReadOnlyList<ActivityAttachmentMetadata> _attachments;

        public StubActivityService(Activity activity, IReadOnlyList<ActivityAttachmentMetadata> attachments)
        {
            _activity = activity;
            _attachments = attachments;
        }

        public Task<Activity?> GetAsync(int activityId, CancellationToken cancellationToken = default) => Task.FromResult<Activity?>(_activity);

        public Task<IReadOnlyList<ActivityAttachmentMetadata>> GetAttachmentMetadataAsync(int activityId, CancellationToken cancellationToken = default) => Task.FromResult(_attachments);

        public Task<Activity> CreateAsync(ActivityInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Activity> UpdateAsync(int activityId, ActivityInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(int activityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Activity>> ListByTypeAsync(int activityTypeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityListResult> ListAsync(ActivityListRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityReviewSummaryResult> GetReviewSummaryAsync(ActivityListRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityAttachment> AddAttachmentAsync(int activityId, ActivityAttachmentUpload upload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubActivityAttachmentManager : IActivityAttachmentManager
    {
        public Task<ActivityAttachment> AddAsync(Activity activity, ActivityAttachmentUpload upload, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveAsync(ActivityAttachment attachment, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveAllAsync(Activity activity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IReadOnlyList<ActivityAttachmentMetadata> CreateMetadata(Activity activity) => Array.Empty<ActivityAttachmentMetadata>();
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
