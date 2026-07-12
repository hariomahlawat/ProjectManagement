using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Tests;

public sealed class OfficerConferenceReadServiceTests
{
    [Fact]
    public async Task GetAsync_UsesLatestNativeDirection_AndShowsResponsibleFunctionaryResponses()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var command = CreateUser("command-1", "Command User", "Colonel");
        var officer = CreateUser("officer-1", "Test Officer", "Lt Col");
        var otherOfficer = CreateUser("officer-other", "Other Officer", "Lt Col");
        var mco = CreateUser("mco-1", "MCO User", "Major");
        db.Users.AddRange(command, officer, otherOfficer, mco);

        var project = new Project
        {
            Name = "Conference project",
            CreatedByUserId = command.Id,
            LeadPoUserId = officer.Id,
            WorkflowVersion = ProcurementWorkflow.VersionV2,
            LifecycleStatus = ProjectLifecycleStatus.Active
        };
        db.Projects.Add(project);

        var idea = new ProjectIdea
        {
            Title = "Conference idea",
            Description = "Idea description",
            Status = ProjectIdeaStatuses.Active,
            AssignedProjectOfficerUserId = officer.Id,
            CreatedByUserId = command.Id,
            CreatedAt = Utc(1),
            UpdatedAt = Utc(6)
        };
        db.ProjectIdeas.Add(idea);

