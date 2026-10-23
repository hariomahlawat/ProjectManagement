using Xunit;

namespace ProjectManagement.Tests.Workspace;

/// <summary>
/// Each officer on the Officer workload view can be messaged from the card: the name carries
/// data-chat-user, so the chat scripts add a chat button after it (not on the viewer's own card).
/// </summary>
public sealed class OfficerWorkloadCardContractTests
{
    [Fact]
    public void OfficerName_OffersChat_AndKeepsTheButtonVisible()
    {
        var card = ReadRepoFile("Pages", "Workspace", "_OfficerWorkloadCard.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "command-workspace.css");

        Assert.Contains("data-chat-user=\"@(string.IsNullOrWhiteSpace(officer.UserId) ? null : officer.UserId)\"", card, StringComparison.Ordinal);
        Assert.Contains("data-chat-name=\"@displayName\"", card, StringComparison.Ordinal);
        Assert.Contains("<span class=\"cw-officer-name__text\">@displayName</span>", card, StringComparison.Ordinal);

        // A long name truncates on its own; the chat button after it does not.
        Assert.Contains(".cw-officer-name__text { min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }", css, StringComparison.Ordinal);
        Assert.Contains(".cw-officer-name .pm-chat-person { flex:0 0 auto; }", css, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return File.ReadAllText(Path.Combine(new[] { current.FullName }.Concat(segments).ToArray()));
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }
}
