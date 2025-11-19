using System;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

internal static class FfcBreadcrumbs
{
    private const string Key = "Breadcrumb";

    // SECTION: Public helpers
    public static void Set(ViewDataDictionary viewData, params (string Text, string? Url)[] items)
    {
        if (viewData is null)
        {
            throw new ArgumentNullException(nameof(viewData));
        }

        viewData[Key] = new BreadcrumbModel(items);
    }
}
