using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects
{
    public interface IProjectPhotoService
    {
        Task<ProjectPhoto> AddAsync(int projectId,
                                    Stream content,
                                    string originalFileName,
                                    string? contentType,
                                    string userId,
                                    bool setAsCover,
                                    string? caption,
                                    CancellationToken cancellationToken);

        Task<ProjectPhoto> AddAsync(int projectId,
                                    Stream content,
                                    string originalFileName,
                                    string? contentType,
                                    string userId,
                                    bool setAsCover,
                                    string? caption,
                                    ProjectPhotoCrop crop,
                                    CancellationToken cancellationToken);

        Task<ProjectPhoto?> ReplaceAsync(int projectId,
                                         int photoId,
                                         Stream content,
                                         string originalFileName,
                                         string? contentType,
                                         string userId,
                                         CancellationToken cancellationToken);

        Task<ProjectPhoto?> ReplaceAsync(int projectId,
                                         int photoId,
                                         Stream content,
                                         string originalFileName,
                                         string? contentType,
                                         string userId,
                                         ProjectPhotoCrop crop,
                                         CancellationToken cancellationToken);

        Task<ProjectPhoto?> UpdateAsync(int projectId,
                                        int photoId,
                                        Stream? replacementContent,
                                        string? originalFileName,
                                        string? contentType,
                                        ProjectPhotoCrop? crop,
                                        string? caption,
                                        bool setAsCover,
                                        int expectedVersion,
                                        string userId,
                                        CancellationToken cancellationToken)
            => throw new System.NotSupportedException("Atomic photo updates are not supported by this implementation.");

        Task<ProjectPhoto?> UpdateCaptionAsync(int projectId, int photoId, string? caption, string userId, CancellationToken cancellationToken);

        Task<ProjectPhoto?> UpdateCropAsync(int projectId, int photoId, ProjectPhotoCrop crop, string userId, CancellationToken cancellationToken);

        Task<bool> RemoveAsync(int projectId, int photoId, string userId, CancellationToken cancellationToken);

        Task ReorderAsync(int projectId, IReadOnlyList<int> orderedPhotoIds, string userId, CancellationToken cancellationToken);

        Task<(Stream Stream, string ContentType)?> OpenDerivativeAsync(int projectId,
                                                                      int photoId,
                                                                      string sizeKey,
                                                                      bool preferWebp,
                                                                      CancellationToken cancellationToken);

        Task<(Stream Stream, string ContentType)?> OpenDerivativeAsync(int projectId,
                                                                      int photoId,
                                                                      string sizeKey,
                                                                      string requestedFormat,
                                                                      CancellationToken cancellationToken);

        string GetDerivativePath(ProjectPhoto photo, string sizeKey, bool preferWebp);
    }

    public readonly record struct ProjectPhotoCrop(int X, int Y, int Width, int Height);
}
