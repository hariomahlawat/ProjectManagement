using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ProjectIdeas;

namespace ProjectManagement.Tests;

public class ProjectIdeaReadServiceTests
{
    [Fact]
    public async Task Status_counts_respect_idea_visibility()
    {
        await using var db = CreateContext();
        db.ProjectIdeas.AddRange(
            Idea(1, "Active idea", ProjectIdeaStatuses.Active, createdBy: "user-1"),
            Idea(2, "Held idea", ProjectIdeaStatuses.OnHold, createdBy: "user-2", assignedOfficer: "user-1"),
            Idea(3, "Archived idea", ProjectIdeaStatuses.Archived, createdBy: "user-3"));
        await db.SaveChangesAsync();

        var service = new ProjectIdeaReadService(db);
        var counts = await service.GetBoardStatusCountsAsync(
            query: null,
            myIdeas: false,
            userId: "user-1",
            canViewAll: false);

        Assert.Equal(1, counts[ProjectIdeaStatuses.Active]);
        Assert.Equal(1, counts[ProjectIdeaStatuses.OnHold]);
        Assert.Equal(0, counts[ProjectIdeaStatuses.Archived]);
    }

    [Fact]
    public async Task My_ideas_contains_only_ideas_assigned_to_current_user_as_project_officer()
    {
        await using var db = CreateContext();
        db.ProjectIdeas.AddRange(
            Idea(1, "Assigned to me", ProjectIdeaStatuses.Active, createdBy: "other-user", assignedOfficer: "user-1"),
            Idea(2, "Created by me", ProjectIdeaStatuses.Active, createdBy: "user-1", assignedOfficer: "other-user"),
            Idea(3, "Reviewed by me", ProjectIdeaStatuses.Active, createdBy: "other-user", assignedOfficer: "other-user", assignedHod: "user-1"),
            Idea(4, "Unassigned creation", ProjectIdeaStatuses.Active, createdBy: "user-1"));
        await db.SaveChangesAsync();

        var service = new ProjectIdeaReadService(db);
        var ideas = await service.GetBoardIdeasAsync(
            ProjectIdeaStatuses.Active,
            query: null,
            myIdeas: true,
            userId: "user-1",
            canViewAll: true,
            projectOfficerUserId: "other-user",
            assignment: ProjectIdeaAssignmentFilters.Unassigned);

        var idea = Assert.Single(ideas);
        Assert.Equal("Assigned to me", idea.Title);
    }

    [Fact]
    public async Task My_ideas_status_counts_use_the_same_assigned_project_officer_rule()
    {
        await using var db = CreateContext();
        db.ProjectIdeas.AddRange(
            Idea(1, "My active", ProjectIdeaStatuses.Active, createdBy: "other-user", assignedOfficer: "user-1"),
            Idea(2, "My held", ProjectIdeaStatuses.OnHold, createdBy: "other-user", assignedOfficer: "user-1"),
            Idea(3, "Only created by me", ProjectIdeaStatuses.Archived, createdBy: "user-1", assignedOfficer: "other-user"));
        await db.SaveChangesAsync();

        var service = new ProjectIdeaReadService(db);
        var counts = await service.GetBoardStatusCountsAsync(
            query: null,
            myIdeas: true,
            userId: "user-1",
            canViewAll: true,
            projectOfficerUserId: "other-user",
            assignment: ProjectIdeaAssignmentFilters.Unassigned);

        Assert.Equal(1, counts[ProjectIdeaStatuses.Active]);
        Assert.Equal(1, counts[ProjectIdeaStatuses.OnHold]);
        Assert.Equal(0, counts[ProjectIdeaStatuses.Archived]);
    }

    [Fact]
    public async Task Board_sort_supports_title_and_latest_activity()
    {
        await using var db = CreateContext();
        var alpha = Idea(1, "Alpha", ProjectIdeaStatuses.Active, createdBy: "user-1");
        var zulu = Idea(2, "Zulu", ProjectIdeaStatuses.Active, createdBy: "user-1");
        zulu.Comments.Add(new ProjectIdeaComment
        {
            Id = 10,
            ProjectIdeaId = zulu.Id,
            CommentText = "Recent activity",
            CreatedByUserId = "user-1",
            CreatedAt = DateTime.UtcNow.AddMinutes(5)
        });
        db.ProjectIdeas.AddRange(alpha, zulu);
        await db.SaveChangesAsync();

        var service = new ProjectIdeaReadService(db);

        var alphabetical = await service.GetBoardIdeasAsync(
            ProjectIdeaStatuses.Active,
            query: null,
            myIdeas: false,
            userId: "user-1",
            canViewAll: true,
            sort: ProjectIdeaSorts.Title);

        var latest = await service.GetBoardIdeasAsync(
            ProjectIdeaStatuses.Active,
            query: null,
            myIdeas: false,
            userId: "user-1",
            canViewAll: true,
            sort: ProjectIdeaSorts.LatestActivity);

        Assert.Equal(new[] { "Alpha", "Zulu" }, alphabetical.Select(x => x.Title));
        Assert.Equal("Zulu", latest[0].Title);
    }

