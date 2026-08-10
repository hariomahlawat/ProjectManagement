using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;
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

        var hodHtml = await RenderAsync(model, isHoD: true, isAssignedProjectOfficer: false);
        Assert.Contains("data-direct-apply", hodHtml, StringComparison.Ordinal);

        var otherHtml = await RenderAsync(model, isHoD: false, isAssignedProjectOfficer: false);
        Assert.DoesNotContain("data-direct-apply", otherHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotStartedStage_OffersDirectCompletionWithSuggestedEditableStart()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "BID",
                    Name = "Bidding/Tendering",
                    Status = StageStatus.NotStarted,
                    SortOrder = 1,
                    SuggestedStartDate = new DateOnly(2026, 6, 11),
                    EarliestAllowedStartDate = new DateOnly(2026, 6, 10),
                    SuggestedStartSourceName = "Acceptance of Necessity"
                }
            }
        };

        var html = await RenderAsync(timeline, isHoD: true, isAssignedProjectOfficer: false);

        Assert.Contains("Complete stage directly", html, StringComparison.Ordinal);
        Assert.Contains("data-direct-completion=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-default-start-date=\"2026-06-11\"", html, StringComparison.Ordinal);
        Assert.Contains("data-earliest-start=\"2026-06-10\"", html, StringComparison.Ordinal);
        Assert.Contains("data-start-source=\"Acceptance of Necessity\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActionsDropdown_FiltersActionsByCurrentStatusAndAuthority()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "IPA",
                    Name = "In-Principle Approval",
                    Status = StageStatus.InProgress,
                    SortOrder = 1
                }
            }
        };

        var hodHtml = await RenderAsync(timeline, isHoD: true, isAssignedProjectOfficer: false);
        Assert.Contains("Complete stage", hodHtml, StringComparison.Ordinal);
        Assert.Contains("Mark blocked", hodHtml, StringComparison.Ordinal);
        Assert.Contains("Skip stage", hodHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Start stage", hodHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Reopen stage", hodHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Update stage", hodHtml, StringComparison.Ordinal);

        var dualRoleHtml = await RenderAsync(timeline, isHoD: true, isAssignedProjectOfficer: true);
        Assert.DoesNotContain("Update stage", dualRoleHtml, StringComparison.Ordinal);

        var poHtml = await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: true);
        Assert.Contains("Update stage", poHtml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(poHtml, "Update stage"));
        Assert.DoesNotContain("Request change", poHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("data-direct-apply", poHtml, StringComparison.Ordinal);
    }


    [Fact]
    public async Task CompletedSameDayStage_RendersOneInclusiveCalendarDay()
    {
        var date = new DateOnly(2026, 8, 10);
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "FS",
                    Name = "Feasibility Study",
                    Status = StageStatus.Completed,
                    ActualStart = date,
                    EffectiveActualStart = date,
                    CompletedOn = date,
                    SortOrder = 1
                }
            }
        };

        var html = await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: false);

        Assert.Contains("1 calendar day", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArppManagedIpa_RendersAuthorityAndUnknownDuration_WithoutManualActions()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 208,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "IPA",
                    Name = "In-Principle Approval",
                    Status = StageStatus.Completed,
                    CompletedOn = new DateOnly(2026, 2, 26),
                    ActualStart = null,
                    EffectiveActualStart = null,
                    SortOrder = 1,
                    IsArppManaged = true,
                    ArppSourceIssueId = 10,
                    ArppSourceDocumentLabel = "Original ARPP",
                    ArppSourceIssueDate = new DateOnly(2026, 2, 26),
                    ArppSourceSerialNumber = "33"
                }
            }
        };

        var html = await RenderAsync(timeline, isHoD: true, isAssignedProjectOfficer: true);

        Assert.Contains("ARPP-derived", html, StringComparison.Ordinal);
        Assert.Contains("Initial IPA approval", html, StringComparison.Ordinal);
        Assert.Contains("Original ARPP", html, StringComparison.Ordinal);
        Assert.Contains("Unavailable", html, StringComparison.Ordinal);
        Assert.Contains("Not recorded", html, StringComparison.Ordinal);
        Assert.Contains("View ARPP issue", html, StringComparison.Ordinal);
        Assert.Contains("Record actual start", html, StringComparison.Ordinal);
        Assert.Contains("data-actuals-focus-stage=\"IPA\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-direct-apply", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-stage-request-button", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArppManagedIpa_WithInvalidStart_ShowsCorrectionWarningAndNoFabricatedDuration()
    {
        var item = new TimelineItemVm
        {
            Code = "IPA",
            Name = "In-Principle Approval",
            Status = StageStatus.Completed,
            CompletedOn = new DateOnly(2026, 2, 26),
            ActualStart = new DateOnly(2026, 3, 1),
            EffectiveActualStart = new DateOnly(2026, 3, 1),
            SortOrder = 1,
            IsArppManaged = true,
            ArppSourceIssueId = 10,
            ArppSourceDocumentLabel = "Original ARPP",
            ArppSourceIssueDate = new DateOnly(2026, 2, 26)
        };
        var timeline = new TimelineVm
        {
            ProjectId = 208,
            Items = new[] { item }
        };

        Assert.Null(item.ActualDurationDays);

        var html = await RenderAsync(timeline, isHoD: true, isAssignedProjectOfficer: true);

        Assert.Contains("Actual start needs correction", html, StringComparison.Ordinal);
        Assert.Contains("Correct actual start", html, StringComparison.Ordinal);
        Assert.Contains("Use Correct actual start to amend or clear it.", html, StringComparison.Ordinal);
        Assert.Contains("Unavailable", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">1 day<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArppManagedIpa_OnTerminalLegacyProject_UsesHistoricalStartCorrection()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 182,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "IPA",
                    Name = "In-Principle Approval",
                    Status = StageStatus.Completed,
                    CompletedOn = new DateOnly(2026, 2, 26),
                    ActualStart = null,
                    EffectiveActualStart = null,
                    SortOrder = 1,
                    IsArppManaged = true,
                    ArppSourceIssueId = 10,
                    ArppSourceDocumentLabel = "Original ARPP",
                    ArppSourceIssueDate = new DateOnly(2026, 2, 26)
                }
            }
        };

        var html = await RenderAsync(
            timeline,
            isHoD: true,
            isAssignedProjectOfficer: false,
            lifecycleStatus: ProjectLifecycleStatus.Cancelled,
            isLegacy: true,
            canManageHistoricalStageHistory: true);

        Assert.Contains("Record actual start", html, StringComparison.Ordinal);
        Assert.Contains("data-historical-focus-stage=\"IPA\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-actuals-focus-stage=\"IPA\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PendingProjectOfficerUpdate_IsImmediatelyVisibleAsAwaitingApproval()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "DEVP",
                    Name = "Development",
                    Status = StageStatus.InProgress,
                    SortOrder = 1,
                    HasPendingRequest = true,
                    PendingStatus = StageStatus.Completed.ToString(),
                    PendingDate = new DateOnly(2026, 6, 26),
                    PendingNote = "Development completed.",
                    PendingRequestedBy = "Project Officer",
                    PendingRequestedOn = new DateTimeOffset(2026, 6, 26, 8, 30, 0, TimeSpan.Zero)
                }
            }
        };

        var html = await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: true);

        Assert.Contains("Awaiting HoD approval", html, StringComparison.Ordinal);
        Assert.Contains("Completion submitted", html, StringComparison.Ordinal);
        Assert.Contains("Proposed completion", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Planned completion missing", html, StringComparison.Ordinal);
        Assert.Contains("Review update", html, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(html, "Review update"));
    }


    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static async Task<string> RenderAsync(
        TimelineVm model,
        bool isHoD,
        bool isAssignedProjectOfficer,
        ProjectLifecycleStatus lifecycleStatus = ProjectLifecycleStatus.Active,
        bool isLegacy = false,
        bool canManageHistoricalStageHistory = false)
    {
        using var scope = Services.CreateScope();
        var provider = scope.ServiceProvider;
        var viewEngine = provider.GetRequiredService<IRazorViewEngine>();
        var tempDataProvider = provider.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = BuildPrincipal(isHoD ? new[] { "HoD" } : Array.Empty<string>())
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var viewResult = viewEngine.GetView(executingFilePath: null, viewPath: "/Pages/Shared/_ProjectTimeline.cshtml", isMainPage: false);

        if (!viewResult.Success)
        {
            throw new InvalidOperationException("Unable to locate _ProjectTimeline partial view.");
        }

        await using var writer = new StringWriter();
        var panel = new ProjectTimelinePanelVm
        {
            Timeline = model,
            Access = new ProjectOverviewAccessVm
            {
                IsHoD = isHoD,
                IsAssignedProjectOfficer = isAssignedProjectOfficer,
                CanEditTimeline = isHoD || isAssignedProjectOfficer
            },
            LifecycleStatus = lifecycleStatus,
            IsLegacy = isLegacy,
            CanManageHistoricalStageHistory = canManageHistoricalStageHistory
        };
        var viewData = new ViewDataDictionary<ProjectTimelinePanelVm>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = panel
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
        var diagnosticListener = new DiagnosticListener("Microsoft.AspNetCore");

        services.AddSingleton<DiagnosticListener>(diagnosticListener);
        services.AddSingleton<DiagnosticSource>(diagnosticListener);
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddLogging();
        services.AddRouting();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                ProjectOfficeReportsPolicies.ViewArpp,
                policy => policy.RequireAssertion(_ => true));
        });
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
