using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services
{
    public class NoteService : INoteService
    {
        private readonly ApplicationDbContext _db;
        private readonly IAuditService _audit;

        public NoteService(ApplicationDbContext db, IAuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        public async Task<IList<Note>> ListForTodoAsync(string ownerId, Guid todoId)
        {
            return await _db.Notes.AsNoTracking()
                .Where(n => n.OwnerId == ownerId && n.TodoId == todoId && n.DeletedUtc == null)
                .OrderBy(n => n.CreatedUtc)
                .ToListAsync();
        }

        public async Task<IList<Note>> ListStandaloneAsync(string ownerId)
        {
            return await _db.Notes.AsNoTracking()
                .Where(n => n.OwnerId == ownerId && n.TodoId == null && n.DeletedUtc == null)
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.CreatedUtc)
                .ToListAsync();
        }

        public async Task<Note> CreateAsync(string ownerId, Guid? todoId, string title, string? body)
        {
            var note = new Note
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                TodoId = todoId,
                Title = title,
                Body = body,
                IsPinned = false,
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            _db.Notes.Add(note);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Note.Create", userId: ownerId,
                data: new Dictionary<string, string?> { ["Id"] = note.Id.ToString(), ["TodoId"] = todoId?.ToString() });
            return note;
        }

        public async Task<bool> EditAsync(string ownerId, Guid id, string? title = null, string? body = null, bool? pinned = null)
        {
            var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.OwnerId == ownerId && n.DeletedUtc == null);
            if (note == null) return false;
            if (title != null) note.Title = title;
            if (body != null) note.Body = body;
            if (pinned.HasValue) note.IsPinned = pinned.Value;
            note.UpdatedUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Note.Update", userId: ownerId,
                data: new Dictionary<string, string?> { ["Id"] = id.ToString() });
            return true;
        }

        public async Task<bool> DeleteAsync(string ownerId, Guid id)
        {
            var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.OwnerId == ownerId && n.DeletedUtc == null);
            if (note == null) return false;
            note.DeletedUtc = DateTimeOffset.UtcNow;
            note.UpdatedUtc = note.DeletedUtc.Value;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Note.Delete", userId: ownerId,
                data: new Dictionary<string, string?> { ["Id"] = id.ToString() });
            return true;
        }
    }
}
