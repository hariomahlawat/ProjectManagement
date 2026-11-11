namespace ProjectManagement.Pages.Photos;

/// <summary>
/// Rules for adding PRISM uploads that the catalogue has not indexed yet to the catalogue
/// timeline while it catches up. Kept outside the PageModel so they are testable without
/// Razor infrastructure.
/// </summary>
public static class PhotosUploadOverlay
{
    /// <summary>
    /// Recent uploads carry no classification or people data and are newest by definition,
    /// so they belong only on the first page of a newest-first timeline that is not filtered
    /// by classification, people or the external-folder source.
    /// </summary>
    public static bool Applies(
        int pageNumber,
        string sort,
        string classification,
        int selectedPeopleCount,
        string source)
        => pageNumber == 1
           && string.Equals(sort, "newest", StringComparison.Ordinal)
           && string.Equals(classification, "all", StringComparison.Ordinal)
           && selectedPeopleCount == 0
           && !string.Equals(source, "external", StringComparison.Ordinal);

    /// <summary>
    /// Returns recent uploads the catalogue does not already contain, newest first, capped
    /// at <paramref name="take"/>. Items already in the catalogue are excluded whatever
    /// their version, so a photo never appears twice on the page.
    /// </summary>
    public static IReadOnlyList<IndexModel.MediaItem> SelectPending(
        IEnumerable<IndexModel.MediaItem> recentUploads,
        IReadOnlySet<string> cataloguedSourceEntityIds,
        int take)
    {
        ArgumentNullException.ThrowIfNull(recentUploads);
        ArgumentNullException.ThrowIfNull(cataloguedSourceEntityIds);
        return recentUploads
            .Where(item => !cataloguedSourceEntityIds.Contains(item.Id))
            .OrderByDescending(item => item.MediaDate)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(Math.Max(0, take))
            .ToList();
    }
}
