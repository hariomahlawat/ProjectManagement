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
    public const string CardPartialPath = "~/Pages/Notebook/_NotebookCard.cshtml";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IModelMetadataProvider _metadataProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IRazorViewEngine _viewEngine;

    public RazorNotebookCardRenderer(
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IModelMetadataProvider metadataProvider,
        IHttpContextAccessor httpContextAccessor,
        IServiceProvider serviceProvider)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _metadataProvider = metadataProvider;
        _httpContextAccessor = httpContextAccessor;
        _serviceProvider = serviceProvider;
    }

    public async Task<string> RenderAsync(NotebookCardRenderVm model, CancellationToken ct = default)
    {
        // SECTION: Rendering context setup
        ct.ThrowIfCancellationRequested();
        var httpContext = _httpContextAccessor.HttpContext ?? new DefaultHttpContext();
        httpContext.RequestServices ??= _serviceProvider;

        var actionContext = new ActionContext(
            httpContext,
            httpContext.GetRouteData() ?? new RouteData(),
            new ActionDescriptor());

        // SECTION: Partial view lookup
        var viewResult = _viewEngine.GetView(
            executingFilePath: CardPartialPath,
            viewPath: CardPartialPath,
            isMainPage: false);

        if (!viewResult.Success)
        {
            throw new InvalidOperationException(
                "Notebook card partial could not be located. " +
                string.Join(Environment.NewLine, viewResult.SearchedLocations));
        }

        // SECTION: Partial rendering
        await using var writer = new StringWriter();
        var viewData = new ViewDataDictionary<NotebookCardRenderVm>(_metadataProvider, new ModelStateDictionary())
        {
            Model = model
        };
        var tempData = new TempDataDictionary(httpContext, _tempDataProvider);
        var viewContext = new ViewContext(actionContext, viewResult.View, viewData, tempData, writer, new HtmlHelperOptions());
        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }
}
