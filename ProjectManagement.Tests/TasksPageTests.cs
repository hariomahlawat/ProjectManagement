using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Pages.Tasks;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests;

public class TasksPageTests
{
    [Fact]
    public async Task OnPostAddAsync_OnlyQuickTokens_SetsErrorAndSkipsCreate()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(context),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            serviceProvider,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var todo = new RecordingTodoService();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1")
            }, "TestAuth"))
        };

        var page = new IndexModel(context, todo, userManager)
        {
            PageContext = new PageContext(new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor()))
        };

        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());

        var result = await page.OnPostAddAsync("tomorrow !high");

        Assert.IsType<RedirectToPageResult>(result);
        Assert.True(page.TempData.ContainsKey("Error"));
        Assert.Equal("Task title cannot be empty.", page.TempData["Error"]);
        Assert.False(todo.CreateCalled);
    }

    [Fact]
    public async Task OnPostSnoozeAsync_ClearPreset_CallsEditWithNullDueDate()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(context),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            serviceProvider,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var todo = new RecordingTodoService();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1")
            }, "TestAuth"))
        };

        var page = new IndexModel(context, todo, userManager)
        {
            PageContext = new PageContext(new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor()))
        };

        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());

        var id = Guid.NewGuid();
        var result = await page.OnPostSnoozeAsync(id, "clear");

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(id, todo.LastEditId);
        Assert.Null(todo.LastDueAtLocal);
        Assert.True(todo.LastUpdateDueDate);
    }

    private sealed class RecordingTodoService : ITodoService
    {
        public bool CreateCalled { get; private set; }
        public Guid? LastEditId { get; private set; }
        public DateTimeOffset? LastDueAtLocal { get; private set; }
        public bool LastUpdateDueDate { get; private set; }

        public Task<TodoWidgetResult> GetWidgetAsync(string ownerId, int take = 20)
            => throw new NotImplementedException();

        public Task<TodoItem> CreateAsync(string ownerId, string title, DateTimeOffset? dueAtLocal = null,
            TodoPriority priority = TodoPriority.Normal, bool pinned = false)
        {
            CreateCalled = true;
            return Task.FromResult(new TodoItem());
        }

        public Task<bool> ToggleDoneAsync(string ownerId, Guid id, bool done)
            => throw new NotImplementedException();

        public Task<bool> EditAsync(string ownerId, Guid id, string? title = null, DateTimeOffset? dueAtLocal = null,
            bool updateDueDate = false, TodoPriority? priority = null, bool? pinned = null)
        {
            LastEditId = id;
            LastDueAtLocal = dueAtLocal;
            LastUpdateDueDate = updateDueDate;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(string ownerId, Guid id)
            => throw new NotImplementedException();

        public Task<int> ClearCompletedAsync(string ownerId)
            => throw new NotImplementedException();

        public Task<bool> ReorderAsync(string ownerId, IList<Guid> orderedIds)
            => throw new NotImplementedException();

        public Task MarkDoneAsync(string ownerId, IList<Guid> ids)
            => throw new NotImplementedException();

        public Task DeleteManyAsync(string ownerId, IList<Guid> ids)
            => throw new NotImplementedException();
    }
}