    [Fact]
    public async Task Board_filters_support_specific_officer_and_assignment_state()
    {
        await using var db = CreateContext();
        db.ProjectIdeas.AddRange(
            Idea(1, "Officer one", ProjectIdeaStatuses.Active, "creator", assignedOfficer: "po-1"),
            Idea(2, "Officer two", ProjectIdeaStatuses.Active, "creator", assignedOfficer: "po-2"),
            Idea(3, "Unassigned", ProjectIdeaStatuses.Active, "creator"));
        await db.SaveChangesAsync();

        var service = new ProjectIdeaReadService(db);

        var officerIdeas = await service.GetBoardIdeasAsync(
            ProjectIdeaStatuses.Active,
            query: null,
            myIdeas: false,
            userId: "creator",
            canViewAll: true,
            projectOfficerUserId: "po-1");

        var assignedIdeas = await service.GetBoardIdeasAsync(
            ProjectIdeaStatuses.Active,
            query: null,
            myIdeas: false,
            userId: "creator",
            canViewAll: true,
            assignment: ProjectIdeaAssignmentFilters.Assigned);

        var unassignedIdeas = await service.GetBoardIdeasAsync(
            ProjectIdeaStatuses.Active,
            query: null,
            myIdeas: false,
            userId: "creator",
            canViewAll: true,
            assignment: ProjectIdeaAssignmentFilters.Unassigned);

        Assert.Equal(new[] { "Officer one" }, officerIdeas.Select(x => x.Title));
        Assert.Equal(2, assignedIdeas.Count);
        Assert.Equal(new[] { "Unassigned" }, unassignedIdeas.Select(x => x.Title));
    }

    [Fact]
    public async Task Status_counts_apply_board_filters_consistently()
    {
        await using var db = CreateContext();
        db.ProjectIdeas.AddRange(
            Idea(1, "Active assigned", ProjectIdeaStatuses.Active, "creator", assignedOfficer: "po-1"),
            Idea(2, "Held assigned", ProjectIdeaStatuses.OnHold, "creator", assignedOfficer: "po-1"),
            Idea(3, "Active unassigned", ProjectIdeaStatuses.Active, "creator"));
        await db.SaveChangesAsync();

        var service = new ProjectIdeaReadService(db);
        var counts = await service.GetBoardStatusCountsAsync(
            query: null,
            myIdeas: false,
            userId: "creator",
            canViewAll: true,
            projectOfficerUserId: "po-1");

        Assert.Equal(1, counts[ProjectIdeaStatuses.Active]);
        Assert.Equal(1, counts[ProjectIdeaStatuses.OnHold]);
        Assert.Equal(0, counts[ProjectIdeaStatuses.Archived]);
    }

    [Fact]
    public async Task Officer_options_are_distinct_sorted_and_visibility_scoped()
    {
        await using var db = CreateContext();
        db.Users.AddRange(
            new ApplicationUser { Id = "po-1", UserName = "zulu", FullName = "Zulu Officer" },
            new ApplicationUser { Id = "po-2", UserName = "alpha", FullName = "Alpha Officer" });
        db.ProjectIdeas.AddRange(
            Idea(1, "Visible one", ProjectIdeaStatuses.Active, "user-1", assignedOfficer: "po-1"),
            Idea(2, "Visible duplicate", ProjectIdeaStatuses.OnHold, "user-1", assignedOfficer: "po-1"),
            Idea(3, "Hidden", ProjectIdeaStatuses.Active, "other", assignedOfficer: "po-2"));
        await db.SaveChangesAsync();

        var service = new ProjectIdeaReadService(db);
        var options = await service.GetBoardProjectOfficersAsync("user-1", canViewAll: false);

        var option = Assert.Single(options);
        Assert.Equal("po-1", option.UserId);
        Assert.Equal("Zulu Officer", option.DisplayName);
    }

    [Theory]
    [InlineData(null, ProjectIdeaSorts.LatestActivity)]
    [InlineData("unknown", ProjectIdeaSorts.LatestActivity)]
    [InlineData("TITLE", ProjectIdeaSorts.Title)]
    public void Sort_normalisation_is_safe(string? input, string expected)
    {
        Assert.Equal(expected, ProjectIdeaSorts.Normalise(input));
    }

    [Theory]
    [InlineData(null, ProjectIdeaAssignmentFilters.All)]
    [InlineData("unknown", ProjectIdeaAssignmentFilters.All)]
    [InlineData("ASSIGNED", ProjectIdeaAssignmentFilters.Assigned)]
    [InlineData("unassigned", ProjectIdeaAssignmentFilters.Unassigned)]
    public void Assignment_filter_normalisation_is_safe(string? input, string expected)
    {
        Assert.Equal(expected, ProjectIdeaAssignmentFilters.Normalise(input));
    }

    private static ProjectIdea Idea(
        int id,
        string title,
        string status,
        string createdBy,
        string? assignedOfficer = null,
        string? assignedHod = null)
    {
        return new ProjectIdea
        {
            Id = id,
            Title = title,
            Description = $"Description for {title}",
            Status = status,
            CreatedByUserId = createdBy,
            AssignedProjectOfficerUserId = assignedOfficer,
            AssignedHodUserId = assignedHod,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"project-ideas-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }
}
