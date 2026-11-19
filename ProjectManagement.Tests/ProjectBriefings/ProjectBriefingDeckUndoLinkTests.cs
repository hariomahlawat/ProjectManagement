using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectManagement.Data;
using ProjectManagement.Pages.Workspace.BriefingDecks;
using ProjectManagement.Tests.Infrastructure;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// The delete toast offers Undo only when its link is a local URL (Views/Shared/_Toasts.cshtml).
// The handler tests stub the URL helper, so this checks the link against the app's real routing:
// the earlier link (with deckId = null) came out empty when the delete was posted from
// /Workspace/BriefingDecks, from the library and from the Studio alike (cold review, 19 Nov 2026).
public sealed class ProjectBriefingDeckUndoLinkTests : IClassFixture<ProjectBriefingDeckUndoLinkTests.Factory>
{
    private readonly Factory _factory;

    public ProjectBriefingDeckUndoLinkTests(Factory factory) => _factory = factory;

    [Theory]
    [InlineData(null)]
    [InlineData(7L)]
    public void TheUndoLink_IsALocalRestoreUrl_WhereverTheDeleteWasPostedFrom(long? routeDeckId)
    {
        var links = _factory.Services.GetRequiredService<LinkGenerator>();
        var request = new DefaultHttpContext { RequestServices = _factory.Services };
        request.Request.RouteValues["page"] = IndexModel.RestorePage;
        if (routeDeckId is { } id)
        {
            request.Request.RouteValues["deckId"] = id;
        }

        var url = links.GetPathByPage(request, IndexModel.RestorePage, IndexModel.RestoreHandler);

        Assert.False(string.IsNullOrEmpty(url));
        Assert.StartsWith("/Workspace/BriefingDecks", url, StringComparison.Ordinal);
        Assert.Contains("handler=Restore", url, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEarlierLink_WithAnExplicitNullDeckId_CameOutEmpty()
    {
        // Pins the cause, so the handler does not go back to passing deckId = null.
        var links = _factory.Services.GetRequiredService<LinkGenerator>();
        var request = new DefaultHttpContext { RequestServices = _factory.Services };
        request.Request.RouteValues["page"] = IndexModel.RestorePage;

        Assert.Null(links.GetPathByPage(request, IndexModel.RestorePage, IndexModel.RestoreHandler, new { deckId = (long?)null }));
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UsePrismTestInfrastructure("briefingundolink");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase($"briefing-undo-link-{Guid.NewGuid()}"));
            });
        }
    }
}
