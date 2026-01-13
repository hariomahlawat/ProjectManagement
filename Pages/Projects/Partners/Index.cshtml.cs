using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models.Partners;
using ProjectManagement.ViewModels.Partners;

namespace ProjectManagement.Pages.Projects.Partners;

[Authorize(Policy = Policies.Partners.View)]
public class IndexModel : PageModel
{
    // SECTION: Constants
    private const int DefaultPageSize = 20;

    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    // SECTION: Query parameters
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string[] Statuses { get; set; } = Array.Empty<string>();

    [BindProperty(SupportsGet = true)]
    public string? PartnerType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? City { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    // SECTION: View state
    public IReadOnlyList<IndustryPartnerSummaryVm> Items { get; private set; } = Array.Empty<IndustryPartnerSummaryVm>();

    public IReadOnlyList<string> StatusOptions => IndustryPartnerStatuses.All;

    public IReadOnlyList<string> PartnerTypeOptions => IndustryPartnerTypes.All;

    public IReadOnlyList<string> CityOptions { get; private set; } = Array.Empty<string>();

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Search) ||
        Statuses.Length > 0 ||
        !string.IsNullOrWhiteSpace(PartnerType) ||
        !string.IsNullOrWhiteSpace(City);

    public async Task OnGetAsync(CancellationToken ct)
    {
        // SECTION: Base query
        var query = _db.IndustryPartners
            .AsNoTracking()
            .AsQueryable();

        // SECTION: Search
        var trimmed = Search?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            var like = $"%{trimmed}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.FirmName, like) ||
                (p.City != null && EF.Functions.ILike(p.City, like)) ||
                p.Contacts.Any(c =>
                    EF.Functions.ILike(c.Name, like) ||
                    (c.Email != null && EF.Functions.ILike(c.Email, like)) ||
                    (c.Designation != null && EF.Functions.ILike(c.Designation, like)) ||
                    c.Phones.Any(phone => EF.Functions.ILike(phone.PhoneNumber, like))));
        }

        // SECTION: Filters
        if (Statuses.Length > 0)
        {
            query = query.Where(p => Statuses.Contains(p.Status));
        }

        if (!string.IsNullOrWhiteSpace(PartnerType))
        {
            query = query.Where(p => p.PartnerType == PartnerType);
        }

        if (!string.IsNullOrWhiteSpace(City))
        {
            query = query.Where(p => p.City == City);
        }

        // SECTION: Pagination
        TotalCount = await query.CountAsync(ct);
        TotalPages = (int)Math.Ceiling(TotalCount / (double)DefaultPageSize);
        if (PageIndex < 1)
        {
            PageIndex = 1;
        }
        else if (TotalPages > 0 && PageIndex > TotalPages)
        {
            PageIndex = TotalPages;
        }

        // SECTION: Lookup data
        CityOptions = await _db.IndustryPartners
            .AsNoTracking()
            .Where(p => p.City != null && p.City != string.Empty)
            .Select(p => p.City!)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync(ct);

        // SECTION: List projection
        Items = await query
            .OrderBy(p => p.FirmName)
            .Skip((PageIndex - 1) * DefaultPageSize)
            .Take(DefaultPageSize)
            .Select(p => new IndustryPartnerSummaryVm(
                p.Id,
                p.FirmName,
                p.Status,
                p.PartnerType,
                p.City,
                p.Contacts.OrderByDescending(c => c.IsPrimary).Select(c => c.Name).FirstOrDefault(),
                p.Contacts.OrderByDescending(c => c.IsPrimary).Select(c => c.Designation).FirstOrDefault(),
                p.Contacts.OrderByDescending(c => c.IsPrimary).Select(c => c.Email).FirstOrDefault(),
                p.Contacts.OrderByDescending(c => c.IsPrimary)
                    .SelectMany(c => c.Phones)
                    .Select(phone => phone.PhoneNumber)
                    .FirstOrDefault(),
                p.ProjectLinks.Count))
            .ToListAsync(ct);
    }
}
