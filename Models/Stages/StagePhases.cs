using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Models.Stages;

/// <summary>Where an ongoing project stands in the procurement workflow, by its current stage.</summary>
public enum StagePhase
{
    /// <summary>Acceptance of Necessity not yet completed (FS, IPA, SoW, AoN).</summary>
    PreAon,

    /// <summary>AoN completed, supply order not yet placed (Bid to SO).</summary>
    Procurement,

    /// <summary>The development stage is the current stage.</summary>
    Development,

    /// <summary>Development completed; acceptance, payment and transfer of technology remain.</summary>
    PostDevelopment
}

/// <summary>
/// The one definition of which workflow stages make up each phase. The Analytics phase cards,
/// the chart brackets under the stage axis and any report that groups by phase read it from here.
/// </summary>
public static class StagePhases
{
    public sealed record Definition(StagePhase Phase, string Key, string Name, string Span, IReadOnlyList<string> StageCodes);

    public static readonly IReadOnlyList<Definition> All = new[]
    {
        new Definition(StagePhase.PreAon, "pre-aon", "Pre-AoN", "FS to AoN",
            new[] { StageCodes.FS, StageCodes.IPA, StageCodes.SOW, StageCodes.AON }),
        new Definition(StagePhase.Procurement, "procurement", "Procurement", "Bid to SO",
            new[] { StageCodes.BID, StageCodes.TEC, StageCodes.BM, StageCodes.COB, StageCodes.PNC, StageCodes.EAS, StageCodes.SO }),
        new Definition(StagePhase.Development, "development", "Development", "Devp",
            new[] { StageCodes.DEVP }),
        new Definition(StagePhase.PostDevelopment, "post-development", "Post-development", "ATP to ToT",
            new[] { StageCodes.ATP, StageCodes.PAYMENT, StageCodes.TOT })
    };

    private static readonly IReadOnlyDictionary<string, StagePhase> PhaseByStageCode = All
        .SelectMany(definition => definition.StageCodes.Select(code => (code, definition.Phase)))
        .ToDictionary(pair => pair.code, pair => pair.Phase, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The phase of a project whose current stage is <paramref name="currentStageCode"/>. A project
    /// with no current stage has completed nothing, so AoN is not completed and it is Pre-AoN.
    /// A stage code outside the workflow has no phase.
    /// </summary>
    public static StagePhase? Of(string? currentStageCode)
    {
        if (string.IsNullOrWhiteSpace(currentStageCode))
        {
            return StagePhase.PreAon;
        }

        return PhaseByStageCode.TryGetValue(currentStageCode.Trim(), out var phase) ? phase : null;
    }

    public static Definition Describe(StagePhase phase) => All.First(definition => definition.Phase == phase);
}
