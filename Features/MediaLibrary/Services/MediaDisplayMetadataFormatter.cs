using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Produces one consistent display hierarchy for tiles, viewer metadata and albums.
/// Source metadata remains untouched; repeated title/context strings are collapsed only
/// for presentation so catalogue provenance is never lost.
/// </summary>
public static class MediaDisplayMetadataFormatter
{
    public sealed record DisplayMetadata(
        string DisplayTitle,
        string? DisplayContext,
        string? DisplaySubtitle,
        string? EffectiveCaption);

    public static DisplayMetadata Format(
        MediaAssetOrigin origin,
        string title,
        string? sourceCaption,
        string? editorialCaption,
        string contextTitle,
        string? contextSubtitle)
    {
        var normalizedTitle = origin == MediaAssetOrigin.VisitPhoto
            ? MediaCollectionTitleFormatter.FormatVisitTitle(title)
            : Clean(title);
        var normalizedContext = MediaCollectionTitleFormatter.FormatCollectionTitle(origin, contextTitle);
        var normalizedSubtitle = Clean(contextSubtitle);
        var curatedCaption = Clean(editorialCaption);
        var effectiveCaption = curatedCaption ?? Clean(sourceCaption);

        // A curator-authored caption is intentionally the strongest display label. A
        // source caption is also preferred when present, but the original media title is
        // retained in the information panel/filename metadata.
        var displayTitle = effectiveCaption ?? normalizedTitle;
        if (string.IsNullOrWhiteSpace(displayTitle))
        {
            displayTitle = normalizedContext;
        }

        var displayContext = DistinctOrNull(normalizedContext, displayTitle);
        var displaySubtitle = DistinctOrNull(normalizedSubtitle, displayTitle, displayContext);

        // If title and context are identical (the common "Visit of X / Visit of X"
        // case), use the distinct subtitle instead of rendering the same string twice.
        if (displayContext is null && displaySubtitle is not null)
        {
            displayContext = displaySubtitle;
            displaySubtitle = null;
        }

        return new DisplayMetadata(displayTitle, displayContext, displaySubtitle, effectiveCaption);
    }

    public static string? DistinctOrNull(string? value, params string?[] others)
    {
        var cleaned = Clean(value);
        if (cleaned is null) return null;
        return others.Any(other => Equivalent(cleaned, other)) ? null : cleaned;
    }

    private static bool Equivalent(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(
               NormalizeComparison(left),
               NormalizeComparison(right),
               StringComparison.OrdinalIgnoreCase);

    private static string NormalizeComparison(string value)
        => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
