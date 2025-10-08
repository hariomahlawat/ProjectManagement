using System;

namespace ProjectManagement.ViewModels;

public sealed class ProjectRolesViewModel
{
    public static readonly ProjectRolesViewModel Empty = new();

    public bool IsAdmin { get; init; }
    public bool IsHoD { get; init; }
    public bool IsProjectOfficer { get; init; }
    public bool IsAssignedProjectOfficer { get; init; }
    public bool IsAssignedHoD { get; init; }
}
