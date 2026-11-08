using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Services.Approvals;
using ProjectManagement.Services.Navigation.ModuleNav;
using ProjectManagement.ViewModels;
using Xunit;
using DecisionCentrePage = ProjectManagement.Pages.Approvals.Pending.IndexModel;

namespace ProjectManagement.Tests;

/// <summary>
/// Third admin review, owner decision 2, option A, PR 14 (docs/ux/admin-review-3-fixes.md):
/// the Document Repository trash renders inside Administration, and the Decision Centre names
/// "Document deletion approvals" when Administration opens it.
/// </summary>
public sealed class AdminRepositoryTrashInShellTests
{
    private static readonly IQueryCollection NoQuery = new QueryCollection();

    [Fact]
    public void RepositoryTrash_RendersInsideTheAdminShell()
    {
        var entry = AdminNavigationCatalog.GetEntry(AdminNavigationKeys.RepositoryTrash);

        Assert.True(entry.UsesAdminShell);
        Assert.Equal(AdminNavigationKeys.RecoveryCentre, entry.SidebarParent);
        Assert.Same(entry, AdminNavigationCatalog.FindActiveEntry("DocumentRepository", "/Admin/Trash/Index", NoQuery));
        Assert.True(AdminNavigationCatalog.IsInScope("DocumentRepository", "/Admin/Trash/Index", null, null, NoQuery));
        // Same eyebrow as the other kinds of deleted item.
        Assert.Equal(AdminNavigationGroups.RestoreArchive, AdminNavigationCatalog.EyebrowFor("DocumentRepository", "/Admin/Trash/Index", NoQuery, null));
        // The rest of the Document Repository keeps its own layout.
        Assert.False(AdminNavigationCatalog.IsInScope("DocumentRepository", "/Documents/Index", null, null, NoQuery));
    }

    [Fact]
    public void TrashPage_UsesTheAdminHeaderAndTabs_AndKeepsItsActions()
    {
        var view = File.ReadAllText(Path.Combine(RepositoryRoot(), "Areas", "DocumentRepository", "Pages", "Admin", "Trash", "Index.cshtml"));

        Assert.Contains("_AdminPageHeader.cshtml", view, StringComparison.Ordinal);
        Assert.Contains("AdminRestoreTabKeys.RepositoryDocuments", view, StringComparison.Ordinal);
        Assert.DoesNotContain("library-head", view, StringComparison.Ordinal);
        // Paging, search, Restore and Delete for good are unchanged.
        Assert.Contains("asp-page-handler=\"Restore\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"Purge\"", view, StringComparison.Ordinal);
        Assert.Contains("data-confirm-action=\"Delete for good\"", view, StringComparison.Ordinal);
        Assert.Contains("name=\"q\"", view, StringComparison.Ordinal);
        Assert.Contains("Model.PageUrl(", view, StringComparison.Ordinal);
        Assert.Contains("Model.SizeUrl(", view, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentDeletionApprovals_StaysInTheDecisionCentre_WithAReturnBarToAdministration()
    {
        var entry = AdminNavigationCatalog.GetEntry(AdminNavigationKeys.DocumentDeleteRequests);

        // Not in the admin shell, so the layout shows the "Administration ›" return bar.
        Assert.False(entry.UsesAdminShell);
        Assert.Equal("Document deletion approvals", entry.Item.Text);
    }

    [Theory]
    [InlineData("RepositoryDocumentDelete")]
    [InlineData("repositorydocumentdelete")]
    public void DecisionCentre_FilteredToRepositoryDeletions_NamesThem(string type)
    {
        var page = new DecisionCentrePage(new NoApprovals(), ProjectManagement.Tests.Fakes.FakeClock.AtUtc(DateTimeOffset.UtcNow)) { Type = type };

        Assert.Equal("Document deletion approvals", page.Heading.Title);
        Assert.Equal("Decision Centre", page.Heading.Eyebrow);
        Assert.Equal(AdminNavigationCatalog.GetEntry(AdminNavigationKeys.DocumentDeleteRequests).Item.Text, page.Heading.Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ActivityDelete")]
    [InlineData("not-a-type")]
    public void DecisionCentre_OtherwiseKeepsItsOwnHeading(string? type)
    {
        var page = new DecisionCentrePage(new NoApprovals(), ProjectManagement.Tests.Fakes.FakeClock.AtUtc(DateTimeOffset.UtcNow)) { Type = type };

        Assert.Equal("Decision Centre", page.Heading.Title);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    private sealed class NoApprovals : IApprovalQueueService
    {
        public Task<IReadOnlyList<ApprovalQueueItemVm>> GetPendingAsync(ApprovalQueueQuery query, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ApprovalQueueItemVm>>(Array.Empty<ApprovalQueueItemVm>());

        public Task<int> GetPendingCountAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<ApprovalQueueDetailVm?> GetDetailAsync(ApprovalQueueType type, string requestId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => Task.FromResult<ApprovalQueueDetailVm?>(null);
    }
}
