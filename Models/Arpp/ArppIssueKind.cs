namespace ProjectManagement.Models.Arpp;

public enum ArppIssueKind
{
    Original = 1,
    Addendum = 2,

    /// <summary>
    /// An intermediate HQ letter that changes the ARPP position without being a numbered
    /// addendum (a "for-read" correction, a revised cost, a delisting). Numbered separately
    /// from addenda within the financial year.
    /// </summary>
    Amendment = 3
}
