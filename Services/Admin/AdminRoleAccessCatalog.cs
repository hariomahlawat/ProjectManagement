using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Activities;

namespace ProjectManagement.Services.Admin;

public sealed record AdminRoleAccessItem(
    string Key,
    string Title,
    string Description,
    IReadOnlyList<string> PermittedRoles,
    int SortOrder);

public sealed record AdminRoleAccessGroup(
    string Key,
    string Title,
    string Icon,
    int SortOrder,
    IReadOnlyList<AdminRoleAccessItem> Items);

public interface IAdminRoleAccessCatalog
{
    IReadOnlyList<AdminRoleAccessGroup> BuildForRoles(IEnumerable<string> availableRoleNames);

    IReadOnlyList<AdminRoleAccessItem> ForRoles(
        IEnumerable<string> selectedRoleNames,
        IEnumerable<string>? availableRoleNames = null);
}

/// <summary>
/// Human-readable access reference used while assigning Identity roles.
/// Entries are deliberately expressed as operational capabilities and use the
/// same role collections as the registered authorisation policies wherever a
/// policy collection exists. Record ownership and project assignment rules are
/// stated explicitly in the descriptions because they cannot be represented by
/// a role alone.
/// </summary>
public sealed class AdminRoleAccessCatalog : IAdminRoleAccessCatalog
{
    private static readonly IReadOnlyList<AccessDefinition> Definitions = BuildDefinitions();

    public IReadOnlyList<AdminRoleAccessGroup> BuildForRoles(IEnumerable<string> availableRoleNames)
    {
        var availableRoles = NormaliseRoles(availableRoleNames);
        if (availableRoles.Count == 0)
        {
            return Array.Empty<AdminRoleAccessGroup>();
        }

        return Definitions
            .Select(definition => Materialise(definition, availableRoles))
            .Where(item => item is not null)
            .Cast<MaterialisedAccess>()
            .GroupBy(item => new
            {
                item.Definition.GroupKey,
                item.Definition.GroupTitle,
                item.Definition.GroupIcon,
                item.Definition.GroupSortOrder
            })
            .OrderBy(group => group.Key.GroupSortOrder)
            .ThenBy(group => group.Key.GroupTitle, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AdminRoleAccessGroup(
                group.Key.GroupKey,
                group.Key.GroupTitle,
                group.Key.GroupIcon,
                group.Key.GroupSortOrder,
                group.OrderBy(item => item.Definition.SortOrder)
                    .ThenBy(item => item.Definition.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new AdminRoleAccessItem(
                        item.Definition.Key,
                        item.Definition.Title,
                        item.Definition.Description,
                        item.PermittedRoles,
                        item.Definition.SortOrder))
                    .ToArray()))
            .ToArray();
    }

