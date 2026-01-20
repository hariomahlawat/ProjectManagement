using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;
using Xunit;
using ProjectsOverviewModel = ProjectManagement.Pages.Projects.OverviewModel;

namespace ProjectManagement.Tests;

public sealed class ProjectOverviewLifecycleTests
{
    [Fact]
    public async Task Overview_WhenProjectActive_DoesNotShowPostCompletionView()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, projectId: 1);

        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var overview = CreateOverviewPage(db, clock);
        ConfigurePageContext(overview);

        var result = await overview.OnGetAsync(1, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(overview.LifecycleSummary.ShowPostCompletionView);
        Assert.Equal(ProjectLifecycleStatus.Active, overview.LifecycleSummary.Status);
        Assert.Equal(0, overview.MediaSummary.PhotoCount);
        Assert.Equal(0, overview.DocumentSummary.PublishedCount);
        Assert.False(overview.TotSummary.HasTotRecord);
        Assert.Equal("Not tracked", overview.TotSummary.StatusLabel);
        Assert.Equal(0, overview.RemarkSummary.TotalCount);
    }

    [Fact]
    public async Task Overview_WhenProjectCompleted_BuildsPostCompletionSummaries()
    {
        await using var db = CreateContext();
        await SeedCompletedProjectAsync(db, projectId: 2);

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 8, 0, 0, TimeSpan.Zero));
        var overview = CreateOverviewPage(db, clock);
        ConfigurePageContext(overview);

        var result = await overview.OnGetAsync(2, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(overview.LifecycleSummary.ShowPostCompletionView);
        Assert.Equal(ProjectLifecycleStatus.Completed, overview.LifecycleSummary.Status);
        Assert.Contains("completed on", overview.LifecycleSummary.PrimaryDetail!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(overview.LifecycleSummary.Facts, fact => fact.Label == "Completed on");
        Assert.Equal(2, overview.MediaSummary.PhotoCount);
        Assert.True(overview.MediaSummary.HasAdditionalPhotos);
        Assert.Equal(1, overview.DocumentSummary.PublishedCount);
        Assert.Equal(1, overview.RemarkSummary.TotalCount);
        Assert.Equal(RemarkType.Internal, overview.RemarkSummary.LastRemarkType);
        Assert.Equal(ProjectTotStatus.Completed, overview.TotSummary.Status);
        Assert.Contains("Transfer of Technology completed", overview.TotSummary.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(overview.TotSummary.LatestRemark);
        Assert.Equal("External", overview.TotSummary.LatestRemark?.TypeLabel);
    }

    [Fact]
    public async Task Overview_WhenProjectOfficerAssigned_ShowsReadOnlyLifecycleActions()
    {
        await using var db = CreateContext();
        await SeedLifecycleProjectAsync(db, projectId: 3, hodUserId: "hod-3", poUserId: "po-3");

        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var overview = CreateOverviewPage(db, clock);
        var user = CreateUser("po-3", "Project Officer");
        ConfigurePageContext(overview, user);

        var result = await overview.OnGetAsync(3, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(overview.LifecycleActions.CanMarkCompleted);
        Assert.False(overview.LifecycleActions.CanEndorseCompletedDate);
        Assert.False(overview.LifecycleActions.CanCancel);
        Assert.False(overview.LifecycleActions.HasActions);
        Assert.False(overview.LifecycleActions.CanManageLifecycle);
    }

    [Fact]
    public async Task Overview_WhenProjectCancelled_ShowsLifecycleMenuForManagers()
    {
        await using var db = CreateContext();
        await SeedCancelledProjectAsync(db, projectId: 7, hodUserId: "hod-7");

        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var overview = CreateOverviewPage(db, clock);
        var user = CreateUser("hod-7", "HoD");
        ConfigurePageContext(overview, user);

        var result = await overview.OnGetAsync(7, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(overview.LifecycleActions.CanManageLifecycle);
        Assert.True(overview.LifecycleActions.HasActions);
        Assert.True(overview.LifecycleActions.CanReactivate);
        Assert.Equal(ProjectLifecycleStatus.Cancelled, overview.LifecycleActions.Status);
    }

    [Fact]
    public async Task CompleteLifecycle_AllowsAdminUsers()
    {
        await using var db = CreateContext();
        await SeedLifecycleProjectAsync(db, projectId: 4, hodUserId: "hod-4", poUserId: "po-4");

        var clock = new FixedClock(new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var overview = CreateOverviewPage(db, clock);
        overview.CompleteProjectInput = new ProjectsOverviewModel.CompleteLifecycleInput
        {
            ProjectId = 4,
            CompletedYear = 2024
        };

        var user = CreateUser("admin-4", "Admin");
        ConfigurePageContext(overview, user);

        var result = await overview.OnPostCompleteAsync(4, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var project = await db.Projects.FindAsync(4);
        Assert.NotNull(project);
        Assert.Equal(ProjectLifecycleStatus.Completed, project!.LifecycleStatus);
        Assert.Equal(2024, project.CompletedYear);
    }

    [Fact]
    public async Task CancelLifecycle_AllowsAssignedHoD()
    {
        await using var db = CreateContext();
        await SeedLifecycleProjectAsync(db, projectId: 5, hodUserId: "hod-5", poUserId: "po-5");

        var clock = new FixedClock(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var overview = CreateOverviewPage(db, clock);
        overview.CancelProjectInput = new ProjectsOverviewModel.CancelLifecycleInput
        {
            ProjectId = 5,
            CancelledOn = new DateOnly(2024, 5, 15),
            Reason = "Scope reduced"
        };

        var user = CreateUser("hod-5");
        ConfigurePageContext(overview, user);

        var result = await overview.OnPostCancelAsync(5, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var project = await db.Projects.FindAsync(5);
        Assert.NotNull(project);
        Assert.Equal(ProjectLifecycleStatus.Cancelled, project!.LifecycleStatus);
        Assert.Equal(new DateOnly(2024, 5, 15), project.CancelledOn);
        Assert.Equal("Scope reduced", project.CancelReason);
    }

    [Fact]
    public async Task CompleteLifecycle_ForbidsProjectOfficer()
    {
        await using var db = CreateContext();
        await SeedLifecycleProjectAsync(db, projectId: 6, hodUserId: "hod-6", poUserId: "po-6");

        var clock = new FixedClock(new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var overview = CreateOverviewPage(db, clock);
        overview.CompleteProjectInput = new ProjectsOverviewModel.CompleteLifecycleInput
        {
            ProjectId = 6
        };

        var user = CreateUser("po-6", "Project Officer");
        ConfigurePageContext(overview, user);

        var result = await overview.OnPostCompleteAsync(6, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        var project = await db.Projects.FindAsync(6);
        Assert.NotNull(project);
        Assert.Equal(ProjectLifecycleStatus.Active, project!.LifecycleStatus);
    }

    private static async Task SeedProjectAsync(ApplicationDbContext db, int projectId)
    {
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"Project {projectId}",
            CreatedByUserId = "creator",
            CreatedAt = new DateTime(2024, 1, 1),
            RowVersion = new byte[] { 1 }
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCompletedProjectAsync(ApplicationDbContext db, int projectId)
    {
        var project = new Project
        {
            Id = projectId,
            Name = "Completed Project",
            CreatedByUserId = "creator",
            CreatedAt = new DateTime(2023, 3, 10),
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CompletedOn = new DateOnly(2024, 4, 5),
            CompletedYear = 2024,
            RowVersion = new byte[] { 2 }
        };
        db.Projects.Add(project);

        db.ProjectPhotos.AddRange(
            new ProjectPhoto
            {
                Id = 10,
                ProjectId = projectId,
                StorageKey = "photos/2/10.png",
                OriginalFileName = "cover.png",
                ContentType = "image/png",
                Width = 800,
                Height = 600,
                Ordinal = 1,
                Caption = "Cover",
                IsCover = true,
                Version = 2,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            },
            new ProjectPhoto
            {
                Id = 11,
                ProjectId = projectId,
                StorageKey = "photos/2/11.png",
                OriginalFileName = "additional.png",
                ContentType = "image/png",
                Width = 800,
                Height = 600,
                Ordinal = 2,
                Caption = "Preview",
                Version = 1,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });

        db.ProjectDocuments.Add(new ProjectDocument
        {
            Id = 20,
            ProjectId = projectId,
            Title = "Completion Report",
            StorageKey = "docs/2/20.pdf",
            OriginalFileName = "report.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            Status = ProjectDocumentStatus.Published,
            FileStamp = 1,
            UploadedByUserId = "uploader",
            UploadedAtUtc = DateTimeOffset.UtcNow
        });

        db.Remarks.Add(new Remark
        {
            ProjectId = projectId,
            AuthorUserId = "author",
            AuthorRole = RemarkActorRole.ProjectOfficer,
            Type = RemarkType.Internal,
            Body = "Final walkthrough completed.",
            EventDate = new DateOnly(2024, 4, 5),
            CreatedAtUtc = DateTime.UtcNow
        });

        db.Remarks.Add(new Remark
        {
            ProjectId = projectId,
            AuthorUserId = "author",
            AuthorRole = RemarkActorRole.ProjectOffice,
            Type = RemarkType.External,
            Scope = RemarkScope.TransferOfTechnology,
            Body = "Knowledge handover with partner completed.",
            EventDate = new DateOnly(2024, 3, 20),
            CreatedAtUtc = DateTime.UtcNow
        });

        db.ProjectTots.Add(new ProjectTot
        {
            ProjectId = projectId,
            Status = ProjectTotStatus.Completed,
            StartedOn = new DateOnly(2023, 8, 1),
            CompletedOn = new DateOnly(2024, 3, 15)
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedLifecycleProjectAsync(ApplicationDbContext db, int projectId, string? hodUserId, string? poUserId)
    {
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"Lifecycle Project {projectId}",
            CreatedByUserId = "creator",
            CreatedAt = new DateTime(2024, 1, 1),
            LifecycleStatus = ProjectLifecycleStatus.Active,
            HodUserId = hodUserId,
            LeadPoUserId = poUserId,
            RowVersion = new byte[] { 3 }
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedCancelledProjectAsync(ApplicationDbContext db, int projectId, string? hodUserId)
    {
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"Cancelled Project {projectId}",
            CreatedByUserId = "creator",
            CreatedAt = new DateTime(2024, 2, 1),
            LifecycleStatus = ProjectLifecycleStatus.Cancelled,
            CancelledOn = new DateOnly(2024, 3, 15),
            CancelReason = "Funding withdrawn",
            HodUserId = hodUserId,
            RowVersion = new byte[] { 4 }
        });

        await db.SaveChangesAsync();
    }

    private static ProjectsOverviewModel CreateOverviewPage(ApplicationDbContext db, IClock clock)
    {
        var procure = new ProjectProcurementReadService(db);
        var workflowMetadata = new WorkflowStageMetadataProvider();
        var timeline = new ProjectTimelineReadService(db, clock, workflowMetadata);
        var planRead = new PlanReadService(db);
        var planCompare = new PlanCompareService(db);
        var userManager = CreateUserManager(db);
        var remarksPanel = new ProjectRemarksPanelService(userManager, clock, workflowMetadata);
        var lifecycle = new ProjectLifecycleService(db, new NoOpAuditService(), clock);
        var mediaAggregator = new ProjectMediaAggregator();
        return new ProjectsOverviewModel(db, procure, timeline, userManager, planRead, planCompare, NullLogger<ProjectsOverviewModel>.Instance, clock, remarksPanel, lifecycle, mediaAggregator);
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ILookupNormalizer, UpperInvariantLookupNormalizer>()
            .BuildServiceProvider();

        return new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            services.GetRequiredService<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            services,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static void ConfigurePageContext(PageModel page, ClaimsPrincipal? user = null)
    {
        var httpContext = new DefaultHttpContext();
        var tempDataProvider = new InMemoryTempDataProvider();
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton<ITempDataProvider>(tempDataProvider)
            .BuildServiceProvider();
        httpContext.User = user ?? new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "page-user")
        }, "Test"));

        var actionContext = new ActionContext(httpContext, new(), new ActionDescriptor());
        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        page.TempData = new TempDataDictionary(httpContext, tempDataProvider);
        page.Url = new SimpleUrlHelper(page.PageContext);
    }

    private static ClaimsPrincipal CreateUser(string userId, params string[] roles)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null)
            => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
