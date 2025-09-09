using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services
{
    public interface INoteService
    {
        Task<IList<Note>> ListForTodoAsync(string ownerId, Guid todoId);
        Task<IList<Note>> ListStandaloneAsync(string ownerId);
        Task<Note> CreateAsync(string ownerId, Guid? todoId, string title, string? body);
        Task<bool> EditAsync(string ownerId, Guid id, string? title = null, string? body = null, bool? pinned = null);
        Task<bool> DeleteAsync(string ownerId, Guid id);
    }
}
