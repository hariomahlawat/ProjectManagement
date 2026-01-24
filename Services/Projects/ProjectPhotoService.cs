using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Storage;
using ProjectManagement.Infrastructure;
using ProjectManagement.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ProjectManagement.Services.Projects
{
    public class ProjectPhotoService : IProjectPhotoService
    {
        private readonly ApplicationDbContext _db;
        private readonly IClock _clock;
        private readonly IAuditService _audit;
        private readonly ProjectPhotoOptions _options;
        private readonly ILogger<ProjectPhotoService> _logger;
        private readonly IVirusScanner? _virusScanner;
        private readonly IUploadRootProvider _uploadRootProvider;

        // SECTION: Quality thresholds
        private const int LowResolutionWidthThreshold = 400;
        private const int LowResolutionHeightThreshold = 300;

        private static readonly object SemaphoreSync = new();
        private static SemaphoreSlim? _processingSemaphore;
        private static SemaphoreSlim? _encodingSemaphore;

        public ProjectPhotoService(ApplicationDbContext db,
                                   IClock clock,
                                   IAuditService audit,
                                   IOptions<ProjectPhotoOptions> options,
                                   IUploadRootProvider uploadRootProvider,
                                   ILogger<ProjectPhotoService> logger,
                                   IVirusScanner? virusScanner = null)
        {
            _db = db;
            _clock = clock;
            _audit = audit;
            _options = options.Value;
            _logger = logger;
            _virusScanner = virusScanner;
            _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));

            EnsureSemaphores();
        }

        public async Task<ProjectPhoto> AddAsync(int projectId,
                                                 Stream content,
                                                 string originalFileName,
                                                 string? contentType,
                                                 string userId,
                                                 bool setAsCover,
                                                 string? caption,
                                                 int? totId,
                                                 CancellationToken cancellationToken)
        {
            _ = contentType;
            return await AddInternalAsync(projectId, content, originalFileName, userId, setAsCover, caption, null, totId, cancellationToken);
        }

        public async Task<ProjectPhoto> AddAsync(int projectId,
                                                 Stream content,
                                                 string originalFileName,
                                                 string? contentType,
                                                 string userId,
                                                 bool setAsCover,
                                                 string? caption,
                                                 ProjectPhotoCrop crop,
                                                 int? totId,
                                                 CancellationToken cancellationToken)
        {
            _ = contentType;
            return await AddInternalAsync(projectId, content, originalFileName, userId, setAsCover, caption, crop, totId, cancellationToken);
        }

        public async Task<ProjectPhoto?> ReplaceAsync(int projectId,
                                                      int photoId,
                                                      Stream content,
                                                      string originalFileName,
                                                      string? contentType,
                                                      string userId,
                                                      CancellationToken cancellationToken)
        {
            _ = contentType;
            return await ReplaceInternalAsync(projectId, photoId, content, originalFileName, userId, null, cancellationToken);
        }

        public async Task<ProjectPhoto?> ReplaceAsync(int projectId,
                                                      int photoId,
                                                      Stream content,
                                                      string originalFileName,
                                                      string? contentType,
                                                      string userId,
                                                      ProjectPhotoCrop crop,
                                                      CancellationToken cancellationToken)
        {
            _ = contentType;
            return await ReplaceInternalAsync(projectId, photoId, content, originalFileName, userId, crop, cancellationToken);
        }

        public async Task<ProjectPhoto?> UpdateCaptionAsync(int projectId, int photoId, string? caption, string userId, CancellationToken cancellationToken)
        {
            var photo = await _db.ProjectPhotos.Include(p => p.Project)
                .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, cancellationToken);

            if (photo == null)
            {
                return null;
            }

            var trimmed = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
            photo.Caption = trimmed;
            photo.UpdatedUtc = _clock.UtcNow.UtcDateTime;
            photo.Version += 1;

            await _db.SaveChangesAsync(cancellationToken);

            if (photo.Project != null && photo.Project.CoverPhotoId == photo.Id)
            {
                photo.Project.CoverPhotoVersion = photo.Version;
                await _db.SaveChangesAsync(cancellationToken);
            }

            await Audit.Events.ProjectPhotoUpdated(projectId, photo.Id, userId, "CaptionUpdated").WriteAsync(_audit);

            return photo;
        }

        public async Task<ProjectPhoto?> UpdateCropAsync(int projectId, int photoId, ProjectPhotoCrop crop, string userId, CancellationToken cancellationToken)
        {
            return await ReplaceInternalAsync(projectId, photoId, null, null, userId, crop, cancellationToken, reuseOriginal: true);
        }

        public async Task<ProjectPhoto?> UpdateTotAsync(int projectId, int photoId, int? totId, string userId, CancellationToken cancellationToken)
        {
            var photo = await _db.ProjectPhotos
                .Include(p => p.Project)
                .ThenInclude(p => p.Tot)
                .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, cancellationToken);

            if (photo == null)
            {
                return null;
            }

            var project = photo.Project;
            if (project == null)
            {
                project = await _db.Projects
                    .Include(p => p.Tot)
                    .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                    ?? throw new InvalidOperationException("Project not found.");
            }

            if (totId.HasValue)
            {
                if (project.Tot is null || project.Tot.Id != totId.Value)
                {
                    throw new InvalidOperationException("Selected Transfer of Technology record was not found for this project.");
                }

                if (project.Tot.Status == ProjectTotStatus.NotRequired)
                {
                    throw new InvalidOperationException("Transfer of Technology is not required for this project.");
                }
            }

            if (photo.TotId == totId)
            {
                return photo;
            }

            photo.TotId = totId;
            photo.Version += 1;
            photo.UpdatedUtc = _clock.UtcNow.UtcDateTime;

            await _db.SaveChangesAsync(cancellationToken);

            var changeType = totId.HasValue ? "TotLinked" : "TotCleared";
            await Audit.Events.ProjectPhotoUpdated(projectId, photo.Id, userId, changeType).WriteAsync(_audit);

            return photo;
        }

        public async Task<bool> RemoveAsync(int projectId, int photoId, string userId, CancellationToken cancellationToken)
        {
            var photo = await _db.ProjectPhotos.Include(p => p.Project)
                .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, cancellationToken);

            if (photo == null)
            {
                return false;
            }

            await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

            _db.ProjectPhotos.Remove(photo);
            await _db.SaveChangesAsync(cancellationToken);

            if (photo.Project?.CoverPhotoId == photo.Id)
            {
                var replacement = await _db.ProjectPhotos
                    .Where(p => p.ProjectId == projectId)
                    .OrderBy(p => p.Ordinal)
                    .ThenBy(p => p.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (replacement != null)
                {
                    await _db.ProjectPhotos
                        .Where(p => p.ProjectId == projectId && p.Id != replacement.Id && p.IsCover)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.IsCover, false), cancellationToken);

                    replacement.IsCover = true;
                    replacement.Version += 1;
                    replacement.UpdatedUtc = _clock.UtcNow.UtcDateTime;

                    photo.Project.CoverPhotoId = replacement.Id;
                    photo.Project.CoverPhotoVersion = replacement.Version;
                }
                else
                {
                    photo.Project.CoverPhotoId = null;
                    photo.Project.CoverPhotoVersion = 0;
                }

                await _db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            DeleteAllFiles(photo);

            await Audit.Events.ProjectPhotoRemoved(projectId, photo.Id, userId).WriteAsync(_audit);

            return true;
        }

        public async Task ReorderAsync(int projectId, IReadOnlyList<int> orderedPhotoIds, string userId, CancellationToken cancellationToken)
        {
            if (orderedPhotoIds == null)
            {
                throw new ArgumentNullException(nameof(orderedPhotoIds));
            }

            var photos = await _db.ProjectPhotos
                .Where(p => p.ProjectId == projectId)
                .ToListAsync(cancellationToken);

            if (photos.Count != orderedPhotoIds.Count || photos.Any(p => !orderedPhotoIds.Contains(p.Id)) || orderedPhotoIds.Distinct().Count() != orderedPhotoIds.Count)
            {
                throw new InvalidOperationException("Order does not include all project photos.");
            }

            await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

            for (var i = 0; i < orderedPhotoIds.Count; i++)
            {
                var photo = photos.Single(p => p.Id == orderedPhotoIds[i]);
                photo.Ordinal = i + 1;
                photo.Version += 1;
                photo.UpdatedUtc = _clock.UtcNow.UtcDateTime;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await Audit.Events.ProjectPhotoReordered(projectId, userId, orderedPhotoIds).WriteAsync(_audit);
        }

        public async Task<(Stream Stream, string ContentType)?> OpenDerivativeAsync(int projectId,
                                                                                    int photoId,
                                                                                    string sizeKey,
                                                                                    bool preferWebp,
                                                                                    CancellationToken cancellationToken)
        {
            var photo = await _db.ProjectPhotos
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, cancellationToken);

            if (photo == null)
            {
                return null;
            }

            foreach (var (path, contentType) in EnumerateDerivativeCandidates(photo, sizeKey, preferWebp))
            {
                if (File.Exists(path))
                {
                    var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return (stream, contentType);
                }
            }

            return null;
        }

        public string GetDerivativePath(ProjectPhoto photo, string sizeKey, bool preferWebp)
        {
            if (photo == null)
            {
                throw new ArgumentNullException(nameof(photo));
            }

            if (!_options.Derivatives.TryGetValue(sizeKey, out _))
            {
                throw new KeyNotFoundException($"Derivative size '{sizeKey}' is not configured.");
            }

            var directory = BuildProjectDirectory(photo.ProjectId);
            var extension = preferWebp ? ".webp" : GetFallbackExtension(photo);
            return Path.Combine(directory, $"{photo.StorageKey}-{sizeKey}{extension}");
        }

        private IEnumerable<(string Path, string ContentType)> EnumerateDerivativeCandidates(ProjectPhoto photo,
                                                                                             string sizeKey,
                                                                                             bool preferWebp)
        {
            if (!_options.Derivatives.ContainsKey(sizeKey))
            {
                yield break;
            }

            var directory = BuildProjectDirectory(photo.ProjectId);
            var basePath = Path.Combine(directory, $"{photo.StorageKey}-{sizeKey}");
            var fallbackExtension = GetFallbackExtension(photo);
            var fallbackContentType = GetFallbackContentType(photo);

            var fallbackCandidates = new List<(string Path, string ContentType)>
            {
                ($"{basePath}{fallbackExtension}", fallbackContentType)
            };

            if (!string.Equals(fallbackExtension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                fallbackCandidates.Add(($"{basePath}.png", "image/png"));
            }

            if (!string.Equals(fallbackExtension, ".jpg", StringComparison.OrdinalIgnoreCase))
            {
                fallbackCandidates.Add(($"{basePath}.jpg", "image/jpeg"));
            }

            if (!string.Equals(fallbackExtension, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                fallbackCandidates.Add(($"{basePath}.jpeg", "image/jpeg"));
            }

            var webpCandidate = ($"{basePath}.webp", "image/webp");

            var ordered = preferWebp
                ? Enumerable.Repeat(webpCandidate, 1).Concat(fallbackCandidates)
                : fallbackCandidates.Concat(Enumerable.Repeat(webpCandidate, 1));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, contentType) in ordered)
            {
                if (seen.Add(path))
                {
                    yield return (path, contentType);
                }
            }
        }

        private void EnsureSemaphores()
        {
            lock (SemaphoreSync)
            {
                _processingSemaphore ??= new SemaphoreSlim(Math.Max(_options.MaxProcessingConcurrency, 1), Math.Max(_options.MaxProcessingConcurrency, 1));
                _encodingSemaphore ??= new SemaphoreSlim(Math.Max(_options.MaxEncodingConcurrency, 1), Math.Max(_options.MaxEncodingConcurrency, 1));
            }
        }

        private SemaphoreSlim ProcessingSemaphore => _processingSemaphore ?? throw new InvalidOperationException("Processing semaphore not initialised.");

        private SemaphoreSlim EncodingSemaphore => _encodingSemaphore ?? throw new InvalidOperationException("Encoding semaphore not initialised.");

        private async Task<ProjectPhoto> AddInternalAsync(int projectId,
                                                          Stream content,
                                                          string originalFileName,
                                                          string userId,
                                                          bool setAsCover,
                                                          string? caption,
                                                          ProjectPhotoCrop? crop,
                                                          int? totId,
                                                          CancellationToken cancellationToken)
        {
            var project = await _db.Projects
                .Include(p => p.Tot)
                .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                ?? throw new InvalidOperationException("Project not found.");

            if (totId.HasValue)
            {
                if (project.Tot is null || project.Tot.Id != totId.Value)
                {
                    throw new InvalidOperationException("Selected Transfer of Technology record was not found for this project.");
                }

                if (project.Tot.Status == ProjectTotStatus.NotRequired)
                {
                    throw new InvalidOperationException("Transfer of Technology is not required for this project.");
                }
            }

            await using var copy = await CopyToMemoryStreamAsync(content, cancellationToken);

            var validation = await LoadAndValidateAsync(copy, originalFileName, crop, cancellationToken);

            var now = _clock.UtcNow.UtcDateTime;
            var storageKey = Guid.NewGuid().ToString("N");
            var sanitizedName = SanitizeFileName(originalFileName);
            var captionValue = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();

            var ordinal = await _db.ProjectPhotos
                .Where(p => p.ProjectId == projectId)
                .Select(p => (int?)p.Ordinal)
                .MaxAsync(cancellationToken) ?? 0;

            var shouldSetCover = setAsCover || project.CoverPhotoId == null;

            var photo = new ProjectPhoto
            {
                ProjectId = projectId,
                StorageKey = storageKey,
                OriginalFileName = sanitizedName,
                ContentType = validation.FallbackContentType,
                Width = validation.CroppedWidth,
                Height = validation.CroppedHeight,
                Ordinal = ordinal + 1,
                Caption = captionValue,
                TotId = totId,
                IsCover = false,
                IsLowResolution = validation.IsLowResolution,
                CreatedUtc = now,
                UpdatedUtc = now,
                Version = 1
            };

            await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

            await WriteImageFilesAsync(projectId, storageKey, validation, cancellationToken);

            _db.ProjectPhotos.Add(photo);
            await _db.SaveChangesAsync(cancellationToken);

            if (shouldSetCover)
            {
                await _db.ProjectPhotos
                    .Where(p => p.ProjectId == projectId && p.Id != photo.Id && p.IsCover)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.IsCover, false), cancellationToken);

                var coverUpdatedAt = _clock.UtcNow.UtcDateTime;

                photo.IsCover = true;
                photo.Version += 1;
                photo.UpdatedUtc = coverUpdatedAt;

                project.CoverPhotoId = photo.Id;
                project.CoverPhotoVersion = photo.Version;

                await _db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);

            await Audit.Events.ProjectPhotoAdded(projectId, photo.Id, userId, photo.IsCover).WriteAsync(_audit);

            return photo;
        }

        private async Task<ProjectPhoto?> ReplaceInternalAsync(int projectId,
                                                               int photoId,
                                                               Stream? content,
                                                               string? originalFileName,
                                                               string userId,
                                                               ProjectPhotoCrop? crop,
                                                               CancellationToken cancellationToken,
                                                               bool reuseOriginal = false)
        {
            var photo = await _db.ProjectPhotos.Include(p => p.Project)
                .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, cancellationToken);

            if (photo == null)
            {
                return null;
            }

            await using MemoryStream? copy = reuseOriginal
                ? await LoadOriginalAsync(photo, cancellationToken)
                : content != null
                    ? await CopyToMemoryStreamAsync(content, cancellationToken)
                    : null;

            if (copy == null)
            {
                throw new InvalidOperationException("Content stream is required.");
            }

            var validation = await LoadAndValidateAsync(copy, originalFileName ?? photo.OriginalFileName, crop, cancellationToken);

            var now = _clock.UtcNow.UtcDateTime;

            await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

            DeleteAllFiles(photo);
            await WriteImageFilesAsync(projectId, photo.StorageKey, validation, cancellationToken);

            photo.OriginalFileName = SanitizeFileName(originalFileName ?? photo.OriginalFileName);
            photo.ContentType = validation.FallbackContentType;
            photo.Width = validation.CroppedWidth;
            photo.Height = validation.CroppedHeight;
            photo.IsLowResolution = validation.IsLowResolution;
            photo.Version += 1;
            photo.UpdatedUtc = now;

            await _db.SaveChangesAsync(cancellationToken);

            if (photo.Project?.CoverPhotoId == photo.Id)
            {
                photo.Project.CoverPhotoVersion = photo.Version;
                await _db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            await Audit.Events.ProjectPhotoUpdated(projectId, photo.Id, userId, reuseOriginal ? "CropUpdated" : "ImageReplaced").WriteAsync(_audit);

            return photo;
        }

        private async Task<ImageValidationResult> LoadAndValidateAsync(MemoryStream content,
                                                                       string originalFileName,
                                                                       ProjectPhotoCrop? crop,
                                                                       CancellationToken cancellationToken)
        {
            if (content.Length > _options.MaxFileSizeBytes)
            {
                throw new InvalidOperationException($"Photo exceeds the maximum size of {_options.MaxFileSizeBytes} bytes.");
            }

            content.Position = 0;
            IImageFormat? detectedFormat = await Image.DetectFormatAsync(content, cancellationToken);
            if (detectedFormat == null)
            {
                throw new InvalidOperationException("Unsupported or unrecognised image format.");
            }

            if (!_options.AllowedContentTypes.Contains(detectedFormat.DefaultMimeType))
            {
                throw new InvalidOperationException($"Image format '{detectedFormat.DefaultMimeType}' is not allowed.");
            }

            content.Position = 0;

            if (_virusScanner != null)
            {
                await _virusScanner.ScanAsync(content, originalFileName, cancellationToken);
                content.Position = 0;
            }

            await ProcessingSemaphore.WaitAsync(cancellationToken);
            try
            {
                using var image = await Image.LoadAsync<Rgba32>(content, cancellationToken);
                image.Mutate(ctx => ctx.AutoOrient());
                image.Metadata.ExifProfile = null;
                image.Metadata.IptcProfile = null;
                image.Metadata.XmpProfile = null;

                var cropRectangle = crop.HasValue
                    ? ValidateCrop(image.Width, image.Height, crop.Value)
                    : CalculateDefaultCrop(image.Width, image.Height);

                image.Mutate(ctx => ctx.Crop(cropRectangle));

                var hasTransparency = DetectTransparency(image);
                var fallbackContentType = hasTransparency ? "image/png" : "image/jpeg";
                var isLowResolution = image.Width < LowResolutionWidthThreshold ||
                                      image.Height < LowResolutionHeightThreshold;

                var derivativeFiles = await GenerateDerivativesAsync(image, hasTransparency, cancellationToken);

                var result = new ImageValidationResult
                {
                    FallbackContentType = fallbackContentType,
                    DerivativeFiles = derivativeFiles,
                    CroppedWidth = image.Width,
                    CroppedHeight = image.Height,
                    IsLowResolution = isLowResolution
                };

                return result;
            }
            finally
            {
                ProcessingSemaphore.Release();
            }
        }

        private async Task<Dictionary<string, DerivativeSet>> GenerateDerivativesAsync(Image<Rgba32> image,
                                                                                         bool hasTransparency,
                                                                                         CancellationToken cancellationToken)
        {
            var map = new Dictionary<string, DerivativeSet>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _options.Derivatives)
            {
                var sizeKey = kvp.Key;
                var derivative = kvp.Value;

                await EncodingSemaphore.WaitAsync(cancellationToken);
                try
                {
                    using var clone = image.Clone(ctx => ctx.Resize(new ResizeOptions
                    {
                        // SECTION: Derivative scaling
                        Mode = ResizeMode.Max,
                        Size = new Size(derivative.Width, derivative.Height),
                        Sampler = KnownResamplers.Lanczos3
                    }));

                    clone.Metadata.ExifProfile = null;
                    clone.Metadata.IptcProfile = null;
                    clone.Metadata.XmpProfile = null;

                    var webpStream = new MemoryStream();
                    var webpEncoder = new WebpEncoder
                    {
                        FileFormat = WebpFileFormatType.Lossy,
                        Quality = derivative.Quality
                    };

                    await clone.SaveAsync(webpStream, webpEncoder, cancellationToken);
                    webpStream.Position = 0;

                    var fallbackStream = new MemoryStream();
                    if (hasTransparency)
                    {
                        var pngEncoder = new PngEncoder
                        {
                            ColorType = PngColorType.Rgb,
                            CompressionLevel = PngCompressionLevel.BestCompression
                        };

                        await clone.SaveAsync(fallbackStream, pngEncoder, cancellationToken);
                        fallbackStream.Position = 0;

                        map[sizeKey] = new DerivativeSet(
                            new InMemoryFile(webpStream, ".webp", "image/webp"),
                            new InMemoryFile(fallbackStream, ".png", "image/png"));
                    }
                    else
                    {
                        var jpegEncoder = new JpegEncoder
                        {
                            Quality = derivative.Quality
                        };

                        await clone.SaveAsync(fallbackStream, jpegEncoder, cancellationToken);
                        fallbackStream.Position = 0;

                        map[sizeKey] = new DerivativeSet(
                            new InMemoryFile(webpStream, ".webp", "image/webp"),
                            new InMemoryFile(fallbackStream, ".jpg", "image/jpeg"));
                    }
                }
                finally
                {
                    EncodingSemaphore.Release();
                }
            }

            return map;
        }

        private async Task WriteImageFilesAsync(int projectId,
                                                string storageKey,
                                                ImageValidationResult validation,
                                                CancellationToken cancellationToken)
        {
            var directory = BuildProjectDirectory(projectId);
            Directory.CreateDirectory(directory);

            foreach (var kvp in validation.DerivativeFiles)
            {
                await WriteDerivativeFileAsync(directory, storageKey, kvp.Key, kvp.Value.Webp, cancellationToken);
                await WriteDerivativeFileAsync(directory, storageKey, kvp.Key, kvp.Value.Fallback, cancellationToken);
            }
        }

        private static async Task WriteDerivativeFileAsync(string directory,
                                                            string storageKey,
                                                            string sizeKey,
                                                            InMemoryFile file,
                                                            CancellationToken cancellationToken)
        {
            var path = Path.Combine(directory, $"{storageKey}-{sizeKey}{file.Extension}");
            file.Stream.Position = 0;
            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await file.Stream.CopyToAsync(fs, cancellationToken);
            file.Stream.Dispose();
        }

        private void DeleteAllFiles(ProjectPhoto photo)
        {
            foreach (var key in _options.Derivatives.Keys)
            {
                var directory = BuildProjectDirectory(photo.ProjectId);
                var basePath = Path.Combine(directory, $"{photo.StorageKey}-{key}");
                foreach (var extension in new[] { ".webp", ".jpg", ".jpeg", ".png" })
                {
                    var path = $"{basePath}{extension}";
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete derivative {Path} for photo {PhotoId}", path, photo.Id);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _logger.LogWarning(ex, "Access denied deleting derivative {Path} for photo {PhotoId}", path, photo.Id);
                    }
                }
            }
        }

        private string BuildProjectDirectory(int projectId)
        {
            return _uploadRootProvider.GetProjectPhotosRoot(projectId);
        }

        private static string GetFallbackExtension(ProjectPhoto photo)
        {
            return photo.ContentType?.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/jpg" => ".jpg",
                _ => ".webp"
            };
        }

        private static string GetFallbackContentType(ProjectPhoto photo)
        {
            return photo.ContentType?.ToLowerInvariant() switch
            {
                "image/png" => "image/png",
                "image/jpeg" => "image/jpeg",
                "image/jpg" => "image/jpeg",
                _ => "image/webp"
            };
        }

        private static Rectangle ValidateCrop(int width, int height, ProjectPhotoCrop crop)
        {
            // SECTION: Positive dimension validation.
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                throw new InvalidOperationException("Crop dimensions must be positive.");
            }

            // SECTION: Crop bounds validation.
            if (crop.X < 0 || crop.Y < 0 || crop.X + crop.Width > width || crop.Y + crop.Height > height)
            {
                throw new InvalidOperationException("Crop rectangle must be within the image bounds.");
            }

            return new Rectangle(crop.X, crop.Y, crop.Width, crop.Height);
        }

        private static Rectangle CalculateDefaultCrop(int width, int height)
        {
            // SECTION: Default crop uses the full image to preserve arbitrary aspect ratios.
            return new Rectangle(0, 0, width, height);
        }

        private static bool DetectTransparency(Image<Rgba32> image)
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = image.DangerousGetPixelRowMemory(y).Span;
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A < 255)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static async Task<MemoryStream> CopyToMemoryStreamAsync(Stream content, CancellationToken cancellationToken)
        {
            var ms = new MemoryStream();
            if (content.CanSeek)
            {
                content.Position = 0;
            }

            await content.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;
            return ms;
        }

        private async Task<MemoryStream> LoadOriginalAsync(ProjectPhoto photo, CancellationToken cancellationToken)
        {
            var path = GetDerivativePath(photo, "xl", preferWebp: true);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Original derivative not found for recropping.", path);
            }

            var ms = new MemoryStream();
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            await fs.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;
            return ms;
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "upload";
            }

            if (fileName.Length > 260)
            {
                fileName = fileName[..260];
            }

            return FileNameSanitizer.Sanitize(fileName);
        }

        private sealed record ImageValidationResult
        {
            public string FallbackContentType { get; init; } = "image/jpeg";

            public Dictionary<string, DerivativeSet> DerivativeFiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);

            public int CroppedWidth { get; init; }

            public int CroppedHeight { get; init; }

            // SECTION: Validation metadata
            public bool IsLowResolution { get; init; }
        }

        private sealed record DerivativeSet(InMemoryFile Webp, InMemoryFile Fallback)
        {
            public InMemoryFile Webp { get; } = Webp;

            public InMemoryFile Fallback { get; } = Fallback;
        }

        private sealed record InMemoryFile(MemoryStream Stream, string Extension, string ContentType)
        {
            public MemoryStream Stream { get; } = Stream;

            public string Extension { get; } = Extension;

            public string ContentType { get; } = ContentType;
        }
    }
}
