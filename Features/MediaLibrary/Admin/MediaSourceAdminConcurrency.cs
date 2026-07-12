using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Admin;

/// <summary>
/// Produces an opaque concurrency token from administrator-controlled source settings only.
/// Runtime scan/health updates therefore do not invalidate an edit form unnecessarily.
/// </summary>
public static class MediaSourceAdminConcurrency
{
    public static string Create(MediaLibrarySource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var payload = JsonSerializer.Serialize(new
        {
            source.Name,
            source.Key,
            source.RootPath,
            source.IsEnabled,
            source.IsVisibleInLibrary,
            source.IsReadOnly,
            source.IncludeSubfolders,
            source.IsConfigurationManaged,
            source.IsDeleted,
            source.ScanIntervalMinutes,
            source.AllowedExtensionsJson
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
