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


    [Fact]
    public void FinalPolish_UsesProfileImageTerminology_AndMakesCurrentStateScannable()
    {
        var root = FindProjectRoot();
        var account = File.ReadAllText(Path.Combine(root, "Areas", "Identity", "Pages", "Account", "Manage", "Index.cshtml"));
        var accountModel = File.ReadAllText(Path.Combine(root, "Areas", "Identity", "Pages", "Account", "Manage", "Index.cshtml.cs"));
        var details = File.ReadAllText(Path.Combine(root, "Pages", "Photos", "People", "Details.cshtml"));
        var service = File.ReadAllText(Path.Combine(root, "Features", "MediaLibrary", "Services", "MediaPersonUserLinkService.cs"));
        var siteCss = File.ReadAllText(Path.Combine(root, "wwwroot", "css", "site.css"));
        var peopleCss = File.ReadAllText(Path.Combine(root, "wwwroot", "css", "pages", "photos-reference-readiness.css"));

        Assert.Contains("PRISM profile image", account, StringComparison.Ordinal);
        Assert.Contains("PRISM profile image", details, StringComparison.Ordinal);
        Assert.DoesNotContain("PRISM avatar", account, StringComparison.Ordinal);
        Assert.DoesNotContain("PRISM avatar", accountModel, StringComparison.Ordinal);
        Assert.DoesNotContain("PRISM avatar", details, StringComparison.Ordinal);
        Assert.DoesNotContain("PRISM avatar", service, StringComparison.Ordinal);

        Assert.Contains("Photos portrait in use", details, StringComparison.Ordinal);
        Assert.Contains("Initials in use", details, StringComparison.Ordinal);
        Assert.Contains("ShouldUsePortraitAsAvatar", details, StringComparison.Ordinal);
        Assert.Contains("person-account-link__profile-state", details, StringComparison.Ordinal);
        Assert.Contains(".person-account-link__current .person-account-link__profile-state.is-photo", peopleCss, StringComparison.Ordinal);
        Assert.Contains("Choose or prepare a trusted matching reference below.", details, StringComparison.Ordinal);

        Assert.Contains("account-photo-avatar-setting__use-initials", account, StringComparison.Ordinal);
        Assert.Contains(".account-photo-avatar-setting__use-initials", siteCss, StringComparison.Ordinal);
        Assert.Contains("height: 36px;", siteCss, StringComparison.Ordinal);
        Assert.Contains("width: 36px;", siteCss, StringComparison.Ordinal);
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
