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

/// <summary>
/// What a later year's IT PPP says about a carried-over project: the IT PPP reference number it
/// was given in that year and, optionally, a type set by hand. The project's own year keeps its
/// number on <see cref="ItProject.PppReferenceNumber"/>; no row means "number not yet entered,
/// automatic type".
/// </summary>
public class ItProjectFinancialYear
{
    public const int ReferenceMaxLength = 100;

    public int Id { get; set; }

    public int ItProjectId { get; set; }

    public ItProject? ItProject { get; set; }

    /// <summary>Start year of the financial year, e.g. 2026 for 2026-27.</summary>
    public int FinancialYearStart { get; set; }

    /// <summary>The IT PPP reference number in that year's IT PPP. Free text, not unique.</summary>
    [MaxLength(ReferenceMaxLength)]
    public string? PppReferenceNumber { get; set; }

    /// <summary>A type set by hand for that year; null means the automatic type.</summary>
    public ItProjectYearType? TypeOverride { get; set; }

    [Required, MaxLength(450)]
    public string UpdatedByUserId { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }
}
