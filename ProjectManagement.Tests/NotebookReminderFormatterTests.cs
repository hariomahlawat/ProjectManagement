using ProjectManagement.Services.Notebook;

namespace ProjectManagement.Tests;

public sealed class NotebookReminderFormatterTests
{
    // Wednesday 21 Oct 2026, 10:00 IST.
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 4, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("2026-10-21T17:00:00+05:30", "Today, 17:00")]
    [InlineData("2026-10-22T09:00:00+05:30", "Tomorrow, 09:00")]
    [InlineData("2026-10-20T10:00:00+05:30", "Yesterday, 10:00")]
    [InlineData("2026-10-26T09:00:00+05:30", "Mon, 09:00")]
    [InlineData("2026-11-04T09:00:00+05:30", "4 Nov, 09:00")]
    [InlineData("2027-01-15T09:00:00+05:30", "15 Jan 2027, 09:00")]
    [InlineData("2026-10-21T23:59:00+05:30", "Today, 23:59")]
    public void Relative_reminder_text_uses_the_IST_day(string reminder, string expected)
    {
        Assert.Equal(expected, NotebookReminderFormatter.FormatReminder(DateTimeOffset.Parse(reminder), Now));
    }

    [Fact]
    public void Day_boundary_follows_IST_not_UTC()
    {
        // 20:00 UTC on the 21st is already 01:30 IST on the 22nd.
        Assert.Equal("Tomorrow, 01:30", NotebookReminderFormatter.FormatReminder(new DateTimeOffset(2026, 10, 21, 20, 0, 0, TimeSpan.Zero), Now));
    }

    [Fact]
    public void Missing_reminder_has_no_text()
    {
        Assert.Null(NotebookReminderFormatter.FormatReminder(null, Now));
    }
}
