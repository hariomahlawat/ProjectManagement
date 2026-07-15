using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectOfficerConferenceActionQueryTests
{
    [Fact]
    public async Task GetPendingAsync_RemovesDirectionsAfterOfficerProgressIsRecorded()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var command = User("command", "Command User");
        var officer = User("officer", "Project Officer");
        db.Users.AddRange(command, officer);

        var project = new Project
        {
            Name = "Directed project",
            CreatedByUserId = command.Id,
            LeadPoUserId = officer.Id,
            LifecycleStatus = ProjectLifecycleStatus.Active
        };
        var idea = new ProjectIdea
        {
            Title = "Directed idea",
            Description = "A sufficiently clear idea description.",
            Status = ProjectIdeaStatuses.Active,
            AssignedProjectOfficerUserId = officer.Id,
            CreatedByUserId = command.Id,
            CreatedAt = Utc(1),
            UpdatedAt = Utc(1)
        };
        var task = new ActionTaskItem
        {
            Title = "Directed task",
            Description = "Prepare the required response.",
            CreatedByUserId = command.Id,
            AssignedToUserId = officer.Id,
            CreatedByRole = "HoD",
            AssignedToRole = "ProjectOfficer",
            AssignedOn = Utc(1),
            DueDate = Utc(20),
            Priority = "High",
            Status = ActionTaskStatuses.InProgress
        };
        db.AddRange(project, idea, task);
        await db.SaveChangesAsync();

        db.Remarks.Add(new Remark
        {
            ProjectId = project.Id,
            AuthorUserId = command.Id,
            AuthorRole = RemarkActorRole.HeadOfDepartment,
            Type = RemarkType.Conference,
            Scope = RemarkScope.General,
            Body = "Update the project paper.",
            EventDate = new DateOnly(2026, 7, 2),
            CreatedAtUtc = Utc(2)
        });
        db.ProjectIdeaComments.Add(new ProjectIdeaComment
        {
            ProjectIdeaId = idea.Id,
            CommentText = "Refine the concept.",
            CommentType = ProjectIdeaCommentTypes.Conference,
            CreatedByUserId = command.Id,
            CreatedAt = Utc(2)
        });
        db.ActionTaskUpdates.Add(new ActionTaskUpdate
        {
            TaskId = task.Id,
            CreatedByUserId = command.Id,
            CreatedAtUtc = Utc(2),
            UpdateType = ActionTaskUpdateTypes.Conference,
            Body = "Complete the task response."
        });
        await db.SaveChangesAsync();

        var query = new ProjectOfficerConferenceActionQuery(db);
        var projects = new Dictionary<int, string> { [project.Id] = project.Name };
        var ideas = new Dictionary<int, string> { [idea.Id] = idea.Title };
        var tasks = new Dictionary<int, string> { [task.Id] = task.Title };

        var pending = await query.GetPendingAsync(officer.Id, projects, ideas, tasks);

        Assert.Equal(3, pending.Count);
        Assert.Contains(pending, item => item.Kind == ConferenceItemKind.Project && item.ProjectId == project.Id);
        Assert.Contains(pending, item => item.Kind == ConferenceItemKind.ProjectIdea && item.ItemId == idea.Id);
        Assert.Contains(pending, item => item.Kind == ConferenceItemKind.ActionTask && item.ItemId == task.Id);

        db.Remarks.Add(new Remark
        {
            ProjectId = project.Id,
            AuthorUserId = officer.Id,
            AuthorRole = RemarkActorRole.ProjectOfficer,
            Type = RemarkType.Internal,
            Scope = RemarkScope.General,
            Body = "Project paper updated.",
            EventDate = new DateOnly(2026, 7, 3),
            CreatedAtUtc = Utc(3)
        });
        db.ProjectIdeaNotes.Add(new ProjectIdeaNote
        {
            ProjectIdeaId = idea.Id,
            Title = "Progress",
            Body = "Concept refined.",
            CreatedByUserId = officer.Id,
            CreatedAt = Utc(3),
            UpdatedAt = Utc(3)
        });
        db.ActionTaskUpdates.Add(new ActionTaskUpdate
        {
            TaskId = task.Id,
            CreatedByUserId = officer.Id,
            CreatedAtUtc = Utc(3),
            UpdateType = ActionTaskUpdateTypes.Progress,
            Body = "Task response completed."
        });
        await db.SaveChangesAsync();

        pending = await query.GetPendingAsync(officer.Id, projects, ideas, tasks);

        Assert.Empty(pending);
    }

    private static ApplicationUser User(string id, string fullName)
        => new()
        {
            Id = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            FullName = fullName,
            SecurityStamp = Guid.NewGuid().ToString()
        };

    private static DateTime Utc(int day)
        => new(2026, 7, day, 8, 0, 0, DateTimeKind.Utc);
}
