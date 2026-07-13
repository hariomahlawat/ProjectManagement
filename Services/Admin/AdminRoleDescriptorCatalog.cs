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
                "Full control of system administration, security, recovery and controlled configuration.",
                "System control",
                "bi-shield-lock",
                privileged: true,
                sortOrder: 10),
            Descriptor(
                RoleNames.Comdt,
                "Commandant",
                "Command-level visibility and decision authority across authorised operational workspaces.",
                "Command oversight",
                "bi-stars",
                privileged: true,
                sortOrder: 20),
            Descriptor(
                RoleNames.HoD,
                "Head of Department",
                "Department-level oversight, approvals and administrative access within authorised modules.",
                "Command oversight",
                "bi-person-check",
                privileged: true,
                sortOrder: 30),
            Descriptor(
                RoleNames.ProjectOfficer,
                "Project Officer",
                "Creates, updates and manages assigned projects and associated development activities.",
                "Project delivery",
                "bi-kanban",
                privileged: false,
                sortOrder: 100),
            Descriptor(
                RoleNames.ProjectOffice,
                "Project Office",
                "Supports project records, reporting, documentation and project-office administration.",
                "Project delivery",
                "bi-briefcase",
                privileged: false,
                sortOrder: 110),
            Descriptor(
                RoleNames.ProjectOfficeAlternate,
                "Project Office",
                "Compatibility role for project-office administration in installations using the legacy role name.",
                "Project delivery",
                "bi-briefcase",
                privileged: false,
                sortOrder: 111),
            Descriptor(
                RoleNames.Mco,
                "MCO",
                "Monitoring and coordination access for authorised project and reporting workflows.",
                "Technical & coordination",
                "bi-diagram-2",
                privileged: false,
                sortOrder: 200),
            Descriptor(
                RoleNames.Ta,
                "Technical Assistant",
                "Technical coordination and authorised metadata or workflow support functions.",
                "Technical & coordination",
                "bi-tools",
                privileged: false,
                sortOrder: 210),
            Descriptor(
                RoleNames.Ito,
                "ITO",
                "Information-technology operations and authorised technical administration functions.",
                "Technical & coordination",
                "bi-pc-display-horizontal",
                privileged: false,
                sortOrder: 220),
            Descriptor(
                RoleNames.MainOfficeClerk,
                "Main Office Clerk",
                "Clerical access for authorised main-office records and document workflows.",
                "Office support",
                "bi-building",
                privileged: false,
                sortOrder: 300),
            Descriptor(
                RoleNames.MainOfficeAlternate,
                "Main Office",
                "Compatibility role for authorised main-office workflows using the legacy display name.",
                "Office support",
                "bi-building",
                privileged: false,
                sortOrder: 301),
            Descriptor(
                RoleNames.McCellClerk,
                "MC Cell Clerk",
                "Clerical access for authorised MC Cell records and document workflows.",
                "Office support",
                "bi-folder2-open",
                privileged: false,
                sortOrder: 310),
            Descriptor(
                RoleNames.ItCellClerk,
                "IT Cell Clerk",
                "Clerical access for authorised IT Cell records and document workflows.",
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
