using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

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
        var now = DateTimeOffset.UtcNow;
        var configured = new List<SourceDefinition>
        {
            new(
                PrismSourceKey,
                "PRISM uploads",
                MediaLibrarySourceType.Prism,
                null,
                true,
                true,
                true,
                Array.Empty<string>())
        };

        configured.AddRange(_options.Sources
            .Where(source => !string.IsNullOrWhiteSpace(source.Key))
            .Select(source => new SourceDefinition(
            NormalizeKey(source.Key),
            source.Name.Trim(),
            MediaLibrarySourceType.NetworkShare,
            source.RootPath.Trim(),
            source.Enabled,
            source.ReadOnly,
            source.IncludeSubfolders,
            NormalizeExtensions(source.AllowedExtensions))));

        var keys = configured.Select(source => source.Key).ToArray();
        var existing = await _db.Sources
            .ToDictionaryAsync(source => source.Key, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var definition in configured)
        {
            var fingerprint = ComputeFingerprint(definition);
            if (!existing.TryGetValue(definition.Key, out var entity))
            {
                entity = new MediaLibrarySource
                {
                    Id = Guid.NewGuid(),
                    Key = definition.Key,
                    CreatedAtUtc = now,
                    ScanStatus = "Never"
                };
                _db.Sources.Add(entity);
            }

            entity.Name = definition.Name;
            entity.SourceType = definition.Type;
            entity.RootPath = definition.RootPath;
            entity.IsEnabled = definition.Enabled;
            entity.IsReadOnly = definition.ReadOnly;
            entity.IncludeSubfolders = definition.IncludeSubfolders;
            entity.AllowedExtensionsJson = JsonSerializer.Serialize(definition.AllowedExtensions);
            entity.ConfigurationFingerprint = fingerprint;
            entity.UpdatedAtUtc = now;
        }


        foreach (var staleSource in existing.Values.Where(source =>
                     source.SourceType == MediaLibrarySourceType.NetworkShare
                     && !keys.Contains(source.Key, StringComparer.OrdinalIgnoreCase)))
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

        return normalized;
    }

    public static string[] NormalizeExtensions(IEnumerable<string> extensions)
        => extensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.Trim().ToLowerInvariant())
            .Select(extension => extension.StartsWith('.') ? extension : $".{extension}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ComputeFingerprint(SourceDefinition source)
    {
        var raw = JsonSerializer.Serialize(source);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private sealed record SourceDefinition(
        string Key,
        string Name,
        MediaLibrarySourceType Type,
        string? RootPath,
        bool Enabled,
        bool ReadOnly,
        bool IncludeSubfolders,
        IReadOnlyList<string> AllowedExtensions);
}
