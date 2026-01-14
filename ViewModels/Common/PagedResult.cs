using System.Collections.Generic;

namespace ProjectManagement.ViewModels.Common
{
    public sealed class PagedResult<T>
    {
        // Section: Result data
        public IReadOnlyList<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
    }
}
