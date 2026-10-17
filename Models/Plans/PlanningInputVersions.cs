namespace ProjectManagement.Models.Plans;

/// <summary>Persisted planning-input authority, independent of workflow/template versions.</summary>
public static class PlanningInputVersions
{
    /// <summary>Ambiguous shared baseline or revision with incomplete input ownership.</summary>
    public const int Legacy = 0;

    /// <summary>Complete proposal inputs or a baseline published by the current application.</summary>
    public const int Current = 1;
}
