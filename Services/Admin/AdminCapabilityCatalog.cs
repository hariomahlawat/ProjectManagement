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
            Capability("administration-access", "Open Administration", "Open Administration and its Home page.", AdminPolicies.Access, admin, AdminCapabilityRisk.High, "bi-shield-lock", Route("Admin", "/Index", "Home")),
            Capability("users-manage", "Manage users", "Add accounts, give roles, reset passwords, disable and delete accounts.", AdminPolicies.UsersManage, admin, AdminCapabilityRisk.Critical, "bi-people", Route("Admin", "/Users/Index", "Users")),
            Capability("access-governance", "See who has which role", "See every admin and HoD, who holds each role and what each role can do.", AdminPolicies.AccessGovernanceView, admin, AdminCapabilityRisk.Critical, "bi-shield-check", Route("Admin", "/AccessGovernance/Index", "Who has which role")),
            Capability("security-view", "See sign-in activity and system health", "See who signed in, failed sign-ins and whether PRISM is working.", AdminPolicies.SecurityView, admin, AdminCapabilityRisk.High, "bi-activity", Route("Admin", "/Analytics/Logins", "Sign-in activity"), Route("Admin", "/Diagnostics/DbHealth", "System health")),
            ExternalCapability("erp-usage-view", "See ERP usage", "See how many people use PRISM each day and which modules they use.", Policies.Usage.View, Policies.Usage.ViewerRoles, AdminCapabilityRisk.High, "bi-person-check", Route(string.Empty, "/Usage/Index", "ERP usage")),
            Capability("logs-view", "Read the audit log", "See who changed what, to which record and when.", AdminPolicies.LogsView, admin, AdminCapabilityRisk.High, "bi-journal-text", Route("Admin", "/Logs/Index", "Audit log")),
            Capability("recovery-manage", "Restore deleted items", "Bring back deleted records, or delete them for good once their time is up.", AdminPolicies.RecoveryManage, admin, AdminCapabilityRisk.Critical, "bi-arrow-counterclockwise", Route("Admin", "/Recovery/Index", "Restore deleted items")),
            Capability("master-data-manage", "Manage lists used in forms", "Add, rename, reorder and deactivate the choices offered in forms.", AdminPolicies.MasterDataManage, admin, AdminCapabilityRisk.High, "bi-sliders2", Route("Admin", "/MasterData/Index", "Lists used in forms")),
            Capability("integrity-manage", "Run data checks", "Find problems in the lists and renumber a list's order.", AdminPolicies.IntegrityManage, admin, AdminCapabilityRisk.Critical, "bi-clipboard2-check", Route("Admin", "/MasterData/Integrity/Index", "Data checks")),
            Capability("activity-types-manage", "Manage activity types", "Keep the list of activity types used in planning and reports.", AdminPolicies.ActivityTypesManage, adminAndHod, AdminCapabilityRisk.Moderate, "bi-list-task", Route("Admin", "/ActivityTypes/Index", "Activity types")),
            Capability("holidays-manage", "Manage holidays", "Keep the gazetted and restricted holidays and choose which ones the office observes. Stage dates skip them.", AdminPolicies.HolidaysManage, adminAndHod, AdminCapabilityRisk.Moderate, "bi-calendar-week", Route(string.Empty, "/Settings/Holidays/Index", "Holidays")),
            Capability("social-media-lists-manage", "Manage the social media lists", "Keep the event types and platforms offered on the social media post form.", AdminPolicies.SocialMediaListsManage, adminAndHod, AdminCapabilityRisk.Moderate, "bi-megaphone", Route("ProjectOfficeReports", "/Admin/SocialMediaTypes/Index", "Social media event types"), Route("ProjectOfficeReports", "/Admin/SocialMediaTypes/Platforms/Index", "Social media platforms")),
            ExternalCapability("celebrations-manage", "Manage celebrations", "Add and change the birthdays and anniversaries shown on the shared calendar.", Policies.Calendar.ManageCelebrations, Policies.Calendar.CelebrationManagerRoles, AdminCapabilityRisk.Moderate, "bi-stars", Route(string.Empty, "/Celebrations/Index", "Celebrations")),
            Capability("ingestion-manage", "Run data jobs", "Copy PDFs into the repository and import older projects from Excel.", AdminPolicies.IngestionManage, admin, AdminCapabilityRisk.Critical, "bi-tools", Route("Admin", "/Maintenance/Index", "Data jobs")),
            Capability("media-manage", "Manage the media library", "Kept for older links. No page needs it today.", AdminPolicies.MediaManage, adminAndHod, AdminCapabilityRisk.High, "bi-images"),
            Capability("media-view", "See media administration", "Open Media sources, Media intelligence and Classification review.", AdminPolicies.MediaView, adminAndHod, AdminCapabilityRisk.Moderate, "bi-eye"),
            Capability("media-configure", "Set up the media library", "Set up how the media library works. No page needs it today.", AdminPolicies.MediaConfigure, adminAndHod, AdminCapabilityRisk.High, "bi-gear"),
            Capability("media-operate", "Run the media queue", "Queue photos for processing and refresh the suggested people on Media intelligence.", AdminPolicies.MediaOperateQueue, adminAndHod, AdminCapabilityRisk.High, "bi-list-check"),
            Capability("media-recover", "Recover media", "Bring back or fix media records. No page needs it today.", AdminPolicies.MediaRecover, adminAndHod, AdminCapabilityRisk.High, "bi-recycle"),
            Capability("media-classification", "Manage media classifications", "Change how photos are classified on Classification review.", AdminPolicies.MediaClassificationManage, adminAndHod, AdminCapabilityRisk.High, "bi-tags"),
            ExternalCapability("docrepo-delete-approve", "Approve document deletions", "Approve or reject requests to delete Document Repository files, and review unreadable or missing files.", Policies.Documents.DeleteApprove, Policies.Documents.DeleteApprovalRoles, AdminCapabilityRisk.High, "bi-file-earmark-x", Route("DocumentRepository", "/Admin/DeleteRequests/Index", "Document deletion approvals")),
            ExternalCapability("docrepo-categories", "Manage repository lists", "Keep the document types and offices offered when a document is added to the Document Repository.", Policies.Documents.ManageCategories, Policies.Documents.CategoryManagerRoles, AdminCapabilityRisk.Moderate, "bi-folder2", Route("DocumentRepository", "/Admin/DocumentCategories/Index", "Repository document types")),
            ExternalCapability("docrepo-purge", "Delete repository files for good", "Permanently delete files from Deleted repository documents.", Policies.Documents.Purge, Policies.Documents.PurgeRoles, AdminCapabilityRisk.Critical, "bi-trash", Route("DocumentRepository", "/Admin/Trash/Index", "Deleted repository documents"))
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
