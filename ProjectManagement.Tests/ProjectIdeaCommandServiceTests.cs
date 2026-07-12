using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ProjectIdeas;

namespace ProjectManagement.Tests;

public sealed class ProjectIdeaCommandServiceTests
{
    [Fact]
    public async Task AddCommentAsync_CapturesGeneralTypeAndStatusSnapshot()
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.OnHold);
        var service = new ProjectIdeaCommandService(db);

        await service.AddCommentAsync(idea, "  Progress recorded.  ", "po-1");
        var comment = await db.ProjectIdeaComments.SingleAsync();

        Assert.Equal(ProjectIdeaCommentTypes.General, comment.CommentType);
        Assert.Equal("Progress recorded.", comment.CommentText);
        Assert.Equal(ProjectIdeaStatuses.OnHold, comment.StatusSnapshot);
        Assert.Null(comment.CreatedByRole);
    }

    [Fact]
    public async Task AddConferenceCommentAsync_RejectsNonCommandRole()
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var service = new ProjectIdeaCommandService(db);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddConferenceCommentAsync(
                idea,
                "Command direction",
                "admin-1",
                RoleNames.Admin));

        Assert.Equal("Only Comdt or HoD may add conference remarks.", exception.Message);
        Assert.Empty(await db.ProjectIdeaComments.ToListAsync());
    }

    [Fact]
    public async Task AddConferenceCommentAsync_CapturesCommandContext()
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var service = new ProjectIdeaCommandService(db);

        var comment = await service.AddConferenceCommentAsync(
            idea,
            "Complete the feasibility review.",
            "hod-1",
            RoleNames.HoD);

        Assert.Equal(ProjectIdeaCommentTypes.Conference, comment.CommentType);
        Assert.Equal(RoleNames.HoD, comment.CreatedByRole);
        Assert.Equal(ProjectIdeaStatuses.Active, comment.StatusSnapshot);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<ProjectIdea> SeedIdeaAsync(ApplicationDbContext db, string status)
    {
        var idea = new ProjectIdea
        {
            Title = "Idea",
            Description = "Description",
            Status = status,
            CreatedByUserId = "creator"
        };

        db.ProjectIdeas.Add(idea);
        await db.SaveChangesAsync();
        return idea;
    }
}
