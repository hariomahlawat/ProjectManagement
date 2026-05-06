using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Pages.ActionTasks;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskPageTests
{
    [Fact]
    public async Task OnGet_InvalidTaskId_DoesNotThrowAndClearsSelection()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        page.ViewMode = "MyTasks";
        page.TaskId = 9999;

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.Null(page.TaskId);
        Assert.Null(page.SelectedTask);
    }

    [Fact]
    public async Task MyTasksView_StaysActiveWhenSortingTaskList()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        page.ViewMode = "MyTasks";
        page.SortBy = "title";
        page.SortDir = "desc";

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.True(page.IsMyTasksView);
        Assert.Equal("MyTasks", page.ResolvedViewMode);
    }

    [Fact]
    public async Task TaskList_SelectedTaskLoadsWhenFilteredOutOfRegister()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        var selectedTaskId = await setup.Db.ActionTasks.Select(t => t.Id).SingleAsync();
        page.ViewMode = "TaskList";
        page.FilterStatus = ActionTaskStatuses.Closed;
        page.TaskId = selectedTaskId;

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.Empty(page.Tasks);
        Assert.NotNull(page.SelectedTask);
        Assert.Equal(selectedTaskId, page.SelectedTask!.Id);
    }

    [Fact]
    public async Task CreateValidationFailure_ReopensPanel()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        page.Input = new IndexModel.CreateTaskInput();
        page.ModelState.AddModelError("Input.Title", "required");

        // SECTION: Act
        var result = await page.OnPostCreateAsync();

        // SECTION: Assert
        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowCreateModal);
    }

    [Fact]
    public async Task CreatePastDueDate_ReopensPanelAndRejectsTask()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        var existingTaskCount = await setup.Db.ActionTasks.CountAsync();
        page.Input = new IndexModel.CreateTaskInput
        {
            Title = "Late task",
            Description = "Should be rejected",
            AssignedToUserId = "user-1",
            DueDate = DateTime.UtcNow.Date.AddDays(-1),
            Priority = "Normal"
        };

        // SECTION: Act
        var result = await page.OnPostCreateAsync();

        // SECTION: Assert
        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowCreateModal);
        Assert.False(page.ModelState.IsValid);
        Assert.Equal(existingTaskCount, await setup.Db.ActionTasks.CountAsync());
        Assert.Contains(page.ModelState[nameof(IndexModel.CreateTaskInput.DueDate)]!.Errors, e => e.ErrorMessage == "Due date cannot be in the past.");
    }


    [Fact]
    public async Task BacklogView_ShowsOnlyTasksWithoutSprintAssignment()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var sprint = AddSprint(setup.Db, "Active Sprint", ActionSprintStatus.Active);
        await setup.Db.SaveChangesAsync();
        setup.Db.ActionTasks.Add(NewTask("Sprint task", ActionTaskStatuses.Assigned, sprint.Id));
        setup.Db.ActionTasks.Add(NewTask("Closed backlog", ActionTaskStatuses.Closed));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Backlog";

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.NotEmpty(page.BacklogTasks);
        Assert.All(page.BacklogTasks, task => Assert.Null(task.SprintId));
        Assert.Contains(page.BacklogTasks, task => task.Title == "Mine");
        Assert.Contains(page.BacklogTasks, task => task.Title == "Closed backlog");
        Assert.DoesNotContain(page.BacklogTasks, task => task.Title == "Sprint task");
    }

    [Fact]
    public async Task SprintsView_SelectsActiveSprintByDefault()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        AddSprint(setup.Db, "Older planned sprint", ActionSprintStatus.Planned, startOffsetDays: -14);
        var activeSprint = AddSprint(setup.Db, "Current active sprint", ActionSprintStatus.Active, startOffsetDays: -1);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Sprints";

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.NotNull(page.SelectedSprint);
        Assert.Equal(activeSprint.Id, page.SelectedSprint!.Id);
        Assert.Equal(activeSprint.Id, page.SelectedSprintId);
        Assert.True(page.CanModifySelectedSprint);
    }

    [Fact]
    public async Task SprintsView_DoesNotOfferClosedBacklogTasksForAssignment()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        AddSprint(setup.Db, "Active Sprint", ActionSprintStatus.Active);
        setup.Db.ActionTasks.Add(NewTask("Closed backlog", ActionTaskStatuses.Closed));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Sprints";

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.Contains(page.SprintBacklogTasks, task => task.Title == "Closed backlog");
        Assert.DoesNotContain(page.AssignableBacklogTaskDisplays, item => item.Task.Title == "Closed backlog");
        Assert.All(page.AssignableBacklogTaskDisplays, item => Assert.NotEqual(ActionTaskStatuses.Closed, item.Task.Status));
        Assert.False(page.CanAssignTaskToSprint(page.SprintBacklogTasks.Single(task => task.Title == "Closed backlog")));
    }

    [Fact]
    public async Task SelectedClosedSprint_IsReadOnlyInUiHelperLogic()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var closedSprint = AddSprint(setup.Db, "Closed Sprint", ActionSprintStatus.Closed, startOffsetDays: -7);
        await setup.Db.SaveChangesAsync();
        setup.Db.ActionTasks.Add(NewTask("Closed sprint task", ActionTaskStatuses.Assigned, closedSprint.Id));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Sprints";
        page.SelectedSprintId = closedSprint.Id;

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.NotNull(page.SelectedSprint);
        Assert.Equal(ActionSprintStatus.Closed, page.SelectedSprint!.Status);
        Assert.False(page.CanModifySelectedSprint);
        var sprintTask = Assert.Single(page.SelectedSprintTasks);
        Assert.False(page.CanMoveTaskToBacklog(sprintTask));
    }



    [Fact]
    public async Task CreateSprintPageHandler_WithPlanningAuthority_CreatesSprint()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var page = setup.Page;
        page.ViewMode = "Sprints";
        page.SprintInput = new IndexModel.CreateSprintInput
        {
            Name = "Command Sprint",
            StartDate = DateTime.UtcNow.Date,
            EndDate = DateTime.UtcNow.Date.AddDays(14),
            Goal = "Command focus"
        };

        // SECTION: Act
        var result = await page.OnPostCreateSprintAsync();

        // SECTION: Assert
        Assert.IsType<RedirectToPageResult>(result);
        var sprint = await setup.Db.ActionSprints.SingleAsync(s => s.Name == "Command Sprint");
        Assert.Equal(ActionSprintStatus.Planned, sprint.Status);
    }

    [Fact]
    public async Task CreateSprintPageHandler_WithNonAuthority_ReturnsPageAndDoesNotCreateSprint()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.ProjectOfficer);
        var page = setup.Page;
        page.ViewMode = "Sprints";
        page.SprintInput = new IndexModel.CreateSprintInput
        {
            Name = "Unauthorized Sprint",
            StartDate = DateTime.UtcNow.Date,
            EndDate = DateTime.UtcNow.Date.AddDays(14)
        };

        // SECTION: Act
        var result = await page.OnPostCreateSprintAsync();

        // SECTION: Assert
        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowCreateSprintPanel);
        Assert.Empty(await setup.Db.ActionSprints.Where(s => s.Name == "Unauthorized Sprint").ToListAsync());
        Assert.False(page.ModelState.IsValid);
    }

    [Fact]
    public async Task CreateSprintPageHandler_WithInvalidDateRange_ReturnsPageError()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        var page = setup.Page;
        page.ViewMode = "Sprints";
        page.SprintInput = new IndexModel.CreateSprintInput
        {
            Name = "Invalid Sprint",
            StartDate = DateTime.UtcNow.Date.AddDays(5),
            EndDate = DateTime.UtcNow.Date
        };

        // SECTION: Act
        var result = await page.OnPostCreateSprintAsync();

        // SECTION: Assert
        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowCreateSprintPanel);
        Assert.False(page.ModelState.IsValid);
        Assert.Empty(await setup.Db.ActionSprints.Where(s => s.Name == "Invalid Sprint").ToListAsync());
    }

    [Fact]
    public async Task UpdateSprintPageHandler_WithPlannedSprint_UpdatesDetails()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        var sprint = AddSprint(setup.Db, "Planned Sprint", ActionSprintStatus.Planned);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.SprintEditInput = new IndexModel.UpdateSprintInput
        {
            SprintId = sprint.Id,
            RowVersion = Convert.ToBase64String(sprint.RowVersion),
            Name = "Updated Sprint",
            Goal = "Updated focus",
            StartDate = sprint.StartDate,
            EndDate = sprint.EndDate.AddDays(1)
        };

        // SECTION: Act
        var result = await page.OnPostUpdateSprintAsync();

        // SECTION: Assert
        Assert.IsType<RedirectToPageResult>(result);
        var updated = await setup.Db.ActionSprints.SingleAsync(s => s.Id == sprint.Id);
        Assert.Equal("Updated Sprint", updated.Name);
        Assert.Equal("Updated focus", updated.Goal);
    }

    [Fact]
    public async Task UpdateSprintPageHandler_WithClosedSprint_ReturnsPageError()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var sprint = AddSprint(setup.Db, "Closed Sprint", ActionSprintStatus.Closed);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.SprintEditInput = new IndexModel.UpdateSprintInput
        {
            SprintId = sprint.Id,
            RowVersion = Convert.ToBase64String(sprint.RowVersion),
            Name = "Should Not Update",
            StartDate = sprint.StartDate,
            EndDate = sprint.EndDate
        };

        // SECTION: Act
        var result = await page.OnPostUpdateSprintAsync();

        // SECTION: Assert
        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowEditSprintPanel);
        var unchanged = await setup.Db.ActionSprints.SingleAsync(s => s.Id == sprint.Id);
        Assert.Equal("Closed Sprint", unchanged.Name);
    }

    [Fact]
    public async Task ActivateSprintPageHandler_WithPlannedSprint_ActivatesSprint()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var sprint = AddSprint(setup.Db, "Planned Sprint", ActionSprintStatus.Planned);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;

        // SECTION: Act
        var result = await page.OnPostActivateSprintAsync(sprint.Id, Convert.ToBase64String(sprint.RowVersion));

        // SECTION: Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(ActionSprintStatus.Active, (await setup.Db.ActionSprints.SingleAsync(s => s.Id == sprint.Id)).Status);
    }

    [Fact]
    public async Task ActivateSprintPageHandler_WhenAnotherSprintActive_ShowsError()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        AddSprint(setup.Db, "Active Sprint", ActionSprintStatus.Active);
        var planned = AddSprint(setup.Db, "Planned Sprint", ActionSprintStatus.Planned, startOffsetDays: 20);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;

        // SECTION: Act
        var result = await page.OnPostActivateSprintAsync(planned.Id, Convert.ToBase64String(planned.RowVersion));

        // SECTION: Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(ActionSprintStatus.Planned, (await setup.Db.ActionSprints.SingleAsync(s => s.Id == planned.Id)).Status);
        Assert.Contains("Only one active sprint", page.TempData["ToastError"]?.ToString());
    }

    [Fact]
    public async Task CreateNextSprintPageHandler_UsesDayAfterSourceEndDateAndBecomesCarryTarget()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var source = AddSprint(setup.Db, "Active Source", ActionSprintStatus.Active, startOffsetDays: 0);
        setup.Db.ActionTasks.Add(NewTask("Carry me", ActionTaskStatuses.Assigned, source.Id));
        await setup.Db.SaveChangesAsync();
        var expectedStart = source.EndDate.Date.AddDays(1);
        var page = setup.Page;
        page.NextSprintInput = IndexModel.CreateNextSprintInput.FromSourceSprint(source);

        // SECTION: Act
        var result = await page.OnPostCreateNextSprintAsync();

        // SECTION: Assert
        Assert.IsType<RedirectToPageResult>(result);
        var next = await setup.Db.ActionSprints.SingleAsync(s => s.Name == page.NextSprintInput.Name);
        Assert.Equal(expectedStart, next.StartDate.Date);
        page.SelectedSprintId = source.Id;
        page.ViewMode = "Sprints";
        await page.OnGetAsync();
        Assert.Contains(page.ClosureTargetSprints, s => s.Id == next.Id);
    }

    [Fact]
    public async Task SprintHistoryReadModel_ReturnsAuditEventsWithActorVisibility()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var page = setup.Page;
        page.SprintInput = new IndexModel.CreateSprintInput
        {
            Name = "Audited Sprint",
            StartDate = DateTime.UtcNow.Date,
            EndDate = DateTime.UtcNow.Date.AddDays(14)
        };
        await page.OnPostCreateSprintAsync();
        var sprint = await setup.Db.ActionSprints.SingleAsync(s => s.Name == "Audited Sprint");
        page.ViewMode = "Sprints";
        page.SelectedSprintId = sprint.Id;

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.Contains(page.SprintAuditHistory, x => x.ActionType == "SprintCreated");
        Assert.Equal("User One", page.ResolveSprintActorName("user-1"));
    }

    [Fact]
    public void AuditLogModel_DoesNotCreateShadowTaskIdRelationship()
    {
        // SECTION: Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new ApplicationDbContext(options);

        // SECTION: Act
        var entityType = db.Model.FindEntityType(typeof(ActionTaskAuditLog))!;
        var taskId1 = entityType.FindProperty("TaskId1");

        // SECTION: Assert
        Assert.Null(taskId1);
        Assert.Single(entityType.GetForeignKeys().Where(fk => fk.PrincipalEntityType.ClrType == typeof(ActionTaskItem)));
    }

    private static async Task<(IndexModel Page, ApplicationDbContext Db)> CreateSetupAsync(string currentRole = RoleNames.HoD)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new ApplicationDbContext(options);

        var user = new ApplicationUser
        {
            Id = "user-1", UserName = "user1@test", NormalizedUserName = "USER1@TEST", Email = "user1@test", NormalizedEmail = "USER1@TEST", SecurityStamp = Guid.NewGuid().ToString(), FullName = "User One"
        };
        db.Users.Add(user);
        db.Roles.Add(new IdentityRole { Id = RoleNames.Ta, Name = RoleNames.Ta, NormalizedName = RoleNames.Ta.ToUpperInvariant() });
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = RoleNames.Ta });
        db.ActionTasks.Add(new ActionTaskItem
        {
            Title = "Mine", Description = "d", CreatedByUserId = "creator", AssignedToUserId = "user-1", CreatedByRole = RoleNames.HoD, AssignedToRole = RoleNames.Ta, DueDate = DateTime.UtcNow.AddDays(1), Priority = "Normal", AssignedOn = DateTime.UtcNow, Status = ActionTaskStatuses.Assigned
        });
        await db.SaveChangesAsync();

        var sp = new ServiceCollection().BuildServiceProvider();
        var userManager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db), Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(), new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), sp, NullLogger<UserManager<ApplicationUser>>.Instance);

        // SECTION: Action task page services
        var permission = new ActionTaskPermissionService();
        var service = new ActionTaskService(db, permission);
        var collab = new StubCollabService();
        var queryService = new ActionTaskQueryService();
        var workflowPolicy = new ActionTaskWorkflowPolicy(permission);
        var sprintWorkflowPolicy = new ActionSprintWorkflowPolicy();
        var sprintService = new ActionSprintService(db, permission, sprintWorkflowPolicy);
        var page = new IndexModel(service, collab, permission, sprintService, queryService, workflowPolicy, userManager);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim(ClaimTypes.Role, currentRole)
            }, "TestAuth"))
        };

        page.PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
        return (page, db);
    }


    private static ActionSprint AddSprint(ApplicationDbContext db, string name, ActionSprintStatus status, int startOffsetDays = 0)
    {
        // SECTION: Sprint test fixture creation helper.
        var sprint = new ActionSprint
        {
            Name = name,
            Goal = $"Goal for {name}",
            StartDate = DateTime.UtcNow.Date.AddDays(startOffsetDays),
            EndDate = DateTime.UtcNow.Date.AddDays(startOffsetDays + 7),
            Status = status,
            CreatedByUserId = "creator",
            CreatedByRole = RoleNames.HoD,
            CreatedAtUtc = DateTime.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        db.ActionSprints.Add(sprint);
        return sprint;
    }

    private static ActionTaskItem NewTask(string title, string status, int? sprintId = null)
    {
        // SECTION: Action task test fixture creation helper.
        return new ActionTaskItem
        {
            Title = title,
            Description = "d",
            CreatedByUserId = "creator",
            AssignedToUserId = "user-1",
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = DateTime.UtcNow.AddDays(2),
            Priority = "Normal",
            AssignedOn = DateTime.UtcNow,
            Status = status,
            SprintId = sprintId
        };
    }

    private sealed class StubCollabService : IActionTaskCollaborationService
    {
        public Task<ActionTaskUpdate> AddUpdateAsync(int taskId, string body, string updateType, string userId, string role, IReadOnlyList<IFormFile> files, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>>> GetAttachmentMetadataByUpdateAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyDictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>>)new Dictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>>());
        public Task<List<ActionTaskUpdate>> GetUpdatesAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default) => Task.FromResult(new List<ActionTaskUpdate>());
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }
}
