using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Produces stable human-facing titles for automatically derived media collections.
/// Source records remain authoritative; this formatter only removes mechanical prefix
/// duplication introduced when an already-labelled source value is composed again.
/// </summary>
public static class MediaCollectionTitleFormatter
{
    private const string VisitLabel = "Visit of";

    public static string FormatVisitTitle(string? visitorOrTitle)
    {
        var value = visitorOrTitle?.Trim() ?? string.Empty;

        while (value.Equals(VisitLabel, StringComparison.OrdinalIgnoreCase)
               || value.StartsWith($"{VisitLabel} ", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Length == VisitLabel.Length
                ? string.Empty
                : value[VisitLabel.Length..].Trim();
        }

        return string.IsNullOrWhiteSpace(value)
            ? "Visit"
            : $"{VisitLabel} {value}";
    }

    public static string FormatCollectionTitle(MediaAssetOrigin origin, string? title)
        => origin == MediaAssetOrigin.VisitPhoto
            ? FormatVisitTitle(title)
            : string.IsNullOrWhiteSpace(title)
                ? "Untitled collection"
                : title.Trim();
}
