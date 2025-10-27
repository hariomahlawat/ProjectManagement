using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;

public sealed class MiscActivityIndexViewModel
{
    public MiscActivityIndexFilterViewModel Filter { get; init; } = new();

    public IReadOnlyList<MiscActivityListItemViewModel> Activities { get; init; } = Array.Empty<MiscActivityListItemViewModel>();

    public MiscActivityIndexPaginationViewModel Pagination { get; init; } = new();
}
