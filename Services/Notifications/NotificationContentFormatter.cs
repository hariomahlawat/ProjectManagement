using System;
using System.Globalization;
using System.Text.RegularExpressions;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

/// <summary>
/// Produces concise, user-facing notification text from current and retained legacy rows.
/// Durable notification data remains unchanged; presentation is normalised at read time.
/// </summary>
public static class NotificationContentFormatter
{
    private const int SummaryDisplayMaxLength = 112;

    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex StageTransitionRegex = new(
        @"(?:stage\s+[A-Za-z0-9/_-]+\s+)?(?:moved|changed)\s+from\s+(?<from>[A-Za-z0-9_ -]+?)\s+to\s+(?<to>[A-Za-z0-9_ -]+?)(?:\.|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StageCodeBeforeWordRegex = new(
        @"\b(?<code>[A-Z][A-Z0-9/_-]{1,15})\s+stage\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StageCodeAfterWordRegex = new(
        @"\bstage\s+(?<code>[A-Z][A-Z0-9/_-]{1,15})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DocumentSentenceRegex = new(
        @"^document\s+(?<label>.+?)\s+was\s+(?:published|updated|replaced|archived|restored|deleted)\.?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GeneratedDocumentSuffixRegex = new(
        @"^(?<label>.*?\S)\s*[-–]\s*(?<ordinal>\d+)[-_](?<generated>\d{8,}(?:[_-]\d+){1,}.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DocumentReferenceTailRegex = new(
        @"(?:^|[_-])(?<reference>\d{5,})[_-](?<part>\d{1,3})[_-](?<year>20\d{2})(?:\.[A-Za-z0-9]{1,10})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ProjectOfficerTransitionRegex = new(
        @"(?:project\s+officer\s+assignment\s+)?(?:changed|updated)\s+from\s+(?<from>.+?)\s+to\s+(?<to>.+?)(?:\.|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ProjectOfficerAssignedRegex = new(
        @"^assigned\s+to\s+(?<to>.+?)(?:\.|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ProjectOfficerUnassignedRegex = new(
        @"^removed\s+(?<from>.+?)\s+as\s+project\s+officer(?:\.|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ReviewSuffixRegex = new(
        @"\s*Review\s+the\s+project\s+overview.*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CamelCaseBoundaryRegex = new(
        @"(?<=[a-z0-9])(?=[A-Z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static NotificationContent Format(
        NotificationKind? kind,
        string? module,
        string? eventType,
        string? scopeId,
        string? projectName,
        string? storedTitle,
        string? storedSummary)
    {
        if (kind == NotificationKind.StageStatusChanged
            || (!kind.HasValue && LooksLikeStage(module, eventType, storedTitle, storedSummary)))
        {
            return FormatStage(scopeId, projectName, storedTitle, storedSummary);
        }

        if (kind == NotificationKind.ProjectAssignmentChanged
            || Contains(eventType, "ProjectOfficerAssignment"))
        {
            return FormatProjectOfficerAssignment(storedTitle, storedSummary);
        }

        if (kind == NotificationKind.RoleAssignmentsChanged)
        {
            return FormatRoleAssignment(storedSummary);
        }

        var documentAction = ResolveDocumentAction(kind, module, eventType, storedTitle, storedSummary);
        if (documentAction is not null)
        {
            return FormatDocument(documentAction, projectName, storedTitle, storedSummary);
        }

        return FormatGeneral(eventType, projectName, storedTitle, storedSummary);
    }

