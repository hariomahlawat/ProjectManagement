using Microsoft.Extensions.Configuration;

namespace ProjectManagement.Infrastructure;

/// <summary>
/// Resolves the transport-level request-body ceiling from the largest configured upload
/// workflow. Feature services remain responsible for enforcing their own lower file and
/// batch limits.
/// </summary>
internal static class UploadRequestLimitResolver
{
    internal const long OneMebibyte = 1024L * 1024L;
    internal const long MultipartOverheadBytes = 8L * OneMebibyte;
    internal const long MinimumRequestBodyBytes = 32L * OneMebibyte;

    public static long Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var documentLimitMegabytes = Math.Max(
            0,
            configuration.GetValue<int?>("ProjectDocuments:MaxSizeMb") ?? 0);

        var configuredLimits = new[]
        {
            checked(documentLimitMegabytes * OneMebibyte),
            ReadPositiveInt64(configuration, "ProjectVideos:MaxFileSizeBytes"),
            ReadPositiveInt64(configuration, "ProjectPhotos:MaxFileSizeBytes"),
            ReadPositiveInt64(configuration, "ProjectOfficeReports:VisitPhotos:MaxBatchSizeBytes"),
            ReadPositiveInt64(configuration, "ProjectOfficeReports:VisitPhotos:MaxFileSizeBytes"),
            ReadPositiveInt64(configuration, "ProjectOfficeReports:SocialMediaPhotos:MaxFileSizeBytes"),
            ReadPositiveInt64(configuration, "IprAttachments:MaxFileSizeBytes"),
            ReadPositiveInt64(configuration, "FfcAttachments:MaxFileSizeBytes"),
            ReadPositiveInt64(configuration, "DocRepo:MaxFileSizeBytes"),
            ReadPositiveInt64(configuration, "MediaLibrary:Processing:MaxImageFileSizeBytes")
        };

        var largestConfiguredUpload = configuredLimits.Max();
        if (largestConfiguredUpload <= 0)
        {
            return MinimumRequestBodyBytes;
        }

        return checked(
            Math.Max(
                MinimumRequestBodyBytes,
                largestConfiguredUpload + MultipartOverheadBytes));
    }

    private static long ReadPositiveInt64(IConfiguration configuration, string key)
        => Math.Max(0L, configuration.GetValue<long?>(key) ?? 0L);
}
