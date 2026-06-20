using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Services.Notebook;

// SECTION: Razor partial based notebook card renderer
public sealed class RazorNotebookCardRenderer : INotebookCardRenderer
{
    private const string CardPartialPath = "~/Pages/Notebook/_NotebookCard.cshtml";
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IRazorViewEngine _viewEngine;

    public RazorNotebookCardRenderer(IRazorViewEngine viewEngine, ITempDataProvider tempDataProvider, IHttpContextAccessor httpContextAccessor)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<string> RenderAsync(NotebookItemListVm item, string view, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var httpContext = _httpContextAccessor.HttpContext ?? new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, httpContext.GetRouteData() ?? new RouteData(), new ActionDescriptor());
        var viewResult = _viewEngine.GetView(executingFilePath: null, viewPath: CardPartialPath, isMainPage: false);
        if (!viewResult.Success)
        {
            throw new InvalidOperationException($"Notebook card partial '{CardPartialPath}' could not be found.");
        }

        await using var writer = new StringWriter();
        var model = new NotebookCardRenderVm { Item = item, View = string.IsNullOrWhiteSpace(view) ? "home" : view };
        var viewData = new ViewDataDictionary<NotebookCardRenderVm>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        var tempData = new TempDataDictionary(httpContext, _tempDataProvider);
        var viewContext = new ViewContext(actionContext, viewResult.View, viewData, tempData, writer, new HtmlHelperOptions());
        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }
}
