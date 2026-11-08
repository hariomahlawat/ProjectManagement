using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.ItProjects;

/// <summary>
/// One of the nine fixed stages of an IT project (<see cref="ItProjectStages"/>). Nine rows are
/// created with the project. Completion is a flag; the date is optional.
/// </summary>
public class ItProjectStage
{
    public int Id { get; set; }

    public int ItProjectId { get; set; }

    public ItProject? ItProject { get; set; }

    [Required, MaxLength(8)]
    public string StageCode { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsCompleted { get; set; }

    public DateOnly? CompletedOn { get; set; }

    /// <summary>Only Benchmarking and PNC may be skipped.</summary>
    public bool IsSkipped { get; set; }

    /// <summary>SO stage only: the supply order cost in lakhs.</summary>
    public decimal? CostLakhs { get; set; }
}
