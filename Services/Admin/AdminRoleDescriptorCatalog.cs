using ProjectManagement.Configuration;

namespace ProjectManagement.Services.Admin;

public sealed record AdminRoleDescriptor(
    string Name,
    string DisplayName,
    string Description,
    string Category,
    string Icon,
    bool IsPrivileged,
    int SortOrder);

public interface IAdminRoleDescriptorCatalog
{
    AdminRoleDescriptor Describe(string roleName);

    IReadOnlyList<AdminRoleDescriptor> DescribeMany(IEnumerable<string> roleNames);
}

public sealed class AdminRoleDescriptorCatalog : IAdminRoleDescriptorCatalog
{
    private static readonly IReadOnlyDictionary<string, AdminRoleDescriptor> KnownRoles =
        BuildKnownRoles();

    public AdminRoleDescriptor Describe(string roleName)
    {
        var normalized = roleName?.Trim() ?? string.Empty;
        if (KnownRoles.TryGetValue(normalized, out var descriptor))
        {
            return descriptor with { Name = normalized };
        }

        return new AdminRoleDescriptor(
            normalized,
            string.IsNullOrWhiteSpace(normalized) ? "Unnamed role" : normalized,
            "Provides the permissions configured for this Identity role.",
            "Other access",
            "bi-person-badge",
            IsPrivileged: false,
            SortOrder: 900);
    }

    public IReadOnlyList<AdminRoleDescriptor> DescribeMany(IEnumerable<string> roleNames) =>
        (roleNames ?? Enumerable.Empty<string>())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Describe)
            .OrderBy(role => role.SortOrder)
            .ThenBy(role => role.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyDictionary<string, AdminRoleDescriptor> BuildKnownRoles()
    {
        var roles = new[]
        {
            Descriptor(
                RoleNames.Admin,
                "Administrator",
                "Runs PRISM: accounts and roles, restoring deleted items, lists used in forms and system checks.",
                "System control",
                "bi-shield-lock",
                privileged: true,
                sortOrder: 10),
            Descriptor(
                RoleNames.Comdt,
                "Commandant",
                "Sees every project and module, and takes the decisions that need the Commandant.",
                "Command oversight",
                "bi-stars",
                privileged: true,
                sortOrder: 20),
            Descriptor(
                RoleNames.HoD,
                "Head of Department",
                "Oversees the department, approves requests and looks after some lists and media.",
                "Command oversight",
                "bi-person-check",
                privileged: true,
                sortOrder: 30),
            Descriptor(
                RoleNames.ProjectOfficer,
                "Project Officer",
                "Runs their own projects: stages, dates, documents and progress.",
                "Project delivery",
                "bi-kanban",
                privileged: false,
                sortOrder: 100),
            Descriptor(
                RoleNames.ProjectOffice,
                "Project Office",
                "Keeps project records, reports and documents up to date for the project office.",
                "Project delivery",
                "bi-briefcase",
                privileged: false,
                sortOrder: 110),
            Descriptor(
                RoleNames.ProjectOfficeAlternate,
                "Project Office (old role name)",
                "The same as Project Office, under the role's old name. Some older accounts still use it.",
                "Project delivery",
                "bi-briefcase",
                privileged: false,
                sortOrder: 111),
            Descriptor(
                RoleNames.Mco,
                "MCO",
                "Follows projects and reports to keep work coordinated.",
                "Technical & coordination",
                "bi-diagram-2",
                privileged: false,
                sortOrder: 200),
            Descriptor(
                RoleNames.Ta,
                "TA",
                "Helps with coordination, document details and day-to-day records.",
                "Coordination",
                "bi-person-badge",
                privileged: false,
                sortOrder: 210),
            Descriptor(
                RoleNames.Ito,
                "ITO",
                "IT Officer: publishes the Brochure and Compendium and keeps the FFC projects up to date. Changing overall status and progress in the FFC detailed table is left to Admin, HoD and Commandant.",
                "Technical & coordination",
                "bi-pc-display-horizontal",
                privileged: false,
                sortOrder: 220),
            Descriptor(
                RoleNames.MainOfficeClerk,
                "Main Office Clerk",
                "Keeps the main office's records and documents.",
                "Office support",
                "bi-building",
                privileged: false,
                sortOrder: 300),
            Descriptor(
                RoleNames.MainOfficeAlternate,
                "Main Office Clerk (old role name)",
                "The same as Main Office Clerk, under the role's old name. Some older accounts still use it.",
                "Office support",
                "bi-building",
                privileged: false,
                sortOrder: 301),
            Descriptor(
                RoleNames.McCellClerk,
                "MC Cell Clerk",
                "Keeps the MC Cell's records and documents.",
                "Office support",
                "bi-folder2-open",
                privileged: false,
                sortOrder: 310),
            Descriptor(
                RoleNames.ItCellClerk,
                "IT Cell Clerk",
                "Keeps the IT Cell's records and documents.",
                "Office support",
                "bi-folder2-open",
                privileged: false,
                sortOrder: 320)
        };

        return roles.ToDictionary(role => role.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static AdminRoleDescriptor Descriptor(
        string name,
        string displayName,
        string description,
        string category,
        string icon,
        bool privileged,
        int sortOrder) =>
        new(name, displayName, description, category, icon, privileged, sortOrder);
}
