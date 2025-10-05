using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public IndexModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public IList<Project> Projects { get; private set; } = new List<Project>();

        [BindProperty(SupportsGet = true)]
        public string? Query { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? CategoryId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? LeadPoUserId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? HodUserId { get; set; }

        public IEnumerable<SelectListItem> CategoryOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> LeadPoOptions { get; private set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> HodOptions { get; private set; } = Array.Empty<SelectListItem>();

        public async Task OnGetAsync()
        {
            await LoadFilterOptionsAsync();

            var query = _db.Projects
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.HodUser)
                .Include(p => p.LeadPoUser)
                .OrderByDescending(p => p.CreatedAt)
                .AsQueryable();

            var filters = new ProjectSearchFilters(Query, CategoryId, LeadPoUserId, HodUserId);
            query = query.ApplyProjectSearch(filters);

            Projects = await query.Take(100).ToListAsync();
        }

        private async Task LoadFilterOptionsAsync()
        {
            var categories = await _db.ProjectCategories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryOption(c.Id, c.Name))
                .ToListAsync();

            CategoryOptions = BuildCategoryOptions(categories, CategoryId);

            var hodUsers = await _db.Projects
                .AsNoTracking()
                .Where(p => p.HodUserId != null)
                .Select(p => new UserOption(
                    p.HodUserId!,
                    p.HodUser != null ? p.HodUser.FullName : null,
                    p.HodUser != null ? p.HodUser.UserName : null))
                .ToListAsync();

            HodOptions = BuildUserOptions(hodUsers, HodUserId, "Any HoD");

            var leadPoUsers = await _db.Projects
                .AsNoTracking()
                .Where(p => p.LeadPoUserId != null)
                .Select(p => new UserOption(
                    p.LeadPoUserId!,
                    p.LeadPoUser != null ? p.LeadPoUser.FullName : null,
                    p.LeadPoUser != null ? p.LeadPoUser.UserName : null))
                .ToListAsync();

            LeadPoOptions = BuildUserOptions(leadPoUsers, LeadPoUserId, "Any Project Officer");
        }

        private static IEnumerable<SelectListItem> BuildCategoryOptions(IEnumerable<CategoryOption> categories, int? selectedId)
        {
            var options = new List<SelectListItem>
            {
                new("All categories", string.Empty, !selectedId.HasValue)
            };

            var selectedValue = selectedId?.ToString();
            options.AddRange(categories.Select(c => new SelectListItem(c.Name, c.Id.ToString())
            {
                Selected = selectedValue is not null && string.Equals(selectedValue, c.Id.ToString(), StringComparison.Ordinal)
            }));

            return options;
        }

        private static IEnumerable<SelectListItem> BuildUserOptions(IEnumerable<UserOption> users, string? selectedId, string emptyLabel)
        {
            var options = new List<SelectListItem>
            {
                new(emptyLabel, string.Empty, string.IsNullOrWhiteSpace(selectedId))
            };

            var uniqueUsers = users
                .Where(u => !string.IsNullOrWhiteSpace(u.Id))
                .GroupBy(u => u.Id, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(u => DisplayName(u), StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => DisplayName(u));

            foreach (var user in uniqueUsers)
            {
                var selected = selectedId is not null && string.Equals(user.Id, selectedId, StringComparison.Ordinal);
                options.Add(new SelectListItem(DisplayName(user), user.Id, selected));
            }

            return options;
        }

        private static string DisplayName(UserOption option)
        {
            if (!string.IsNullOrWhiteSpace(option.FullName))
            {
                return option.FullName!;
            }

            if (!string.IsNullOrWhiteSpace(option.UserName))
            {
                return option.UserName!;
            }

            return option.Id;
        }

        private sealed record UserOption(string Id, string? FullName, string? UserName);

        private sealed record CategoryOption(int Id, string Name);
    }
}
