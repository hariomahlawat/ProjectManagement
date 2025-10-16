using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

public enum ProjectTotProgressUpdateState
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum ProjectTotUpdateActorRole
{
    ProjectOfficer = 0,
    ProjectOffice = 1,
    HeadOfDepartment = 2,
    Administrator = 3
}

public sealed class ProjectTotProgressUpdate
{
    public int Id { get; set; }

    [Required]
    public int ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    [Required]
    [MaxLength(2000)]
    public string Body { get; set; } = string.Empty;

    public DateOnly? EventDate { get; set; }

    [Required]
    [MaxLength(450)]
    public string SubmittedByUserId { get; set; } = string.Empty;

    public ApplicationUser SubmittedByUser { get; set; } = null!;

    [Required]
    public ProjectTotUpdateActorRole SubmittedByRole { get; set; }
        = ProjectTotUpdateActorRole.ProjectOfficer;

    public DateTime SubmittedOnUtc { get; set; }
        = DateTime.UtcNow;

    [Required]
    public ProjectTotProgressUpdateState State { get; set; }
        = ProjectTotProgressUpdateState.Pending;

    [MaxLength(450)]
    public string? DecidedByUserId { get; set; }

    public ApplicationUser? DecidedByUser { get; set; }

    public ProjectTotUpdateActorRole? DecidedByRole { get; set; }

    public DateTime? DecidedOnUtc { get; set; }

    [MaxLength(2000)]
    public string? DecisionRemarks { get; set; }

    public DateTime? PublishedOnUtc { get; set; }

    [ConcurrencyCheck]
    public byte[] RowVersion { get; set; } = Guid.NewGuid().ToByteArray();
}
