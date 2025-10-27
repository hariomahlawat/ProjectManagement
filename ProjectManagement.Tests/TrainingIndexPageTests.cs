using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class TrainingIndexPageTests
{
    private static readonly IServiceProvider RazorServices = BuildRazorServiceProvider();

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task OnGetAsync_SetsAuthorizationFlags(bool canManage, bool canApprove)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(dbOptions);

        var optionsSnapshot = new StubOptionsSnapshot<TrainingTrackerOptions>(new TrainingTrackerOptions
        {
            Enabled = true
        });

        var readService = new TrainingTrackerReadService(db);
        var exportService = new StubTrainingExportService();
        var services = new ServiceCollection().BuildServiceProvider();
        using var userManager = new StubUserManager(new ApplicationUser
        {
            Id = "approver-1",
            UserName = "approver"
        }, services);

        var authorizedPolicies = new List<string>();
        if (canManage)
        {
            authorizedPolicies.Add(ProjectOfficeReportsPolicies.ManageTrainingTracker);
        }

        if (canApprove)
        {
            authorizedPolicies.Add(ProjectOfficeReportsPolicies.ApproveTrainingTracker);
        }

        var authorizationService = new StubAuthorizationService(authorizedPolicies.ToArray());

        var page = new IndexModel(optionsSnapshot, readService, exportService, userManager, authorizationService)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "approver-1")
                    }, "Test"))
                }
            }
        };

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(canManage, page.CanManageTrainingTracker);
        Assert.Equal(canApprove, page.CanApproveTrainingTracker);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManageColumn_RenderedOnlyForManagers(bool canManage)
    {
        var html = await RenderIndexPageAsync(canManage);

        if (canManage)
        {
            Assert.Contains("<th scope=\"col\" class=\"text-end\">Actions</th>", html, StringComparison.Ordinal);
            Assert.Contains(">Manage<", html, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("<th scope=\"col\" class=\"text-end\">Actions</th>", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Manage<", html, StringComparison.Ordinal);
        }
    }

    private sealed class StubTrainingExportService : ITrainingExportService
    {
        public Task<TrainingExportResult> ExportAsync(TrainingExportRequest request, CancellationToken cancellationToken)
            => Task.FromResult(TrainingExportResult.Failure("Not implemented."));
    }

    private static async Task<string> RenderIndexPageAsync(bool canManage)
    {
        using var scope = RazorServices.CreateScope();
        var provider = scope.ServiceProvider;
        var viewEngine = provider.GetRequiredService<IRazorViewEngine>();
        var tempDataProvider = provider.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider
        };

        var pageDescriptor = new PageActionDescriptor
        {
            AreaName = "ProjectOfficeReports",
            ViewEnginePath = "/Areas/ProjectOfficeReports/Pages/Training/Index.cshtml",
            RelativePath = "/Areas/ProjectOfficeReports/Pages/Training/Index.cshtml"
        };

        var routeData = new RouteData();
        routeData.Values["area"] = "ProjectOfficeReports";

        var actionContext = new ActionContext(httpContext, routeData, pageDescriptor);
        var viewResult = viewEngine.GetView(executingFilePath: null, viewPath: "/Areas/ProjectOfficeReports/Pages/Training/Index.cshtml", isMainPage: true);

        if (!viewResult.Success)
        {
            throw new InvalidOperationException("Unable to locate Training Index page.");
        }

        await using var writer = new StringWriter();

        var model = CreateIndexModel(canManage);
        var viewData = new ViewDataDictionary<IndexModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);
        var viewContext = new ViewContext(actionContext, viewResult.View, viewData, tempData, writer, new HtmlHelperOptions());

        model.PageContext = new PageContext(actionContext)
        {
            ViewData = viewData,
            ModelState = viewContext.ModelState
        };
        model.RouteData = routeData;
        model.TempData = tempData;
        model.ViewContext = viewContext;

        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private static IndexModel CreateIndexModel(bool canManage)
    {
        var optionsSnapshot = new StubOptionsSnapshot<TrainingTrackerOptions>(new TrainingTrackerOptions { Enabled = true });
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new ApplicationDbContext(dbOptions);
        var readService = new TrainingTrackerReadService(db);
        var exportService = new StubTrainingExportService();

        var services = new ServiceCollection().BuildServiceProvider();
        var userManager = new StubUserManager(new ApplicationUser
        {
            Id = "viewer-1",
            UserName = "viewer"
        }, services);

        var authorizationService = new StubAuthorizationService(Array.Empty<string>());
        var model = new IndexModel(optionsSnapshot, readService, exportService, userManager, authorizationService);

        SetProperty(model, nameof(IndexModel.IsFeatureEnabled), true);
        SetProperty(model, nameof(IndexModel.CanManageTrainingTracker), canManage);
        SetProperty(model, nameof(IndexModel.CanApproveTrainingTracker), false);
        SetProperty(model, nameof(IndexModel.TrainingTypes), new List<SelectListItem>
        {
            new("Signals", Guid.NewGuid().ToString())
        });
        SetProperty(model, nameof(IndexModel.ProjectTechnicalCategoryOptions), Array.Empty<SelectListItem>());
        SetProperty(model, nameof(IndexModel.Kpis), new TrainingKpiDto
        {
            TotalTrainings = 1,
            TotalTrainees = 6
        });

        var trainings = new List<IndexModel.TrainingRowViewModel>
        {
            new(
                Guid.NewGuid(),
                "Signals Refresher",
                "Jan 2024",
                "1 – 2 – 3",
                6,
                TrainingCounterSource.Legacy,
                "Routine update",
                new[] { "Project Atlas" })
        };

        SetProperty(model, nameof(IndexModel.Trainings), trainings);
        return model;
    }

    private static void SetProperty<T>(IndexModel model, string propertyName, T value)
    {
        var property = typeof(IndexModel).GetProperty(propertyName);
        var setter = property?.SetMethod ?? property?.GetSetMethod(nonPublic: true);
        setter?.Invoke(model, new object?[] { value });
    }

    private static IServiceProvider BuildRazorServiceProvider()
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

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = Environments.Development;

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubAuthorizationService : IAuthorizationService
    {
        private readonly HashSet<string> _authorizedPolicies;

        public StubAuthorizationService(params string[] authorizedPolicies)
        {
            _authorizedPolicies = new HashSet<string>(authorizedPolicies ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(_authorizedPolicies.Contains(policyName)
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());
    }

    private sealed class StubOptionsSnapshot<TOptions> : IOptionsSnapshot<TOptions>
        where TOptions : class
    {
        private readonly TOptions _value;

        public StubOptionsSnapshot(TOptions value)
        {
            _value = value;
        }

        public TOptions Value => _value;

        public TOptions Get(string? name) => _value;
    }

    private sealed class StubUserManager : UserManager<ApplicationUser>
    {
        private readonly ApplicationUser _user;

        public StubUserManager(ApplicationUser user, IServiceProvider services)
            : base(
                new StubUserStore(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                services,
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            _user = user;
        }

        public override Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal)
            => Task.FromResult<ApplicationUser?>(_user);
    }

    private sealed class StubUserStore : IUserStore<ApplicationUser>
    {
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public void Dispose()
        {
        }

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult<ApplicationUser?>(null);

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
            => Task.FromResult<ApplicationUser?>(null);

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.UserName);

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.Id!);

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.UserName);

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);
    }
}
