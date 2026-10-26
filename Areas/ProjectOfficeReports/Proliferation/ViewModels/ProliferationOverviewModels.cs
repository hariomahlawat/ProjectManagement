using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
