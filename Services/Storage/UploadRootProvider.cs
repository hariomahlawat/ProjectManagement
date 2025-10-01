using System;
using System.IO;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Storage;

public sealed class UploadRootProvider : IUploadRootProvider
{
    private readonly ProjectDocumentOptions _documentOptions;

    public UploadRootProvider(IOptions<ProjectPhotoOptions> photoOptions,
                              IOptions<ProjectDocumentOptions> documentOptions)
    {
        if (photoOptions == null)
        {
            throw new ArgumentNullException(nameof(photoOptions));
        }

        if (documentOptions == null)
        {
            throw new ArgumentNullException(nameof(documentOptions));
        }

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

        RootPath = EnsureDirectory(Path.GetFullPath(resolvedRoot));
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

    private static string CombineOptional(string root, string? subpath)
    {
        return string.IsNullOrWhiteSpace(subpath) ? root : Path.Combine(root, subpath);
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
