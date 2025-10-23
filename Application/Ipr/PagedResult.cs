using System.Collections.Generic;

namespace ProjectManagement.Application.Ipr;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
