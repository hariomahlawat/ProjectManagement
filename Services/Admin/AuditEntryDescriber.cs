using System.Globalization;
using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Admin;

/// <summary>The kinds of record an audit entry can name, and the words shown for each.</summary>
public static class AuditRecordKinds
{
    /// <summary>A person, keyed by user id.</summary>
    public const string Person = "Person";

    /// <summary>A person, keyed by the user name typed at sign-in (the row kept no id).</summary>
    public const string PersonUserName = "PersonUserName";

    public const string Project = "Project";
    public const string BriefingDeck = "BriefingDeck";
    public const string ArppIssue = "ArppIssue";

    /// <summary>A whole list (Data checks put one back in order), keyed by the check's key.</summary>
    public const string List = "List";

    public const string ProjectCategory = "ProjectCategory";
    public const string TechnicalCategory = "TechnicalCategory";
    public const string ProjectType = "ProjectType";
    public const string SponsoringUnit = "SponsoringUnit";
    public const string LineDirectorate = "LineDirectorate";
    public const string ActivityType = "ActivityType";
    public const string Holiday = "Holiday";
    public const string Celebration = "Celebration";

    private static readonly IReadOnlyDictionary<string, string> TypeLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Person] = "Person",
            [PersonUserName] = "Person",
            [Project] = "Project",
            [BriefingDeck] = "Briefing deck",
            [ArppIssue] = "ARPP issue",
            [List] = "List",
            [ProjectCategory] = "Project category",
            [TechnicalCategory] = "Technical category",
            [ProjectType] = "Project type",
            [SponsoringUnit] = "Sponsoring unit",
            [LineDirectorate] = "Line directorate",
            [ActivityType] = "Activity type",
            [Holiday] = "Holiday",
            [Celebration] = "Celebration"
        };

    /// <summary>Entity types stored by <see cref="AdminAuditService"/> that name a list item.</summary>
    public static readonly IReadOnlyCollection<string> ListItemKinds = new[]
    {
        ProjectCategory, TechnicalCategory, ProjectType, SponsoringUnit, LineDirectorate,
        ActivityType, Holiday, Celebration
    };

    // The keys Data checks store when it puts a list back in order, and the list's name.
    private static readonly IReadOnlyDictionary<string, string> ListNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["project-category-order"] = "Project categories",
            ["technical-category-order"] = "Technical categories",
            ["project-type-order"] = "Project types",
            ["sponsoring-unit-order"] = "Sponsoring units",
            ["line-directorate-order"] = "Line directorates",
            ["document-category-order"] = "Repository document types",
            ["office-category-order"] = "Repository offices"
        };

    public static string TypeLabel(string kind) =>
        TypeLabels.TryGetValue(kind, out var label) ? label : kind;

    public static string? ListName(string key) =>
        ListNames.TryGetValue(key.Trim(), out var name) ? name : null;
}

/// <summary>A record an audit entry names, before its name is looked up.</summary>
/// <param name="Kind">One of <see cref="AuditRecordKinds"/>.</param>
/// <param name="Key">The record's id (or user name, or list key) as stored.</param>
/// <param name="FallbackName">A name the entry stored itself, shown when the record is gone.</param>
public sealed record AuditRecordRef(string Kind, string Key, string? FallbackName = null);

/// <summary>The record as shown: its name, and what kind of record it is.</summary>
public sealed record AuditRecordDisplay(string Name, string TypeLabel)
{
    public override string ToString() => $"{TypeLabel}: {Name}";
}

/// <summary>
/// What an audit entry says in plain words.
/// </summary>
/// <param name="Detail">One plain sentence, or null when the event's title says it all.
/// "{name}" stands for <paramref name="Mention"/>'s name.</param>
/// <param name="Record">The record the entry changed, when it is known from the entry.</param>
/// <param name="Mention">A second record named in the detail (the project inside a deck).</param>
/// <param name="ActorNotRecorded">True for old account entries whose user columns hold the
/// affected account, not the person who acted.</param>
public sealed record AuditEntryText(
    string? Detail,
    AuditRecordRef? Record,
    AuditRecordRef? Mention = null,
    bool ActorNotRecorded = false);

/// <summary>
/// Turns a stored audit entry into a plain detail and the record it names. Runs at display
/// time from what every entry already stores (the action, the message, the user columns and
/// the details JSON), so old entries read as well as new ones; nothing stored changes
/// (docs/ux/admin-review-4-fixes.md, PR 7).
/// </summary>
public static partial class AuditEntryDescriber
{
    public const string MentionToken = "{name}";

