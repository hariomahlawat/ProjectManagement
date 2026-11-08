using Microsoft.AspNetCore.Http;
using ProjectManagement.Configuration;
using ProjectManagement.Models.Navigation;

namespace ProjectManagement.Services.Navigation.ModuleNav;

public static class AdminNavigationKeys
{
    public const string Dashboard = "dashboard";
    public const string Users = "users";
    public const string AccessGovernance = "access-governance";
    public const string Logins = "logins";
    public const string ErpUsage = "erp-usage";
    public const string Logs = "logs";
    public const string DatabaseHealth = "database-health";
    public const string SearchIndex = "search-index";
    public const string RecoveryCentre = "recovery-centre";
    public const string ChatStorage = "chat-storage";
    public const string ProjectTrash = "project-trash";
    public const string DocumentRecycle = "document-recycle";
    public const string PdfIngestion = "pdf-ingestion";
    public const string DeletedEvents = "deleted-events";
    public const string MasterDataCentre = "master-data-centre";
    public const string MasterDataIntegrity = "master-data-integrity";
    public const string Holidays = "holidays";
    public const string Celebrations = "celebrations";
    public const string ProjectCategories = "project-categories";
    public const string TechnicalCategories = "technical-categories";
    public const string ActivityTypes = "activity-types";
    public const string ProjectTypes = "project-types";
    public const string SponsoringUnits = "sponsoring-units";
    public const string LineDirectorates = "line-directorates";
    public const string ArppReferenceData = "arpp-reference-data";
    public const string LegacyImport = "legacy-import";
    public const string ArchivedProjects = "archived-projects";
    public const string MaintenanceCentre = "maintenance-centre";
    public const string Help = "help";
    public const string AllPages = "all-pages";
    public const string DocumentDeleteRequests = "document-delete-requests";
    public const string DocumentTypes = "document-types";
    public const string DocumentOffices = "document-offices";
    public const string UnreadablePdfs = "unreadable-pdfs";
    public const string MissingDocumentFiles = "missing-document-files";
    public const string RepositoryTrash = "repository-trash";
    public const string MediaSources = "media-sources";
    public const string MediaIntelligence = "media-intelligence";
    public const string MediaClassification = "media-classification";
    public const string SocialMediaTypes = "social-media-types";
    public const string SocialMediaPlatforms = "social-media-platforms";
}

// Task groups, named for what an administrator wants to do (docs/ux/admin-module.md).
public static class AdminNavigationGroups
{
    public const string Home = "Home";
    public const string People = "People";
    public const string SignInsAudit = "Sign-ins & audit";
    public const string RestoreArchive = "Restore & archive";
    public const string ReferenceLists = "Reference lists";
    public const string System = "System";
    public const string Help = "Help";

    public static IReadOnlyList<string> Ordered { get; } = new[]
    {
        Home,
        People,
        SignInsAudit,
        RestoreArchive,
        ReferenceLists,
        System,
        Help
    };
}

