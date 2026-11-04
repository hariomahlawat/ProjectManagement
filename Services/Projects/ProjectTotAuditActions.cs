using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using ProjectManagement.Models;
using ProjectManagement.Utilities.PartialDates;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// ToT history is kept as <see cref="ProjectAudit"/> rows. These are the action names and the
/// metadata format, shared by the writer (<see cref="ProjectTotService"/>) and the history reader.
/// </summary>
public static class ProjectTotAuditActions
{
    public const string Submitted = "TotRequestSubmitted";
    public const string Approved = "TotRequestApproved";
    public const string Rejected = "TotRequestRejected";
    public const string Updated = "TotUpdated";

    public static readonly IReadOnlyList<string> All = new[] { Submitted, Approved, Rejected, Updated };

    // Mirrors the ProjectAudit column limits (Reason 512, MetadataJson 4000).
    public const int MaxReasonLength = 512;
    private const int MaxMetadataLength = 4000;
    private const int MaxMetDetailsInMetadata = 1500;

    public static string Label(string action) => action switch
    {
        Submitted => "Submitted for approval",
        Approved => "Approved",
        Rejected => "Rejected",
        Updated => "Updated directly",
        _ => action
    };

    public static string SerializeValues(ProjectTotUpdateRequest values, string? submittedByUserId)
    {
        var metadata = new ProjectTotAuditValues(
            values.Status.ToString(),
            PartialDateDisplay.Format(values.StartedOn, values.StartDatePrecision),
            PartialDateDisplay.Format(values.CompletedOn, values.CompletionDatePrecision),
            Shorten(values.MetDetails, MaxMetDetailsInMetadata),
            values.MetCompletedOn?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            values.FirstProductionModelManufactured,
            values.FirstProductionModelManufacturedOn?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            submittedByUserId);

        var json = JsonSerializer.Serialize(metadata);
        if (json.Length <= MaxMetadataLength)
        {
            return json;
        }

        // Heavy escaping can still push the MET text over the column limit; drop it rather than fail.
        return JsonSerializer.Serialize(metadata with { MetDetails = "(too long to keep in history)" });
    }

    public static ProjectTotAuditValues? DeserializeValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProjectTotAuditValues>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Shorten(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}

public sealed record ProjectTotAuditValues(
    string Status,
    string? Started,
    string? Completed,
    string? MetDetails,
    string? MetCompletedOn,
    bool? FirstProductionModelManufactured,
    string? FirstProductionModelManufacturedOn,
    string? SubmittedByUserId)
{
    public string StatusLabel => Enum.TryParse<ProjectTotStatus>(Status, out var status)
        ? status switch
        {
            ProjectTotStatus.NotRequired => "Not required",
            ProjectTotStatus.NotStarted => "Not started",
            ProjectTotStatus.InProgress => "In progress",
            ProjectTotStatus.Completed => "Completed",
            _ => Status
        }
        : Status;
}
