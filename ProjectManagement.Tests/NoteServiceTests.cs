using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests
{
    public class NoteServiceTests
    {
        private class FakeAudit : IAuditService
        {
            public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, Microsoft.AspNetCore.Http.HttpContext? http = null)
                => Task.CompletedTask;
        }

        private ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task CreateAndList()
        {
            using var db = CreateContext();
            var svc = new NoteService(db, new FakeAudit());
            var n = await svc.CreateAsync("alice", null, "hello", "body");
            var list = await svc.ListStandaloneAsync("alice");
            Assert.Single(list);
            Assert.Equal("hello", list[0].Title);
        }

        [Fact]
        public async Task EditAndDelete()
        {
            using var db = CreateContext();
            var svc = new NoteService(db, new FakeAudit());
            var n = await svc.CreateAsync("alice", null, "t", null);
            var ok = await svc.EditAsync("alice", n.Id, title: "new");
            Assert.True(ok);
            ok = await svc.DeleteAsync("alice", n.Id);
            Assert.True(ok);
            var list = await svc.ListStandaloneAsync("alice");
            Assert.Empty(list);
        }
    }
}
