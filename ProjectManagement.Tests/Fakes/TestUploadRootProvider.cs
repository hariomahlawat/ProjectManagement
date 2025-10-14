using System;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Tests.Fakes;

public sealed class TestUploadRootProvider : IUploadRootProvider
{
    public TestUploadRootProvider(string rootPath)
    {
        RootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
    }

    public string RootPath { get; }

    public string GetProjectRoot(int projectId) => RootPath;

    public string GetProjectPhotosRoot(int projectId) => RootPath;

    public string GetProjectDocumentsRoot(int projectId) => RootPath;

    public string GetProjectCommentsRoot(int projectId) => RootPath;

    public string GetProjectVideosRoot(int projectId) => RootPath;

    public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => RootPath;
}