    private static NotificationContent FormatStage(
        string? scopeId,
        string? projectName,
        string? storedTitle,
        string? storedSummary)
    {
        var titleWithoutProject = StripProjectPrefix(Collapse(storedTitle), projectName);
        var stageCode = ResolveStageCode(scopeId, titleWithoutProject, storedSummary);
        var transition = ResolveTransition(storedSummary);
        var currentStatus = transition.Current
            ?? ResolveStatusFromTitle(titleWithoutProject, stageCode)
            ?? "updated";

        var statusText = HumanizeToken(currentStatus).ToLowerInvariant();
        var title = string.IsNullOrWhiteSpace(stageCode)
            ? $"Stage {statusText}"
            : $"{stageCode} stage {statusText}";

        string? summary;
        if (!string.IsNullOrWhiteSpace(transition.Previous)
            && !string.IsNullOrWhiteSpace(transition.Current))
        {
            summary = string.Format(
                CultureInfo.InvariantCulture,
                "Status changed from {0} to {1}.",
                HumanizeToken(transition.Previous),
                HumanizeToken(transition.Current));
        }
        else
        {
            summary = Collapse(storedSummary);
            if (!string.IsNullOrWhiteSpace(stageCode))
            {
                summary = Regex.Replace(
                    summary,
                    $@"^stage\s+{Regex.Escape(stageCode)}\s+",
                    string.Empty,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
            }
        }

        return new NotificationContent(title, NullIfEmpty(summary), null);
    }

    private static NotificationContent FormatProjectOfficerAssignment(
        string? storedTitle,
        string? storedSummary)
    {
        var summary = ReviewSuffixRegex.Replace(Collapse(storedSummary), string.Empty).Trim();

        var assigned = ProjectOfficerAssignedRegex.Match(summary);
        if (assigned.Success)
        {
            var assignedOfficer = CleanAssignmentParty(assigned.Groups["to"].Value);
            return new NotificationContent(
                "Project officer assigned",
                $"Assigned to {assignedOfficer}.",
                null);
        }

        var unassigned = ProjectOfficerUnassignedRegex.Match(summary);
        if (unassigned.Success)
        {
            var previousOfficer = CleanAssignmentParty(unassigned.Groups["from"].Value);
            return new NotificationContent(
                "Project officer unassigned",
                $"Removed {previousOfficer} as project officer.",
                null);
        }

        var match = ProjectOfficerTransitionRegex.Match(summary);
        if (!match.Success)
        {
            var title = Collapse(storedTitle);
            if (!title.StartsWith("Project officer", StringComparison.OrdinalIgnoreCase))
            {
                title = "Project officer assignment updated";
            }

            return new NotificationContent(
                CapitalizeFirst(title),
                NullIfEmpty(summary),
                null);
        }

        var previous = CleanAssignmentParty(match.Groups["from"].Value);
        var current = CleanAssignmentParty(match.Groups["to"].Value);
        var previousUnassigned = IsUnassigned(previous);
        var currentUnassigned = IsUnassigned(current);

        if (previousUnassigned && !currentUnassigned)
        {
            return new NotificationContent(
                "Project officer assigned",
                $"Assigned to {current}.",
                null);
        }

        if (!previousUnassigned && currentUnassigned)
        {
            return new NotificationContent(
                "Project officer unassigned",
                $"Removed {previous} as project officer.",
                null);
        }

        if (!previousUnassigned && !currentUnassigned)
        {
            return new NotificationContent(
                "Project officer changed",
                $"Changed from {previous} to {current}.",
                null);
        }

        return new NotificationContent(
            "Project officer assignment updated",
            null,
            null);
    }

    private static NotificationContent FormatRoleAssignment(string? storedSummary)
    {
        var summary = ReviewSuffixRegex.Replace(Collapse(storedSummary), string.Empty).Trim();
        return new NotificationContent(
            "Project roles updated",
            NullIfEmpty(summary),
            null);
    }

    private static NotificationContent FormatDocument(
        string title,
        string? projectName,
        string? storedTitle,
        string? storedSummary)
    {
        var fullLabel = ResolveDocumentLabel(projectName, storedTitle, storedSummary);
        if (string.IsNullOrWhiteSpace(fullLabel))
        {
            return new NotificationContent(title, null, null);
        }

        var displayLabel = CapitalizeFirst(CompactDocumentLabel(fullLabel));
        var tooltip = string.Equals(displayLabel, fullLabel, StringComparison.Ordinal)
            ? null
            : fullLabel;

        return new NotificationContent(title, displayLabel, tooltip);
    }

    private static NotificationContent FormatGeneral(
        string? eventType,
        string? projectName,
        string? storedTitle,
        string? storedSummary)
    {
        var title = StripProjectPrefix(Collapse(storedTitle), projectName);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = HumanizeToken(eventType);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Notification";
        }

        title = CapitalizeFirst(title);
        var summary = Collapse(storedSummary);
        return new NotificationContent(title, NullIfEmpty(summary), null);
    }

