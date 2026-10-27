using System;
using System.Globalization;
using ProjectManagement.Models;

namespace ProjectManagement.ViewModels;

/// <summary>
/// Display text for the Completed, Cancelled and All register tables
/// (docs/ux/projects-register.md), read from the project's recorded fields.
/// </summary>
public sealed record ProjectRegisterClosedRowVm(
    string StatusText,
    string StatusTone,
    string CompletedText,
    string TotText,
    string TotTone,
    string RecordText,
    bool IsLegacy,
    string CancelledText,
    string ReasonText,
    string BuildText)
{
    public static ProjectRegisterClosedRowVm Create(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var (statusText, statusTone) = project.LifecycleStatus switch
        {
            ProjectLifecycleStatus.Active => ("Active", "accent"),
            ProjectLifecycleStatus.Completed => ("Completed", "ok"),
            ProjectLifecycleStatus.Cancelled => ("Cancelled", "none"),
            _ => (project.LifecycleStatus.ToString(), "none")
        };

        var (totText, totTone) = TotOf(project);

        return new ProjectRegisterClosedRowVm(
            statusText,
            statusTone,
            CompletedOf(project),
            totText,
            totTone,
            project.IsLegacy ? "Legacy entry" : "Recorded in PRISM",
            project.IsLegacy,
            project.CancelledOn is { } cancelled ? FormatDate(cancelled) : "Not recorded",
            string.IsNullOrWhiteSpace(project.CancelReason) ? "—" : project.CancelReason.Trim(),
            project.IsBuild ? "Repeat build" : "New development");
    }

    // Day when recorded, else month and year, else the year alone.
    private static string CompletedOf(Project project)
    {
        if (project.CompletedOn is { } on)
        {
            return FormatDate(on);
        }

        if (project.CompletedYear is int year)
        {
            return project.CompletedMonth is short month and >= 1 and <= 12
                ? new DateOnly(year, month, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture)
                : year.ToString(CultureInfo.InvariantCulture);
        }

        return "Not recorded";
    }

    // The same wording as the cards' ToT line.
    private static (string Text, string Tone) TotOf(Project project)
    {
        if (project.IsBuild)
        {
            return ("Not applicable", "none");
        }

        return project.Tot?.Status switch
        {
            ProjectTotStatus.Completed => ("Completed", "ok"),
            ProjectTotStatus.InProgress => ("In progress", "accent"),
            ProjectTotStatus.NotStarted => ("Not started", "late"),
            ProjectTotStatus.NotRequired => ("Not required", "none"),
            _ => ("Not recorded", "none")
        };
    }

    private static string FormatDate(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
