using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Helpers;

namespace ProjectManagement.Areas.Admin.Pages.Categories
{
    internal static class CategorySelectListBuilder
    {
        public static async Task<List<SelectListItem>> BuildAsync(ApplicationDbContext db, int? selectedId = null, int? excludeId = null)
        {
            return await CategoryHierarchyBuilder.BuildSelectListAsync(
                db.ProjectCategories,
                selectedId,
                excludeId,
                c => c.Id,
                c => c.ParentId,
                c => c.SortOrder,
                c => c.Name);
        }
    }
}
