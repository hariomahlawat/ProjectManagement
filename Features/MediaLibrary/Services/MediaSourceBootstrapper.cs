using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Ensures the internal PRISM source and optionally imports configuration-defined
/// file-system sources. Database-created sources remain authoritative and are never
/// deleted merely because they are absent from appsettings.
/// </summary>
public sealed class MediaSourceBootstrapper : IMediaSourceBootstrapper
{
    public const string PrismSourceKey = "prism";

    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibraryOptions _options;

    public MediaSourceBootstrapper(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task EnsureConfiguredSourcesAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsCatalogueEnabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await _db.Sources
            .ToDictionaryAsync(source => source.Key, StringComparer.OrdinalIgnoreCase, cancellationToken);

        await EnsurePrismSourceAsync(existing, now);

        var configuredDefinitions = _options.IsExternalSourceFeatureEnabled
            ? _options.GetBootstrapSources()
                .Where(source => !string.IsNullOrWhiteSpace(source.Key))
                .Select(source => ToDefinition(source, _options.ExternalSources.DefaultScanIntervalMinutes))
                .ToList()
            : new List<SourceDefinition>();

        foreach (var definition in configuredDefinitions)
        {
            var fingerprint = ComputeFingerprint(definition);
            if (!existing.TryGetValue(definition.Key, out var entity))
            {
                entity = new MediaLibrarySource
                {
                    Id = Guid.NewGuid(),
                    Key = definition.Key,
                    CreatedAtUtc = now,
                    ScanStatus = "Never",
                    HealthStatus = "Unknown",
                    IsConfigurationManaged = true
                };
                _db.Sources.Add(entity);
                existing[definition.Key] = entity;
            }

            // A database-managed source is never silently converted into a
            // configuration-managed source with the same key.
            if (!entity.IsConfigurationManaged && entity.SourceType != MediaLibrarySourceType.Prism)
            {
                continue;
            }

            entity.Name = definition.Name;
            entity.SourceType = MediaLibrarySourceType.FileSystem;
            entity.RootPath = definition.RootPath;
            entity.IsEnabled = definition.Enabled;
            entity.IsVisibleInLibrary = definition.VisibleInLibrary;
            entity.IsReadOnly = true;
            entity.IncludeSubfolders = definition.IncludeSubfolders;
            entity.ScanIntervalMinutes = definition.ScanIntervalMinutes;
            entity.AllowedExtensionsJson = JsonSerializer.Serialize(definition.AllowedExtensions);
            entity.ConfigurationFingerprint = fingerprint;
            entity.IsConfigurationManaged = true;
            entity.IsDeleted = false;
            entity.DisconnectedAtUtc = null;
            entity.UpdatedAtUtc = now;
        }

        var configuredKeys = configuredDefinitions.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleSource in existing.Values.Where(source =>
                     source.IsConfigurationManaged
                     && source.SourceType == MediaLibrarySourceType.FileSystem
                     && !configuredKeys.Contains(source.Key)))
        {
            staleSource.IsEnabled = false;
            staleSource.ScanStatus = "Not configured";
            staleSource.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public static string NormalizeKey(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("A media source key cannot be empty.");
        }

        var builder = new StringBuilder(normalized.Length);
        var previousDash = false;
        foreach (var character in normalized)
        {
            var accepted = char.IsLetterOrDigit(character);
            if (accepted)
            {
                builder.Append(character);
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var result = builder.ToString().Trim('-');
        if (result.Length == 0)
        {
            throw new InvalidOperationException("A media source key must contain a letter or number.");
        }

        return result.Length <= 64 ? result : result[..64].TrimEnd('-');
    }

    public static string[] NormalizeExtensions(IEnumerable<string> extensions)
        => extensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.Trim().ToLowerInvariant())
            .Select(extension => extension.StartsWith('.') ? extension : $".{extension}")
            .Where(extension => extension.Length is > 1 and <= 16)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private async Task EnsurePrismSourceAsync(
        IDictionary<string, MediaLibrarySource> existing,
        DateTimeOffset now)
    {
        if (!existing.TryGetValue(PrismSourceKey, out var prism))
        {
            prism = new MediaLibrarySource
            {
                Id = Guid.NewGuid(),
                Key = PrismSourceKey,
                CreatedAtUtc = now,
                ScanStatus = "Never",
                HealthStatus = "Internal"
            };
            _db.Sources.Add(prism);
            existing[PrismSourceKey] = prism;
        }

        prism.Name = "PRISM uploads";
        prism.SourceType = MediaLibrarySourceType.Prism;
        prism.RootPath = null;
        prism.IsEnabled = _options.Catalogue.SynchronizePrismMedia;
        prism.IsVisibleInLibrary = false;
        prism.IsReadOnly = true;
        prism.IncludeSubfolders = true;
        prism.ScanIntervalMinutes = _options.Catalogue.SynchronizeIntervalMinutes;
        prism.AllowedExtensionsJson = "[]";
        prism.IsConfigurationManaged = true;
        prism.IsDeleted = false;
        prism.UpdatedAtUtc = now;

        await Task.CompletedTask;
    }

    private static SourceDefinition ToDefinition(MediaSourceOptions source, int defaultInterval)
        => new(
            NormalizeKey(source.Key),
            source.Name.Trim(),
            source.RootPath.Trim(),
            source.Enabled,
            source.VisibleInLibrary,
            source.IncludeSubfolders,
            Math.Clamp(source.ScanIntervalMinutes ?? defaultInterval, 1, 10080),
            NormalizeExtensions(source.AllowedExtensions));

    private static string ComputeFingerprint(SourceDefinition source)
    {
        var raw = JsonSerializer.Serialize(source);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private sealed record SourceDefinition(
        string Key,
        string Name,
        string RootPath,
        bool Enabled,
        bool VisibleInLibrary,
        bool IncludeSubfolders,
        int ScanIntervalMinutes,
        IReadOnlyList<string> AllowedExtensions);
}
