using System;
using ProjectManagement.Models;

namespace ProjectManagement.ViewModels;

public interface ITotStatusMilestoneFields
{
    ProjectTotStatus Status { get; set; }

    DateOnly? StartedOn { get; set; }

    DateOnly? CompletedOn { get; set; }

    string? MetDetails { get; set; }

    DateOnly? MetCompletedOn { get; set; }

    bool? FirstProductionModelManufactured { get; set; }

    DateOnly? FirstProductionModelManufacturedOn { get; set; }
}
