using Xunit;

namespace ProjectManagement.Tests;

public sealed class AuthenticationLandingRouteTests
{
    [Fact]
    public void Login_ConvertsResolvedRazorPageNameToBrowserUrl()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Areas", "Identity", "Pages", "Account", "Login.cshtml.cs"));

        Assert.Contains("Url.Page(landingPage)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Url.Content(await _landingPageResolver.ResolveAsync(user))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordChange_ReturnsUserToRoleSpecificLandingPage()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Areas", "Identity", "Pages", "Account", "Manage", "ChangePassword.cshtml.cs"));

        Assert.Contains("_landingPageResolver.ResolveAsync(user)", source, StringComparison.Ordinal);
        Assert.Contains("RedirectToPage(landingPage)", source, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Project root could not be located.");
    }
}
