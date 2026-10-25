namespace ProjectManagement.Models.Arpp;

public static class ArppDisplayNames
{
    public static string For(ArppCategory category)
        => category switch
        {
            ArppCategory.New => "New",
            ArppCategory.CommittedLiability => "CL",
            ArppCategory.CarryForward => "CF",
            ArppCategory.Delisted => "Delisted",
            _ => category.ToString()
        };

    public static string For(ArppIssueKind kind)
        => kind switch
        {
            ArppIssueKind.Original => "Original ARPP",
            ArppIssueKind.Addendum => "Addendum",
            ArppIssueKind.Amendment => "Amendment letter",
            _ => kind.ToString()
        };

    /// <summary>
    /// Label for one issued letter: "Original ARPP", "Addendum No. 2" or "Amendment No. 1".
    /// </summary>
    public static string IssueLabel(ArppIssueKind kind, int issueSequence)
        => kind switch
        {
            ArppIssueKind.Original => "Original ARPP",
            ArppIssueKind.Addendum => $"Addendum No. {issueSequence}",
            ArppIssueKind.Amendment => $"Amendment No. {issueSequence}",
            _ => $"{kind} {issueSequence}"
        };

    /// <summary>
    /// Label when only the sequence survived (older contracts without a kind).
    /// Sequence 0 is always the original; anything else is read as an addendum.
    /// </summary>
    public static string IssueLabel(ArppIssueKind? kind, int? issueSequence)
        => IssueLabel(
            kind ?? (issueSequence.GetValueOrDefault() == 0 ? ArppIssueKind.Original : ArppIssueKind.Addendum),
            issueSequence.GetValueOrDefault());

    /// <summary>
    /// File-name safe label: "Original", "Addendum_2", "Amendment_1".
    /// </summary>
    public static string IssueFileLabel(ArppIssueKind kind, int issueSequence)
        => kind switch
        {
            ArppIssueKind.Original => "Original",
            ArppIssueKind.Addendum => $"Addendum_{issueSequence}",
            ArppIssueKind.Amendment => $"Amendment_{issueSequence}",
            _ => $"{kind}_{issueSequence}"
        };
}
