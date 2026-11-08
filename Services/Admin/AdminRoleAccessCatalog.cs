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
                "Everyday pages",
                "Open the dashboard, calendar, notebook, photos, projects, activities, project ideas and the common reports. Some records stay limited to the people they belong to.",
                Group.Core,
                10),

            Access(
                "projects-create",
                "Add projects",
                "Add a new project. It starts at its first stage.",
                Policies.Projects.CreatorRoles,
                Group.Projects,
                10),
            Access(
                "projects-govern",
                "Assign and approve on projects",
                "Assign people to projects, and approve or reject requests to change a project's details, stages or documents.",
                adminAndHod,
                Group.Projects,
                20),
            Access(
                "projects-maintain",
                "Keep their projects up to date",
                "Update stages, plans, actual dates, procurement details, documents, photos and videos. Project Officers can do this only on projects assigned to them.",
                projectEditors,
                Group.Projects,
                30),
            Access(
                "projects-request-change",
                "Ask for project changes",
                "Ask for changes to the details and stages of projects assigned to them.",
                Roles(RoleNames.ProjectOfficer),
                Group.Projects,
                40),
            Access(
                "projects-completed-summary",
                "Edit completed-project summaries",
                "Edit the summary of a completed project, used in reports and the proliferation compendium.",
                Roles(RoleNames.Admin, RoleNames.HoD, RoleNames.ProjectOffice),
                Group.Projects,
                50),
            Access(
                "projects-jdp-command",
                "Link JDPs to any project",
                "Add or remove the JDPs linked to any project.",
                Roles(RoleNames.Admin, RoleNames.HoD, RoleNames.Comdt),
                Group.Projects,
                60),
            Access(
                "projects-jdp-assigned",
                "Link JDPs to their projects",
                "Add or remove the JDPs linked to projects they are the Project Officer for.",
                Roles(RoleNames.ProjectOfficer),
                Group.Projects,
                70),

            Access(
                "publications-shared-manage",
                "Make the Brochure and Compendium",
                "Make, rename, copy and retire Brochure and Compendium editions, including the Compendium's cover and sections. It does not let them change the projects' own details.",
                Policies.Publications.SharedPublicationManagerRoles,
                Group.Publications,
                10),

            Access(
                "command-action-tracker",
                "Use the action tracker",
                "See and work on the actions and follow-ups given to them.",
                Policies.ActionTracker.AccessAllowedRoles,
                Group.Command,
                10),
            Access(
                "command-briefing-decks",
                "Make briefing decks",
                "Pick projects and make briefing slides as a PowerPoint file people can edit.",
                Policies.ProjectBriefingDecks.ManageAllowedRoles,
                Group.Command,
                20),
            Access(
                "command-conference",
                "Record conference directions",
                "Add directions and remarks from conferences. Each becomes an action to follow up.",
                Policies.ConferenceRemarks.ManageAllowedRoles,
                Group.Command,
                30),
            Access(
                "command-usage",
                "See ERP usage",
                "See how many people use PRISM, when, and which modules they use.",
                Policies.Usage.ViewerRoles,
                Group.Command,
                40),
            Access(
                "command-checklist",
                "Edit command checklists",
                "Change the items on the command checklists.",
                Policies.Checklist.EditorRoles,
                Group.Command,
                50),

            AllRoles(
                "reports-common-view",
                "View visits, ToT and proliferation trackers",
                "Open the Project Office trackers every signed-in person can see.",
                Group.Reports,
                10),
            Access(
                "reports-ffc-manage",
                "Keep the FFC records",
                "Add, edit, archive and restore FFC country and year records, with their projects, quantities, delivery and installation dates, attachments and the list of countries.",
                ProjectOfficeReportsPolicies.FfcManagerRoles,
                Group.Reports,
                12),
            Access(
                "reports-ffc-inline-edit",
                "Change FFC status and progress in the table",
                "Change overall status and project progress straight in the FFC detailed table. The IT Officer cannot do these two.",
                ProjectOfficeReportsPolicies.FfcInlineEditorRoles,
                Group.Reports,
                14),
            Access(
                "reports-visits-social",
                "Keep visits and social media posts",
                "Add and edit visits and social media posts.",
                projectOfficeManagers,
                Group.Reports,
                20),
            Access(
                "reports-training-view",
                "View the training tracker",
                "See training records and whether they are approved.",
                ProjectOfficeReportsPolicies.TrainingTrackerViewerRoles,
                Group.Reports,
                30),
            Access(
                "reports-training-manage",
                "Keep training records",
                "Add and edit training records.",
                ProjectOfficeReportsPolicies.TrainingTrackerManagerRoles,
                Group.Reports,
                40),
            Access(
                "reports-training-approve",
                "Approve training records",
                "Approve training records, or send them back.",
                ProjectOfficeReportsPolicies.TrainingTrackerApproverRoles,
                Group.Reports,
                50),
            Access(
                "reports-tot-submit",
                "Add ToT records",
                "Add and edit Transfer of Technology records.",
                ProjectOfficeReportsPolicies.TotTrackerSubmitterRoles,
                Group.Reports,
                60),
            Access(
                "reports-tot-approve",
                "Approve ToT records",
                "Approve Transfer of Technology records, or send them back.",
                ProjectOfficeReportsPolicies.TotTrackerApproverRoles,
                Group.Reports,
                70),
            Access(
                "reports-proliferation-manage",
                "Keep proliferation records",
                "Add proliferation records and choose which source counts for a project and year.",
                projectOfficeManagers,
                Group.Reports,
                80),
            Access(
                "reports-proliferation-approve",
                "Approve proliferation records",
                "Approve proliferation records, or send them back.",
                ProjectOfficeReportsPolicies.TotTrackerApproverRoles,
                Group.Reports,
                90),
            Access(
                "reports-progress-review",
                "View progress review",
                "Open Progress review and see where each project stands.",
                ProjectOfficeReportsPolicies.ProgressReviewViewerRoles,
                Group.Reports,
                100),
            Access(
                "reports-arpp-view",
                "View ARPP / PPP",
                "See issued ARPP/PPP documents, their addenda and the projects in them.",
                ProjectOfficeReportsPolicies.ArppViewerRoles,
                Group.Reports,
                110),
            Access(
                "reports-arpp-manage",
                "Keep ARPP / PPP",
                "Add and edit ARPP/PPP issues, addenda and their projects.",
                projectOfficeManagers,
                Group.Reports,
                120),
            Access(
                "reports-arpp-verify",
                "Verify ARPP / PPP",
                "Check and verify ARPP/PPP records.",
                ProjectOfficeReportsPolicies.ArppVerifierRoles,
                Group.Reports,
                130),
            Access(
                "reports-arpp-unlock",
                "Unlock ARPP / PPP",
                "Reopen a verified ARPP/PPP record when it needs a correction.",
                ProjectOfficeReportsPolicies.ArppUnlockRoles,
                Group.Reports,
                140),
            AllRoles(
                "reports-ipr-view",
                "View the IPR Register",
                "See patents and copyrights, their files, projects, follow-up and charts.",
                Group.Reports,
                150),
            Access(
                "reports-ipr-edit",
                "Edit IPR records",
                "Add and edit patents and copyrights.",
                Policies.Ipr.EditAllowedRoles,
                Group.Reports,
                160),

            AllRoles(
                "it-projects-view",
                "View IT projects",
                "See the IT projects board, each project's stages, remarks and attachments.",
                Group.ItProjects,
                10),
            Access(
                "it-projects-manage",
                "Keep IT projects",
                "Create IT projects, edit their details, record stage completion and SO cost, upload and remove attachments, and mark a project dropped.",
                Policies.ItProjects.ManageRoles,
                Group.ItProjects,
                20),
            Access(
                "it-projects-remark",
                "Write IT project remarks",
                "Add remarks on an IT project and edit or delete their own. Remarks carry the author's role.",
                Policies.ItProjects.RemarkRoles,
                Group.ItProjects,
                30),

            AllRoles(
                "industry-view-contact",
                "Use the Industry Directory",
                "See organisations and add contacts. They can edit the contacts they added.",
                Group.Industry,
                10),
            Access(
                "industry-create",
                "Add organisations",
                "Add an organisation or a JDP to the Industry Directory.",
                Policies.IndustryPartners.CreateAllowedRoles,
                Group.Industry,
                20),
            Access(
                "industry-edit-own",
                "Edit organisations they added",
                "Edit the details, files and linked JDP projects of organisations they added.",
                Policies.IndustryPartners.CreateAllowedRoles,
                Group.Industry,
                30),
            Access(
                "industry-edit-any",
                "Edit any organisation or contact",
                "Edit any organisation or contact, whoever added it.",
                Policies.IndustryPartners.EditAnyAllowedRoles,
                Group.Industry,
                40),
            Access(
                "industry-delete",
                "Delete organisations",
                "Delete an organisation from the Industry Directory.",
                Policies.IndustryPartners.DeleteAllowedRoles,
                Group.Industry,
                50),

            AllRoles(
                "documents-view",
                "View the document repository",
                "Search and open the documents they are allowed to see.",
                Group.Documents,
                10),
            Access(
                "documents-upload-delete-request",
                "Upload documents and request deletion",
                "Add documents and ask for one to be deleted.",
                Policies.Documents.UploadAndSoftDeleteRoles,
                Group.Documents,
                20),
            Access(
                "documents-metadata",
                "Edit document details",
                "Change a document's title, type, office and other details.",
                Policies.Documents.MetadataEditorRoles,
                Group.Documents,
                30),
            Access(
                "documents-approve-delete",
                "Approve document deletion",
                "Approve or reject requests to delete documents, and look at PDFs that could not be read.",
                Policies.Documents.DeleteApprovalRoles,
                Group.Documents,
                40),
            Access(
                "documents-admin",
                "Keep repository lists and delete for good",
                "Keep the lists of offices and document types, and delete documents for good.",
                Policies.Documents.CategoryManagerRoles.Concat(Policies.Documents.PurgeRoles).ToArray(),
                Group.Documents,
                50),

            Access(
                "calendar-events",
                "Keep calendar events",
                "Add and edit events on the shared calendar.",
                Policies.Calendar.EventManagerRoles,
                Group.Calendar,
                10),
            Access(
                "calendar-celebrations",
                "Keep birthdays and anniversaries",
                "Add and edit the birthdays and anniversaries shown on the shared calendar.",
                Policies.Calendar.CelebrationManagerRoles,
                Group.Calendar,
                20),
            Access(
                "activities-manage",
                "Keep other activities",
                "Add and edit activities, and ask for one to be deleted.",
                ActivityRoleLists.ManagerRoles,
                Group.Calendar,
                30),
            Access(
                "activities-approve-delete",
                "Approve activity deletion",
                "Approve or reject requests to delete an activity.",
                ActivityRoleLists.DeleteApproverRoles,
                Group.Calendar,
                40),

            Access(
                "admin-users",
                "Manage users and roles",
                "Add accounts, give roles, reset passwords, and disable or delete accounts.",
                Roles(RoleNames.Admin),
                Group.Administration,
                10),
            Access(
                "admin-security",
                "See roles, sign-ins and the audit log",
                "See who has which role, sign-in activity, system health and the audit log.",
                Roles(RoleNames.Admin),
                Group.Administration,
                20),
            Access(
                "admin-recovery",
                "Restore deleted items",
                "Bring back deleted records, or delete them for good once their time is up.",
                Roles(RoleNames.Admin),
                Group.Administration,
                30),
            Access(
                "admin-master-data",
                "Keep lists used in forms and run data jobs",
                "Keep the lists offered in forms, run data checks and run data jobs.",
                Roles(RoleNames.Admin),
                Group.Administration,
                40),
            Access(
                "admin-department-settings",
                "Keep activity types and holidays",
                "Keep the activity types, holidays and social media lists.",
                adminAndHod,
                Group.Administration,
                50),
            Access(
                "admin-media",
                "Run the media library",
                "Set up photo sources, retry failed photos, recheck missing files and change how photos are classified.",
                adminAndHod,
                Group.Administration,
                60),
            Access(
                "admin-people-gallery",
                "Name people in Photos",
                "Check suggested face matches and keep the list of people in Photos.",
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
        Group.Publications => "publications",
        Group.Command => "command",
        Group.Reports => "reports",
        Group.ItProjects => "it-projects",
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
        Group.Publications => "Publications",
        Group.Command => "Command and coordination",
        Group.Reports => "Project Office reports",
        Group.ItProjects => "IT projects",
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
        Group.Publications => "bi-journal-richtext",
        Group.Command => "bi-diagram-3",
        Group.Reports => "bi-clipboard-data",
        Group.ItProjects => "bi-pc-display",
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
        Group.Publications => 30,
        Group.Command => 40,
        Group.Reports => 50,
        Group.ItProjects => 55,
        Group.Industry => 60,
        Group.Documents => 70,
        Group.Calendar => 80,
        Group.Administration => 90,
        _ => 900
    };

    private enum Group
    {
        Core,
        Projects,
        Publications,
        Command,
        Reports,
        ItProjects,
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