    private static string? ResolveDocumentAction(
        NotificationKind? kind,
        string? module,
        string? eventType,
        string? storedTitle,
        string? storedSummary)
    {
        if (kind.HasValue)
        {
            return kind.Value switch
            {
                NotificationKind.DocumentPublished => "Document published",
                NotificationKind.DocumentReplaced => "Document updated",
                NotificationKind.DocumentArchived => "Document archived",
                NotificationKind.DocumentRestored => "Document restored",
                NotificationKind.DocumentDeleted => "Document deleted",
                _ => null,
            };
        }

        var combined = string.Join(
            " ",
            Collapse(module),
            Collapse(eventType),
            Collapse(storedTitle),
            Collapse(storedSummary));
        if (!combined.Contains("document", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (ContainsAny(combined, "deleted", "delete")) return "Document deleted";
        if (ContainsAny(combined, "restored", "restore")) return "Document restored";
        if (ContainsAny(combined, "archived", "archive")) return "Document archived";
        if (ContainsAny(combined, "replaced", "replace", "updated", "update")) return "Document updated";
        if (ContainsAny(combined, "published", "publish")) return "Document published";

        return "Document updated";
    }

    private static bool LooksLikeStage(
        string? module,
        string? eventType,
        string? storedTitle,
        string? storedSummary)
    {
        var combined = string.Join(
            " ",
            Collapse(module),
            Collapse(eventType),
            Collapse(storedTitle),
            Collapse(storedSummary));

        return combined.Contains("stage", StringComparison.OrdinalIgnoreCase)
            && (combined.Contains("status", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("moved", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("completed", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("inprogress", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("notstarted", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveDocumentLabel(
        string? projectName,
        string? storedTitle,
        string? storedSummary)
    {
        var summary = Collapse(storedSummary);
        var sentenceMatch = DocumentSentenceRegex.Match(summary);
        if (sentenceMatch.Success)
        {
            summary = sentenceMatch.Groups["label"].Value;
        }

        summary = StripProjectPrefix(summary, projectName);
        summary = Regex.Replace(
            summary,
            @"^document\s+",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

        if (!string.IsNullOrWhiteSpace(summary))
        {
            return summary;
        }

        var title = StripProjectPrefix(Collapse(storedTitle), projectName);
        title = Regex.Replace(
            title,
            @"^document\s+",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        title = Regex.Replace(
            title,
            @"\s+(?:published|updated|replaced|archived|restored|deleted)$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

        return title;
    }

    private static string CompactDocumentLabel(string value)
    {
        var normalized = Collapse(value);
        var generatedSuffix = GeneratedDocumentSuffixRegex.Match(normalized);
        if (generatedSuffix.Success)
        {
            var label = generatedSuffix.Groups["label"].Value.Trim();
            var ordinal = generatedSuffix.Groups["ordinal"].Value;
            var generated = generatedSuffix.Groups["generated"].Value.Trim();
            var reference = DocumentReferenceTailRegex.Match(generated);

            normalized = reference.Success
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} – {1} · {2}/{3}/{4}",
                    label,
                    ordinal,
                    reference.Groups["reference"].Value,
                    reference.Groups["part"].Value,
                    reference.Groups["year"].Value)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} – {1} · {2}",
                    label,
                    ordinal,
                    CompactIdentifier(generated));
        }

        if (normalized.Length <= SummaryDisplayMaxLength)
        {
            return normalized;
        }

        const int trailingLength = 30;
        var leadingLength = SummaryDisplayMaxLength - trailingLength - 1;
        return normalized[..leadingLength].TrimEnd()
            + "…"
            + normalized[^trailingLength..].TrimStart();
    }

    private static string CompactIdentifier(string value)
    {
        var normalized = Collapse(value);
        const int maxLength = 28;
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        const int trailingLength = 14;
        var leadingLength = maxLength - trailingLength - 1;
        return normalized[..leadingLength] + "…" + normalized[^trailingLength..];
    }

    private static string ResolveStageCode(string? scopeId, string? title, string? summary)
    {
        var normalizedScope = Collapse(scopeId);
        if (!string.IsNullOrWhiteSpace(normalizedScope))
        {
            var separator = normalizedScope.LastIndexOf(':');
            var candidate = separator >= 0 ? normalizedScope[(separator + 1)..] : normalizedScope;
            candidate = candidate.Trim();
            if (candidate.Length is > 0 and <= 16)
            {
                return candidate.ToUpperInvariant();
            }
        }

        foreach (var source in new[] { Collapse(title), Collapse(summary) })
        {
            var before = StageCodeBeforeWordRegex.Match(source);
            if (before.Success)
            {
                return before.Groups["code"].Value.ToUpperInvariant();
            }

            var after = StageCodeAfterWordRegex.Match(source);
            if (after.Success)
            {
                return after.Groups["code"].Value.ToUpperInvariant();
            }
        }

        return string.Empty;
    }

    private static (string? Previous, string? Current) ResolveTransition(string? summary)
    {
        var match = StageTransitionRegex.Match(Collapse(summary));
        if (!match.Success)
        {
            return (null, null);
        }

        return (
            HumanizeToken(match.Groups["from"].Value),
            HumanizeToken(match.Groups["to"].Value));
    }

    private static string? ResolveStatusFromTitle(string? title, string? stageCode)
    {
        var normalized = Collapse(title);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(stageCode))
        {
            normalized = Regex.Replace(
                normalized,
                $@"^(?:stage\s+{Regex.Escape(stageCode)}|{Regex.Escape(stageCode)}\s+stage)\s*",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        }

        normalized = Regex.Replace(
            normalized,
            @"^stage\s+",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

        return NullIfEmpty(HumanizeToken(normalized));
    }

    private static string StripProjectPrefix(string? value, string? projectName)
    {
        var normalized = Collapse(value);
        var project = Collapse(projectName);
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(project))
        {
            return normalized;
        }

        if (!normalized.StartsWith(project, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.Length > project.Length)
        {
            var boundary = normalized[project.Length];
            if (!char.IsWhiteSpace(boundary)
                && boundary != '-'
                && boundary != '–'
                && boundary != ':'
                && boundary != '|')
            {
                return normalized;
            }
        }

        return normalized[project.Length..]
            .TrimStart(' ', '-', '–', ':', '|')
            .Trim();
    }

    private static string CleanAssignmentParty(string value)
        => Collapse(value).Trim(' ', '.', ',', ';', ':');

    private static bool IsUnassigned(string? value)
        => string.IsNullOrWhiteSpace(value)
            || value.Equals("Unassigned", StringComparison.OrdinalIgnoreCase)
            || value.Equals("None", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Not assigned", StringComparison.OrdinalIgnoreCase);

    private static string HumanizeToken(string? value)
    {
        var normalized = Collapse(value).Replace('_', ' ');
        normalized = CamelCaseBoundaryRegex.Replace(normalized, " ");
        normalized = Collapse(normalized);

        return normalized.ToLowerInvariant() switch
        {
            "notstarted" or "not started" => "Not started",
            "inprogress" or "in progress" => "In progress",
            "notapplicable" or "not applicable" => "Not applicable",
            "onhold" or "on hold" => "On hold",
            "completed" => "Completed",
            "skipped" => "Skipped",
            "blocked" => "Blocked",
            "published" => "Published",
            "updated" => "Updated",
            "replaced" => "Replaced",
            "archived" => "Archived",
            "restored" => "Restored",
            "deleted" => "Deleted",
            _ => ToSentenceCase(normalized),
        };
    }

    private static bool ContainsAny(string value, params string[] candidates)
        => Array.Exists(candidates, candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static bool Contains(string? value, string fragment)
        => !string.IsNullOrWhiteSpace(value)
            && value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static string CapitalizeFirst(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string ToSentenceCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lower = value.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private static string Collapse(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : WhitespaceRegex.Replace(value.Trim(), " ");

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record NotificationContent(
    string Title,
    string? Summary,
    string? SummaryTooltip);
