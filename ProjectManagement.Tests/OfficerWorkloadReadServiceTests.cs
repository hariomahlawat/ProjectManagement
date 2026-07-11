using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Workspace;

namespace ProjectManagement.Tests;

public sealed class OfficerWorkloadReadServiceTests
{
    [Fact]
    public async Task GetOfficerAsync_UsesCanonicalWorkflowStageResolution()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var officer = new ApplicationUser
        {
            Id = "officer-1",
            UserName = "officer1",
            NormalizedUserName = "OFFICER1",
            FullName = "Test Officer",
            Rank = "Lt Col",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(officer);

        var project = new Project
        {
            Name = "Workflow-aware project",
            CreatedByUserId = "seed",
            LeadPoUserId = officer.Id,
            WorkflowVersion = ProcurementWorkflow.VersionV2,
            LifecycleStatus = ProjectLifecycleStatus.Active
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = project.Id,
            StageCode = StageCodes.FS,
            SortOrder = 0,
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2026, 7, 1),
            CompletedOn = new DateOnly(2026, 7, 5)
        });
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var service = new OfficerWorkloadReadService(
            db,
            userManager,
            new WorkflowStageMetadataProvider());

        var result = await service.GetOfficerAsync(officer.Id);

        Assert.NotNull(result);
        var projectCard = Assert.Single(result!.Projects);
        Assert.Equal(StageCodes.SOW, projectCard.StageCode);
        Assert.Equal(StageCodes.DisplayNameOf(ProcurementWorkflow.VersionV2, StageCodes.SOW), projectCard.StageName);
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db);
        return new UserManager<ApplicationUser>(
            store,
            new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }
}
