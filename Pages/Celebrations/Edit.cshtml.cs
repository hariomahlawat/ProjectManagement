using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Pages.Celebrations
{
    [Authorize(Roles="Admin,TA")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;

        public EditModel(ApplicationDbContext db, UserManager<ApplicationUser> users)
        {
            _db = db; _users = users;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            public Guid? Id { get; set; }
            [Required]
            public CelebrationType EventType { get; set; }
            [Required, StringLength(120)]
            public string Name { get; set; } = string.Empty;
            [StringLength(120)]
            [Display(Name = "Spouse Name (Optional)")]
            public string? SpouseName { get; set; }
            [Range(1,31)]
            public byte Day { get; set; }
            [Range(1,12)]
            public byte Month { get; set; }
            public short? Year { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(Guid? id)
        {
            if (id != null)
            {
                var c = await _db.Celebrations.FirstOrDefaultAsync(x => x.Id == id && x.DeletedUtc == null);
                if (c == null) return RedirectToPage("Index");
                Input = new InputModel
                {
                    Id = c.Id,
                    EventType = c.EventType,
                    Name = c.Name,
                    SpouseName = c.SpouseName,
                    Day = c.Day,
                    Month = c.Month,
                    Year = c.Year
                };
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }
            var max = DateTime.DaysInMonth(Input.Year ?? 2000, Input.Month);
            if (Input.Day > max) ModelState.AddModelError(string.Empty, "Invalid date");
            if (!ModelState.IsValid) return Page();

            Celebration entity;
            if (Input.Id == null)
            {
                entity = new Celebration
                {
                    Id = Guid.NewGuid(),
                    CreatedById = _users.GetUserId(User)!,
                    CreatedUtc = DateTimeOffset.UtcNow,
                };
                _db.Celebrations.Add(entity);
            }
            else
            {
                entity = await _db.Celebrations.FirstAsync(x => x.Id == Input.Id);
            }

            entity.EventType = Input.EventType;
            entity.Name = Input.Name.Trim();
            entity.SpouseName = string.IsNullOrWhiteSpace(Input.SpouseName) ? null : Input.SpouseName.Trim();
            entity.Day = Input.Day;
            entity.Month = Input.Month;
            entity.Year = Input.Year;
            entity.UpdatedUtc = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync();
            return RedirectToPage("Index");
        }
    }
}
