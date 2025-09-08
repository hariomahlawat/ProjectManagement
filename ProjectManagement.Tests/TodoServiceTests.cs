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

        [Fact]
        public async Task CreateSetsOrderIndex()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);
            var a = await service.CreateAsync("alice", "A");
            var b = await service.CreateAsync("alice", "B");
            Assert.Equal(0, a.OrderIndex);
            Assert.Equal(1, b.OrderIndex);
        }

        [Fact]
        public async Task IstToUtcConversion()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);
            var local = new DateTimeOffset(2023, 1, 1, 14, 0, 0, TimeSpan.FromHours(5.5));
            var item = await service.CreateAsync("alice", "Task", dueAtLocal: local);
            var stored = await context.TodoItems.FindAsync(item.Id);
            Assert.Equal(new DateTimeOffset(2023, 1, 1, 8, 30, 0, TimeSpan.Zero), stored!.DueAtUtc);
        }

        [Fact]
        public async Task EditUpdatesFields()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);
            var item = await service.CreateAsync("alice", "Old");
            var dueLocal = new DateTimeOffset(2023, 2, 1, 10, 0, 0, TimeSpan.FromHours(5.5));
            await service.EditAsync("alice", item.Id, title: "New", priority: TodoPriority.High, dueAtLocal: dueLocal, pinned: true);
            var stored = await context.TodoItems.FindAsync(item.Id);
            Assert.Equal("New", stored!.Title);
            Assert.Equal(TodoPriority.High, stored.Priority);
            Assert.Equal(new DateTimeOffset(2023, 2, 1, 4, 30, 0, TimeSpan.Zero), stored.DueAtUtc);
            Assert.True(stored.IsPinned);
        }

        [Fact]
        public async Task ReorderRespectsOwnership()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);
            var a = await service.CreateAsync("alice", "A1");
            var b = await service.CreateAsync("bob", "B1");
            var ok = await service.ReorderAsync("alice", new List<Guid> { a.Id, b.Id });
            Assert.False(ok);
        }

        [Fact]
        public async Task ToggleDoneTwice()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);
            var item = await service.CreateAsync("alice", "Task");
            await service.ToggleDoneAsync("alice", item.Id, true);
            var mid = await context.TodoItems.FindAsync(item.Id);
            Assert.Equal(TodoStatus.Done, mid!.Status);
            Assert.NotNull(mid.CompletedUtc);
            await service.ToggleDoneAsync("alice", item.Id, false);
            var stored = await context.TodoItems.FindAsync(item.Id);
            Assert.Equal(TodoStatus.Open, stored!.Status);
            Assert.Null(stored.CompletedUtc);
        }
    }
}
