using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Helpers;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Pages.Celebrations
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private static readonly TimeZoneInfo Ist = IstClock.TimeZone;

        public IndexModel(ApplicationDbContext db) { _db = db; }

        public record Row(Guid Id, CelebrationType EventType, string Name, DateOnly NextOccurrence, int DaysAway);
        public Row[] Items { get; set; } = Array.Empty<Row>();

        [BindProperty(SupportsGet = true)] public string Type { get; set; } = "all"; // all|birthday|anniversary
        [BindProperty(SupportsGet = true)] public string Window { get; set; } = "all"; // today|7|15|30|all
        [BindProperty(SupportsGet = true)] public string? Q { get; set; }

        public bool CanEdit => User.IsInRole("Admin") || User.IsInRole("TA");

        public async Task OnGetAsync()
        {
            var q = _db.Celebrations.AsNoTracking().Where(x => x.DeletedUtc == null);

            if (Type == "birthday") q = q.Where(x => x.EventType == CelebrationType.Birthday);
            else if (Type == "anniversary") q = q.Where(x => x.EventType == CelebrationType.Anniversary);

            if (!string.IsNullOrWhiteSpace(Q))
            {
                var s = Q.Trim();
                q = q.Where(x => EF.Functions.ILike(x.Name, $"%{s}%") || (x.PartnerName != null && EF.Functions.ILike(x.PartnerName, $"%{s}%")));
            }

            var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
            var today = DateOnly.FromDateTime(nowLocal.DateTime);

            var list = await q.ToListAsync();
            var rows = new List<Row>();
            foreach (var c in list)
            {
                var next = CelebrationHelpers.NextOccurrenceLocal(c, today);
                var days = CelebrationHelpers.DaysAway(today, next);
                rows.Add(new Row(c.Id, c.EventType, CelebrationHelpers.DisplayName(c), next, days));
            }

            rows = Window switch
            {
                "today" => rows.Where(r => r.DaysAway == 0).ToList(),
                "7"     => rows.Where(r => r.DaysAway < 7).ToList(),
                "15"    => rows.Where(r => r.DaysAway < 15).ToList(),
                "30"    => rows.Where(r => r.DaysAway < 30).ToList(),
                _       => rows
            };

            Items = rows.OrderBy(r => r.NextOccurrence).ThenBy(r => r.Name).ToArray();
        }

        public async Task<IActionResult> OnPostDeleteAsync(Guid id)
        {
            if (!CanEdit) return Unauthorized();
            var c = await _db.Celebrations.FirstOrDefaultAsync(x => x.Id == id && x.DeletedUtc == null);
            if (c == null) return RedirectToPage();
            c.DeletedUtc = DateTimeOffset.UtcNow;
            c.UpdatedUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
            return RedirectToPage(new { Type, Window, Q });
        }
    }
}