    public IReadOnlyList<AdminRoleAccessItem> ForRoles(
        IEnumerable<string> selectedRoleNames,
        IEnumerable<string>? availableRoleNames = null)
    {
        var selectedRoles = NormaliseRoles(selectedRoleNames);
        if (selectedRoles.Count == 0)
        {
            return Array.Empty<AdminRoleAccessItem>();
        }

        var available = availableRoleNames is null
            ? selectedRoles
            : NormaliseRoles(availableRoleNames);

        return BuildForRoles(available)
            .SelectMany(group => group.Items)
            .Where(item => item.PermittedRoles.Any(selectedRoles.Contains))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static MaterialisedAccess? Materialise(
        AccessDefinition definition,
        HashSet<string> availableRoles)
    {
        var permittedRoles = definition.AppliesToEveryAuthenticatedRole
            ? availableRoles.OrderBy(role => role, StringComparer.OrdinalIgnoreCase).ToArray()
            : definition.PermittedRoles
                .Where(availableRoles.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return permittedRoles.Length == 0
            ? null
            : new MaterialisedAccess(definition, permittedRoles);
    }

    private static HashSet<string> NormaliseRoles(IEnumerable<string>? roles) =>
        (roles ?? Enumerable.Empty<string>())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<AccessDefinition> BuildDefinitions()
    {
        var adminAndHod = Roles(RoleNames.Admin, RoleNames.HoD);
        var projectEditors = Roles(RoleNames.Admin, RoleNames.HoD, RoleNames.ProjectOfficer);
        var projectOfficeManagers = ProjectOfficeReportsPolicies.ProjectOfficeManagerRoles;

        return new[]
        {
            AllRoles(
                "core-workspaces",
                "Core PRISM workspaces",
                "View the dashboard, calendar, notebook, photos, project repository, activities, project ideas and common reports, subject to record-level restrictions.",
                Group.Core,
                10),

            Access(
                "projects-create",
                "Create projects",
                "Create a new project record and initiate its controlled lifecycle.",
                Policies.Projects.CreatorRoles,
                Group.Projects,
                10),
            Access(
                "projects-govern",
                "Govern project records",
                "Assign project roles and decide controlled metadata, stage, document and lifecycle change requests.",
                adminAndHod,
                Group.Projects,
                20),
            Access(
                "projects-maintain",
                "Maintain assigned projects",
                "Update lifecycle stages, plans, actuals, procurement facts, project documents, photographs and videos. Project Officers are limited to projects assigned to them where the workflow enforces assignment.",
                projectEditors,
                Group.Projects,
                30),
            Access(
                "projects-request-change",
                "Submit project change requests",
                "Request controlled metadata and stage changes for assigned projects.",
                Roles(RoleNames.ProjectOfficer),
                Group.Projects,
                40),
            Access(
                "projects-completed-summary",
                "Maintain completed-project summaries",
                "Edit completed-project summary information used in portfolio reporting and the proliferation compendium.",
                Roles(RoleNames.Admin, RoleNames.HoD, RoleNames.ProjectOffice),
                Group.Projects,
                50),
            Access(
                "projects-jdp-command",
                "Manage project–JDP associations",
                "Add or remove JDP associations on project records with command-level override.",
                Roles(RoleNames.Admin, RoleNames.HoD, RoleNames.Comdt),
                Group.Projects,
                60),
            Access(
                "projects-jdp-assigned",
                "Manage JDPs on assigned projects",
                "Add or remove JDP associations when the user is the assigned Project Officer for that project.",
                Roles(RoleNames.ProjectOfficer),
                Group.Projects,
                70),

            Access(
                "command-action-tracker",
                "Use the action tracker",
                "View and work on authorised command actions and assigned follow-up tasks.",
                Policies.ActionTracker.AccessAllowedRoles,
                Group.Command,
                10),
            Access(
                "command-briefing-decks",
                "Manage shared briefing decks",
                "Create shared project collections and generate editable command briefing PowerPoint presentations.",
                Policies.ProjectBriefingDecks.ManageAllowedRoles,
                Group.Command,
                20),
            Access(
                "command-conference",
                "Record conference directions",
                "Add conference directions and command remarks that feed the action queue.",
                Policies.ConferenceRemarks.ManageAllowedRoles,
                Group.Command,
                30),
            Access(
                "command-usage",
                "Review ERP usage",
                "Review adoption, activity patterns and meaningful system use across authorised users.",
                Policies.Usage.ViewerRoles,
                Group.Command,
                40),
            Access(
                "command-checklist",
                "Edit command checklists",
                "Update checklist items used for monitoring and coordination.",
                Policies.Checklist.EditorRoles,
                Group.Command,
                50),

            AllRoles(
                "reports-common-view",
                "View visits, ToT and proliferation trackers",
                "Open the common Project Office trackers available to authenticated users.",
                Group.Reports,
                10),
            Access(
                "reports-visits-social",
                "Manage visits and social-media records",
                "Create and maintain visit tracker and social-media tracker records.",
                projectOfficeManagers,
                Group.Reports,
                20),
            Access(
                "reports-training-view",
                "View the training tracker",
                "Review training records and their approval state.",
                ProjectOfficeReportsPolicies.TrainingTrackerViewerRoles,
                Group.Reports,
                30),
            Access(
                "reports-training-manage",
                "Manage training records",
                "Create and update training tracker entries.",
                ProjectOfficeReportsPolicies.TrainingTrackerManagerRoles,
                Group.Reports,
                40),
            Access(
                "reports-training-approve",
                "Approve training records",
                "Approve or return submitted training tracker entries.",
                ProjectOfficeReportsPolicies.TotTrackerApproverRoles,
                Group.Reports,
                50),
            Access(
                "reports-tot-submit",
                "Submit ToT tracker entries",
                "Create and update Transfer of Technology tracker submissions.",
                ProjectOfficeReportsPolicies.TotTrackerSubmitterRoles,
                Group.Reports,
                60),
            Access(
                "reports-tot-approve",
                "Approve ToT tracker entries",
                "Approve or return submitted Transfer of Technology entries.",
                ProjectOfficeReportsPolicies.TotTrackerApproverRoles,
                Group.Reports,
                70),
            Access(
                "reports-proliferation-manage",
                "Manage proliferation records",
                "Submit proliferation entries and maintain proliferation preferences.",
                projectOfficeManagers,
                Group.Reports,
                80),
            Access(
                "reports-proliferation-approve",
                "Approve proliferation records",
                "Approve or return submitted proliferation entries.",
                ProjectOfficeReportsPolicies.TotTrackerApproverRoles,
                Group.Reports,
                90),
            Access(
                "reports-progress-review",
                "View progress review",
                "Open the command progress-review workspace and current project position.",
                ProjectOfficeReportsPolicies.ProgressReviewViewerRoles,
                Group.Reports,
                100),
            Access(
                "reports-arpp-view",
                "View ARPP / PPP",
                "Review issued ARPP/PPP records, addenda and project positions.",
                ProjectOfficeReportsPolicies.ArppViewerRoles,
                Group.Reports,
                110),
            Access(
                "reports-arpp-manage",
                "Manage ARPP / PPP",
                "Create and maintain ARPP/PPP issues, addenda and project rows.",
                projectOfficeManagers,
                Group.Reports,
                120),
            Access(
                "reports-arpp-verify",
                "Verify ARPP / PPP",
                "Perform command verification of ARPP/PPP records.",
                ProjectOfficeReportsPolicies.ArppVerifierRoles,
                Group.Reports,
                130),
            Access(
                "reports-arpp-unlock",
                "Unlock ARPP / PPP",
                "Reopen a controlled ARPP/PPP record when correction is authorised.",
                ProjectOfficeReportsPolicies.ArppUnlockRoles,
                Group.Reports,
                140),
            Access(
                "reports-ipr-view",
                "View the IPR tracker",
                "Review patent and copyright records.",
                Policies.Ipr.ViewAllowedRoles,
                Group.Reports,
                150),
            Access(
                "reports-ipr-edit",
                "Edit IPR records",
                "Create and maintain patent and copyright records.",
                Policies.Ipr.EditAllowedRoles,
                Group.Reports,
                160),

            AllRoles(
                "industry-view-contact",
                "Use the Industry Directory",
                "View organisations and add contact details. Contacts created by the user remain editable by that user.",
                Group.Industry,
                10),
            Access(
                "industry-create",
                "Add organisations",
                "Create a new organisation or JDP record in the Industry Directory.",
                Policies.IndustryPartners.CreateAllowedRoles,
                Group.Industry,
                20),
            Access(
                "industry-edit-own",
                "Edit organisations created by the user",
                "Edit organisation details, files and JDP project associations when the user owns the organisation record.",
                Policies.IndustryPartners.CreateAllowedRoles,
                Group.Industry,
                30),
            Access(
                "industry-edit-any",
                "Edit any organisation or contact",
                "Override ownership and edit any organisation or contact record.",
                Policies.IndustryPartners.EditAnyAllowedRoles,
                Group.Industry,
                40),
            Access(
                "industry-delete",
                "Delete organisations",
                "Delete an organisation record under the controlled directory workflow.",
                Policies.IndustryPartners.DeleteAllowedRoles,
                Group.Industry,
                50),

            AllRoles(
                "documents-view",
                "View the document repository",
                "Search and open authorised repository documents.",
                Group.Documents,
                10),
            Access(
                "documents-upload-delete-request",
                "Upload documents and request deletion",
                "Upload repository documents and submit soft-delete requests.",
                Policies.Documents.UploadAndSoftDeleteRoles,
                Group.Documents,
                20),
            Access(
                "documents-metadata",
                "Edit document metadata",
                "Update authorised document metadata and classification fields.",
                Policies.Documents.MetadataEditorRoles,
                Group.Documents,
                30),
            Access(
                "documents-approve-delete",
                "Approve document deletion",
                "Review delete requests and OCR failure records.",
                Policies.Documents.DeleteApprovalRoles,
                Group.Documents,
                40),
            Access(
                "documents-admin",
                "Manage repository structure and purge",
                "Maintain office/document categories and perform authorised permanent deletion.",
                Policies.Documents.CategoryManagerRoles.Concat(Policies.Documents.PurgeRoles).ToArray(),
                Group.Documents,
                50),

            Access(
                "calendar-events",
                "Manage calendar events",
                "Create and update shared operational calendar events.",
                Policies.Calendar.EventManagerRoles,
                Group.Calendar,
                10),
            Access(
                "calendar-celebrations",
                "Manage birthdays and anniversaries",
                "Maintain birthday and anniversary entries shown in the shared calendar.",
                Policies.Calendar.CelebrationManagerRoles,
                Group.Calendar,
                20),
            Access(
                "activities-manage",
                "Manage miscellaneous activities",
                "Create and edit activity records and request their deletion.",
                ActivityRoleLists.ManagerRoles,
                Group.Calendar,
                30),
            Access(
                "activities-approve-delete",
                "Approve activity deletion",
                "Approve or reject activity deletion requests.",
                ActivityRoleLists.DeleteApproverRoles,
                Group.Calendar,
                40),

            Access(
                "admin-users",
                "Manage users and roles",
                "Create accounts, assign roles, reset passwords and control account lifecycle state.",
                Roles(RoleNames.Admin),
                Group.Administration,
                10),
            Access(
                "admin-security",
                "Review security and access governance",
                "Review privileged access, login activity, system health and audit logs.",
                Roles(RoleNames.Admin),
                Group.Administration,
                20),
            Access(
                "admin-recovery",
                "Manage recovery and retention",
                "Restore deleted records and perform retention-controlled permanent deletion.",
                Roles(RoleNames.Admin),
                Group.Administration,
                30),
            Access(
                "admin-master-data",
                "Manage master data and maintenance",
                "Maintain controlled taxonomies, configuration integrity and approved maintenance workflows.",
                Roles(RoleNames.Admin),
                Group.Administration,
                40),
            Access(
                "admin-department-settings",
                "Manage department settings",
                "Maintain activity types, holidays and authorised shared configuration.",
                adminAndHod,
                Group.Administration,
                50),
            Access(
                "admin-media",
                "Administer the media library",
                "Configure media processing, operate queues, recover media and manage controlled classification values.",
                adminAndHod,
                Group.Administration,
                60),
            Access(
                "admin-people-gallery",
                "Manage people classification in Photos",
                "Review face matches and maintain people identities in the media library.",
                adminAndHod,
                Group.Administration,
                70)
        };
    }

    private static AccessDefinition Access(
        string key,
        string title,
        string description,
        IReadOnlyList<string> permittedRoles,
        Group group,
        int sortOrder) =>
        new(
            key,
            title,
            description,
            permittedRoles,
            AppliesToEveryAuthenticatedRole: false,
            GroupKey(group),
            GroupTitle(group),
            GroupIcon(group),
            GroupSortOrder(group),
            sortOrder);

    private static AccessDefinition AllRoles(
        string key,
        string title,
        string description,
        Group group,
        int sortOrder) =>
        new(
            key,
            title,
            description,
            Array.Empty<string>(),
            AppliesToEveryAuthenticatedRole: true,
            GroupKey(group),
            GroupTitle(group),
            GroupIcon(group),
            GroupSortOrder(group),
            sortOrder);

    private static string[] Roles(params string[] roles) => roles;

    private static string GroupKey(Group group) => group switch
    {
        Group.Core => "core",
        Group.Projects => "projects",
        Group.Command => "command",
        Group.Reports => "reports",
        Group.Industry => "industry",
        Group.Documents => "documents",
        Group.Calendar => "calendar-activities",
        Group.Administration => "administration",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null)
    };

    private static string GroupTitle(Group group) => group switch
    {
        Group.Core => "Common access",
        Group.Projects => "Projects",
        Group.Command => "Command and coordination",
        Group.Reports => "Project Office reports",
        Group.Industry => "Industry Directory",
        Group.Documents => "Documents",
        Group.Calendar => "Calendar and activities",
        Group.Administration => "Administration",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null)
    };

