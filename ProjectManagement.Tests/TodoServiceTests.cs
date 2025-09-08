using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests
{
    public class TodoServiceTests
    {
        private class FakeAudit : IAuditService
        {
            public List<string> Actions = new();
            public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, Microsoft.AspNetCore.Http.HttpContext? http = null)
            {
                Actions.Add(action);
                return Task.CompletedTask;
            }
        }

        private ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task WidgetReturnsOnlyOwnersTasks()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);

            context.TodoItems.Add(new TodoItem { Id = Guid.NewGuid(), OwnerId = "alice", Title = "A" });
            context.TodoItems.Add(new TodoItem { Id = Guid.NewGuid(), OwnerId = "bob", Title = "B" });
            context.SaveChanges();

            var result = await service.GetWidgetAsync("alice");
            Assert.Single(result.Items);
            Assert.Equal("A", result.Items[0].Title);
        }

        [Fact]
        public async Task ToggleDoneSetsCompletedUtc()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);
            var item = new TodoItem { Id = Guid.NewGuid(), OwnerId = "alice", Title = "Test" };
            context.TodoItems.Add(item);
            context.SaveChanges();

            var ok = await service.ToggleDoneAsync("alice", item.Id, true);
            Assert.True(ok);
            var stored = await context.TodoItems.FindAsync(item.Id);
            Assert.Equal(TodoStatus.Done, stored!.Status);
            Assert.NotNull(stored.CompletedUtc);
        }
    }
}
