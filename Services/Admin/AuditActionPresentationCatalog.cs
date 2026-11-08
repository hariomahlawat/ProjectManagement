namespace ProjectManagement.Services.Admin;

public sealed record AuditActionPresentation(
    string Label,
    string Icon,
    string Tone = "neutral",
    string Category = "Other",
    string? EntityType = null);

public interface IAuditActionPresentationCatalog
{
    AuditActionPresentation Describe(string? action, string? level = null);
}

public sealed class AuditActionPresentationCatalog : IAuditActionPresentationCatalog
{
    // Category names match the Administration sidebar (docs/ux/admin-review-4-fixes.md, PR 7).
    public const string ListsCategory = "Lists used in forms";
    public const string PdfCopyCategory = "Copy PDFs into the repository";
    public const string LegacyImportCategory = "Legacy project import";

    private static readonly IReadOnlyDictionary<string, AuditActionPresentation> KnownActions =
        new Dictionary<string, AuditActionPresentation>(StringComparer.OrdinalIgnoreCase)
        {
            ["AdminUserCreated"] = new("User account created", "bi-person-plus", "success", "Access & security", "ApplicationUser"),
            ["AdminUserUpdated"] = new("User profile and roles updated", "bi-person-gear", "neutral", "Access & security", "ApplicationUser"),
            ["AdminUserPasswordReset"] = new("User password reset", "bi-key", "warning", "Access & security", "ApplicationUser"),
            ["AdminUserUnlocked"] = new("User lockout cleared", "bi-unlock", "info", "Access & security", "ApplicationUser"),
            ["AdminUserDisabled"] = new("User account disabled", "bi-person-slash", "warning", "Access & security", "ApplicationUser"),
            ["AdminUserEnabled"] = new("User account enabled", "bi-person-check", "success", "Access & security", "ApplicationUser"),
            ["AdminUserDeleteRequested"] = new("User deletion requested", "bi-person-x", "danger", "Access & security", "ApplicationUser"),
            ["AdminUserDeleteUndone"] = new("User deletion request withdrawn", "bi-arrow-counterclockwise", "success", "Access & security", "ApplicationUser"),
            ["AdminUserPurged"] = new("User account permanently removed", "bi-person-dash", "danger", "Access & security", "ApplicationUser"),
            ["AdminUserDetailsUpdated"] = new("Account details changed", "bi-person-gear", "neutral", "Access & security", "ApplicationUser"),
            ["AdminUserRolesUpdated"] = new("Roles changed", "bi-person-gear", "neutral", "Access & security", "ApplicationUser"),
            ["AdminUserDeleted"] = new("User account deleted", "bi-person-dash", "danger", "Access & security", "ApplicationUser"),
            [AuthenticationEventNames.AuditLoginSuccess] = new("Successful sign-in", "bi-box-arrow-in-right", "success", "Authentication", "ApplicationUser"),
            [AuthenticationEventNames.AuditLoginFailed] = new("Failed sign-in", "bi-shield-exclamation", "warning", "Authentication", "ApplicationUser"),
            [AuthenticationEventNames.AuditLoginLockedOut] = new("Sign-in blocked — account locked", "bi-lock", "danger", "Authentication", "ApplicationUser"),
            ["Projects.ActualsUpdated"] = new("Project actuals updated", "bi-cash-stack", "neutral", "Projects", "Project"),
            ["Projects.MetaChangedDirect"] = new("Project details changed", "bi-pencil-square", "neutral", "Projects", "Project"),
            ["Projects.AssignRoles"] = new("Project roles assigned", "bi-people", "neutral", "Projects", "Project"),
            ["Projects.StageChanged"] = new("Project stage changed", "bi-signpost-split", "neutral", "Projects", "Project"),
            ["Project.ProliferationProfileUpdated"] = new("Project proliferation details updated", "bi-diagram-2", "neutral", "Projects", "Project"),
            ["Archive"] = new("Project archived", "bi-archive", "neutral", "Recovery", "Project"),
            ["RestoreArchive"] = new("Archived project restored", "bi-folder2-open", "success", "Recovery", "Project"),
            ["Trash"] = new("Project moved to trash", "bi-trash3", "warning", "Recovery", "Project"),
            ["RestoreTrash"] = new("Project restored from trash", "bi-arrow-counterclockwise", "success", "Recovery", "Project"),
            ["Projects.Purge"] = new("Project permanently deleted", "bi-trash3-fill", "danger", "Recovery", "Project"),
            ["Project.Trashed"] = new("Project moved to trash", "bi-trash3", "warning", "Recovery", "Project"),
            ["Project.Restored"] = new("Project restored", "bi-arrow-counterclockwise", "success", "Recovery", "Project"),
            ["ProjectPurgeAuthorised"] = new("Project purge authorised", "bi-shield-exclamation", "warning", "Recovery", "Project"),
            ["Project.Purged"] = new("Project permanently deleted", "bi-trash3-fill", "danger", "Recovery", "Project"),
            ["Documents.Restored"] = new("Document restored", "bi-file-earmark-check", "success", "Recovery", "Document"),
            ["Documents.Purged"] = new("Document permanently deleted", "bi-file-earmark-x", "danger", "Recovery", "Document"),
            ["Calendar.EventRestored"] = new("Calendar event restored", "bi-calendar-check", "success", "Recovery", "Event"),
            ["CalendarEventRestored"] = new("Calendar event restored", "bi-calendar-check", "success", "Recovery", "Event"),
            ["PdfIngestionStarted"] = new("Copying PDFs started", "bi-file-earmark-arrow-up", "neutral", PdfCopyCategory, "DocumentRepository"),
            ["PdfIngestionCompleted"] = new("Copying PDFs finished", "bi-file-earmark-check", "success", PdfCopyCategory, "DocumentRepository"),
            ["PdfIngestionCancelled"] = new("Copying PDFs cancelled", "bi-slash-circle", "warning", PdfCopyCategory, "DocumentRepository"),
            ["PdfIngestionFailed"] = new("Copying PDFs failed", "bi-file-earmark-x", "danger", PdfCopyCategory, "DocumentRepository"),
            ["LegacyImportCommitted"] = new("Old projects imported", "bi-database-check", "success", LegacyImportCategory, "ProjectLegacyImport"),
            ["MasterData.ProjectCategoryCreated"] = new("Project category created", "bi-node-plus", "success", ListsCategory, "ProjectCategory"),
            ["MasterData.ProjectCategoryUpdated"] = new("Project category updated", "bi-pencil-square", "neutral", ListsCategory, "ProjectCategory"),
            ["MasterData.ProjectCategoryStatusChanged"] = new("Project category status changed", "bi-toggle-on", "warning", ListsCategory, "ProjectCategory"),
            ["MasterData.ProjectCategoryReordered"] = new("Project category reordered", "bi-arrow-down-up", "neutral", ListsCategory, "ProjectCategory"),
            ["MasterData.ProjectCategoryDeleted"] = new("Project category deleted", "bi-trash3", "danger", ListsCategory, "ProjectCategory"),
            ["MasterData.TechnicalCategoryCreated"] = new("Technical category created", "bi-node-plus", "success", ListsCategory, "TechnicalCategory"),
            ["MasterData.TechnicalCategoryUpdated"] = new("Technical category updated", "bi-pencil-square", "neutral", ListsCategory, "TechnicalCategory"),
            ["MasterData.TechnicalCategoryStatusChanged"] = new("Technical category status changed", "bi-toggle-on", "warning", ListsCategory, "TechnicalCategory"),
            ["MasterData.TechnicalCategoryReordered"] = new("Technical category reordered", "bi-arrow-down-up", "neutral", ListsCategory, "TechnicalCategory"),
            ["MasterData.TechnicalCategoryDeleted"] = new("Technical category deleted", "bi-trash3", "danger", ListsCategory, "TechnicalCategory"),
            ["MasterData.ProjectTypeCreated"] = new("Project type created", "bi-plus-circle", "success", ListsCategory, "ProjectType"),
            ["MasterData.ProjectTypeUpdated"] = new("Project type updated", "bi-pencil-square", "neutral", ListsCategory, "ProjectType"),
            ["MasterData.ProjectTypeStatusChanged"] = new("Project type status changed", "bi-toggle-on", "warning", ListsCategory, "ProjectType"),
            ["MasterData.ProjectTypeReordered"] = new("Project type reordered", "bi-arrow-down-up", "neutral", ListsCategory, "ProjectType"),
            ["MasterData.ProjectTypeDeleted"] = new("Project type deleted", "bi-trash3", "danger", ListsCategory, "ProjectType"),
            ["MasterData.SponsoringUnitCreated"] = new("Sponsoring unit created", "bi-building-add", "success", ListsCategory, "SponsoringUnit"),
            ["MasterData.SponsoringUnitUpdated"] = new("Sponsoring unit updated", "bi-pencil-square", "neutral", ListsCategory, "SponsoringUnit"),
            ["MasterData.SponsoringUnitStatusChanged"] = new("Sponsoring unit status changed", "bi-toggle-on", "warning", ListsCategory, "SponsoringUnit"),
            ["MasterData.SponsoringUnitReordered"] = new("Sponsoring unit reordered", "bi-arrow-down-up", "neutral", ListsCategory, "SponsoringUnit"),
            ["MasterData.SponsoringUnitDeleted"] = new("Sponsoring unit deleted", "bi-trash3", "danger", ListsCategory, "SponsoringUnit"),
            ["MasterData.LineDirectorateCreated"] = new("Line directorate created", "bi-diagram-2", "success", ListsCategory, "LineDirectorate"),
            ["MasterData.LineDirectorateUpdated"] = new("Line directorate updated", "bi-pencil-square", "neutral", ListsCategory, "LineDirectorate"),
            ["MasterData.LineDirectorateStatusChanged"] = new("Line directorate status changed", "bi-toggle-on", "warning", ListsCategory, "LineDirectorate"),
            ["MasterData.LineDirectorateReordered"] = new("Line directorate reordered", "bi-arrow-down-up", "neutral", ListsCategory, "LineDirectorate"),
            ["MasterData.LineDirectorateDeleted"] = new("Line directorate deleted", "bi-trash3", "danger", ListsCategory, "LineDirectorate"),
            ["MasterData.ActivityTypeCreated"] = new("Activity type created", "bi-list-task", "success", ListsCategory, "ActivityType"),
            ["MasterData.ActivityTypeUpdated"] = new("Activity type updated", "bi-pencil-square", "neutral", ListsCategory, "ActivityType"),
            ["MasterData.ActivityTypeDeleted"] = new("Activity type deleted", "bi-trash3", "danger", ListsCategory, "ActivityType"),
            ["MasterData.ArppReferenceDeleted"] = new("ARPP value deleted", "bi-trash3", "danger", ListsCategory, "ArppReference"),
            ["HolidayCreated"] = new("Holiday created", "bi-calendar-plus", "success", ListsCategory, "Holiday"),
            ["GazettedHolidayCreated"] = new("Gazetted Holiday created", "bi-calendar2-check", "success", ListsCategory, "Holiday"),
            ["RestrictedHolidayCreated"] = new("Restricted Holiday created", "bi-info-square", "success", ListsCategory, "Holiday"),
            ["RestrictedHolidayDeclaredOfficeHoliday"] = new("Restricted Holiday declared as office holiday", "bi-building-check", "warning", ListsCategory, "Holiday"),
            ["RestrictedHolidayOfficeObservanceWithdrawn"] = new("Restricted Holiday office observance withdrawn", "bi-building-dash", "warning", ListsCategory, "Holiday"),
            ["HolidayUpdated"] = new("Holiday updated", "bi-calendar-event", "neutral", ListsCategory, "Holiday"),
            ["HolidayDeleted"] = new("Holiday deleted", "bi-calendar-x", "danger", ListsCategory, "Holiday"),
            ["CelebrationCreated"] = new("Celebration created", "bi-stars", "success", ListsCategory, "Celebration"),
            ["CelebrationUpdated"] = new("Celebration updated", "bi-pencil-square", "neutral", ListsCategory, "Celebration"),
            ["CelebrationDeleted"] = new("Celebration deleted", "bi-trash3", "danger", ListsCategory, "Celebration"),
            ["MasterData.Created"] = new("List item added", "bi-plus-circle", "success", ListsCategory),
            ["MasterData.Updated"] = new("List item changed", "bi-pencil-square", "neutral", ListsCategory),
            ["MasterData.Deactivated"] = new("List item switched off", "bi-slash-circle", "warning", ListsCategory),
            ["MasterData.IntegrityOrderNormalised"] = new("List put back in order", "bi-arrow-down-up", "success", ListsCategory, "MasterDataIntegrity"),

            // Common actions that used to show machine-made titles.
            ["Projects.Created"] = new("Project created", "bi-plus-circle", "success", "Projects", "Project"),
            ["Project.Create"] = new("Project created", "bi-plus-circle", "success", "Projects", "Project"),
            ["Projects.PlanUpdated"] = new("Project plan updated", "bi-calendar-range", "neutral", "Projects", "Project"),
            ["Projects.PlanGeneratedFromDurations"] = new("Project plan worked out from durations", "bi-calendar-range", "neutral", "Projects", "Project"),
            ["Projects.StageBackfilled"] = new("Earlier project stage filled in", "bi-signpost-split", "neutral", "Projects", "Project"),
            ["Projects.MetaChangedViaApproval"] = new("Project details changed after approval", "bi-pencil-square", "neutral", "Projects", "Project"),
            ["Projects.MetaChangeApproved"] = new("Project details change approved", "bi-check2-circle", "success", "Projects", "Project"),
            ["Projects.MetaChangeRejected"] = new("Project details change rejected", "bi-x-circle", "warning", "Projects", "Project"),
            ["Projects.ActualCorrectionSubmitted"] = new("Correction to project actuals sent for approval", "bi-pencil", "neutral", "Projects", "Project"),
            ["Projects.ActualCorrectionApproved"] = new("Correction to project actuals approved", "bi-check2-circle", "success", "Projects", "Project"),
            ["Projects.ActualCorrectionRejected"] = new("Correction to project actuals rejected", "bi-x-circle", "warning", "Projects", "Project"),
            ["Projects.ActualCorrectionWithdrawn"] = new("Correction to project actuals withdrawn", "bi-arrow-counterclockwise", "neutral", "Projects", "Project"),
            ["Projects.HistoricalStageHistoryUpdated"] = new("Past stage history updated", "bi-clock-history", "neutral", "Projects", "Project"),
            ["Stages.StageStatusChanged"] = new("Stage status changed", "bi-signpost-split", "neutral", "Projects", "Project"),
            ["Stages.StageAutoCompleted"] = new("Stage completed by PRISM", "bi-check2-circle", "success", "Projects", "Project"),
            ["Project.LifecycleCompleted"] = new("Project marked completed", "bi-flag", "success", "Projects", "Project"),
            ["Project.LifecycleCancelled"] = new("Project cancelled", "bi-x-octagon", "warning", "Projects", "Project"),
            ["Project.LifecycleReactivated"] = new("Project made active again", "bi-arrow-repeat", "success", "Projects", "Project"),
            ["Project.LifecycleCompletionUpdated"] = new("Project completion details updated", "bi-flag", "neutral", "Projects", "Project"),
            ["Project.LifecycleCompletionEndorsed"] = new("Project completion endorsed", "bi-patch-check", "success", "Projects", "Project"),
            ["Project.Content.BriefUpdated"] = new("Project brief updated", "bi-card-text", "neutral", "Projects", "Project"),
            ["Project.Content.DescriptionUpdated"] = new("Project description updated", "bi-card-text", "neutral", "Projects", "Project"),
            ["Project.Content.KeyPointsUpdated"] = new("Project key points updated", "bi-list-check", "neutral", "Projects", "Project"),
            ["Project.Content.CapabilitiesUpdated"] = new("Project capabilities updated", "bi-card-text", "neutral", "Projects", "Project"),
            ["Project.Content.TechnicalSpecificationsUpdated"] = new("Project technical specifications updated", "bi-card-text", "neutral", "Projects", "Project"),
            ["Project.PhotoAdded"] = new("Project photo added", "bi-image", "success", "Projects", "Project"),
            ["Project.PhotoUpdated"] = new("Project photo updated", "bi-image", "neutral", "Projects", "Project"),
            ["Project.PhotoRemoved"] = new("Project photo removed", "bi-image", "warning", "Projects", "Project"),
            ["Project.PhotoReordered"] = new("Project photos reordered", "bi-images", "neutral", "Projects", "Project"),
            ["Project.DocumentRequested"] = new("Project document sent for approval", "bi-file-earmark-arrow-up", "neutral", "Documents", "Document"),
            ["Project.DocumentApproved"] = new("Project document approved", "bi-file-earmark-check", "success", "Documents", "Document"),
            ["Project.DocumentRejected"] = new("Project document rejected", "bi-file-earmark-x", "warning", "Documents", "Document"),
            ["Project.DocumentPublished"] = new("Project document published", "bi-file-earmark-check", "success", "Documents", "Document"),
            ["Project.DocumentReplaced"] = new("Project document replaced", "bi-file-earmark-diff", "neutral", "Documents", "Document"),
            ["Project.DocumentRemoved"] = new("Project document moved to trash", "bi-file-earmark-minus", "warning", "Recovery", "Document"),
            ["Project.DocumentRestored"] = new("Project document restored", "bi-file-earmark-check", "success", "Recovery", "Document"),
            ["Project.DocumentHardDeleted"] = new("Project document permanently deleted", "bi-file-earmark-x", "danger", "Recovery", "Document"),
            ["ProjectBriefing.DeckCreated"] = new("Briefing deck created", "bi-easel", "success", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.DeckUpdated"] = new("Briefing deck settings changed", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.DeckDuplicated"] = new("Briefing deck copied", "bi-easel", "success", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.DeckDeleted"] = new("Briefing deck deleted", "bi-easel", "warning", "Recovery", "ProjectBriefingDeck"),
            ["ProjectBriefing.DeckRestored"] = new("Briefing deck restored", "bi-arrow-counterclockwise", "success", "Recovery", "ProjectBriefingDeck"),
            ["ProjectBriefing.PowerPointGenerated"] = new("Briefing deck downloaded as PowerPoint", "bi-file-earmark-slides", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.ProjectsAdded"] = new("Projects added to a briefing deck", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.ProjectRemoved"] = new("Project removed from a briefing deck", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.ProjectsReordered"] = new("Briefing deck projects reordered", "bi-arrow-down-up", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.SpeakerNotesUpdated"] = new("Briefing deck speaker notes updated", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.MembershipUpdated"] = new("Projects in a briefing deck changed", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.AdditionalSlideEnabled"] = new("Extra slide switched on in a briefing deck", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.AdditionalSlideDisabled"] = new("Extra slide switched off in a briefing deck", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.AdditionalSlideRemoved"] = new("Extra slide removed from a briefing deck", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.AdditionalSlidesReordered"] = new("Extra slides reordered in a briefing deck", "bi-arrow-down-up", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.PresetApplied"] = new("Briefing deck template applied", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.PresetUndone"] = new("Briefing deck template change undone", "bi-arrow-counterclockwise", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.DescriptionUpdated"] = new("Project description changed in a briefing deck", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.ItemPresentationChanged"] = new("Project slide style changed in a briefing deck", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.ItemPhotoChanged"] = new("Project photo changed in a briefing deck", "bi-image", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.InstitutionalProfileUpdated"] = new("SDD profile slide changed", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.RoleCharterUpdated"] = new("Role and charter slide changed", "bi-easel", "neutral", "Projects", "ProjectBriefingDeck"),
            ["ProjectBriefing.FfcGlobalFootprintUpdated"] = new("FFC global footprint slide changed", "bi-globe", "neutral", "Projects", "ProjectBriefingDeck"),
            ["Arpp.IssueVerified"] = new("ARPP issue verified", "bi-patch-check", "success", "Projects", "ArppIssue"),
            ["Arpp.IssueCreated"] = new("ARPP issue created", "bi-journal-plus", "success", "Projects", "ArppIssue"),
            ["Arpp.IssueUnlocked"] = new("ARPP issue unlocked for correction", "bi-unlock", "warning", "Projects", "ArppIssue"),
            ["Arpp.WorkspaceSaved"] = new("ARPP issue saved", "bi-save", "neutral", "Projects", "ArppIssue"),
            ["Arpp.AttachmentUploaded"] = new("ARPP letter attached", "bi-paperclip", "neutral", "Projects", "ArppIssue"),
            ["Arpp.IpaStageSynchronized"] = new("IPA stage set from the ARPP", "bi-arrow-repeat", "neutral", "Projects", "Project"),
            ["project.video.upload"] = new("Project video added", "bi-camera-video", "success", "Projects", "Project"),
            ["Arpp.EntriesReconciled"] = new("ARPP entries matched to projects", "bi-link-45deg", "neutral", "Projects", "ArppIssue"),
            ["Arpp.IpaStageStartupReconciled"] = new("IPA stages brought in line with ARPP at start-up", "bi-arrow-repeat", "neutral", "Projects", "Project"),
            ["Arpp.IpaStagePendingRequestsSuperseded"] = new("Pending IPA stage requests replaced by ARPP", "bi-arrow-repeat", "warning", "Projects", "Project"),
            ["Arpp.IpaStageDataQualityIssue"] = new("IPA start date is later than the ARPP completion date", "bi-exclamation-triangle", "warning", "Projects", "Project"),
            ["SearchIndex.RebuildRequested"] = new("Search index rebuild requested", "bi-arrow-clockwise", "neutral", "Maintenance", "SearchIndex"),
            ["SearchIndex.RetryRequested"] = new("Failed search indexing retried", "bi-arrow-repeat", "neutral", "Maintenance", "SearchIndex"),
            [AdminExportKind.Users.Action] = new(AdminExportKind.Users.Title, "bi-download", "neutral", "Exports", AdminExportAuditService.EntityType),
            [AdminExportKind.AccessReview.Action] = new(AdminExportKind.AccessReview.Title, "bi-download", "neutral", "Exports", AdminExportAuditService.EntityType),
            [AdminExportKind.AuditLog.Action] = new(AdminExportKind.AuditLog.Title, "bi-download", "neutral", "Exports", AdminExportAuditService.EntityType),
            [AdminExportKind.SignIns.Action] = new(AdminExportKind.SignIns.Title, "bi-download", "neutral", "Exports", AdminExportAuditService.EntityType),
            [AdminExportKind.ErpUsage.Action] = new(AdminExportKind.ErpUsage.Title, "bi-download", "neutral", "Exports", AdminExportAuditService.EntityType),
            [AdminExportKind.PdfIngestionFailures.Action] = new(AdminExportKind.PdfIngestionFailures.Title, "bi-download", "neutral", "Exports", AdminExportAuditService.EntityType)
        };

    public AuditActionPresentation Describe(string? action, string? level = null)
    {
        var normalizedAction = action?.Trim();
        var toneFromLevel = ResolveTone(level);

        if (!string.IsNullOrWhiteSpace(normalizedAction)
            && KnownActions.TryGetValue(normalizedAction, out var known))
        {
            // The catalogue is authoritative for known business actions. A broad
            // stored log level must not turn routine metadata operations into warnings.
            // A critical/error level may still escalate a normally neutral action.
            return string.Equals(toneFromLevel, "danger", StringComparison.Ordinal)
                && string.Equals(known.Tone, "neutral", StringComparison.Ordinal)
                    ? known with { Tone = "danger" }
                    : known;
        }

        var label = AuditActionTitleFormatter.Format(normalizedAction);
        var tone = string.Equals(toneFromLevel, "neutral", StringComparison.Ordinal)
            ? InferTone(normalizedAction)
            : toneFromLevel;

        return new AuditActionPresentation(
            label,
            InferIcon(normalizedAction),
            tone,
            InferCategory(normalizedAction),
            InferEntityType(normalizedAction));
    }

    private static string ResolveTone(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "error" or "critical" => "danger",
        "warning" => "warning",
        "success" => "success",
        _ => "neutral"
    };

    private static string InferTone(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "neutral";
        if (ContainsAny(action, "Delete", "Purge", "Failed", "Error", "LockedOut")) return "danger";
        if (ContainsAny(action, "Disable", "Trash", "Warning", "Reset")) return "warning";
        if (ContainsAny(action, "Create", "Enable", "Restore", "Success")) return "success";
        return "neutral";
    }

    private static string InferIcon(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "bi-activity";
        if (action.Contains("User", StringComparison.OrdinalIgnoreCase)) return "bi-person-gear";
        if (action.Contains("Project", StringComparison.OrdinalIgnoreCase)) return "bi-kanban";
        if (action.Contains("Document", StringComparison.OrdinalIgnoreCase)) return "bi-file-earmark-text";
        if (action.Contains("Calendar", StringComparison.OrdinalIgnoreCase)) return "bi-calendar-event";
        if (action.Contains("Login", StringComparison.OrdinalIgnoreCase)) return "bi-box-arrow-in-right";
        if (action.Contains("Database", StringComparison.OrdinalIgnoreCase)) return "bi-database";
        if (action.Contains("Ingestion", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Import", StringComparison.OrdinalIgnoreCase)) return "bi-tools";
        return "bi-activity";
    }

    private static string InferCategory(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "Other";
        if (action.Contains("Login", StringComparison.OrdinalIgnoreCase)) return "Authentication";
        if (action.Contains("User", StringComparison.OrdinalIgnoreCase)) return "Access & security";
        if (action.Contains("Project", StringComparison.OrdinalIgnoreCase)) return "Projects";
        if (action.Contains("Document", StringComparison.OrdinalIgnoreCase)) return "Documents";
        if (action.Contains("Calendar", StringComparison.OrdinalIgnoreCase)) return "Calendar";
        if (action.Contains("Master", StringComparison.OrdinalIgnoreCase)) return ListsCategory;
        if (action.Contains("PdfIngestion", StringComparison.OrdinalIgnoreCase)) return PdfCopyCategory;
        if (action.Contains("LegacyImport", StringComparison.OrdinalIgnoreCase)) return LegacyImportCategory;
        if (action.Contains("Ingestion", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Import", StringComparison.OrdinalIgnoreCase)) return "Maintenance";
        if (ContainsAny(action, "Restore", "Trash", "Purge", "Recycle")) return "Recovery";
        return "Other";
    }

    private static string? InferEntityType(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return null;
        if (action.Contains("User", StringComparison.OrdinalIgnoreCase)) return "ApplicationUser";
        if (action.Contains("Project", StringComparison.OrdinalIgnoreCase)) return "Project";
        if (action.Contains("Document", StringComparison.OrdinalIgnoreCase)) return "Document";
        if (action.Contains("Calendar", StringComparison.OrdinalIgnoreCase)) return "Event";
        return null;
    }

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
}
