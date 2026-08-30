using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcRecordWorkspaceProjectOptionTests
{
    [Fact]
    public async Task GetProjectOptionsAsync_RepeatBuildIsAvailableForNewFfcLink()
    {
        await using var db = CreateDbContext();
        var dcd = new ProjectCategory { Name = "DCD Projects", CreatedByUserId = "admin" };
        db.ProjectCategories.Add(dcd);
        await db.SaveChangesAsync();

        var repeatBuild = new Project
        {
            Name = "VR CMC (Philippines 2026)",
            CreatedByUserId = "admin",
            CategoryId = dcd.Id,
            IsBuild = true,
            LifecycleStatus = ProjectLifecycleStatus.Active
        };
        db.Projects.Add(repeatBuild);
        await db.SaveChangesAsync();

        var service = new FfcRecordWorkspaceService(db, new NoOpProgressService(), new ProjectCategoryHierarchyService(db));

        var options = await service.GetProjectOptionsAsync();

        var option = Assert.Single(options, item => item.Id == repeatBuild.Id);
        Assert.True(option.IsAvailable);
    }

    [Fact]
    public async Task GetProjectOptionsAsync_MarksDcdRootAndAllDescendantsForDefaultScope()
    {
        await using var db = CreateDbContext();
        var dcd = new ProjectCategory { Name = "DCD Projects", CreatedByUserId = "admin" };
        db.ProjectCategories.Add(dcd);
        await db.SaveChangesAsync();

        var child = new ProjectCategory
        {
            Name = "AR VR",
            CreatedByUserId = "admin",
            ParentId = dcd.Id
        };
        db.ProjectCategories.Add(child);
        await db.SaveChangesAsync();

        var grandChild = new ProjectCategory
        {
            Name = "Combat Medical",
            CreatedByUserId = "admin",
            ParentId = child.Id
        };
        var other = new ProjectCategory { Name = "Other R&D Projects", CreatedByUserId = "admin" };
        db.ProjectCategories.AddRange(grandChild, other);
        await db.SaveChangesAsync();

        var projects = new[]
        {
            new Project { Name = "DCD root project", CreatedByUserId = "admin", CategoryId = dcd.Id },
            new Project { Name = "DCD child project", CreatedByUserId = "admin", CategoryId = child.Id },
            new Project { Name = "DCD grandchild project", CreatedByUserId = "admin", CategoryId = grandChild.Id },
            new Project { Name = "Other project", CreatedByUserId = "admin", CategoryId = other.Id }
        };
        db.Projects.AddRange(projects);
        await db.SaveChangesAsync();

        var service = new FfcRecordWorkspaceService(db, new NoOpProgressService(), new ProjectCategoryHierarchyService(db));
        var options = await service.GetProjectOptionsAsync();

        bool IsDcd(int projectId)
            => Assert.Single(options, item => item.Id == projectId).IsDcdProject;

        Assert.True(IsDcd(projects[0].Id));
        Assert.True(IsDcd(projects[1].Id));
        Assert.True(IsDcd(projects[2].Id));
        Assert.False(IsDcd(projects[3].Id));
    }

    [Fact]
    public async Task GetProjectOptionsAsync_DeletedProjectIsUnavailableButRetainedWhenAlreadyLinked()
    {
        await using var db = CreateDbContext();
        var project = new Project
        {
            Name = "Historical deleted project",
            CreatedByUserId = "admin",
            IsDeleted = true,
            IsBuild = true
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = new FfcRecordWorkspaceService(db, new NoOpProgressService(), new ProjectCategoryHierarchyService(db));

        var withoutLegacyLink = await service.GetProjectOptionsAsync();
        var withLegacyLink = await service.GetProjectOptionsAsync(new[] { project.Id });

        Assert.DoesNotContain(withoutLegacyLink, item => item.Id == project.Id);
        var retained = Assert.Single(withLegacyLink, item => item.Id == project.Id);
        Assert.False(retained.IsAvailable);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class NoOpProgressService : IFfcProgressService
    {
        public Task<IReadOnlyDictionary<long, FfcProgressSnapshot>> GetCurrentProgressAsync(
            IReadOnlyCollection<FfcProgressTarget> targets,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<long, FfcProgressSnapshot>>(
                new Dictionary<long, FfcProgressSnapshot>());

        public Task<FfcProgressUpdateResult> UpdateProgressAsync(
            FfcProgressUpdateCommand command,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Progress updates are not expected in project-option tests.");
    }
}
