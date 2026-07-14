using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Models;

public sealed record AdminCategoryDirectoryModel
{
    public CategoryDirectoryResult Result { get; init; } = new(
        MasterDataCategoryKind.Project,
        Array.Empty<CategoryAdminRow>(),
        0, 0, 0, 0, 0,
        string.Empty,
        "active");

    public string Area { get; init; } = "Admin";
    public string IndexPage { get; init; } = string.Empty;
    public string CreatePage { get; init; } = string.Empty;
    public string EditPage { get; init; } = string.Empty;
    public string DeletePage { get; init; } = string.Empty;
    public string ToggleHandler { get; init; } = "Toggle";
    public string MoveHandler { get; init; } = "Move";
}
