using System;
using System.Globalization;
using System.Text.RegularExpressions;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

/// <summary>
/// Produces concise, user-facing notification text from both current and legacy rows.
/// This keeps historical notifications readable without mutating durable audit data.
/// </summary>
public static class NotificationContentFormatter
{
    private const int SummaryDisplayMaxLength = 96;

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
    private static readonly Regex CamelCaseBoundaryRegex = new(
        @"(?<=[a-z0-9])(?=[A-Z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static NotificationContent Format(
        NotificationKind? kind,
        string? eventType,
        string? scopeId,
        string? projectName,
        string? storedTitle,
        string? storedSummary)
    {
        return kind switch
        {
            NotificationKind.StageStatusChanged => FormatStage(scopeId, projectName, storedTitle, storedSummary),
            NotificationKind.DocumentPublished => FormatDocument("Document published", projectName, storedTitle, storedSummary),
            NotificationKind.DocumentReplaced => FormatDocument("Document updated", projectName, storedTitle, storedSummary),
            NotificationKind.DocumentArchived => FormatDocument("Document archived", projectName, storedTitle, storedSummary),
            NotificationKind.DocumentRestored => FormatDocument("Document restored", projectName, storedTitle, storedSummary),
            NotificationKind.DocumentDeleted => FormatDocument("Document deleted", projectName, storedTitle, storedSummary),
            _ => FormatGeneral(eventType, projectName, storedTitle, storedSummary),
        };
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

        var title = string.IsNullOrWhiteSpace(stageCode)
            ? $"Stage {ToTitleStatus(currentStatus)}"
            : $"{stageCode} stage {ToTitleStatus(currentStatus)}";

        string? summary = null;
        if (!string.IsNullOrWhiteSpace(transition.Previous)
            && !string.IsNullOrWhiteSpace(transition.Current))
        {
            summary = string.Format(
                CultureInfo.InvariantCulture,
                "Status changed from {0} to {1}.",
                ToSentenceStatus(transition.Previous),
                ToSentenceStatus(transition.Current));
        }
        else
        {
            summary = Collapse(storedSummary);
            if (!string.IsNullOrWhiteSpace(stageCode))
            {
                summary = Regex.Replace(
                    summary ?? string.Empty,
                    $@"^stage\s+{Regex.Escape(stageCode)}\s+",
                    string.Empty,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
            }
        }

        return new NotificationContent(title, NullIfEmpty(summary), null);
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

        var displayLabel = CompactDocumentLabel(fullLabel);
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

        var summary = Collapse(storedSummary);
        return new NotificationContent(title, NullIfEmpty(summary), null);
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
            normalized = string.Format(CultureInfo.InvariantCulture, "{0} – {1}", label, ordinal);
        }

        if (normalized.Length <= SummaryDisplayMaxLength)
        {
            return normalized;
        }

        const int trailingLength = 24;
        var leadingLength = SummaryDisplayMaxLength - trailingLength - 1;
        return normalized[..leadingLength].TrimEnd()
            + "…"
            + normalized[^trailingLength..].TrimStart();
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

    private static string ToTitleStatus(string value)
        => HumanizeToken(value).ToLowerInvariant();

    private static string ToSentenceStatus(string value)
        => HumanizeToken(value);

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
