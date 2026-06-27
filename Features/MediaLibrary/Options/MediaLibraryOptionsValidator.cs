using Microsoft.Extensions.Options;

namespace ProjectManagement.Features.MediaLibrary.Options;

public sealed class MediaLibraryOptionsValidator : IValidateOptions<MediaLibraryOptions>
{
    public ValidateOptionsResult Validate(string? name, MediaLibraryOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.CacheRoot))
        {
            failures.Add("MediaLibrary:CacheRoot is required.");
        }

        if (options.ScanIntervalMinutes < 1)
        {
            failures.Add("MediaLibrary:ScanIntervalMinutes must be at least 1.");
        }

        if (options.ScanBatchSize is < 10 or > 5000)
        {
            failures.Add("MediaLibrary:ScanBatchSize must be between 10 and 5000.");
        }

        if (options.ProcessingBatchSize is < 1 or > 16)
        {
            failures.Add("MediaLibrary:ProcessingBatchSize must be between 1 and 16.");
        }

        if (options.IdleDelaySeconds is < 1 or > 3600)
        {
            failures.Add("MediaLibrary:IdleDelaySeconds must be between 1 and 3600.");
        }

        if (options.MaxAttempts is < 1 or > 20)
        {
            failures.Add("MediaLibrary:MaxAttempts must be between 1 and 20.");
        }

        if (options.ThumbnailMaxPixels is < 128 or > 2048)
        {
            failures.Add("MediaLibrary:ThumbnailMaxPixels must be between 128 and 2048.");
        }

        if (options.PreviewMaxPixels < options.ThumbnailMaxPixels || options.PreviewMaxPixels > 8192)
        {
            failures.Add("MediaLibrary:PreviewMaxPixels must be greater than or equal to ThumbnailMaxPixels and no more than 8192.");
        }

        if (options.WebpQuality is < 40 or > 100)
        {
            failures.Add("MediaLibrary:WebpQuality must be between 40 and 100.");
        }

        var sources = options.Sources ?? new List<MediaSourceOptions>();
        var duplicateKeys = sources
            .Where(source => source.Enabled)
            .GroupBy(source => source.Key?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateKeys.Length > 0)
        {
            failures.Add($"MediaLibrary source keys must be unique: {string.Join(", ", duplicateKeys)}.");
        }

        foreach (var source in sources.Where(source => source.Enabled))
        {
            if (string.IsNullOrWhiteSpace(source.Key))
            {
                failures.Add("Every enabled MediaLibrary source requires a Key.");
            }

            if (string.IsNullOrWhiteSpace(source.Name))
            {
                failures.Add($"MediaLibrary source '{source.Key}' requires a Name.");
            }

            if (!string.Equals(source.Type, "NetworkShare", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"MediaLibrary source '{source.Key}' has unsupported Type '{source.Type}'.");
            }

            if (string.IsNullOrWhiteSpace(source.RootPath) || !Path.IsPathFullyQualified(source.RootPath))
            {
                failures.Add($"MediaLibrary source '{source.Key}' requires a fully-qualified RootPath. Use a UNC path for NAS shares.");
            }

            if (source.AllowedExtensions is null || source.AllowedExtensions.Count == 0)
            {
                failures.Add($"MediaLibrary source '{source.Key}' requires at least one allowed extension.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
