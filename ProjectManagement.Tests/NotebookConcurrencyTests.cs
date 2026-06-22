using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Controllers.Api;
using ProjectManagement.Services.Notebook;
using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Tests;

public sealed class NotebookConcurrencyTests
{
    [Fact]
    public async Task Notebook_filter_maps_stale_version_to_conflict_with_current_item()
    {
        // SECTION: API concurrency response regression guard
        var itemId = Guid.NewGuid();
        var currentVersion = Guid.NewGuid();
        var currentItem = new NotebookItemDetailVm
        {
            Id = itemId,
            Title = "Authoritative title",
            BodyMarkdown = "Authoritative body",
            Version = currentVersion
        };

        var filter = new NotebookApiExceptionFilter();
        var context = new ExceptionContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            [])
        {
            Exception = new NotebookConcurrencyException(
                itemId,
                Guid.NewGuid(),
                currentVersion,
                currentItem)
        };

        await filter.OnExceptionAsync(context);

        var result = Assert.IsType<ConflictObjectResult>(context.Result);
        Assert.True(context.ExceptionHandled);

        var json = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("notebook_concurrency_conflict", root.GetProperty("code").GetString());
        Assert.Equal(currentVersion.ToString("N"), root.GetProperty("currentVersion").GetString());

        var responseItem = root.GetProperty("currentItem");
        Assert.Equal(itemId, responseItem.GetProperty("id").GetGuid());
        Assert.Equal("Authoritative title", responseItem.GetProperty("title").GetString());
        Assert.Equal(currentVersion, responseItem.GetProperty("version").GetGuid());
    }
}
