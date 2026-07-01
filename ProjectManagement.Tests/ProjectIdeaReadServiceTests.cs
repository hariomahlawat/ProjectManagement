using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
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

    [Theory]
    [InlineData(null, ProjectIdeaSorts.LatestActivity)]
    [InlineData("unknown", ProjectIdeaSorts.LatestActivity)]
    [InlineData("TITLE", ProjectIdeaSorts.Title)]
    public void Sort_normalisation_is_safe(string? input, string expected)
    {
        Assert.Equal(expected, ProjectIdeaSorts.Normalise(input));
    }

    private static ProjectIdea Idea(
        int id,
        string title,
        string status,
        string createdBy,
        string? assignedOfficer = null)
    {
        return new ProjectIdea
        {
            Id = id,
            Title = title,
            Description = $"Description for {title}",
            Status = status,
            CreatedByUserId = createdBy,
            AssignedProjectOfficerUserId = assignedOfficer,
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
