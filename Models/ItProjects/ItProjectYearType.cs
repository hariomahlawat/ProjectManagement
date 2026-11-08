using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.ItProjects;

/// <summary>
/// What an IT project is in one financial year (docs/ux/it-projects-redesign.md, "Year type").
/// New in its own year; in a later year CL when the SO was placed, else CF.
/// </summary>
public enum ItProjectYearType
{
    New = 0,

    /// <summary>CL: committed liability, the SO is placed and delivery is still due.</summary>
    CommittedLiability = 1,

    /// <summary>CF: carried forward without an SO yet.</summary>
    CarriedForward = 2
}

/// <summary>A type set by hand for one project in one financial year. No row means the automatic type.</summary>
public class ItProjectYearTypeOverride
{
    public int Id { get; set; }

    public int ItProjectId { get; set; }

    public ItProject? ItProject { get; set; }

    /// <summary>Start year of the financial year the type applies to.</summary>
    public int FinancialYearStart { get; set; }

    public ItProjectYearType Type { get; set; }

    [Required, MaxLength(450)]
    public string SetByUserId { get; set; } = string.Empty;

    public DateTime SetAtUtc { get; set; }
}
