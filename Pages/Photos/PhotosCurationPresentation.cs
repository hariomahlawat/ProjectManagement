using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos;

/// <summary>
/// Small, deterministic presentation rules for the Photos curation workspace.
/// Keeping these rules outside the PageModel makes album-state semantics testable
/// without coupling them to Razor infrastructure.
/// </summary>
public static class PhotosCurationPresentation
{
    public static bool CanAddMedia(MediaAlbumDetails? album)
        => album is { CanManage: true, IsArchived: false }
           && album.TotalMembershipCount < MediaAlbumService.MaximumAlbumItems;

    public static bool CanOrganize(MediaAlbumDetails? album)
        => album is { CanManage: true, IsArchived: false, ItemCount: >= 2 };

    public static string BuildCreatorDisplayName(
        string? rank,
        string? fullName,
        string? userName)
    {
        var normalizedRank = Normalize(rank);
        var normalizedName = Normalize(fullName);
        var normalizedUserName = Normalize(userName);

        if (!string.IsNullOrWhiteSpace(normalizedName))
        {
            if (string.IsNullOrWhiteSpace(normalizedRank)
                || normalizedName.StartsWith(normalizedRank + " ", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedName, normalizedRank, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedName;
            }

            return $"{normalizedRank} {normalizedName}";
        }

        if (!string.IsNullOrWhiteSpace(normalizedUserName))
        {
            return normalizedUserName;
        }

        return "PRISM user";
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
