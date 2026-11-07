using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Navigation.ModuleNav;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Program.cs only requires sign-in for the whole Admin area (AuthorizeAreaFolder("Admin", "/")),
/// so a new admin page that forgets its [Authorize(Policy = ...)] would open to every signed-in
/// user. This guard fails the build instead.
/// </summary>
public sealed class AdminPagePolicyGuardTests
{
    // Namespaces that hold administration pages, inside and outside the Admin area.
    private static readonly string[] AdminNamespaces =
    {
        "ProjectManagement.Areas.Admin.Pages",
        "ProjectManagement.Pages.Admin",
        "ProjectManagement.Pages.Settings.Holidays",
        "ProjectManagement.Pages.Celebrations",
        "ProjectManagement.Pages.Usage",
    };

    // Catalog entries whose page is gated differently from its navigation entry, mapped to what
    // the page requires. Keep each one explained.
    private static readonly IReadOnlyDictionary<string, string> CatalogPolicyExceptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The social media lists belong to Project Office Reports and gate on the Admin and HoD
            // roles, not an admin policy; the catalog policy only decides who sees the link in
            // Administration. Converging them would change who can open the page, so it is out of
            // scope for a guard test.
            [AdminNavigationKeys.SocialMediaTypes] = "Roles: Admin,HoD",
            [AdminNavigationKeys.SocialMediaPlatforms] = "Roles: Admin,HoD",
            // Repository deletion requests are decided in the Decision Centre (Admin and HoD).
            // The old Document Repository address already redirected there, so linking to it
            // directly changes nothing about who can open it.
            [AdminNavigationKeys.DocumentDeleteRequests] = "Roles: Admin,HoD",
        };

    public static IEnumerable<object[]> AdminPageModels() =>
        AdminPageModelTypes().Select(type => new object[] { type.FullName! });

    [Fact]
    public void AdminNamespaces_ContainPages()
    {
        // Guards the guard: if a namespace is renamed, the test below would silently check nothing.
        foreach (var ns in AdminNamespaces)
        {
            Assert.True(
                AdminPageModelTypes().Any(type => IsInNamespace(type, ns)),
                $"No page models found under {ns}; update {nameof(AdminNamespaces)}.");
        }
    }

    [Theory]
    [MemberData(nameof(AdminPageModels))]
    public void EveryAdminPage_RequiresAPolicyOrRole(string typeName)
    {
        var type = typeof(Program).Assembly.GetType(typeName, throwOnError: true)!;

        Assert.Empty(type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));

        var authorize = type.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToArray();
        Assert.True(
            authorize.Any(attribute => !string.IsNullOrWhiteSpace(attribute.Policy) || !string.IsNullOrWhiteSpace(attribute.Roles)),
            $"{typeName} needs [Authorize(Policy = ...)]: the Admin area convention only requires sign-in.");

        foreach (var handler in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            Assert.False(
                handler.IsDefined(typeof(AllowAnonymousAttribute), inherit: true),
                $"{typeName}.{handler.Name} must not allow anonymous access.");
        }
    }

    [Fact]
    public void EveryCatalogEntry_UsesTheSamePolicyAsItsPage()
    {
        var root = RepositoryRoot();
        var failures = new List<string>();

        foreach (var entry in AdminNavigationCatalog.Entries)
        {
            var area = entry.Item.Area ?? string.Empty;
            var pageType = FindPageModel(root, area, entry.Item.Page!);
            if (pageType is null)
            {
                failures.Add($"{entry.Key}: could not find the page model for {area}{entry.Item.Page}.");
                continue;
            }

            var authorize = pageType.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToArray();
            var pagePolicies = authorize
                .Select(attribute => attribute.Policy)
                .Where(policy => !string.IsNullOrWhiteSpace(policy))
                .Concat(authorize
                    .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Roles))
                    .Select(attribute => "Roles: " + attribute.Roles))
                .ToArray();

            var expected = CatalogPolicyExceptions.TryGetValue(entry.Key, out var exception)
                ? exception
                : entry.Item.AuthorizationPolicy;

            if (!pagePolicies.Contains(expected, StringComparer.Ordinal))
            {
                failures.Add(
                    $"{entry.Key}: the catalog shows it to '{entry.Item.AuthorizationPolicy}' but {pageType.Name} requires " +
                    $"'{string.Join("', '", pagePolicies)}'.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static IEnumerable<Type> AdminPageModelTypes() =>
        typeof(Program).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(PageModel).IsAssignableFrom(type))
            .Where(type => AdminNamespaces.Any(ns => IsInNamespace(type, ns)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

    private static bool IsInNamespace(Type type, string ns) =>
        type.Namespace is { } name && (name == ns || name.StartsWith(ns + ".", StringComparison.Ordinal));

    // Resolves a catalog destination to its PageModel through the page's @model directive, so the
    // check does not depend on namespaces matching folders.
    private static Type? FindPageModel(string root, string area, string page)
    {
        var pagesFolder = string.IsNullOrEmpty(area)
            ? Path.Combine(root, "Pages")
            : Path.Combine(root, "Areas", area, "Pages");
        var file = Path.Combine(pagesFolder, page.TrimStart('/').Replace('/', Path.DirectorySeparatorChar) + ".cshtml");
        if (!File.Exists(file)) return null;

        var match = Regex.Match(File.ReadAllText(file), @"^\s*@model\s+([\w\.]+)", RegexOptions.Multiline);
        if (!match.Success) return null;

        var modelName = match.Groups[1].Value;
        var expectedNamespace = string.IsNullOrEmpty(area)
            ? "ProjectManagement.Pages." + string.Join('.', SplitFolders(page))
            : $"ProjectManagement.Areas.{area}.Pages." + string.Join('.', SplitFolders(page));
        expectedNamespace = expectedNamespace.TrimEnd('.');

        var candidates = typeof(Program).Assembly.GetTypes()
            .Where(type => typeof(PageModel).IsAssignableFrom(type))
            .Where(type => type.FullName == modelName || type.FullName!.EndsWith("." + modelName, StringComparison.Ordinal))
            .ToArray();

        return candidates.Length == 1
            ? candidates[0]
            : candidates.FirstOrDefault(type => type.Namespace == expectedNamespace);
    }

    private static IEnumerable<string> SplitFolders(string page)
    {
        var parts = page.Trim('/').Split('/');
        return parts.Take(parts.Length - 1);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
