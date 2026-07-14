using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Configuration;

namespace ProjectManagement.Services.Admin;

public enum AdminCapabilityRisk
{
    Low = 0,
    Moderate = 1,
    High = 2,
    Critical = 3
}

public sealed record AdminCapabilityDescriptor(
    string Key,
    string Title,
    string Description,
    string Policy,
    IReadOnlyList<string> PermittedRoles,
    AdminCapabilityRisk Risk,
    string Icon,
    bool IsRegisteredByCatalog,
    IReadOnlyList<AdminCapabilityRoute> Routes);

public sealed record AdminCapabilityRoute(string Area, string Page, string Label);

public interface IAdminCapabilityCatalog
{
    IReadOnlyList<AdminCapabilityDescriptor> Capabilities { get; }
    AdminCapabilityDescriptor? FindByPolicy(string policy);
    IReadOnlyList<AdminCapabilityDescriptor> ForRole(string roleName);
}

public sealed class AdminCapabilityCatalog : IAdminCapabilityCatalog
{
    private static readonly IReadOnlyList<AdminCapabilityDescriptor> Items = Build();

    public IReadOnlyList<AdminCapabilityDescriptor> Capabilities => Items;

    public AdminCapabilityDescriptor? FindByPolicy(string policy) =>
        Items.FirstOrDefault(item => string.Equals(item.Policy, policy, StringComparison.Ordinal));

    public IReadOnlyList<AdminCapabilityDescriptor> ForRole(string roleName) =>
        Items.Where(item => item.PermittedRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Risk)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static void RegisterPolicies(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var capability in Items.Where(item => item.IsRegisteredByCatalog))
        {
            options.AddPolicy(capability.Policy, policy => policy.RequireRole(capability.PermittedRoles.ToArray()));
        }
    }

    private static IReadOnlyList<AdminCapabilityDescriptor> Build()
    {
        var admin = new[] { RoleNames.Admin };
        var adminAndHod = new[] { RoleNames.Admin, RoleNames.HoD };

        return new[]
        {
            Capability("administration-access", "Access Administration", "Open the Administration workspace and its operational overview.", AdminPolicies.Access, admin, AdminCapabilityRisk.High, "bi-shield-lock", Route("Admin", "/Index", "Administration overview")),
            Capability("users-manage", "Manage Users", "Create accounts, assign roles and control account lifecycle state.", AdminPolicies.UsersManage, admin, AdminCapabilityRisk.Critical, "bi-people", Route("Admin", "/Users/Index", "Users")),
            Capability("access-governance", "Review Access Governance", "Review privileged users, role holdings and policy coverage.", AdminPolicies.AccessGovernanceView, admin, AdminCapabilityRisk.Critical, "bi-shield-check", Route("Admin", "/AccessGovernance/Index", "Access governance")),
            Capability("security-view", "Review Login and System Health", "Review authentication activity and application readiness indicators.", AdminPolicies.SecurityView, admin, AdminCapabilityRisk.High, "bi-activity", Route("Admin", "/Analytics/Logins", "Login activity"), Route("Admin", "/Diagnostics/DbHealth", "System health")),
            Capability("logs-view", "Review Audit Logs", "Trace administrative, security and operational events.", AdminPolicies.LogsView, admin, AdminCapabilityRisk.High, "bi-journal-text", Route("Admin", "/Logs/Index", "Audit logs")),
            Capability("recovery-manage", "Manage Recovery", "Restore deleted records and conduct retention-controlled permanent deletion.", AdminPolicies.RecoveryManage, admin, AdminCapabilityRisk.Critical, "bi-arrow-counterclockwise", Route("Admin", "/Recovery/Index", "Recovery centre")),
            Capability("master-data-manage", "Manage Master Data", "Maintain project taxonomies and controlled reference lists.", AdminPolicies.MasterDataManage, admin, AdminCapabilityRisk.High, "bi-sliders2", Route("Admin", "/MasterData/Index", "Master data")),
            Capability("integrity-manage", "Review Configuration Integrity", "Assess and repair deterministic master-data integrity findings.", AdminPolicies.IntegrityManage, admin, AdminCapabilityRisk.Critical, "bi-clipboard2-check", Route("Admin", "/MasterData/Integrity/Index", "Configuration integrity")),
            Capability("activity-types-manage", "Manage Activity Types", "Maintain activity classifications used in planning and reporting.", AdminPolicies.ActivityTypesManage, adminAndHod, AdminCapabilityRisk.Moderate, "bi-list-task", Route("Admin", "/ActivityTypes/Index", "Activity types")),
            Capability("holidays-manage", "Manage Holidays", "Maintain non-working dates used by scheduling calculations.", AdminPolicies.HolidaysManage, adminAndHod, AdminCapabilityRisk.Moderate, "bi-calendar-week", Route(string.Empty, "/Settings/Holidays/Index", "Holidays")),
            ExternalCapability("celebrations-manage", "Manage Celebrations", "Maintain birthdays and anniversaries shown in the shared calendar.", Policies.Calendar.ManageCelebrations, Policies.Calendar.CelebrationManagerRoles, AdminCapabilityRisk.Moderate, "bi-stars", Route(string.Empty, "/Celebrations/Index", "Celebrations")),
            Capability("ingestion-manage", "Run Controlled Maintenance", "Run governed PDF ingestion and approved legacy-data workflows.", AdminPolicies.IngestionManage, admin, AdminCapabilityRisk.Critical, "bi-tools", Route("Admin", "/Maintenance/Index", "Maintenance centre")),
            Capability("media-manage", "Manage Media Library", "Compatibility access for established media administration workflows.", AdminPolicies.MediaManage, adminAndHod, AdminCapabilityRisk.High, "bi-images"),
            Capability("media-view", "View Media Administration", "Review media-library administration data.", AdminPolicies.MediaView, adminAndHod, AdminCapabilityRisk.Moderate, "bi-eye"),
            Capability("media-configure", "Configure Media Library", "Maintain media-library configuration.", AdminPolicies.MediaConfigure, adminAndHod, AdminCapabilityRisk.High, "bi-gear"),
            Capability("media-operate", "Operate Media Queue", "Run authorised media-processing queue operations.", AdminPolicies.MediaOperateQueue, adminAndHod, AdminCapabilityRisk.High, "bi-list-check"),
            Capability("media-recover", "Recover Media", "Restore or reconcile recoverable media records.", AdminPolicies.MediaRecover, adminAndHod, AdminCapabilityRisk.High, "bi-recycle"),
            Capability("media-classification", "Manage Media Classification", "Maintain controlled media classification values.", AdminPolicies.MediaClassificationManage, adminAndHod, AdminCapabilityRisk.High, "bi-tags")
        };
    }

    private static AdminCapabilityDescriptor Capability(
        string key,
        string title,
        string description,
        string policy,
        IReadOnlyList<string> roles,
        AdminCapabilityRisk risk,
        string icon,
        params AdminCapabilityRoute[] routes) =>
        new(key, title, description, policy, roles, risk, icon, true, routes);

    private static AdminCapabilityDescriptor ExternalCapability(
        string key,
        string title,
        string description,
        string policy,
        IReadOnlyList<string> roles,
        AdminCapabilityRisk risk,
        string icon,
        params AdminCapabilityRoute[] routes) =>
        new(key, title, description, policy, roles, risk, icon, false, routes);

    private static AdminCapabilityRoute Route(string area, string page, string label) =>
        new(area, page, label);
}
