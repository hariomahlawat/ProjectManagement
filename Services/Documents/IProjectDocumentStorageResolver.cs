using System;
using System.IO;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.Documents;

// SECTION: Project document storage path resolver contract
public interface IProjectDocumentStorageResolver
{
    string ResolveAbsolutePath(string storageKey);
}

// SECTION: Project document storage path resolver implementation
public sealed class ProjectDocumentStorageResolver : IProjectDocumentStorageResolver
{
    private readonly IUploadRootProvider _uploadRootProvider;

    public ProjectDocumentStorageResolver(IUploadRootProvider uploadRootProvider)
    {
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
    }

    public string ResolveAbsolutePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key is required.", nameof(storageKey));
        }

        var normalized = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.Combine(_uploadRootProvider.RootPath, normalized);
        var full = Path.GetFullPath(combined);
        var root = Path.GetFullPath(_uploadRootProvider.RootPath);

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid storage key.");
        }

        return full;
    }
}
