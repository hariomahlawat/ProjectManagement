using System;
using System.IO;
using ProjectManagement.Contracts.Notifications;
using ProjectManagement.Infrastructure;
using ProjectManagement.ViewModels.Notifications;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class NotificationIstDisplayTests
{
    [Fact]
    public void ToIst_ConvertsUtcToIndianStandardTime()
    {
        // SECTION: Arrange
        var utc = new DateTime(2026, 4, 28, 2, 8, 0, DateTimeKind.Utc);

        // SECTION: Act
        var result = TimeFmt.ToIst(utc);

        // SECTION: Assert
        Assert.Equal("28 Apr 2026, 07:38 IST", result);
    }

    [Fact]
    public void FromContract_UsesCreatedDisplayIstFromNotificationPayload()
    {
        // SECTION: Arrange
        var utc = new DateTime(2026, 4, 28, 2, 8, 0, DateTimeKind.Utc);
        var notification = new NotificationListItem(
            42,
            "Projects",
            "Updated",
            "Project",
            "7",
            7,
            "IST Migration",
            "actor-user-id",
            "/Projects/Details/7",
            "Project updated",
            "A project notification was updated.",
            utc,
            TimeFmt.ToIst(utc),
            null,
            null,
            false);

        // SECTION: Act
        var display = NotificationDisplayModel.FromContract(notification);

        // SECTION: Assert
        Assert.Equal(utc, display.CreatedUtc);
        Assert.Equal("28 Apr 2026, 07:38 IST", display.CreatedDisplayIst);
    }

    [Fact]
    public void NotificationUi_UsesServerProvidedIstDisplayText()
    {
        // SECTION: Arrange
        var bellMarkup = ReadRepoFile("Pages", "Shared", "Components", "NotificationBell", "Default.cshtml");
        var centerMarkup = ReadRepoFile("Pages", "Notifications", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "notifications.js");

        // SECTION: Assert
        Assert.Contains("@item.CreatedDisplayIst", bellMarkup, StringComparison.Ordinal);
        Assert.Contains("@item.CreatedDisplayIst", centerMarkup, StringComparison.Ordinal);
        Assert.Contains("createdDisplayIst", script, StringComparison.Ordinal);
        Assert.Contains("timeZone: 'Asia/Kolkata'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ToLocalTime", bellMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("ToLocalTime", centerMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("toLocaleString(undefined", script, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativePathParts)
    {
        // SECTION: Source assertions locate files from either repository-root or test-output working directories.
        var relativePath = Path.Combine(relativePathParts);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file {relativePath}.", relativePath);
    }
}
