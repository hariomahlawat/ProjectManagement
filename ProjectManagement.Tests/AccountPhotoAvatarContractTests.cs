using Xunit;

namespace ProjectManagement.Tests;

public sealed class AccountPhotoAvatarContractTests
{
    [Fact]
    public void AccountProfile_UsesExplicitAvatarCommands_NotAClientSuppliedBooleanToggle()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Areas", "Identity", "Pages", "Account", "Manage", "Index.cshtml"));
        var model = File.ReadAllText(Path.Combine(root, "Areas", "Identity", "Pages", "Account", "Manage", "Index.cshtml.cs"));

        Assert.Contains("asp-page-handler=\"UsePhotosPortrait\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"UseInitials\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page-handler=\"PhotoAvatar\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"usePhotosPortrait\"", page, StringComparison.Ordinal);

        Assert.Contains("OnPostUsePhotosPortraitAsync()", model, StringComparison.Ordinal);
        Assert.Contains("OnPostUseInitialsAsync()", model, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPostPhotoAvatarAsync", model, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderAndProfile_ConsumeTheSameResolvedPortraitPresentationState()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Areas", "Identity", "Pages", "Account", "Manage", "Index.cshtml"));
        var login = File.ReadAllText(Path.Combine(root, "Pages", "Shared", "_LoginPartial.cshtml"));
        var service = File.ReadAllText(Path.Combine(root, "Features", "MediaLibrary", "Services", "MediaPersonUserLinkService.cs"));

        Assert.Contains("ShouldUsePortraitAsAvatar", page, StringComparison.Ordinal);
        Assert.Contains("ShouldUsePortraitAsAvatar", login, StringComparison.Ordinal);
        Assert.Contains("public bool ShouldUsePortraitAsAvatar =>", service, StringComparison.Ordinal);
        Assert.Contains("Photos avatar preference persistence verification failed", service, StringComparison.Ordinal);
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
