using System;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests
{
    public class TodoQuickParserTests
    {
        [Theory]
        [InlineData("Pay rent tomorrow !high", "Pay rent", TodoPriority.High, 1)]
        [InlineData("Call mom !low", "Call mom", TodoPriority.Low, 0)]
        public void ParsesTokens(string input, string expectedTitle, TodoPriority expectedPrio, int daysAhead)
        {
            TodoQuickParser.Parse(input, out var title, out var due, out var prio);
            Assert.Equal(expectedTitle, title);
            Assert.Equal(expectedPrio, prio);
            if (daysAhead == 0)
            {
                Assert.Null(due);
            }
            else
            {
                var ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
                var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist).Date;
                Assert.Equal(today.AddDays(daysAhead), due?.Date);
            }
        }

        [Fact]
        public void ParsesNextMonday()
        {
            TodoQuickParser.Parse("Report next mon", out var title, out var due, out var prio);
            Assert.Equal("Report", title);
            Assert.Equal(TodoPriority.Normal, prio);
            Assert.NotNull(due);
            Assert.Equal(DayOfWeek.Monday, due!.Value.DayOfWeek);
            Assert.True(due.Value.Date > DateTimeOffset.UtcNow.Date);
        }
    }
}
