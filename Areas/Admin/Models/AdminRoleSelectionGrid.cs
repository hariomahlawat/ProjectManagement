using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Models;

public sealed record AdminRoleSelectionGridModel
{
    public IReadOnlyList<AdminRoleDescriptor> Roles { get; init; } =
        Array.Empty<AdminRoleDescriptor>();

    public IReadOnlyCollection<string> SelectedRoles { get; init; } =
        Array.Empty<string>();

    public string InputName { get; init; } = "Input.Roles";

    public string IdPrefix { get; init; } = "admin-role";
}
