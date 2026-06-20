using Microsoft.AspNetCore.Routing;
using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Services.Notebook;

// SECTION: Centralized notebook card render model creation
public sealed class NotebookCardModelFactory : INotebookCardModelFactory
{
    private readonly LinkGenerator _linkGenerator;

    public NotebookCardModelFactory(LinkGenerator linkGenerator)
    {
        _linkGenerator = linkGenerator;
    }

    public NotebookCardRenderVm Create(NotebookItemListVm item, NotebookCardContext context)
    {
        var view = string.IsNullOrWhiteSpace(context.View) ? "home" : context.View;
        var routeValues = new RouteValueDictionary
        {
            ["view"] = view,
            ["note"] = item.Id
        };

        AddOptionalRouteValue(routeValues, "query", context.Query);
        AddOptionalRouteValue(routeValues, "filter", context.Filter);
        AddOptionalRouteValue(routeValues, "tag", context.Tag);

        return new NotebookCardRenderVm
        {
            Item = item,
            View = view,
            Query = context.Query,
            Filter = context.Filter,
            Tag = context.Tag,
            SelectedId = context.SelectedId,
            OpenUrl = _linkGenerator.GetPathByPage("/Notebook/Index", values: routeValues)
                ?? $"/Notebook?view={Uri.EscapeDataString(view)}&note={Uri.EscapeDataString(item.Id.ToString())}",
            Actions = new NotebookCardActionsVm
            {
                Item = item,
                View = view,
                Query = context.Query,
                Filter = context.Filter,
                Tag = context.Tag,
                SelectedId = context.SelectedId
            }
        };
    }

    // SECTION: Route value helpers
    private static void AddOptionalRouteValue(RouteValueDictionary values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value;
        }
    }
}