        var task = new ActionTaskItem
        {
            Title = "Conference task",
            Description = "Task description",
            CreatedByUserId = command.Id,
            AssignedToUserId = officer.Id,
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.ProjectOfficer,
            AssignedOn = Utc(1),
            DueDate = new DateTime(2026, 12, 31),
            Priority = "High",
            Status = ActionTaskStatuses.InProgress
        };
        db.ActionTasks.Add(task);
        await db.SaveChangesAsync();

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = project.Id,
            StageCode = StageCodes.TEC,
            SortOrder = 5,
            Status = StageStatus.InProgress,
            ActualStart = new DateOnly(2026, 7, 1),
            PlannedDue = new DateOnly(2026, 12, 31)
        });

        db.Remarks.AddRange(
            ProjectRemark(project.Id, command.Id, RemarkActorRole.HeadOfDepartment, RemarkType.Conference, "Older direction", Utc(1), StageCodes.BID),
            ProjectRemark(project.Id, command.Id, RemarkActorRole.HeadOfDepartment, RemarkType.Conference, "Latest project direction", Utc(3), StageCodes.BID),
            ProjectRemark(project.Id, otherOfficer.Id, RemarkActorRole.ProjectOfficer, RemarkType.Internal, "Other officer update", Utc(4), StageCodes.TEC),
            ProjectRemark(project.Id, officer.Id, RemarkActorRole.ProjectOfficer, RemarkType.Internal, "Project update one", Utc(5), StageCodes.TEC),
            ProjectRemark(project.Id, officer.Id, RemarkActorRole.ProjectOfficer, RemarkType.External, "Project update two", Utc(6), StageCodes.TEC),
            ProjectRemark(project.Id, mco.Id, RemarkActorRole.Mco, RemarkType.Internal, "MCO commercial update", Utc(7), StageCodes.TEC));

        db.ProjectIdeaComments.AddRange(
            new ProjectIdeaComment
            {
                ProjectIdeaId = idea.Id,
                CommentText = "Idea direction",
                CommentType = ProjectIdeaCommentTypes.Conference,
                CreatedByUserId = command.Id,
                CreatedByRole = RoleNames.HoD,
                StatusSnapshot = ProjectIdeaStatuses.Active,
                CreatedAt = Utc(2)
            },
            new ProjectIdeaComment
            {
                ProjectIdeaId = idea.Id,
                CommentText = "Idea progress",
                CommentType = ProjectIdeaCommentTypes.General,
                CreatedByUserId = officer.Id,
                CreatedAt = Utc(4)
            });

        db.ProjectIdeaNotes.Add(new ProjectIdeaNote
        {
            ProjectIdeaId = idea.Id,
            Title = "Latest working note",
            Body = "Note progress after direction",
            CreatedByUserId = officer.Id,
            CreatedAt = Utc(1),
            UpdatedAt = Utc(5)
        });

        db.ActionTaskUpdates.AddRange(
            new ActionTaskUpdate
            {
                TaskId = task.Id,
                Body = "Task direction",
                UpdateType = ActionTaskUpdateTypes.Conference,
                CreatedByUserId = command.Id,
                CreatedByRole = RoleNames.HoD,
                StatusSnapshot = ActionTaskStatuses.Assigned,
                DueDateSnapshot = new DateOnly(2026, 12, 31),
                CreatedAtUtc = Utc(2)
            },
            new ActionTaskUpdate
            {
                TaskId = task.Id,
                Body = "Task progress",
                UpdateType = ActionTaskUpdateTypes.Progress,
                CreatedByUserId = officer.Id,
                CreatedAtUtc = Utc(5)
            });
        await db.SaveChangesAsync();

        var workload = new StubOfficerWorkloadReadService(new[]
        {
            new CommandOfficerWorkloadVm
            {
                UserId = officer.Id,
                OfficerName = officer.FullName!,
                Rank = officer.Rank!,
                ProjectCount = 1,
                IdeaCount = 1,
                OtherTaskCount = 1,
                Projects = new[]
                {
                    new CommandOfficerProjectVm(project.Id, project.Name, StageCodes.TEC, "Technical Evaluation", $"/Projects/Overview/{project.Id}")
                },
                Ideas = new[]
                {
                    new CommandOfficerIdeaVm(idea.Id, idea.Title, idea.Status, $"/ProjectIdeas/Details/{idea.Id}")
                },
                OtherTasks = new[]
                {
                    new CommandOfficerTaskVm(task.Id, task.Title, task.Status, task.DueDate, $"/ActionTasks?taskId={task.Id}")
                }
            },
            new CommandOfficerWorkloadVm
            {
                UserId = "officer-2",
                OfficerName = "Next Officer",
                Rank = "Lt Col"
            }
        });
        var service = new OfficerConferenceReadService(
            db,
            workload,
            new WorkflowStageMetadataProvider(),
            new FixedClock(Utc(10)));

        var result = await service.GetAsync(command.Id, officer.Id);

        Assert.NotNull(result);
        Assert.Equal("officer-2", result!.NextOfficerUserId);

        var projectItem = Assert.Single(result.Sections.Single(section => section.Kind == ConferenceItemKind.Project).Items);
        Assert.Equal("Latest project direction", projectItem.LatestDirection!.Body);
        Assert.Empty(projectItem.ProgressSummary);
        Assert.Null(projectItem.LatestProgressText);
        Assert.Collection(
            projectItem.ProgressEntries,
            projectOfficerEntry =>
            {
                Assert.Equal("Project Officer", projectOfficerEntry.Label);
                Assert.Equal("Project update two", projectOfficerEntry.Body);
                Assert.Equal(officer.FullName, projectOfficerEntry.AuthorName);
            },
            mcoEntry =>
            {
                Assert.Equal("MCO", mcoEntry.Label);
                Assert.Equal("MCO commercial update", mcoEntry.Body);
                Assert.Equal(mco.FullName, mcoEntry.AuthorName);
            });

        var ideaItem = Assert.Single(result.Sections.Single(section => section.Kind == ConferenceItemKind.ProjectIdea).Items);
        Assert.Equal("Idea direction", ideaItem.LatestDirection!.Body);
        Assert.Empty(ideaItem.ProgressSummary);
        Assert.Collection(
            ideaItem.ProgressEntries,
            commentEntry =>
            {
                Assert.Equal("Latest comment", commentEntry.Label);
                Assert.Equal("Idea progress", commentEntry.Body);
            },
            noteEntry =>
            {
                Assert.Equal("Latest note", noteEntry.Label);
                Assert.Equal("Latest working note", noteEntry.Title);
                Assert.Equal("Note progress after direction", noteEntry.Body);
            });

        var taskItem = Assert.Single(result.Sections.Single(section => section.Kind == ConferenceItemKind.ActionTask).Items);
        Assert.Equal("Task direction", taskItem.LatestDirection!.Body);
        Assert.Contains("Assigned → In Progress", taskItem.ProgressSummary);
        Assert.Contains("1 subsequent update", taskItem.ProgressSummary);
        Assert.Equal("Task progress", taskItem.LatestProgressText);
    }

    [Fact]
    public async Task GetAsync_ProjectWithoutAssignedOfficerResponse_ShowsOnlyRequiredPlaceholder()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var command = CreateUser("command-1", "Command User", "Colonel");
        var officer = CreateUser("officer-1", "Test Officer", "Lt Col");
        db.Users.AddRange(command, officer);

        var project = new Project
        {
            Name = "Conference project",
            CreatedByUserId = command.Id,
            LeadPoUserId = officer.Id,
            WorkflowVersion = ProcurementWorkflow.VersionV2,
            LifecycleStatus = ProjectLifecycleStatus.Active
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.Remarks.Add(ProjectRemark(
            project.Id,
            command.Id,
            RemarkActorRole.HeadOfDepartment,
            RemarkType.Conference,
            "Direction",
            Utc(2),
            StageCodes.BID));
        await db.SaveChangesAsync();

        var workload = new StubOfficerWorkloadReadService(new[]
        {
            new CommandOfficerWorkloadVm
            {
                UserId = officer.Id,
                OfficerName = officer.FullName!,
                Rank = officer.Rank!,
                Projects = new[]
                {
                    new CommandOfficerProjectVm(project.Id, project.Name, StageCodes.BID, "Bid", $"/Projects/Overview/{project.Id}")
                }
            }
        });
        var service = new OfficerConferenceReadService(
            db,
            workload,
            new WorkflowStageMetadataProvider(),
            new FixedClock(Utc(10)));

        var result = await service.GetAsync(command.Id, officer.Id);

        var projectItem = Assert.Single(
            result!.Sections.Single(section => section.Kind == ConferenceItemKind.Project).Items);
        var placeholder = Assert.Single(projectItem.ProgressEntries);
        Assert.Equal("Project Officer", placeholder.Label);
        Assert.Equal("No remark by the Project Officer after the direction.", placeholder.EmptyText);
        Assert.Null(placeholder.Body);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenOfficerIsOutsideCanonicalWorkloadOrder()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var service = new OfficerConferenceReadService(
            db,
            new StubOfficerWorkloadReadService(Array.Empty<CommandOfficerWorkloadVm>()),
            new WorkflowStageMetadataProvider(),
            new FixedClock(Utc(10)));

        var result = await service.GetAsync("command-1", "officer-1");

        Assert.Null(result);
    }

    private static ApplicationUser CreateUser(string id, string fullName, string rank)
        => new()
        {
            Id = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            FullName = fullName,
            Rank = rank,
            SecurityStamp = Guid.NewGuid().ToString()
        };

    private static Remark ProjectRemark(
        int projectId,
        string authorUserId,
        RemarkActorRole authorRole,
        RemarkType type,
        string body,
        DateTime createdAtUtc,
        string stageCode)
        => new()
        {
            ProjectId = projectId,
            AuthorUserId = authorUserId,
            AuthorRole = authorRole,
            Type = type,
            Scope = RemarkScope.General,
            Body = body,
            EventDate = DateOnly.FromDateTime(createdAtUtc),
            StageRef = stageCode,
            StageNameSnapshot = StageCodes.DisplayNameOf(ProcurementWorkflow.VersionV2, stageCode),
            CreatedAtUtc = createdAtUtc
        };

    private static DateTime Utc(int day)
        => new(2026, 7, day, 8, 0, 0, DateTimeKind.Utc);

    private sealed class StubOfficerWorkloadReadService : IOfficerWorkloadReadService
    {
        private readonly IReadOnlyList<CommandOfficerWorkloadVm> _officers;

        public StubOfficerWorkloadReadService(IReadOnlyList<CommandOfficerWorkloadVm> officers)
        {
            _officers = officers;
        }

        public Task<IReadOnlyList<CommandOfficerWorkloadVm>> GetAllAsync(
            string requestingUserId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_officers);

        public Task<CommandOfficerWorkloadVm?> GetOfficerAsync(
            string officerUserId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_officers.SingleOrDefault(officer => officer.UserId == officerUserId));

        public Task<int> CountActiveOfficersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_officers.Count);
    }
    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utcNow)
        {
            UtcNow = new DateTimeOffset(utcNow);
        }

        public DateTimeOffset UtcNow { get; }
    }

}
