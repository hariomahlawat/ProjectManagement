using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Notebook;
using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Tests;

public sealed class NotebookCardRendererTests : IClassFixture<NotebookCardRendererTests.NotebookRendererFactory>
{
    private readonly NotebookRendererFactory _factory;

    public NotebookCardRendererTests(NotebookRendererFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RenderAsync_returns_valid_note_card_html()
    {
        // SECTION: Arrange hosted Razor card renderer dependencies
        await using var scope = _factory.Services.CreateAsyncScope();
        var renderer = scope.ServiceProvider.GetRequiredService<INotebookCardRenderer>();
        var modelFactory = scope.ServiceProvider.GetRequiredService<INotebookCardModelFactory>();
        var item = CreateNote();
        var model = modelFactory.Create(item, new NotebookCardContext { View = NotebookCardContexts.Home });

        // SECTION: Act
        var html = await renderer.RenderAsync(model, CancellationToken.None);

        // SECTION: Assert card essentials
        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Contains($"data-note-id=\"{item.Id}\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-version=\"{item.Version}\"", html, StringComparison.Ordinal);
        Assert.Contains("data-action=\"open-note\"", html, StringComparison.Ordinal);
        Assert.Contains("data-action=\"pin-note\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_returns_valid_checklist_card_html()
    {
        // SECTION: Arrange checklist card
        await using var scope = _factory.Services.CreateAsyncScope();
        var renderer = scope.ServiceProvider.GetRequiredService<INotebookCardRenderer>();
        var modelFactory = scope.ServiceProvider.GetRequiredService<INotebookCardModelFactory>();
        var item = CreateChecklist();
        var model = modelFactory.Create(item, new NotebookCardContext { View = NotebookCardContexts.Home });

        // SECTION: Act
        var html = await renderer.RenderAsync(model, CancellationToken.None);

        // SECTION: Assert checklist-specific rendering
        Assert.Contains("First checklist row", html, StringComparison.Ordinal);
        Assert.Contains("Second checklist row", html, StringComparison.Ordinal);
        Assert.Contains("data-action=\"toggle-checklist\"", html, StringComparison.Ordinal);
        Assert.Contains("1/2", html, StringComparison.Ordinal);
        Assert.Contains($"data-version=\"{item.Version}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_returns_partial_card_without_layout_markup()
    {
        // SECTION: Arrange pinned card
        await using var scope = _factory.Services.CreateAsyncScope();
        var renderer = scope.ServiceProvider.GetRequiredService<INotebookCardRenderer>();
        var modelFactory = scope.ServiceProvider.GetRequiredService<INotebookCardModelFactory>();
        var item = CreateNote(isPinned: true);
        var model = modelFactory.Create(item, new NotebookCardContext { View = "unsupported-view" });

        // SECTION: Act
        var html = await renderer.RenderAsync(model, CancellationToken.None);

        // SECTION: Assert partial isolation and nested actions
        Assert.Contains("data-is-pinned=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Unpin note\"", html, StringComparison.Ordinal);
        Assert.Contains("data-action=\"archive-note\"", html, StringComparison.Ordinal);
        Assert.Contains("data-action=\"duplicate-note\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<head", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<body", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRISM ERP", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(html, "data-note-id=\""));
    }

    // SECTION: Test host setup
    public sealed class NotebookRendererFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // SECTION: Keep partial-render tests independent from configured PostgreSQL startup
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase($"notebook-renderer-{Guid.NewGuid()}"));
            });
        }
    }

    private static NotebookItemListVm CreateNote(bool isPinned = false) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Renderer note",
        Preview = "Renderer body",
        Type = NotebookItemType.Note,
        Status = NotebookItemStatus.Active,
        Priority = NotebookPriority.Normal,
        IsPinned = isPinned,
        ColorKey = "white",
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        Version = Guid.NewGuid()
    };

    private static NotebookItemListVm CreateChecklist() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Renderer checklist",
        Type = NotebookItemType.Checklist,
        Status = NotebookItemStatus.Active,
        Priority = NotebookPriority.Normal,
        ColorKey = "white",
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        ChecklistTotal = 2,
        ChecklistDone = 1,
        ChecklistPreviewItems = new[]
        {
            new NotebookChecklistItemVm { Id = 1, Text = "First checklist row", IsDone = true, SortOrder = 1 },
            new NotebookChecklistItemVm { Id = 2, Text = "Second checklist row", IsDone = false, SortOrder = 2 }
        },
        Version = Guid.NewGuid()
    };

    private static int CountOccurrences(string value, string token)
    {
        // SECTION: Simple literal occurrence count
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