    private static string GroupIcon(Group group) => group switch
    {
        Group.Core => "bi-grid",
        Group.Projects => "bi-kanban",
        Group.Command => "bi-diagram-3",
        Group.Reports => "bi-clipboard-data",
        Group.Industry => "bi-buildings",
        Group.Documents => "bi-folder2-open",
        Group.Calendar => "bi-calendar3",
        Group.Administration => "bi-shield-lock",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null)
    };

    private static int GroupSortOrder(Group group) => group switch
    {
        Group.Core => 10,
        Group.Projects => 20,
        Group.Command => 30,
        Group.Reports => 40,
        Group.Industry => 50,
        Group.Documents => 60,
        Group.Calendar => 70,
        Group.Administration => 80,
        _ => 900
    };

    private enum Group
    {
        Core,
        Projects,
        Command,
        Reports,
        Industry,
        Documents,
        Calendar,
        Administration
    }

    private sealed record AccessDefinition(
        string Key,
        string Title,
        string Description,
        IReadOnlyList<string> PermittedRoles,
        bool AppliesToEveryAuthenticatedRole,
        string GroupKey,
        string GroupTitle,
        string GroupIcon,
        int GroupSortOrder,
        int SortOrder);

    private sealed record MaterialisedAccess(
        AccessDefinition Definition,
        IReadOnlyList<string> PermittedRoles);
}
