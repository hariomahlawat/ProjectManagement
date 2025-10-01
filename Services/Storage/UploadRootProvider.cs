using System;
using System.IO;
using Microsoft.Extensions.Options;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Storage;

public sealed class UploadRootProvider : IUploadRootProvider
{
    public UploadRootProvider(IOptions<ProjectPhotoOptions> photoOptions)
    {
        if (photoOptions == null)
        {
            throw new ArgumentNullException(nameof(photoOptions));
        }

        var options = photoOptions.Value ?? throw new ArgumentException("Options value cannot be null.", nameof(photoOptions));
        var configuredRoot = string.IsNullOrWhiteSpace(options.StorageRoot) ? "/var/pm/uploads" : options.StorageRoot;
        var resolvedRoot = Environment.GetEnvironmentVariable("PM_UPLOAD_ROOT");

        if (string.IsNullOrWhiteSpace(resolvedRoot))
        {
            resolvedRoot = configuredRoot;
        }

        if (string.IsNullOrWhiteSpace(resolvedRoot))
        {
            throw new InvalidOperationException("Upload root path cannot be empty.");
        }

        var fullPath = Path.GetFullPath(resolvedRoot);
        Directory.CreateDirectory(fullPath);
        RootPath = fullPath;
    }

    public string RootPath { get; }
}
