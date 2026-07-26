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
            _ => kind.ToString()
        };
}
