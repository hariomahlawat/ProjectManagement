using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Models;

public sealed record AdminFlatLookupDirectoryModel
{
    public FlatLookupDirectoryResult Result { get; init; } = new(
        MasterDataFlatLookupKind.ProjectType,
        Array.Empty<FlatLookupAdminRow>(),
        0, 0, 0, 0, 0,
        1, 25, 1,
        string.Empty,
        "active");

    public string SingularLabel { get; init; } = "item";
    public string PluralLabel { get; init; } = "items";
    public string Description { get; init; } = string.Empty;
    public string Icon { get; init; } = "bi-tags";
    public string IndexPage { get; init; } = string.Empty;
    public string CreatePage { get; init; } = string.Empty;
    public string EditPage { get; init; } = string.Empty;
    public string DeactivatePage { get; init; } = string.Empty;
    public string MoveHandler { get; init; } = "Move";
}