    public static AuditEntryText Describe(
        string? action,
        string? message,
        string? userId,
        string? userName,
        AdminAuditPayload? payload,
        string? title = null)
    {
        var code = action?.Trim() ?? string.Empty;
        payload ??= new AdminAuditPayload(null, null, null, null, null, null, null, null, null, null, null, null);
        var text = DescribeKnown(code, message, userId, userName, payload)
            ?? new AuditEntryText(Clean(message, code), RecordFromPayload(code, payload));

        // A detail that only repeats the event's title adds nothing.
        if (text.Detail is { } detail && title is not null
            && string.Equals(detail.Trim().TrimEnd('.'), title.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
        {
            text = text with { Detail = null };
        }

        return text;
    }

    /// <summary>Puts the mentioned record's name into the detail.</summary>
    public static string? Fill(string? detail, string? mentionName) =>
        detail is null || !detail.Contains(MentionToken, StringComparison.Ordinal)
            ? detail
            : detail.Replace(MentionToken, string.IsNullOrWhiteSpace(mentionName) ? "a project" : mentionName, StringComparison.Ordinal);

    private static AuditEntryText? DescribeKnown(
        string code,
        string? message,
        string? userId,
        string? userName,
        AdminAuditPayload payload)
    {
        // Sign-ins: the record is the account that signed in, or the user name that was typed.
        if (string.Equals(code, AuthenticationEventNames.AuditLoginSuccess, StringComparison.OrdinalIgnoreCase))
        {
            return new AuditEntryText(null, PersonFromRow(userId, userName));
        }

        if (string.Equals(code, AuthenticationEventNames.AuditLoginFailed, StringComparison.OrdinalIgnoreCase))
        {
            var detail = message?.StartsWith("Not allowed", StringComparison.OrdinalIgnoreCase) == true
                ? "This account is not allowed to sign in."
                : "Wrong user name or password.";
            return new AuditEntryText(detail, PersonFromRow(userId, userName));
        }

        if (string.Equals(code, AuthenticationEventNames.AuditLoginLockedOut, StringComparison.OrdinalIgnoreCase))
        {
            return new AuditEntryText(
                "Too many wrong passwords, so the account is locked for a while.",
                PersonFromRow(userId, userName));
        }

        if (code.StartsWith("AdminUser", StringComparison.OrdinalIgnoreCase))
        {
            return DescribeAccount(code, userId, userName, payload);
        }

        if (string.Equals(code, "MasterData.IntegrityOrderNormalised", StringComparison.OrdinalIgnoreCase))
        {
            var count = FirstNumber(message, CountedRecordsRegex());
            var record = payload.EntityId is { } key
                ? new AuditRecordRef(AuditRecordKinds.List, key, AuditRecordKinds.ListName(key))
                : null;
            var detail = count switch
            {
                null when message?.Contains("already", StringComparison.OrdinalIgnoreCase) == true
                    => "The list was already in order.",
                null => Clean(message, code),
                0 => "The list was already in order.",
                _ => $"Put {Count(count.Value, "item")} back in order."
            };
            return new AuditEntryText(detail, record);
        }

        if (code.StartsWith("PdfIngestion", StringComparison.OrdinalIgnoreCase))
        {
            // "External PDF ingestion started." only repeats the title.
            return new AuditEntryText(
                message?.StartsWith("External PDF ingestion", StringComparison.OrdinalIgnoreCase) == true
                    ? null
                    : Clean(message, code),
                null);
        }

        if (code.StartsWith("AdminExport.", StringComparison.OrdinalIgnoreCase))
        {
            var rows = FirstNumber(message, RowsRegex());
            return new AuditEntryText(rows is null ? Clean(message, code) : $"{Count(rows.Value, "row")} exported.", null);
        }

        if (code.StartsWith("ProjectBriefing.", StringComparison.OrdinalIgnoreCase))
        {
            return DescribeDeck(code, message, payload);
        }

        if (code.StartsWith("Arpp.", StringComparison.OrdinalIgnoreCase))
        {
            return DescribeArpp(code, message, payload);
        }

        if (string.Equals(code, "project.video.upload", StringComparison.OrdinalIgnoreCase))
        {
            var match = message is null ? null : VideoRegex().Match(message);
            return new AuditEntryText(
                match is { Success: true } ? $"Video “{match.Groups[1].Value}” added." : null,
                ProjectFromPayload(payload));
        }

        if (code.StartsWith("Project.Content.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, "Project.ProliferationProfileUpdated", StringComparison.OrdinalIgnoreCase))
        {
            // "Updated project content for 'X'." — the project is in the record column.
            return new AuditEntryText(null, ProjectFromPayload(payload));
        }

        return null;
    }

    private static AuditEntryText DescribeAccount(
        string code,
        string? userId,
        string? userName,
        AdminAuditPayload payload)
    {
        var reason = payload.Reason ?? payload.Field("Reason");
        var reasonText = reason is null ? null : $"Reason: {EndSentence(reason)}";

        // Entries written through AdminAuditService name the account in EntityId and the person
        // who acted in ActorUserId; their message is only the account's user name.
        if (payload.EntityType is not null)
        {
            var record = string.IsNullOrWhiteSpace(payload.EntityId)
                ? null
                : new AuditRecordRef(AuditRecordKinds.Person, payload.EntityId.Trim());
            var detail = string.Equals(code, "AdminUserPasswordReset", StringComparison.OrdinalIgnoreCase)
                ? "A new temporary password was set; it must be changed at the next sign-in."
                : reasonText;
            return new AuditEntryText(detail, record);
        }

        // Older entries kept the affected account in the user columns, and the person who acted
        // only sometimes, as "Actor" in the details.
        string? oldDetail = code switch
        {
            _ when string.Equals(code, "AdminUserRolesUpdated", StringComparison.OrdinalIgnoreCase)
                => RolesChanged(payload.Field("Added"), payload.Field("Removed")),
            _ when string.Equals(code, "AdminUserCreated", StringComparison.OrdinalIgnoreCase)
                => payload.Field("Roles") is { } roles ? $"Roles: {roles}." : null,
            _ => reasonText
        };
        return new AuditEntryText(
            oldDetail,
            PersonFromRow(userId, userName),
            ActorNotRecorded: payload.Field("Actor") is null);
    }

    private static string? RolesChanged(string? added, string? removed)
    {
        var parts = new List<string>();
        if (added is not null) parts.Add($"Added {added}.");
        if (removed is not null) parts.Add($"Removed {removed}.");
        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private static AuditEntryText DescribeDeck(string code, string? message, AdminAuditPayload payload)
    {
        var record = payload.Field("DeckId") is { } deckId
            ? new AuditRecordRef(AuditRecordKinds.BriefingDeck, deckId, payload.Field("DeckName"))
            : null;
        var mention = payload.Field("ProjectId") is { } projectId
            ? new AuditRecordRef(AuditRecordKinds.Project, projectId, payload.Field("ProjectName"))
            : null;
        var name = code["ProjectBriefing.".Length..];

        string? detail = name switch
        {
            "AdditionalSlideEnabled" => SlideSentence(message, "switched on"),
            "AdditionalSlideDisabled" => SlideSentence(message, "switched off"),
            "AdditionalSlideRemoved" => SlideSentence(message, "removed from the deck"),
            "ProjectsAdded" => FirstNumber(message, LeadingNumberRegex()) is { } added
                ? $"{Count(added, "project")} added."
                : null,
            "MembershipUpdated" => MembershipSentence(message),
            "DeckDeleted" => FirstNumber(message, DaysRegex()) is { } days
                ? $"It can be restored for {Count(days, "day")}."
                : null,
            "PowerPointGenerated" => int.TryParse(payload.Field("SlideCount"), out var slides)
                ? $"{Count(slides, "slide")}."
                : null,
            "PresetApplied" => message is not null && PresetRegex().Match(message) is { Success: true } preset
                ? $"Set from the “{preset.Groups[1].Value}” template."
                : null,
            "DeckDuplicated" => "Copied from another deck.",
            "ProjectRemoved" or "DescriptionUpdated" or "SpeakerNotesUpdated"
                or "ItemPresentationChanged" or "ItemPhotoChanged" => mention is null ? null : $"Project: {MentionToken}.",
            // The other deck events say everything in their title.
            _ => null
        };

        return new AuditEntryText(detail, record, detail?.Contains(MentionToken, StringComparison.Ordinal) == true ? mention : null);
    }

    private static string? SlideSentence(string? message, string what)
    {
        var code = message?.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (code is null || !Enum.TryParse<ProjectManagement.Models.ProjectBriefings.ProjectBriefingAdditionalSlideType>(code, out var slide))
        {
            return null;
        }

        var slideName = slide switch
        {
            ProjectManagement.Models.ProjectBriefings.ProjectBriefingAdditionalSlideType.InstitutionalProfile => "SDD profile",
            ProjectManagement.Models.ProjectBriefings.ProjectBriefingAdditionalSlideType.RoleAndCharter => "Role and charter",
            ProjectManagement.Models.ProjectBriefings.ProjectBriefingAdditionalSlideType.FfcGlobalFootprint => "FFC global footprint",
            _ => code
        };
        return $"{slideName} slide {what}.";
    }

    private static string? MembershipSentence(string? message)
    {
        if (message is null) return null;
        var match = MembershipRegex().Match(message);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var added)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var removed))
        {
            return null;
        }

        return (added, removed) switch
        {
            (0, 0) => "No projects added or removed.",
            (_, 0) => $"{Count(added, "project")} added.",
            (0, _) => $"{Count(removed, "project")} removed.",
            _ => $"{Count(added, "project")} added and {removed} removed."
        };
    }

    private static AuditEntryText DescribeArpp(string code, string? message, AdminAuditPayload payload)
    {
        var name = code["Arpp.".Length..];
        var issue = payload.Field("IssueId") is { } issueId
            ? new AuditRecordRef(AuditRecordKinds.ArppIssue, issueId, payload.Field("IssueName"))
            : null;
        var project = ProjectFromPayload(payload);

        switch (name)
        {
            case "IpaStageSynchronized":
            {
                var source = payload.Field("AuthorityDocument") is { } document
                    ? document.StartsWith("Addendum", StringComparison.OrdinalIgnoreCase) ? document : $"the {document}"
                    : "the published ARPP";
                var detail = Date(payload.Field("CompletionDate")) is { } completed
                    ? $"IPA stage set as completed on {completed}, from {source}."
                    : $"IPA stage set from {source}.";
                return new AuditEntryText(detail, project);
            }
            case "IpaStageDataQualityIssue":
            {
                var start = Date(payload.Field("ActualStart"));
                var completed = Date(payload.Field("CompletionDate"));
                return new AuditEntryText(
                    start is not null && completed is not null
                        ? $"The IPA start ({start}) is later than the ARPP completion date ({completed})."
                        : null,
                    project);
            }
            case "IpaStageStartupReconciled":
            {
                var changed = payload.Field("ChangedProjectCount");
                var checkedCount = payload.Field("EvaluatedProjectCount");
                return new AuditEntryText(
                    int.TryParse(changed, out var c) && int.TryParse(checkedCount, out var e)
                        ? $"{c:N0} of {Count(e, "project")} brought in line with the published ARPP."
                        : Clean(message, code),
                    null);
            }
            case "IssueCreated":
                return new AuditEntryText(
                    payload.Field("FinancialYear") is { } year ? $"Created for FY {year}." : null,
                    issue);
            case "IssueVerified":
                return new AuditEntryText(
                    payload.Field("VerificationNote") is { } note ? $"Checked and locked. Note: {EndSentence(note)}" : "Checked and locked.",
                    issue);
            case "IssueUnlocked":
                return new AuditEntryText(
                    payload.Field("Reason") is { } reason ? $"Unlocked for correction. Reason: {EndSentence(reason)}" : "Unlocked for correction.",
                    issue);
            case "WorkspaceSaved":
            {
                var detail = int.TryParse(payload.Field("EntryCount"), out var entries)
                    ? int.TryParse(payload.Field("LinkedProjectCount"), out var linked)
                        ? $"Saved with {Count(entries, "row")}, {linked:N0} linked to projects."
                        : $"Saved with {Count(entries, "row")}."
                    : null;
                return new AuditEntryText(detail, issue);
            }
            case "AttachmentUploaded":
                return new AuditEntryText(
                    payload.Field("FileName") is { } file ? $"Attached {file}." : null,
                    issue);
            case "EntriesReconciled":
            {
                var issueIds = payload.Field("IssueIds");
                var single = issueIds is not null && !issueIds.Contains(',')
                    ? new AuditRecordRef(AuditRecordKinds.ArppIssue, issueIds)
                    : null;
                return new AuditEntryText(Clean(message, code), issue ?? single);
            }
            default:
                return new AuditEntryText(Clean(message, code), issue ?? project);
        }
    }

    private static AuditRecordRef? RecordFromPayload(string code, AdminAuditPayload payload)
    {
        if (payload.EntityType is { } entityType && payload.EntityId is { } entityId)
        {
            var kind = AuditRecordKinds.ListItemKinds
                .FirstOrDefault(item => string.Equals(item, entityType.Trim(), StringComparison.OrdinalIgnoreCase));
            if (kind is not null) return new AuditRecordRef(kind, entityId.Trim());
            if (string.Equals(entityType.Trim(), "Project", StringComparison.OrdinalIgnoreCase))
                return new AuditRecordRef(AuditRecordKinds.Project, entityId.Trim());
            if (entityType.Contains("User", StringComparison.OrdinalIgnoreCase))
                return new AuditRecordRef(AuditRecordKinds.Person, entityId.Trim());
            return null;
        }

        return ProjectFromPayload(payload);
    }

    private static AuditRecordRef? ProjectFromPayload(AdminAuditPayload payload) =>
        payload.Field("ProjectId") is { } projectId && int.TryParse(projectId, out _)
            ? new AuditRecordRef(
                AuditRecordKinds.Project,
                projectId,
                payload.Field("ProjectName") ?? payload.Field("Name"))
            : null;

    private static AuditRecordRef? PersonFromRow(string? userId, string? userName)
    {
        if (!string.IsNullOrWhiteSpace(userId) && !AuditActorNames.IsSystemMarker(userId))
            return new AuditRecordRef(AuditRecordKinds.Person, userId.Trim(), userName?.Trim());
        if (!string.IsNullOrWhiteSpace(userName) && !AuditActorNames.IsSystemMarker(userName))
            return new AuditRecordRef(AuditRecordKinds.PersonUserName, userName.Trim(), userName.Trim());
        return null;
    }

    /// <summary>
    /// Tidies a stored message for display: "5 record(s)" becomes "5 records", "1 project(s)"
    /// becomes "1 project". An empty message, or one that is only the action code, is dropped.
    /// </summary>
    public static string? Clean(string? message, string? code = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var text = message.Trim();
        if (code is not null && string.Equals(text, code, StringComparison.OrdinalIgnoreCase)) return null;

        text = CountedPluralRegex().Replace(text, match =>
            // A number too big to count keeps its text; only "(s)" is tidied below.
            int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? Count(number, match.Groups[2].Value)
                : match.Value);
        return text.Replace("(s)", "s", StringComparison.Ordinal);
    }

    /// <summary>"1 project", "49 projects".</summary>
    public static string Count(int number, string noun) =>
        number == 1 ? $"1 {noun}" : $"{number.ToString("N0", CultureInfo.InvariantCulture)} {Plural(noun)}";

    private static string Plural(string noun) =>
        noun.EndsWith('y') && noun.Length > 1 && !"aeiou".Contains(noun[^2]) ? noun[..^1] + "ies" : noun + "s";

    private static int? FirstNumber(string? message, Regex pattern)
    {
        if (message is null) return null;
        var match = pattern.Match(message);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? Date(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : null;

    private static string EndSentence(string value)
    {
        var trimmed = value.Trim();
        return trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?') ? trimmed : trimmed + ".";
    }

    [GeneratedRegex(@"\b(\d+) ([A-Za-z]+)\(s\)")]
    private static partial Regex CountedPluralRegex();

    [GeneratedRegex(@"(\d+) record")]
    private static partial Regex CountedRecordsRegex();

    [GeneratedRegex(@"(\d+) rows?\b")]
    private static partial Regex RowsRegex();

    [GeneratedRegex(@"^\s*(\d+)")]
    private static partial Regex LeadingNumberRegex();

    [GeneratedRegex(@"(\d+) days?")]
    private static partial Regex DaysRegex();

    [GeneratedRegex(@"(\d+) added, (\d+) removed")]
    private static partial Regex MembershipRegex();

    [GeneratedRegex(@"from the (.+) preset", RegexOptions.IgnoreCase)]
    private static partial Regex PresetRegex();

    [GeneratedRegex(@"Video '(.+)' uploaded", RegexOptions.IgnoreCase)]
    private static partial Regex VideoRegex();
}
