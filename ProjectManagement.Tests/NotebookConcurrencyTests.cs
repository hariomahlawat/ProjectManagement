using ProjectManagement.Controllers.Api;
using ProjectManagement.Services.Notebook;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;

namespace ProjectManagement.Tests;

public sealed class NotebookConcurrencyTests
{
    [Fact]
    public async Task Notebook_filter_maps_stale_version_to_conflict_with_current_version()
    {
        // SECTION: API concurrency response regression guard
        var currentVersion = Guid.NewGuid();
        var filter = new NotebookApiExceptionFilter();
        var context = new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = new NotebookConcurrencyException(Guid.NewGuid(), Guid.NewGuid(), currentVersion)
        };

        await filter.OnExceptionAsync(context);

        var result = Assert.IsType<ConflictObjectResult>(context.Result);
        Assert.True(context.ExceptionHandled);
        Assert.Contains(currentVersion.ToString("N"), result.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
