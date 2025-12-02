using System;
using ProjectManagement.Models;
using ProjectManagement.Utilities.PartialDates;

namespace ProjectManagement.ViewModels;

public interface ITotStatusMilestoneFields
{
    // SECTION: Identifiers
    int ProjectId { get; set; }

    // SECTION: Status and derived date values
    ProjectTotStatus Status { get; set; }

    DateOnly? StartedOn { get; set; }

    PartialDatePrecision StartDatePrecision { get; set; }

    DateOnly? CompletedOn { get; set; }

    PartialDatePrecision CompletionDatePrecision { get; set; }

    // SECTION: Partial date entry fields
    int? StartYear { get; set; }

    int? StartMonth { get; set; }

    int? StartDay { get; set; }

    int? CompletionYear { get; set; }

    int? CompletionMonth { get; set; }

    int? CompletionDay { get; set; }

    // SECTION: Milestone fields
    string? MetDetails { get; set; }

    DateOnly? MetCompletedOn { get; set; }

    bool? FirstProductionModelManufactured { get; set; }

    DateOnly? FirstProductionModelManufacturedOn { get; set; }
}
