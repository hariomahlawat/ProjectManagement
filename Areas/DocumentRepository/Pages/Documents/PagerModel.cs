using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents;

public record PagerModel(int Page, int PageSize, int TotalCount, HttpRequest Request)
{
    public int PageCount => (int)Math.Ceiling((double)TotalCount / PageSize);

    public string UrlForPage(int newPage)
    {
        var query = QueryHelpers.ParseQuery(Request.QueryString.ToString())
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        query["page"] = Math.Max(1, newPage).ToString();
        query["pageSize"] = PageSize.ToString();

        var q = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var path = Request.Path.HasValue ? Request.Path.Value : string.Empty;
        return string.IsNullOrEmpty(q) ? path : $"{path}?{q}";
    }
}
