using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

public sealed record ProliferationFilterOption(string Value, string Label)
{
    public static IReadOnlyList<ProliferationFilterOption> FromPairs(IEnumerable<(string Value, string Label)> pairs)
    {
        if (pairs is null) throw new ArgumentNullException(nameof(pairs));
        return new ReadOnlyCollection<ProliferationFilterOption>(pairs.Select(p => new ProliferationFilterOption(p.Value, p.Label)).ToList());
    }
}

public sealed record ProliferationFilterToolbarModel(
    string Context,
    IReadOnlyList<ProliferationFilterOption> Projects,
    IReadOnlyList<ProliferationFilterOption> Sources,
    IReadOnlyList<ProliferationFilterOption> Types,
    string SearchPlaceholder)
{
    public static readonly IReadOnlyList<ProliferationFilterOption> DefaultTypes = new ReadOnlyCollection<ProliferationFilterOption>(new List<ProliferationFilterOption>
    {
        new("", "All types"),
        new("yearly", "Yearly"),
        new("granular", "Granular")
    });

    public static ProliferationFilterToolbarModel Create(
        string context,
        IEnumerable<ProliferationFilterOption>? projects = null,
        IEnumerable<ProliferationFilterOption>? sources = null,
        IEnumerable<ProliferationFilterOption>? types = null,
        string? searchPlaceholder = null)
    {
        if (string.IsNullOrWhiteSpace(context)) throw new ArgumentException("Context is required.", nameof(context));
        var projectOptions = projects?.ToList() ?? new List<ProliferationFilterOption>();
        var sourceOptions = sources?.ToList() ?? new List<ProliferationFilterOption>();
        var typeOptions = types?.ToList() ?? new List<ProliferationFilterOption>(DefaultTypes);
        if (typeOptions.Count == 0)
        {
            typeOptions.AddRange(DefaultTypes);
        }

        var placeholder = string.IsNullOrWhiteSpace(searchPlaceholder)
            ? "Search projects, units, or remarks"
            : searchPlaceholder;

        return new ProliferationFilterToolbarModel(
            context,
            new ReadOnlyCollection<ProliferationFilterOption>(projectOptions),
            new ReadOnlyCollection<ProliferationFilterOption>(sourceOptions),
            new ReadOnlyCollection<ProliferationFilterOption>(typeOptions),
            placeholder);
    }
}
