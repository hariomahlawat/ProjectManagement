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

        private ApplicationDbContext CreateContext(string? name = null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
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
        public async Task NotesPersist()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);
            var item = await service.CreateAsync("alice", "Task", notes: "first\nsecond");
            var stored = await context.TodoItems.FindAsync(item.Id);
            Assert.Equal("first\nsecond", stored!.Notes);
        }

        [Fact]
        public async Task ReorderSkipsCompleted()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);
            var a = await service.CreateAsync("alice", "A1");
            var b = await service.CreateAsync("alice", "A2");
            await service.ToggleDoneAsync("alice", b.Id, true);
            var ok = await service.ReorderAsync("alice", new List<Guid> { b.Id, a.Id });
            Assert.False(ok);
        }

        [Fact]
        public async Task SnoozePresets()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);
            var item = await service.CreateAsync("alice", "Task");

            var todayPm = TodayAt(18, 0);
            await service.EditAsync("alice", item.Id, dueAtLocal: todayPm);
            var stored1 = await context.TodoItems.FindAsync(item.Id);
            Assert.Equal(TodayAt(18,0).ToUniversalTime(), stored1!.DueAtUtc);

            var tomAm = TodayAt(10,0).AddDays(1);
            await service.EditAsync("alice", item.Id, dueAtLocal: tomAm);
            var stored2 = await context.TodoItems.FindAsync(item.Id);
            Assert.Equal(tomAm.ToUniversalTime(), stored2!.DueAtUtc);

            var nextMon = NextMondayAt(10,0);
            await service.EditAsync("alice", item.Id, dueAtLocal: nextMon);
            var stored3 = await context.TodoItems.FindAsync(item.Id);
            Assert.Equal(nextMon.ToUniversalTime(), stored3!.DueAtUtc);
        }

        [Fact]
        public async Task DeleteSoftDeletesCompleted()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);
            var item = await service.CreateAsync("alice", "Task");
            await service.ToggleDoneAsync("alice", item.Id, true);
            await service.DeleteAsync("alice", item.Id);
            var stored = await context.TodoItems.FirstOrDefaultAsync(x => x.Id == item.Id);
            Assert.NotNull(stored);
            Assert.NotNull(stored!.DeletedUtc);
        }

        [Fact]
        public async Task ClearCompletedRemovesAllDone()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);
            var a = await service.CreateAsync("alice", "A1");
            var b = await service.CreateAsync("alice", "A2");
            await service.ToggleDoneAsync("alice", a.Id, true);
            await service.ToggleDoneAsync("alice", b.Id, true);
            var cleared = await service.ClearCompletedAsync("alice");
            Assert.Equal(2, cleared);
            var sa = await context.TodoItems.FindAsync(a.Id);
            var sb = await context.TodoItems.FindAsync(b.Id);
            Assert.NotNull(sa!.DeletedUtc);
            Assert.NotNull(sb!.DeletedUtc);
        }

        private static DateTimeOffset TodayAt(int h, int m)
        {
            var ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
            return new DateTimeOffset(nowIst.Year, nowIst.Month, nowIst.Day, h, m, 0, nowIst.Offset);
        }

        private static DateTimeOffset NextMondayAt(int h, int m)
        {
            var ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
            int daysToMon = ((int)DayOfWeek.Monday - (int)nowIst.DayOfWeek + 7) % 7;
            if (daysToMon == 0) daysToMon = 7;
            var next = nowIst.Date.AddDays(daysToMon).AddHours(h).AddMinutes(m);
            return new DateTimeOffset(next, nowIst.Offset);
        }

        [Fact(Skip = "InMemory provider does not enforce concurrency tokens")]
        public async Task ConcurrencyConflictThrows()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context1 = CreateContext(dbName);
            using var context2 = CreateContext(dbName);
            var item = new TodoItem { Id = Guid.NewGuid(), OwnerId = "alice", Title = "Task" };
            context1.TodoItems.Add(item);
            await context1.SaveChangesAsync();

            var entity1 = await context1.TodoItems.FindAsync(item.Id);
            var entity2 = await context2.TodoItems.FindAsync(item.Id);

            entity1!.Title = "first";
            await context1.SaveChangesAsync();

            entity2!.Title = "second";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context2.SaveChangesAsync());
        }

        [Fact]
        public async Task IdorPrevention()
        {
            using var context = CreateContext();
            var audit = new FakeAudit();
            var service = new TodoService(context, audit);
            var item = await service.CreateAsync("alice", "Task");
            var ok = await service.EditAsync("bob", item.Id, title: "Hack");
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
