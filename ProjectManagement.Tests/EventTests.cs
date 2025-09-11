using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests
{
    public class EventTests
    {
        [Fact]
        public void AllDayEndIsExclusive()
        {
            var startDate = new DateTime(2024, 4, 3);
            var endDate = new DateTime(2024, 4, 5);

            var startUtc = new DateTimeOffset(startDate, TimeSpan.Zero);
            var endUtc = new DateTimeOffset(endDate, TimeSpan.Zero).AddDays(1);

            Assert.Equal(new DateTimeOffset(2024, 4, 6, 0, 0, 0, TimeSpan.Zero), endUtc);
            Assert.True(endUtc > startUtc);
        }

        [Fact]
        public void OverlapQueryMatchesCorrectly()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var db = new ApplicationDbContext(options);
            db.Events.Add(new Event { Id = Guid.NewGuid(), Title = "A", StartUtc = new DateTime(2024,1,1,9,0,0,DateTimeKind.Utc), EndUtc = new DateTime(2024,1,1,10,0,0,DateTimeKind.Utc) });
            db.Events.Add(new Event { Id = Guid.NewGuid(), Title = "B", StartUtc = new DateTime(2024,1,1,10,0,0,DateTimeKind.Utc), EndUtc = new DateTime(2024,1,1,11,0,0,DateTimeKind.Utc) });
            db.Events.Add(new Event { Id = Guid.NewGuid(), Title = "C", StartUtc = new DateTime(2024,1,2,0,0,0,DateTimeKind.Utc), EndUtc = new DateTime(2024,1,3,0,0,0,DateTimeKind.Utc) });
            db.SaveChanges();

            var rangeStart = new DateTime(2024,1,1,10,0,0,DateTimeKind.Utc);
            var rangeEnd = new DateTime(2024,1,1,12,0,0,DateTimeKind.Utc);

            var hits = db.Events.Where(e => e.EndUtc > rangeStart && e.StartUtc < rangeEnd).ToList();

            Assert.Single(hits);
            Assert.Equal("B", hits[0].Title);
        }
    }
}

