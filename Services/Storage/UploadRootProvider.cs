using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Storage;

public sealed class UploadRootProvider : IUploadRootProvider
{
    private readonly ProjectDocumentOptions _documentOptions;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<UploadRootProvider> _logger;
    private readonly Func<string, DirectoryInfo> _createDirectory;

    public UploadRootProvider(IOptions<ProjectPhotoOptions> photoOptions,
                              IOptions<ProjectDocumentOptions> documentOptions,
                              IWebHostEnvironment environment,
                              ILogger<UploadRootProvider> logger,
                              Func<string, DirectoryInfo>? directoryFactory = null)
    {
        if (photoOptions == null)
        {
            throw new ArgumentNullException(nameof(photoOptions));
        }

        if (documentOptions == null)
        {
            throw new ArgumentNullException(nameof(documentOptions));
        }

        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _createDirectory = directoryFactory ?? Directory.CreateDirectory;

        var options = photoOptions.Value ?? throw new ArgumentException("Options value cannot be null.", nameof(photoOptions));
        _documentOptions = documentOptions.Value ?? throw new ArgumentException("Options value cannot be null.", nameof(documentOptions));

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

        RootPath = EnsureRootWithFallback(Path.GetFullPath(resolvedRoot));
        ProjectsRootPath = EnsureDirectory(CombineOptional(RootPath, _documentOptions.ProjectsSubpath));
    }

    public string RootPath { get; }

    public string ProjectsRootPath { get; }

    public string GetProjectRoot(int projectId)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        return EnsureDirectory(Path.Combine(ProjectsRootPath, projectId.ToString()));
    }

    public string GetProjectPhotosRoot(int projectId)
    {
        var projectRoot = GetProjectRoot(projectId);
        return EnsureDirectory(CombineOptional(projectRoot, _documentOptions.PhotosSubpath));
    }

    public string GetProjectDocumentsRoot(int projectId)
    {
        var projectRoot = GetProjectRoot(projectId);
        return EnsureDirectory(CombineOptional(projectRoot, _documentOptions.StorageSubPath));
    }

    public string GetProjectCommentsRoot(int projectId)
    {
        var projectRoot = GetProjectRoot(projectId);
        return EnsureDirectory(CombineOptional(projectRoot, _documentOptions.CommentsSubpath));
    }

    public string GetProjectVideosRoot(int projectId)
    {
        var projectRoot = GetProjectRoot(projectId);
        return EnsureDirectory(CombineOptional(projectRoot, _documentOptions.VideosSubpath));
    }

    public string GetSocialMediaRoot(string storagePrefix, Guid eventId)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Event identifier cannot be empty.", nameof(eventId));
        }

        const string token = "{eventId}";
        var normalizedPrefix = string.IsNullOrWhiteSpace(storagePrefix)
            ? "org/social/{eventId}"
            : storagePrefix.Trim();

        normalizedPrefix = normalizedPrefix.TrimStart('/', '\\').TrimEnd('/', '\\');

        var tokenIndex = normalizedPrefix.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (tokenIndex < 0)
        {
            throw new ArgumentException($"Storage prefix must contain the token '{token}'.", nameof(storagePrefix));
        }

        var resolvedPrefix = normalizedPrefix.Replace(token, eventId.ToString("D"), StringComparison.OrdinalIgnoreCase);
        var relativePath = resolvedPrefix.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

        return EnsureDirectory(Path.Combine(RootPath, relativePath));
    }

    private static string CombineOptional(string root, string? subpath)
    {
        return string.IsNullOrWhiteSpace(subpath) ? root : Path.Combine(root, subpath);
    }

    private string EnsureDirectory(string path)
    {
        _createDirectory(path);
        return path;
    }

    private string EnsureRootWithFallback(string primaryPath)
    {
        try
        {
            return EnsureDirectory(primaryPath);
        }
        catch (Exception ex)
        {
            var fallbackPath = Path.GetFullPath(GetFallbackRoot());
            _logger.LogWarning(ex,
                "Failed to create the configured upload root at '{PrimaryPath}'. Falling back to '{FallbackPath}'.",
                primaryPath,
                fallbackPath);

            try
            {
                var resolved = EnsureDirectory(fallbackPath);
                _logger.LogInformation(
                    "Using fallback upload root at '{FallbackPath}'. Consider setting PM_UPLOAD_ROOT to a writable directory if this is unexpected.",
                    fallbackPath);
                return resolved;
            }
            catch (Exception fallbackEx)
            {
                var message =
                    $"Unable to create an upload root directory. Tried '{primaryPath}' and fallback '{fallbackPath}'. Configure a writable path via the PM_UPLOAD_ROOT environment variable or ProjectPhotos:StorageRoot.";
                _logger.LogError(fallbackEx, message);
                throw new InvalidOperationException(message, fallbackEx);
            }
        }
    }

    private string GetFallbackRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.Combine(localAppData, "ProjectManagement", "uploads");
            }
        }

        var contentRoot = _environment.ContentRootPath;
        if (string.IsNullOrWhiteSpace(contentRoot))
        {
            contentRoot = AppContext.BaseDirectory;
        }

        return Path.Combine(contentRoot, "uploads");
    }
}
