using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects
{
    public interface IProjectVideoService
    {
        Task<ProjectVideo> AddAsync(int projectId,
                                    Stream content,
                                    string originalFileName,
                                    string? contentType,
                                    string userId,
                                    string? title,
                                    string? description,
                                    int? totId,
                                    bool setAsFeatured,
                                    CancellationToken cancellationToken);

        Task<ProjectVideo?> UpdateMetadataAsync(int projectId,
                                                int videoId,
                                                string? title,
                                                string? description,
                                                int? totId,
                                                string userId,
                                                CancellationToken cancellationToken);

        Task<ProjectVideo?> SetFeaturedAsync(int projectId,
                                             int videoId,
                                             bool isFeatured,
                                             string userId,
                                             CancellationToken cancellationToken);

        Task<bool> RemoveAsync(int projectId, int videoId, string userId, CancellationToken cancellationToken);

        Task ReorderAsync(int projectId, IReadOnlyList<int> orderedVideoIds, string userId, CancellationToken cancellationToken);

        Task<(Stream Stream, string ContentType)?> OpenOriginalAsync(int projectId,
                                                                     int videoId,
                                                                     CancellationToken cancellationToken);

        Task<(Stream Stream, string ContentType)?> OpenPosterAsync(int projectId,
                                                                   int videoId,
                                                                   CancellationToken cancellationToken);
    }
}
