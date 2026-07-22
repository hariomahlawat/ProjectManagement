namespace ProjectManagement.Areas.Admin.Models;

public sealed record AdminPageHeaderModel
{
    public string Title { get; init; } = string.Empty;

    public string? Eyebrow { get; init; }

    public string? Description { get; init; }

    public string? Icon { get; init; }

    public IReadOnlyList<AdminPageActionModel> Actions { get; init; } =
        Array.Empty<AdminPageActionModel>();
}

public sealed record AdminPageActionModel
{
    public string Text { get; init; } = string.Empty;

    public string? Href { get; init; }

    public string? Icon { get; init; }

    public bool IsPrimary { get; init; }
}
