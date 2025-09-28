using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Models.Execution;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public class ProjectTimelineUiTests
{
    private static readonly IServiceProvider Services = BuildServiceProvider();

    [Fact]
    public async Task ActionsDropdown_RenderedForHoDOnly()
    {
        var model = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "IPA",
                    Name = "In-Principle Approval",
                    Status = StageStatus.NotStarted,
                    SortOrder = 1
                }
            }
        };

        var hodHtml = await RenderAsync(model, new[] { "HoD" });
        Assert.Contains("data-direct-apply", hodHtml, StringComparison.Ordinal);

        var otherHtml = await RenderAsync(model, Array.Empty<string>());
        Assert.DoesNotContain("data-direct-apply", otherHtml, StringComparison.Ordinal);
    }

    private static async Task<string> RenderAsync(TimelineVm model, string[] roles)
    {
        using var scope = Services.CreateScope();
        var provider = scope.ServiceProvider;
        var viewEngine = provider.GetRequiredService<IRazorViewEngine>();
        var tempDataProvider = provider.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = BuildPrincipal(roles)
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var viewResult = viewEngine.GetView(executingFilePath: null, viewPath: "/Pages/Shared/_ProjectTimeline.cshtml", isMainPage: false);

        if (!viewResult.Success)
        {
            throw new InvalidOperationException("Unable to locate _ProjectTimeline partial view.");
        }

        await using var writer = new StringWriter();
        var viewData = new ViewDataDictionary<TimelineVm>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);
        var viewContext = new ViewContext(actionContext, viewResult.View, viewData, tempData, writer, new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private static ClaimsPrincipal BuildPrincipal(string[] roles)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user"));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }

    private static IServiceProvider BuildServiceProvider()
    {
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
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddLogging();
        services.AddRouting();
        services.AddRazorPages();
        services.AddControllersWithViews();

        return services.BuildServiceProvider();
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
