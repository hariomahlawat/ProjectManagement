using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Models.ItProjects;

/// <summary>
/// The modules that run on the IT projects engine (docs/ux/molti-sre-projects.md). For the user
/// they are separate modules; in code a project carries its module and every query is scoped by
/// it. The list lives in code, like <see cref="ItProjectStages"/>. The names are written exactly
/// as the owner gave them: the abbreviations are not expanded.
/// </summary>
public static class ItProjectModules
{
    public const string It = "IT";
    public const string Molti = "MOLTI";
    public const string Sre = "SRE";

    public const int CodeMaxLength = 8;

    /// <param name="Code">The value stored on the project.</param>
    /// <param name="Name">The module's name as the user reads it.</param>
    /// <param name="RoutePrefix">The module's own route.</param>
    /// <param name="HasAssignedOfficer">
    /// Whether each project is run by a Project Officer assigned to it. False for IT projects,
    /// which the ITO runs as a whole.
    /// </param>
    public sealed record Definition(string Code, string Name, string RoutePrefix, bool HasAssignedOfficer)
    {
        /// <summary>One project of the module: "IT project", "MOLTI project".</summary>
        public string Singular => $"{Code} project";

        /// <summary>The yearly list the module's cases belong to: "IT ARPPP", "MOLTI ARPPP" (owner, 11 Nov 2026).</summary>
        public string ListName => $"{Code} ARPPP";

        /// <summary>The Razor page folder, e.g. "/MoltiProjects/Index" is the module's board.</summary>
        public string PageFolder => RoutePrefix;

        /// <summary>Who adds projects, for the empty board of a user who cannot.</summary>
        public string AddedByText => HasAssignedOfficer
            ? "The HoD or an administrator adds them."
            : "The ITO, HoD or an administrator adds them.";

        /// <summary>Who writes remarks, for the empty remarks card of a user who cannot.</summary>
        public string RemarkWritersText => HasAssignedOfficer
            ? "The assigned officer, MCO, HoD and Comdt can write them."
            : "The ITO, MCO, HoD and Comdt can write them.";

        /// <summary>The example in the reference number box. Only IT projects have a known form; the others show none.</summary>
        public string? ReferenceExample => Code == It ? "e.g. IT PPP 09/2026" : null;

        /// <summary>The example in the title box. An IT example would mislead in another module, so the others show none.</summary>
        public string? TitleExample => Code == It ? "e.g. Core switches, Admin block" : null;
    }

    public static readonly IReadOnlyList<Definition> All = new[]
    {
        new Definition(It, "IT projects", "/ItProjects", HasAssignedOfficer: false),
        new Definition(Molti, "MOLTI projects", "/MoltiProjects", HasAssignedOfficer: true),
        new Definition(Sre, "SRE projects", "/SreProjects", HasAssignedOfficer: true)
    };

    private static readonly Definition ItDefinition = All[0];

    private static readonly IReadOnlyDictionary<string, Definition> ByCode =
        All.ToDictionary(definition => definition.Code, StringComparer.Ordinal);

    public static bool IsKnown(string? code) => code is not null && ByCode.ContainsKey(code);

    public static Definition Get(string code) =>
        ByCode.TryGetValue(code ?? string.Empty, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown project module.");

    public static bool HasAssignedOfficer(string code) => Get(code).HasAssignedOfficer;

    /// <summary>
    /// Text written for IT projects, worded for another module: its list name, its plural and its
    /// singular take the place of the IT ones ("IT ARPPP / IPA" reads "MOLTI ARPPP / IPA"). The
    /// engine's rules and messages are written once, for IT projects; this is the one place that
    /// turns them into another module's words, so a MOLTI or SRE page never says "IT". Text for
    /// IT projects, or for an unknown module, comes back unchanged.
    /// </summary>
    public static string Reword(string text, string module)
    {
        if (string.IsNullOrEmpty(text) || module == It || !IsKnown(module))
        {
            return text;
        }

        var definition = Get(module);
        return text
            .Replace(ItDefinition.ListName, definition.ListName, StringComparison.Ordinal)
            .Replace(ItDefinition.Name, definition.Name, StringComparison.Ordinal)
            .Replace(ItDefinition.Singular, definition.Singular, StringComparison.Ordinal);
    }
}
