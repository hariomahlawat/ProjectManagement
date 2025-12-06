using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectRemarksPanelServiceTests
{
    [Fact]
    public async Task BuildAsync_WhenTotInProgress_IncludesTotScopeOption()
    {
        await using var db = CreateContext();
        using var userManager = CreateUserManager(db);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var service = new ProjectRemarksPanelService(userManager, clock, new WorkflowStageMetadataProvider());

        var user = new ApplicationUser { Id = "user-1", UserName = "user@example.com" };
        await userManager.CreateAsync(user);

        var project = new Project
        {
            Id = 1,
            Name = "Project Horizon",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            Tot = new ProjectTot
            {
                ProjectId = 1,
                Status = ProjectTotStatus.InProgress
            }
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) }, "TestAuth"));
        var viewModel = await service.BuildAsync(project, Array.Empty<ProjectStage>(), principal, CancellationToken.None);

        Assert.Contains(viewModel.ScopeOptions, option => option.Value == RemarkScope.TransferOfTechnology.ToString());
    }

    [Fact]
    public async Task BuildAsync_WhenTotNotStarted_ExcludesTotScopeOption()
    {
        await using var db = CreateContext();
        using var userManager = CreateUserManager(db);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var service = new ProjectRemarksPanelService(userManager, clock, new WorkflowStageMetadataProvider());

        var user = new ApplicationUser { Id = "user-2", UserName = "user2@example.com" };
        await userManager.CreateAsync(user);

        var project = new Project
        {
            Id = 2,
            Name = "Project Aurora",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            Tot = new ProjectTot
            {
                ProjectId = 2,
                Status = ProjectTotStatus.NotStarted
            }
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) }, "TestAuth"));
        var viewModel = await service.BuildAsync(project, Array.Empty<ProjectStage>(), principal, CancellationToken.None);

        Assert.DoesNotContain(viewModel.ScopeOptions, option => option.Value == RemarkScope.TransferOfTechnology.ToString());
    }

    [Fact]
    public async Task BuildAsync_WhenNoStages_UsesAllStageCodes()
    {
        await using var db = CreateContext();
        using var userManager = CreateUserManager(db);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var service = new ProjectRemarksPanelService(userManager, clock, new WorkflowStageMetadataProvider());

        var user = new ApplicationUser { Id = "user-3", UserName = "user3@example.com" };
        await userManager.CreateAsync(user);

        var project = new Project
        {
            Id = 3,
            Name = "Project Zenith"
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) }, "TestAuth"));
        var viewModel = await service.BuildAsync(project, Array.Empty<ProjectStage>(), principal, CancellationToken.None);

        Assert.Equal(StageCodes.All.Length, viewModel.StageOptions.Count);
        Assert.All(StageCodes.All, code => Assert.Contains(viewModel.StageOptions, option => string.Equals(option.Value, code, StringComparison.Ordinal)));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
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

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }
}
