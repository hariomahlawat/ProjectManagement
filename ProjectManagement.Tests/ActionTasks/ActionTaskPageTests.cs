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
    public async Task CreateValidationFailure_ReopensPanel()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        var page = setup.Page;
        page.Input = new IndexModel.CreateTaskInput();
        page.ModelState.AddModelError("Input.Title", "required");

        // SECTION: Act
        var result = await page.OnPostCreateAsync();

        // SECTION: Assert
        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowCreateModal);
    }

    private static async Task<(IndexModel Page, ApplicationDbContext Db)> CreateSetupAsync(string role = RoleNames.Ta)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new ApplicationDbContext(options);

        var user = new ApplicationUser
        {
            Id = "user-1", UserName = "user1@test", NormalizedUserName = "USER1@TEST", Email = "user1@test", NormalizedEmail = "USER1@TEST", SecurityStamp = Guid.NewGuid().ToString(), FullName = "User One"
        };
        db.Users.Add(user);
        db.ActionTasks.Add(new ActionTaskItem
        {
            Title = "Mine", Description = "d", CreatedByUserId = "creator", AssignedToUserId = "user-1", CreatedByRole = RoleNames.HoD, AssignedToRole = RoleNames.Ta, DueDate = DateTime.UtcNow.AddDays(1), Priority = "Normal", AssignedOn = DateTime.UtcNow, Status = ActionTaskStatuses.Assigned
        });
        await db.SaveChangesAsync();

        var sp = new ServiceCollection().BuildServiceProvider();
        var userManager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db), Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(), new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), sp, NullLogger<UserManager<ApplicationUser>>.Instance);

        var service = new ActionTaskService(db, new ActionTaskPermissionService());
        var collab = new StubCollabService();
        var page = new IndexModel(service, collab, new ActionTaskPermissionService(), userManager);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim(ClaimTypes.Role, role)
            }, "TestAuth"))
        };

        page.PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
        return (page, db);
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
