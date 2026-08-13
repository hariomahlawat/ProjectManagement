using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ProjectManagement.Services.Publications;

/// <summary>
/// Produces deterministic, server-authoritative fingerprints for brochure review decisions.
/// A final-download approval remains valid only while the reviewed publication inputs are exact.
/// </summary>
public static class BrochureReviewFingerprint
{
    public const int HexLength = 64;

    public static string CreateProject(
        int projectId,
        string projectName,
        string? projectCategory,
        string? technicalCategory,
        BrochureNarrativeSource narrativeSource,
        string narrative,
        int? primaryPhotoId,
        int primaryPhotoVersion,
        int? secondaryPhotoId,
        int secondaryPhotoVersion,
        double primaryFocalX,
        double primaryFocalY,
        double secondaryFocalX,
        double secondaryFocalY,
        BrochureImageMode imageMode)
        => Hash(
            "brochure-project-review-v1",
            projectId.ToString(CultureInfo.InvariantCulture),
            projectName,
            projectCategory,
            technicalCategory,
            ((int)narrativeSource).ToString(CultureInfo.InvariantCulture),
            narrative,
            NullableInt(primaryPhotoId),
            primaryPhotoVersion.ToString(CultureInfo.InvariantCulture),
            NullableInt(secondaryPhotoId),
            secondaryPhotoVersion.ToString(CultureInfo.InvariantCulture),
            Focal(primaryFocalX),
            Focal(primaryFocalY),
            Focal(secondaryFocalX),
            Focal(secondaryFocalY),
            ((int)imageMode).ToString(CultureInfo.InvariantCulture));

    public static string CreateCover(
        BrochureCoverReviewContext context,
        BrochurePublicationProfile publicationProfile,
        int heroProjectId,
        int heroPhotoId,
        int heroPhotoVersion,
        double focalX,
        double focalY)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Hash(
            "brochure-cover-review-v3",
            context.Title,
            context.Subtitle,
            context.Edition,
            context.Strapline,
            context.HandlingMarking,
            context.FrontCoverKicker,
            context.FrontCoverDescriptor,
            context.ShowFrontCoverKicker ? "1" : "0",
            context.ShowFrontCoverDescriptor ? "1" : "0",
            context.ShowFrontCoverTitle ? "1" : "0",
            context.ShowFrontCoverSubtitle ? "1" : "0",
            context.ShowFrontCoverEdition ? "1" : "0",
            context.ShowFrontCoverStrapline ? "1" : "0",
            ((int)publicationProfile).ToString(CultureInfo.InvariantCulture),
            heroProjectId.ToString(CultureInfo.InvariantCulture),
            heroPhotoId.ToString(CultureInfo.InvariantCulture),
            heroPhotoVersion.ToString(CultureInfo.InvariantCulture),
            Focal(focalX),
            Focal(focalY));
    }

    public static bool Matches(string? submitted, string? current)
    {
        if (string.IsNullOrWhiteSpace(submitted)
            || string.IsNullOrWhiteSpace(current)
            || submitted.Length != HexLength
            || current.Length != HexLength)
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(submitted),
                Convert.FromHexString(current));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Hash(params string?[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var lengthBuffer = new byte[sizeof(int)];
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, bytes.Length);
            hash.AppendData(lengthBuffer);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string NullableInt(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Focal(double value)
    {
        var normalized = double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;
        return normalized.ToString("0.0000", CultureInfo.InvariantCulture);
    }
}
