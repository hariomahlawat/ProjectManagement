using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore.Storage;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Storage;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Projects
{
    public sealed class ProjectVideoService : IProjectVideoService
    {
        private readonly ApplicationDbContext _db;
        private readonly IClock _clock;
        private readonly IAuditService _audit;
        private readonly IUploadRootProvider _uploadRootProvider;
        private readonly ProjectVideoOptions _options;
        private readonly ILogger<ProjectVideoService> _logger;
        private readonly IVirusScanner? _virusScanner;

        public ProjectVideoService(ApplicationDbContext db,
                                   IClock clock,
                                   IAuditService audit,
                                   IUploadRootProvider uploadRootProvider,
                                   IOptions<ProjectVideoOptions> options,
                                   ILogger<ProjectVideoService> logger,
                                   IVirusScanner? virusScanner = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _virusScanner = virusScanner;
        }

        public async Task<ProjectVideo> AddAsync(int projectId,
                                                Stream content,
                                                string originalFileName,
                                                string? contentType,
                                                string userId,
                                                string? title,
                                                string? description,
                                                int? totId,
                                                bool setAsFeatured,
                                                CancellationToken cancellationToken)
        {
            if (content is null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User ID is required.", nameof(userId));
            }

            var project = await _db.Projects
                .Include(p => p.Videos)
                .Include(p => p.Tot)
                .SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                .ConfigureAwait(false);

            if (project is null)
            {
                throw new InvalidOperationException($"Project {projectId} was not found.");
            }

            ValidateTotAssociation(project, totId);

            var sanitizedName = FileNameSanitizer.Sanitize(originalFileName);
            var extension = Path.GetExtension(sanitizedName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".mp4";
            }

            var normalizedContentType = NormalizeContentType(contentType, extension);
            EnsureContentTypeAllowed(normalizedContentType);

            var storageKey = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var destinationDirectory = ResolveProjectVideosRoot(projectId);
            var destinationPath = Path.Combine(destinationDirectory, storageKey + extension);

            Directory.CreateDirectory(destinationDirectory);

            long totalBytes = 0;
            await using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                totalBytes = await CopyStreamAsync(content, destination, cancellationToken).ConfigureAwait(false);
            }

            await ScanIfRequiredAsync(destinationPath, sanitizedName, cancellationToken).ConfigureAwait(false);
            if (_options.MaxFileSizeBytes > 0 && totalBytes > _options.MaxFileSizeBytes)
            {
                SafeDelete(destinationPath);
                throw new InvalidOperationException($"The uploaded video exceeds the maximum allowed size of {_options.MaxFileSizeBytes / (1024 * 1024)} MB.");
            }

            var now = _clock.UtcNow.UtcDateTime;
            var ordinal = project.Videos.Count == 0 ? 1 : project.Videos.Max(v => v.Ordinal) + 1;
            var shouldFeature = setAsFeatured || project.FeaturedVideoId is null;

            var video = new ProjectVideo
            {
                ProjectId = projectId,
                StorageKey = storageKey,
                OriginalFileName = sanitizedName,
                ContentType = normalizedContentType,
                FileSize = totalBytes,
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(sanitizedName) : title?.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description!.Trim(),
                TotId = totId,
                Ordinal = ordinal,
                IsFeatured = shouldFeature,
                CreatedUtc = now,
                UpdatedUtc = now,
                Version = 1
            };

            _db.ProjectVideos.Add(video);
            project.Videos.Add(video);

            if (shouldFeature)
            {
                foreach (var existing in project.Videos.Where(v => v.IsFeatured && v.Id != video.Id))
                {
                    existing.IsFeatured = false;
                }

                video.IsFeatured = true;
            }

            IDbContextTransaction? transaction = null;
            try
            {
                transaction = await _db.Database
                    .BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);

                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                if (shouldFeature)
                {
                    project.FeaturedVideoId = video.Id;
                    project.FeaturedVideoVersion = video.Version;
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                SafeDelete(destinationPath);
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
                throw;
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }
            }

            await _audit.LogAsync(
                "project.video.upload",
                $"Video '{video.Title ?? video.OriginalFileName}' uploaded to project {projectId}",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["projectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                    ["videoId"] = video.Id.ToString(CultureInfo.InvariantCulture),
                    ["storageKey"] = video.StorageKey,
                    ["featured"] = shouldFeature.ToString(CultureInfo.InvariantCulture)
                });

            return video;
        }

        public async Task<ProjectVideo?> UpdateMetadataAsync(int projectId,
                                                             int videoId,
                                                             string? title,
                                                             string? description,
                                                             int? totId,
                                                             string userId,
                                                             CancellationToken cancellationToken)
        {
            var video = await _db.ProjectVideos
                .Include(v => v.Project)
                .SingleOrDefaultAsync(v => v.Id == videoId && v.ProjectId == projectId, cancellationToken)
                .ConfigureAwait(false);

            if (video is null)
            {
                return null;
            }

            ValidateTotAssociation(video.Project, totId);

            var trimmedTitle = string.IsNullOrWhiteSpace(title) ? null : title!.Trim();
            var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description!.Trim();

            var hasChanges = false;
            if (!string.Equals(video.Title, trimmedTitle, StringComparison.Ordinal))
            {
                video.Title = trimmedTitle;
                hasChanges = true;
            }

            if (!string.Equals(video.Description, trimmedDescription, StringComparison.Ordinal))
            {
                video.Description = trimmedDescription;
                hasChanges = true;
            }

            if (video.TotId != totId)
            {
                video.TotId = totId;
                hasChanges = true;
            }

            if (!hasChanges)
            {
                return video;
            }

            video.UpdatedUtc = _clock.UtcNow.UtcDateTime;
            video.Version += 1;

            if (video.Project.FeaturedVideoId == video.Id)
            {
                video.Project.FeaturedVideoVersion = video.Version;
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await _audit.LogAsync(
                "project.video.update",
                $"Video '{video.Title ?? video.OriginalFileName}' metadata updated for project {projectId}",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["projectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                    ["videoId"] = video.Id.ToString(CultureInfo.InvariantCulture)
                });

            return video;
        }

        public async Task<ProjectVideo?> SetFeaturedAsync(int projectId,
                                                          int videoId,
                                                          bool isFeatured,
                                                          string userId,
                                                          CancellationToken cancellationToken)
        {
            var project = await _db.Projects
                .Include(p => p.Videos)
                .SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                .ConfigureAwait(false);

            if (project is null)
            {
                return null;
            }

            var target = project.Videos.SingleOrDefault(v => v.Id == videoId);
            if (target is null)
            {
                return null;
            }

            if (isFeatured)
            {
                foreach (var video in project.Videos)
                {
                    video.IsFeatured = video.Id == videoId;
                    if (video.IsFeatured)
                    {
                        video.Version += 1;
                        video.UpdatedUtc = _clock.UtcNow.UtcDateTime;
                        project.FeaturedVideoId = video.Id;
                        project.FeaturedVideoVersion = video.Version;
                    }
                }
            }
            else
            {
                if (project.FeaturedVideoId == videoId)
                {
                    project.FeaturedVideoId = null;
                    project.FeaturedVideoVersion = 0;
                }

                target.IsFeatured = false;
                target.Version += 1;
                target.UpdatedUtc = _clock.UtcNow.UtcDateTime;
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await _audit.LogAsync(
                "project.video.feature",
                $"Video '{target.Title ?? target.OriginalFileName}' featured state changed for project {projectId}",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["projectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                    ["videoId"] = videoId.ToString(CultureInfo.InvariantCulture),
                    ["featured"] = isFeatured.ToString(CultureInfo.InvariantCulture)
                });

            return target;
        }

        public async Task<bool> RemoveAsync(int projectId, int videoId, string userId, CancellationToken cancellationToken)
        {
            var video = await _db.ProjectVideos
                .Include(v => v.Project)
                .SingleOrDefaultAsync(v => v.ProjectId == projectId && v.Id == videoId, cancellationToken)
                .ConfigureAwait(false);

            if (video is null)
            {
                return false;
            }

            _db.ProjectVideos.Remove(video);
            video.Project.Videos.Remove(video);

            if (video.Project.FeaturedVideoId == video.Id)
            {
                video.Project.FeaturedVideoId = null;
                video.Project.FeaturedVideoVersion = 0;
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            DeleteVideoFiles(video);

            await _audit.LogAsync(
                "project.video.remove",
                $"Video '{video.Title ?? video.OriginalFileName}' removed from project {projectId}",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["projectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                    ["videoId"] = videoId.ToString(CultureInfo.InvariantCulture)
                });

            return true;
        }

        public async Task ReorderAsync(int projectId,
                                       IReadOnlyList<int> orderedVideoIds,
                                       string userId,
                                       CancellationToken cancellationToken)
        {
            if (orderedVideoIds is null)
            {
                throw new ArgumentNullException(nameof(orderedVideoIds));
            }

            var videos = await _db.ProjectVideos
                .Include(v => v.Project)
                .Where(v => v.ProjectId == projectId)
                .OrderBy(v => v.Ordinal)
                .ThenBy(v => v.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (videos.Count != orderedVideoIds.Count ||
                videos.Select(v => v.Id).Except(orderedVideoIds).Any())
            {
                throw new InvalidOperationException("The provided video ordering is invalid.");
            }

            for (var i = 0; i < orderedVideoIds.Count; i++)
            {
                var video = videos.Single(v => v.Id == orderedVideoIds[i]);
                var newOrdinal = i + 1;
                if (video.Ordinal != newOrdinal)
                {
                    video.Ordinal = newOrdinal;
                    video.Version += 1;
                    video.UpdatedUtc = _clock.UtcNow.UtcDateTime;
                    if (video.Project?.FeaturedVideoId == video.Id)
                    {
                        video.Project.FeaturedVideoVersion = video.Version;
                    }
                }
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await _audit.LogAsync(
                "project.video.reorder",
                $"Videos reordered for project {projectId}",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["projectId"] = projectId.ToString(CultureInfo.InvariantCulture)
                });
        }

        public async Task<(Stream Stream, string ContentType)?> OpenOriginalAsync(int projectId,
                                                                                  int videoId,
                                                                                  CancellationToken cancellationToken)
        {
            var video = await _db.ProjectVideos
                .AsNoTracking()
                .SingleOrDefaultAsync(v => v.ProjectId == projectId && v.Id == videoId, cancellationToken)
                .ConfigureAwait(false);

            if (video is null)
            {
                return null;
            }

            var path = GetVideoFilePath(video);
            if (!File.Exists(path))
            {
                return null;
            }

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return (stream, video.ContentType);
        }

        public async Task<(Stream Stream, string ContentType)?> OpenPosterAsync(int projectId,
                                                                                int videoId,
                                                                                CancellationToken cancellationToken)
        {
            var video = await _db.ProjectVideos
                .AsNoTracking()
                .SingleOrDefaultAsync(v => v.ProjectId == projectId && v.Id == videoId, cancellationToken)
                .ConfigureAwait(false);

            if (video is null || string.IsNullOrWhiteSpace(video.PosterStorageKey))
            {
                return null;
            }

            var path = GetPosterFilePath(video);
            if (!File.Exists(path))
            {
                return null;
            }

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var contentType = string.IsNullOrWhiteSpace(video.PosterContentType) ? "image/jpeg" : video.PosterContentType;
            return (stream, contentType);
        }

        private static async Task<long> CopyStreamAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            if (source.CanSeek)
            {
                source.Position = 0;
            }

            long total = 0;
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            return total;
        }

        private void EnsureContentTypeAllowed(string contentType)
        {
            if (_options.AllowedContentTypes.Count == 0)
            {
                return;
            }

            if (!_options.AllowedContentTypes.Contains(contentType))
            {
                throw new InvalidOperationException($"Content type '{contentType}' is not allowed for video uploads.");
            }
        }

        private string NormalizeContentType(string? contentType, string extension)
        {
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                return contentType.Trim();
            }

            return extension.ToLowerInvariant() switch
            {
                ".webm" => "video/webm",
                ".ogg" => "video/ogg",
                _ => "video/mp4"
            };
        }

        private async Task ScanIfRequiredAsync(string path, string originalName, CancellationToken cancellationToken)
        {
            if (_virusScanner is null)
            {
                return;
            }

            try
            {
                await using var stream = File.OpenRead(path);
                await _virusScanner.ScanAsync(stream, originalName, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                SafeDelete(path);
                throw;
            }
        }

        private void DeleteVideoFiles(ProjectVideo video)
        {
            try
            {
                var path = GetVideoFilePath(video);
                SafeDelete(path);

                if (!string.IsNullOrWhiteSpace(video.PosterStorageKey))
                {
                    SafeDelete(GetPosterFilePath(video));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete video files for project {ProjectId} video {VideoId}", video.ProjectId, video.Id);
            }
        }

        private string ResolveProjectVideosRoot(int projectId)
        {
            if (!string.IsNullOrWhiteSpace(_options.StorageRootOverride))
            {
                var root = Path.GetFullPath(Path.Combine(_options.StorageRootOverride!, projectId.ToString(CultureInfo.InvariantCulture)));
                Directory.CreateDirectory(root);
                return root;
            }

            return _uploadRootProvider.GetProjectVideosRoot(projectId);
        }

        private string GetVideoFilePath(ProjectVideo video)
        {
            var extension = Path.GetExtension(video.OriginalFileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".mp4";
            }

            return Path.Combine(ResolveProjectVideosRoot(video.ProjectId), video.StorageKey + extension);
        }

        private string GetPosterFilePath(ProjectVideo video)
        {
            if (string.IsNullOrWhiteSpace(video.PosterStorageKey))
            {
                throw new InvalidOperationException("Video does not have a poster file.");
            }

            var extension = ResolvePosterExtension(video.PosterContentType);
            return Path.Combine(ResolveProjectVideosRoot(video.ProjectId), video.PosterStorageKey + extension);
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // ignored
            }
        }

        private void ValidateTotAssociation(Project project, int? totId)
        {
            if (!totId.HasValue)
            {
                return;
            }

            if (project.Tot is null || project.Tot.Id != totId.Value)
            {
                throw new InvalidOperationException("The provided ToT identifier is not associated with this project.");
            }
        }

        private string ResolvePosterExtension(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return _options.PosterFileExtension;
            }

            return contentType.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/jpeg" => ".jpg",
                "image/jpg" => ".jpg",
                _ => _options.PosterFileExtension
            };
        }
    }
}
