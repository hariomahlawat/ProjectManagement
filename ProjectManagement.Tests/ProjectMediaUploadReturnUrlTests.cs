using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using Xunit;
using PhotoUploadModel = ProjectManagement.Pages.Projects.Photos.UploadModel;
using VideoUploadModel = ProjectManagement.Pages.Projects.Videos.UploadModel;

namespace ProjectManagement.Tests;

// Photo and video uploads opened from the Project Officer briefing desk go back to the desk;
// every other entry point keeps returning to the gallery or video library.
public sealed class ProjectMediaUploadReturnUrlTests
{
    private const int ProjectId = 31;
    private const string DeskUrl = "/Workspace?mode=project-officer&view=overview&focus=desk-project-31#desk-project-31";

    // SECTION: Photos
    [Fact]
    public async Task PhotoUpload_FromTheBriefingDesk_ReturnsToTheDeskWithAToast()
    {
        await using var connection = await OpenAsync();
        await using var db = await CreateDbAsync(connection);
        var page = CreatePhotoPage(db);
        page.ReturnUrl = DeskUrl;

        var result = await page.OnPostAsync(ProjectId, CancellationToken.None);

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal(DeskUrl, redirect.Url);
        Assert.Equal("Photo uploaded.", page.TempData["ToastSuccess"]);
        Assert.False(page.TempData.ContainsKey("Flash"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.test/Workspace")]
    [InlineData("/Projects/Overview/31")]
    public async Task PhotoUpload_WithoutADeskAddress_KeepsTheGalleryRedirect(string? returnUrl)
    {
        await using var connection = await OpenAsync();
        await using var db = await CreateDbAsync(connection);
        var page = CreatePhotoPage(db);
        page.ReturnUrl = returnUrl;

        var result = await page.OnPostAsync(ProjectId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Index", redirect.PageName);
        Assert.Equal(ProjectId, redirect.RouteValues!["id"]);
        Assert.Equal("Photo uploaded.", page.TempData["Flash"]);
        Assert.False(page.TempData.ContainsKey("ToastSuccess"));
    }

    // SECTION: Videos
    [Fact]
    public async Task VideoUpload_FromTheBriefingDesk_ReturnsToTheDeskWithAToast()
    {
        await using var connection = await OpenAsync();
        await using var db = await CreateDbAsync(connection);
        var page = CreateVideoPage(db);
        page.ReturnUrl = DeskUrl;

        var result = await page.OnPostAsync(ProjectId, CancellationToken.None);

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal(DeskUrl, redirect.Url);
        Assert.Equal("Video uploaded.", page.TempData["ToastSuccess"]);
        Assert.False(page.TempData.ContainsKey("Flash"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("//evil.example/Workspace")]
    [InlineData("/WorkspaceEvil")]
    public async Task VideoUpload_WithoutADeskAddress_KeepsTheLibraryRedirect(string? returnUrl)
    {
        await using var connection = await OpenAsync();
        await using var db = await CreateDbAsync(connection);
        var page = CreateVideoPage(db);
        page.ReturnUrl = returnUrl;

        var result = await page.OnPostAsync(ProjectId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Projects/Videos/Index", redirect.PageName);
        Assert.Equal(ProjectId, redirect.RouteValues!["id"]);
        Assert.Equal("Video uploaded.", page.TempData["Flash"]);
        Assert.False(page.TempData.ContainsKey("ToastSuccess"));
    }

    // SECTION: Helpers
    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<ApplicationDbContext> CreateDbAsync(SqliteConnection connection)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        foreach (var id in new[] { "seed", "assigned-po" })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id });
        }

        db.Projects.Add(new Project
        {
            Id = ProjectId,
            Name = "Media return project",
            CreatedByUserId = "seed",
            LeadPoUserId = "assigned-po",
            RowVersion = new byte[] { 2 }
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static PhotoUploadModel CreatePhotoPage(ApplicationDbContext db)
    {
        var user = new TestUserContext();
        var page = new PhotoUploadModel(db, user, NoOpService<IProjectPhotoService>(), NullLogger<PhotoUploadModel>.Instance)
        {
            Input = new PhotoUploadModel.UploadInput
            {
                ProjectId = ProjectId,
                RowVersion = Convert.ToBase64String(db.Projects.Single(p => p.Id == ProjectId).RowVersion),
                File = CreateFile("photo.png", "image/png")
            }
        };
        Configure(page, user.User);
        return page;
    }

    private static VideoUploadModel CreateVideoPage(ApplicationDbContext db)
    {
        var user = new TestUserContext();
        var page = new VideoUploadModel(
            db,
            user,
            NoOpService<IProjectVideoService>(),
            Options.Create(new ProjectVideoOptions()),
            NullLogger<VideoUploadModel>.Instance)
        {
            Input = new VideoUploadModel.UploadInput
            {
                ProjectId = ProjectId,
                RowVersion = Convert.ToBase64String(db.Projects.Single(p => p.Id == ProjectId).RowVersion),
                File = CreateFile("clip.mp4", "video/mp4")
            }
        };
        Configure(page, user.User);
        return page;
    }

    private static IFormFile CreateFile(string name, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes("media");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Input.File", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static void Configure(PageModel page, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        page.PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        page.TempData = new TempDataDictionary(httpContext, new NullTempDataProvider());
    }

    // The pages ignore the service's return value, so every call completes with a default result.
    private static T NoOpService<T>() where T : class
        => (T)(object)DispatchProxy.Create<T, NoOpProxy>();

    public class NoOpProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod!.ReturnType;
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                var value = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null, new[] { value });
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }

    private sealed class TestUserContext : IUserContext
    {
        public ClaimsPrincipal User { get; } = new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "assigned-po"),
            new Claim(ClaimTypes.Role, "Project Officer")
        }, "Test"));

        public string? UserId => "assigned-po";
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