public sealed record AdminNavigationMatch(
    string Area,
    string PagePattern,
    bool ExactPage = false,
    IReadOnlyDictionary<string, string?>? RequiredQuery = null)
{
    public bool Matches(
        string? currentArea,
        string? currentPage,
        IQueryCollection? query)
    {
        if (!string.Equals(Area, currentArea ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(currentPage))
        {
            return false;
        }

        var pageMatches = ExactPage
            ? string.Equals(PagePattern, currentPage, StringComparison.OrdinalIgnoreCase)
            : currentPage.StartsWith(PagePattern, StringComparison.OrdinalIgnoreCase);

        if (!pageMatches)
        {
            return false;
        }

        if (RequiredQuery is null || RequiredQuery.Count == 0)
        {
            return true;
        }

        if (query is null)
        {
            return false;
        }

        foreach (var required in RequiredQuery)
        {
            if (!query.TryGetValue(required.Key, out var actual)
                || !string.Equals(actual.ToString(), required.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// One administration page. <see cref="Description"/> is the plain sentence that says what the
/// page is for; <see cref="Keywords"/> are the everyday words people search with ("trash",
/// "locked out", "dropdown"). Pages reached through another page (a hub's own pages, the lists
/// behind "Lists used in forms") stay in the catalogue for search and the page index but out of
/// the sidebar, and highlight their <see cref="SidebarParent"/> there. Pages that belong to
/// another module keep that module's layout (<see cref="UsesAdminShell"/> false).
/// </summary>
public sealed record AdminNavigationEntry(
    string Key,
    string Group,
    int Order,
    NavigationItem Item,
    AdminNavigationMatch Match,
    bool ShowOnDashboard = false,
    bool ShowInQuickLinks = false,
    string Description = "",
    string Keywords = "",
    bool InSidebar = true,
    string? SidebarParent = null,
    bool UsesAdminShell = true);

public static class AdminNavigationCatalog
{
    private static readonly IReadOnlyList<AdminNavigationEntry> Catalog = new AdminNavigationEntry[]
    {
        // Home
        Entry(AdminNavigationKeys.Dashboard, AdminNavigationGroups.Home, 10,
            Item("Home", "Admin", "/Index", "bi-house", AdminPolicies.Access),
            "Start here: what needs your attention and the common admin jobs.",
            "overview, dashboard, start, administration",
            exactPage: true, showOnDashboard: true),

        // People
        Entry(AdminNavigationKeys.Users, AdminNavigationGroups.People, 20,
            Item("Users", "Admin", "/Users/Index", "bi-people", AdminPolicies.UsersManage),
            "Add people, change their roles, reset a password, clear a lockout, disable or delete an account.",
            "add user, create account, new user, password, reset, locked out, unlock, disable, enable, role, delete user",
            matchPagePattern: "/Users/", showOnDashboard: true),
        Entry(AdminNavigationKeys.AccessGovernance, AdminNavigationGroups.People, 25,
            Item("Who has which role", "Admin", "/AccessGovernance/Index", "bi-shield-check", AdminPolicies.AccessGovernanceView),
            "Every admin and HoD at a glance, and accounts that need a look: disabled, locked or never signed in.",
            "access governance, roles, privileges, permissions, admins, access review",
            matchPagePattern: "/AccessGovernance/", showOnDashboard: true),

        // Sign-ins & audit
        Entry(AdminNavigationKeys.Logins, AdminNavigationGroups.SignInsAudit, 30,
            Item("Sign-in activity", "Admin", "/Analytics/Logins", "bi-box-arrow-in-right", AdminPolicies.SecurityView),
            "Who signed in and when, failed attempts and sign-ins at unusual times.",
            "login, logins, login activity, failed login, sessions, authentication",
            matchPagePattern: "/Analytics/", showOnDashboard: true),
        Entry(AdminNavigationKeys.Logs, AdminNavigationGroups.SignInsAudit, 40,
            Item("Audit log", "Admin", "/Logs/Index", "bi-journal-text", AdminPolicies.LogsView),
            "Every change made in PRISM: who did what, to which record and when.",
            "audit logs, logs, history, trail, who changed, events, errors",
            matchPagePattern: "/Logs/", showOnDashboard: true),
        Entry(AdminNavigationKeys.ErpUsage, AdminNavigationGroups.SignInsAudit, 45,
            Item("ERP usage", string.Empty, "/Usage/Index", "bi-person-check", Policies.Usage.View),
            "How many people use PRISM each day and which modules they use.",
            "usage, adoption, active users, statistics",
            exactPage: true, showOnDashboard: true),

        // Restore & archive
        Entry(AdminNavigationKeys.RecoveryCentre, AdminNavigationGroups.RestoreArchive, 55,
            Item("Restore deleted items", "Admin", "/Recovery/Index", "bi-arrow-counterclockwise", AdminPolicies.RecoveryManage),
            "Bring back deleted projects, documents, calendar events and chat files, and see how long each is kept.",
            "recovery centre, trash, recycle bin, undelete, recover, deleted, purge, bin, undo",
            exactPage: true, showOnDashboard: true),
        Entry(AdminNavigationKeys.ProjectTrash, AdminNavigationGroups.RestoreArchive, 60,
            Item("Deleted projects", "Admin", "/Projects/Trash", "bi-trash3", AdminPolicies.RecoveryManage),
            "Projects that were deleted: restore one as it was, or delete it for good once its time is up.",
            "project trash, trash, purge, restore project",
            exactPage: true, showOnDashboard: true, inSidebar: false, sidebarParent: AdminNavigationKeys.RecoveryCentre),
        Entry(AdminNavigationKeys.DocumentRecycle, AdminNavigationGroups.RestoreArchive, 70,
            Item("Deleted project documents", "Admin", "/Documents/Recycle", "bi-recycle", AdminPolicies.RecoveryManage),
            "Project documents that were deleted: restore them, or remove them and their files for good.",
            "document recycle bin, deleted documents, recycle bin, restore document",
            exactPage: true, showOnDashboard: true, inSidebar: false, sidebarParent: AdminNavigationKeys.RecoveryCentre),
        Entry(AdminNavigationKeys.DeletedEvents, AdminNavigationGroups.RestoreArchive, 80,
            Item("Deleted calendar events", "Admin", "/Calendar/Deleted", "bi-calendar-x", AdminPolicies.RecoveryManage),
            "Calendar events that were deleted: put an event or a whole series back on the calendar.",
            "deleted events, calendar, restore event, meeting",
            exactPage: true, showOnDashboard: true, inSidebar: false, sidebarParent: AdminNavigationKeys.RecoveryCentre),
        Entry(AdminNavigationKeys.RepositoryTrash, AdminNavigationGroups.RestoreArchive, 85,
            Item("Deleted repository documents", "DocumentRepository", "/Admin/Trash/Index", "bi-trash", Policies.Documents.Purge),
            "Document Repository files that were deleted: restore them or delete them for good.",
            "document repository trash, docrepo trash, trash, restore",
            matchPagePattern: "/Admin/Trash/", inSidebar: false, sidebarParent: AdminNavigationKeys.RecoveryCentre),
        Entry(AdminNavigationKeys.ArchivedProjects, AdminNavigationGroups.RestoreArchive, 90,
            Item("Archived projects", "Admin", "/Projects/Archived", "bi-archive", AdminPolicies.RecoveryManage),
            "Projects taken out of everyday views. Restore one to make it visible again.",
            "archive, unarchive, hidden projects",
            exactPage: true, showOnDashboard: true, showInQuickLinks: true),

        // Reference lists
        Entry(AdminNavigationKeys.MasterDataCentre, AdminNavigationGroups.ReferenceLists, 115,
            Item("Lists used in forms", "Admin", "/MasterData/Index", "bi-ui-checks", AdminPolicies.MasterDataManage),
            "The choices in PRISM's dropdowns: categories, project types, sponsoring units, activity types and ARPP values.",
            "master data, dropdown, lookup, reference data, taxonomy, choices",
            exactPage: true, showOnDashboard: true),
        Entry(AdminNavigationKeys.ProjectCategories, AdminNavigationGroups.ReferenceLists, 140,
            Item("Project categories", "Admin", "/Categories/Index", "bi-diagram-3", AdminPolicies.MasterDataManage),
            "The category tree that groups projects in dashboards and reports.",
            "category, categories, project category, hierarchy",
            matchPagePattern: "/Categories/", showInQuickLinks: true, inSidebar: false, sidebarParent: AdminNavigationKeys.MasterDataCentre),
        Entry(AdminNavigationKeys.TechnicalCategories, AdminNavigationGroups.ReferenceLists, 150,
            Item("Technical categories", "Admin", "/TechnicalCategories/Index", "bi-cpu", AdminPolicies.MasterDataManage),
            "The technology areas a project can belong to.",
            "technical category, technology, taxonomy",
            matchPagePattern: "/TechnicalCategories/", showInQuickLinks: true, inSidebar: false, sidebarParent: AdminNavigationKeys.MasterDataCentre),
        Entry(AdminNavigationKeys.ProjectTypes, AdminNavigationGroups.ReferenceLists, 170,
            Item("Project types", "Admin", "/Lookups/ProjectTypes/Index", "bi-tags", AdminPolicies.MasterDataManage),
            "The project types offered in the project form.",
            "project type, build, procurement",
            matchPagePattern: "/Lookups/ProjectTypes/", showInQuickLinks: true, inSidebar: false, sidebarParent: AdminNavigationKeys.MasterDataCentre),
        Entry(AdminNavigationKeys.SponsoringUnits, AdminNavigationGroups.ReferenceLists, 180,
            Item("Sponsoring units", "Admin", "/Lookups/SponsoringUnits/Index", "bi-building", AdminPolicies.MasterDataManage),
            "Units and formations that sponsor projects.",
            "sponsoring unit, unit, formation, sponsor",
            matchPagePattern: "/Lookups/SponsoringUnits/", showInQuickLinks: true, inSidebar: false, sidebarParent: AdminNavigationKeys.MasterDataCentre),
        Entry(AdminNavigationKeys.LineDirectorates, AdminNavigationGroups.ReferenceLists, 190,
            Item("Line directorates", "Admin", "/Lookups/LineDirectorates/Index", "bi-diagram-2", AdminPolicies.MasterDataManage),
            "Directorates linked to a project's sponsor.",
            "line directorate, directorate, line dte",
            matchPagePattern: "/Lookups/LineDirectorates/", showInQuickLinks: true, inSidebar: false, sidebarParent: AdminNavigationKeys.MasterDataCentre),
        Entry(AdminNavigationKeys.ActivityTypes, AdminNavigationGroups.ReferenceLists, 195,
            Item("Activity types", "Admin", "/ActivityTypes/Index", "bi-list-task", AdminPolicies.ActivityTypesManage),
            "The kinds of activity people can record in planning and reports.",
            "activity type, activities, classification",
            matchPagePattern: "/ActivityTypes/", showInQuickLinks: true, inSidebar: false, sidebarParent: AdminNavigationKeys.MasterDataCentre),
        Entry(AdminNavigationKeys.ArppReferenceData, AdminNavigationGroups.ReferenceLists, 197,
            Item("ARPP values", "Admin", "/MasterData/ArppReferences/Index", "bi-list-check", AdminPolicies.MasterDataManage),
            "The CFA, Fund and DFPDS values offered in the ARPP workspace.",
            "arpp, arpp reference data, cfa, fund, dfpds, schedule",
            matchPagePattern: "/MasterData/ArppReferences/", showInQuickLinks: true, inSidebar: false, sidebarParent: AdminNavigationKeys.MasterDataCentre),
        Entry(AdminNavigationKeys.DocumentTypes, AdminNavigationGroups.ReferenceLists, 198,
            Item("Repository document types", "DocumentRepository", "/Admin/DocumentCategories/Index", "bi-folder2", Policies.Documents.ManageCategories),
            "The kinds of document people file in the Document Repository.",
            "document categories, document type, docrepo, repository, dropdown",
            matchPagePattern: "/Admin/DocumentCategories/", inSidebar: false, sidebarParent: AdminNavigationKeys.MasterDataCentre, usesAdminShell: false),
        Entry(AdminNavigationKeys.DocumentOffices, AdminNavigationGroups.ReferenceLists, 199,
            Item("Repository offices", "DocumentRepository", "/Admin/OfficeCategories/Index", "bi-building-gear", Policies.Documents.ManageCategories),
            "The offices a Document Repository file can belong to.",
            "office categories, offices, docrepo, repository, dropdown",
            matchPagePattern: "/Admin/OfficeCategories/", inSidebar: false, sidebarParent: AdminNavigationKeys.MasterDataCentre, usesAdminShell: false),
        Entry(AdminNavigationKeys.SocialMediaTypes, AdminNavigationGroups.ReferenceLists, 199,
            Item("Social media event types", "ProjectOfficeReports", "/Admin/SocialMediaTypes/Index", "bi-megaphone", AdminPolicies.SocialMediaListsManage),
            "The kinds of event a social media post can be about.",
            "social media, event type, post, dropdown",
            exactPage: true, inSidebar: false, sidebarParent: AdminNavigationKeys.MasterDataCentre, usesAdminShell: false),
        Entry(AdminNavigationKeys.SocialMediaPlatforms, AdminNavigationGroups.ReferenceLists, 199,
            Item("Social media platforms", "ProjectOfficeReports", "/Admin/SocialMediaTypes/Platforms/Index", "bi-share", AdminPolicies.SocialMediaListsManage),
            "Where a social media post was published, such as X or Facebook.",
            "social media, platform, twitter, facebook, instagram, youtube, dropdown",
            matchPagePattern: "/Admin/SocialMediaTypes/Platforms/", inSidebar: false, sidebarParent: AdminNavigationKeys.MasterDataCentre, usesAdminShell: false),
        Entry(AdminNavigationKeys.Holidays, AdminNavigationGroups.ReferenceLists, 200,
            Item("Holidays", string.Empty, "/Settings/Holidays/Index", "bi-calendar-week", AdminPolicies.HolidaysManage),
            "Gazetted and restricted holidays, and which ones the office observes.",
            "holiday, gazetted, restricted, closed, leave, calendar",
            matchPagePattern: "/Settings/Holidays/", showInQuickLinks: true),
        Entry(AdminNavigationKeys.Celebrations, AdminNavigationGroups.ReferenceLists, 205,
            Item("Celebrations", string.Empty, "/Celebrations/Index", "bi-stars", Policies.Calendar.ManageCelebrations),
            "Birthdays and anniversaries shown on the shared calendar.",
            "birthday, anniversary, calendar",
            matchPagePattern: "/Celebrations/", showInQuickLinks: true),

        // System
        Entry(AdminNavigationKeys.DatabaseHealth, AdminNavigationGroups.System, 300,
            Item("System health", "Admin", "/Diagnostics/DbHealth", "bi-heart-pulse", AdminPolicies.SecurityView),
            "Whether the database, file storage, search and background jobs are working; run the checks again.",
            "diagnostics, database, server, status, health, workers",
            exactPage: true, showOnDashboard: true, showInQuickLinks: true),
        Entry(AdminNavigationKeys.SearchIndex, AdminNavigationGroups.System, 305,
            Item("Search index", "Admin", "/Diagnostics/SearchIndex", "bi-search", AdminPolicies.SecurityView),
            "Whether PRISM search is up to date: retry items that failed, or rebuild it if results look wrong.",
            "search, index, rebuild, results missing, retry, failed",
            exactPage: true, showInQuickLinks: true, inSidebar: false, sidebarParent: AdminNavigationKeys.DatabaseHealth),
        Entry(AdminNavigationKeys.MaintenanceCentre, AdminNavigationGroups.System, 310,
            Item("Data jobs", "Admin", "/Maintenance/Index", "bi-tools", AdminPolicies.IngestionManage),
            "Copy FFC, IPR and activity PDFs into the Document Repository, or import legacy projects from Excel.",
            "maintenance centre, maintenance, ingestion, import, jobs",
            exactPage: true, showOnDashboard: true),
        Entry(AdminNavigationKeys.PdfIngestion, AdminNavigationGroups.System, 315,
            Item("Copy PDFs into the repository", "Admin", "/Documents/IngestExternalPdfs", "bi-file-earmark-arrow-up", AdminPolicies.IngestionManage),
            "Copy FFC, IPR and activity PDFs into the Document Repository so they can be searched.",
            "pdf, pdf ingestion, ingest, ingestion, copy documents, repository",
            exactPage: true, showInQuickLinks: true, inSidebar: false, sidebarParent: AdminNavigationKeys.MaintenanceCentre),
        Entry(AdminNavigationKeys.LegacyImport, AdminNavigationGroups.System, 320,
            Item("Legacy project import", "ProjectOfficeReports", "/Projects/LegacyImport", "bi-database-up", AdminPolicies.IngestionManage),
            "Import older projects from an Excel workbook after a preview.",
            "legacy import, excel, import projects, migration",
            exactPage: true, showInQuickLinks: true, inSidebar: false, sidebarParent: AdminNavigationKeys.MaintenanceCentre),
        Entry(AdminNavigationKeys.MasterDataIntegrity, AdminNavigationGroups.System, 325,
            Item("Data checks", "Admin", "/MasterData/Integrity/Index", "bi-clipboard2-check", AdminPolicies.IntegrityManage),
            "Find and fix problems in the reference lists, such as gaps in display order or duplicate names.",
            "configuration integrity, integrity, validation, duplicates, order",
            matchPagePattern: "/MasterData/Integrity/", showOnDashboard: true),
        Entry(AdminNavigationKeys.DocumentDeleteRequests, AdminNavigationGroups.System, 330,
            // Repository deletion requests are decided in the Decision Centre, filtered to them;
            // the old Document Repository address redirects there.
            new NavigationItem
            {
                Text = "Document deletion approvals",
                Area = string.Empty,
                Page = "/Approvals/Pending/Index",
                Icon = "bi-file-earmark-x",
                AuthorizationPolicy = Policies.Documents.DeleteApprove,
                RouteValues = new Dictionary<string, object?> { ["Type"] = "RepositoryDocumentDelete" }
            },
            "Approve or reject requests to delete Document Repository files. They are decided in the Decision Centre.",
            "document repository, docrepo, delete requests, approvals, decision centre",
            exactPage: true,
            requiredQuery: new Dictionary<string, string?> { ["Type"] = "RepositoryDocumentDelete" },
            usesAdminShell: false),
        Entry(AdminNavigationKeys.UnreadablePdfs, AdminNavigationGroups.System, 335,
            Item("Unreadable PDFs", "DocumentRepository", "/Admin/OCRFailures/OcrFailures", "bi-file-earmark-break", Policies.Documents.DeleteApprove),
            "Document Repository PDFs whose text could not be read, so they cannot be searched; retry them.",
            "ocr, ocr failures, scanned, text not found, docrepo",
            matchPagePattern: "/Admin/OCRFailures/", inSidebar: false, usesAdminShell: false),
        Entry(AdminNavigationKeys.MissingDocumentFiles, AdminNavigationGroups.System, 340,
            Item("Missing files", "DocumentRepository", "/Admin/MissingFiles", "bi-file-earmark-excl", Policies.Documents.DeleteApprove),
            "Document Repository records whose file is missing on disk.",
            "missing files, broken documents, docrepo, storage",
            exactPage: true, inSidebar: false, usesAdminShell: false),
        Entry(AdminNavigationKeys.MediaSources, AdminNavigationGroups.System, 345,
            Item("Media sources", string.Empty, "/Admin/MediaSources/Index", "bi-images", AdminPolicies.MediaView),
            "The folders PRISM reads photos and videos from, and the media processing queue.",
            "media library, photos, folders, sources, queue",
            matchPagePattern: "/Admin/MediaSources/", usesAdminShell: false),
        Entry(AdminNavigationKeys.MediaIntelligence, AdminNavigationGroups.System, 350,
            Item("Media intelligence", string.Empty, "/Admin/MediaIntelligence/Index", "bi-person-bounding-box", AdminPolicies.MediaView),
            "Face detection and identity matching for the photo library.",
            "faces, people in photos, identity, media",
            exactPage: true, inSidebar: false, usesAdminShell: false),
        Entry(AdminNavigationKeys.MediaClassification, AdminNavigationGroups.System, 355,
            Item("Classification review", string.Empty, "/Admin/MediaIntelligence/Classifications", "bi-tags", AdminPolicies.MediaView),
            "Check and correct whether each image is a photograph, a document or a graphic.",
            "photo classification, reclassify, media",
            exactPage: true, inSidebar: false, usesAdminShell: false),
        Entry(AdminNavigationKeys.ChatStorage, AdminNavigationGroups.System, 360,
            Item("Chat file storage", "Admin", "/Chat/Storage", "bi-paperclip", AdminPolicies.RecoveryManage),
            "How much space chat files use, clean-ups with undo, and the automatic retention rule.",
            "chat storage, chat attachments, files, space, retention",
            exactPage: true),

        // Help
        Entry(AdminNavigationKeys.AllPages, AdminNavigationGroups.Help, 395,
            Item("Every admin page", "Admin", "/AllPages/Index", "bi-list-ul", AdminPolicies.Access),
            "Every administration page you can open, with one sentence on what each is for.",
            "all pages, index, site map, list of pages, everything, where is",
            matchPagePattern: "/AllPages/"),
        Entry(AdminNavigationKeys.Help, AdminNavigationGroups.Help, 400,
            Item("Administration guide", "Admin", "/Help/Index", "bi-question-circle", AdminPolicies.Access),
            "How Administration works: access, monitoring, recovery, data jobs and reference lists.",
            "help, admin help, guide, how to, how do i, manual",
            matchPagePattern: "/Help/", showInQuickLinks: true)
    };

    public static IReadOnlyList<AdminNavigationEntry> Entries => Catalog;

    // The sidebar, the module bar and the main menu's Administration panel show the same
    // entries: pages reached through another page are left out (they stay searchable).
    public static IReadOnlyList<NavigationItem> BuildModuleItems() =>
        Catalog.Where(entry => entry.InSidebar).OrderBy(entry => entry.Order).Select(entry => entry.Item).ToArray();

    public static NavigationItem Get(string key) =>
        GetEntry(key).Item;

    public static AdminNavigationEntry GetEntry(string key) =>
        Catalog.Single(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));

    public static bool IsInScope(
        string? currentArea,
        string? currentPage,
        string? currentController,
        string? currentAction,
        IQueryCollection? query = null)
    {
        _ = currentController;
        _ = currentAction;

        if (string.Equals(currentArea, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Catalog.Any(entry => entry.UsesAdminShell && entry.Match.Matches(currentArea, currentPage, query));
    }

    public static AdminNavigationEntry? FindActiveEntry(
        string? currentArea,
        string? currentPage,
        IQueryCollection? query = null) =>
        Catalog.FirstOrDefault(entry => entry.Match.Matches(currentArea, currentPage, query));

    /// <summary>
    /// The eyebrow above an admin page's title: the sidebar group it belongs to, the same words
    /// on every page of the group (docs/ux/admin-module.md, rule 6). Home has none; pages
    /// outside the catalogue keep their own.
    /// </summary>
    public static string? EyebrowFor(string? currentArea, string? currentPage, IQueryCollection? query, string? fallback)
    {
        var entry = FindActiveEntry(currentArea, currentPage, query);
        if (entry is null)
        {
            return fallback;
        }

        return string.Equals(entry.Group, AdminNavigationGroups.Home, StringComparison.Ordinal) ? null : entry.Group;
    }

    public static NavigationItem BuildAdminPanel() => new()
    {
        Text = "Administration",
        Area = "Admin",
        Page = "/Index",
        AuthorizationPolicy = AdminPolicies.Access,
        Icon = "bi-shield-lock",
        Accent = "danger",
        Children = BuildModuleItems()
    };

    public static NavigationItem ActivityTypesItem() => Get(AdminNavigationKeys.ActivityTypes);

    private static AdminNavigationEntry Entry(
        string key,
        string group,
        int order,
        NavigationItem item,
        string description,
        string keywords,
        string? matchPagePattern = null,
        bool exactPage = false,
        IReadOnlyDictionary<string, string?>? requiredQuery = null,
        bool showOnDashboard = false,
        bool showInQuickLinks = false,
        bool inSidebar = true,
        string? sidebarParent = null,
        bool usesAdminShell = true)
    {
        var pagePattern = matchPagePattern ?? item.Page
            ?? throw new InvalidOperationException($"Admin navigation entry '{key}' must define a page.");

        return new AdminNavigationEntry(
            key,
            group,
            order,
            item,
            new AdminNavigationMatch(item.Area ?? string.Empty, pagePattern, exactPage, requiredQuery),
            showOnDashboard,
            showInQuickLinks,
            description,
            keywords,
            inSidebar,
            sidebarParent,
            usesAdminShell);
    }

    private static NavigationItem Item(
        string text,
        string? area,
        string page,
        string icon,
        string policy) => new()
    {
        Text = text,
        Area = area,
        Page = page,
        Icon = icon,
        AuthorizationPolicy = policy
    };
}
