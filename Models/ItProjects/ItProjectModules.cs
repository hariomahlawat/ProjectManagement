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
    public sealed record Definition(string Code, string Name, string RoutePrefix, bool HasAssignedOfficer);

    public static readonly IReadOnlyList<Definition> All = new[]
    {
        new Definition(It, "IT projects", "/ItProjects", HasAssignedOfficer: false),
        new Definition(Molti, "MOLTI projects", "/MoltiProjects", HasAssignedOfficer: true),
        new Definition(Sre, "SRE projects", "/SreProjects", HasAssignedOfficer: true)
    };

    private static readonly IReadOnlyDictionary<string, Definition> ByCode =
        All.ToDictionary(definition => definition.Code, StringComparer.Ordinal);

    public static bool IsKnown(string? code) => code is not null && ByCode.ContainsKey(code);

    public static Definition Get(string code) =>
        ByCode.TryGetValue(code ?? string.Empty, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown project module.");

    public static bool HasAssignedOfficer(string code) => Get(code).HasAssignedOfficer;
}
