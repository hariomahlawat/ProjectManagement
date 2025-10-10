using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Areas.Admin.Pages.Calendar
{
    [Authorize(Roles = "Admin")]
    public class DeletedModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private static readonly TimeZoneInfo IST = IstClock.TimeZone;

        public DeletedModel(ApplicationDbContext db) => _db = db;

        public List<EventVM> Events { get; set; } = new();

        public class EventVM
        {
            public Guid Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Start { get; set; } = string.Empty;
        }

        public async Task OnGetAsync()
        {
            var deletedEvents = await _db.Events.AsNoTracking()
                .Where(e => e.IsDeleted)
                .OrderByDescending(e => e.UpdatedAt)
                .Select(e => new { e.Id, e.Title, e.StartUtc })
                .ToListAsync();

            Events = deletedEvents
                .Select(e => new EventVM
                {
                    Id = e.Id,
                    Title = e.Title,
                    Start = TimeZoneInfo.ConvertTime(e.StartUtc, IST).ToString("dd MMM yyyy")
                })
                .ToList();
        }

        public async Task<IActionResult> OnPostRestoreAsync(Guid id)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted);
            if (ev == null) return NotFound();
            ev.IsDeleted = false;
            await _db.SaveChangesAsync();
            return RedirectToPage();
        }
    }
}

