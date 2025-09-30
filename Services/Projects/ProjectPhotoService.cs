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
using ProjectManagement.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats;
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
        private readonly string _basePath;

        private static readonly object SemaphoreSync = new();
        private static SemaphoreSlim? _processingSemaphore;
        private static SemaphoreSlim? _encodingSemaphore;

        public ProjectPhotoService(ApplicationDbContext db,
                                   IClock clock,
                                   IAuditService audit,
                                   IOptions<ProjectPhotoOptions> options,
                                   ILogger<ProjectPhotoService> logger,
                                   IVirusScanner? virusScanner = null)
        {
            _db = db;
            _clock = clock;
            _audit = audit;
            _options = options.Value;
            _logger = logger;
            _virusScanner = virusScanner;
            _basePath = Environment.GetEnvironmentVariable("PM_UPLOAD_ROOT") ?? "/var/pm/uploads";

            EnsureSemaphores();
        }

        public async Task<ProjectPhoto> AddAsync(int projectId,
                                                 Stream content,
                                                 string originalFileName,
                                                 string? contentType,
                                                 string userId,
                                                 bool setAsCover,
                                                 string? caption,
                                                 CancellationToken cancellationToken)
        {
            _ = contentType;
            return await AddInternalAsync(projectId, content, originalFileName, userId, setAsCover, caption, null, cancellationToken);
        }

        public async Task<ProjectPhoto> AddAsync(int projectId,
                                                 Stream content,
                                                 string originalFileName,
                                                 string? contentType,
                                                 string userId,
                                                 bool setAsCover,
                                                 string? caption,
                                                 ProjectPhotoCrop crop,
                                                 CancellationToken cancellationToken)
        {
            _ = contentType;
            return await AddInternalAsync(projectId, content, originalFileName, userId, setAsCover, caption, crop, cancellationToken);
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

        public async Task<bool> RemoveAsync(int projectId, int photoId, string userId, CancellationToken cancellationToken)
        {
            var photo = await _db.ProjectPhotos.Include(p => p.Project)
                .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, cancellationToken);

            if (photo == null)
            {
                return false;
            }

            using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            _db.ProjectPhotos.Remove(photo);
            await _db.SaveChangesAsync(cancellationToken);

            if (photo.Project?.CoverPhotoId == photo.Id)
            {
                photo.Project.CoverPhotoId = null;
                photo.Project.CoverPhotoVersion = 0;
                await _db.SaveChangesAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);

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

            using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            for (var i = 0; i < orderedPhotoIds.Count; i++)
            {
                var photo = photos.Single(p => p.Id == orderedPhotoIds[i]);
                photo.Ordinal = i + 1;
                photo.Version += 1;
                photo.UpdatedUtc = _clock.UtcNow.UtcDateTime;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            await Audit.Events.ProjectPhotoReordered(projectId, userId, orderedPhotoIds).WriteAsync(_audit);
        }

        public async Task<(Stream Stream, string ContentType)?> OpenDerivativeAsync(int projectId, int photoId, string sizeKey, CancellationToken cancellationToken)
        {
            var photo = await _db.ProjectPhotos
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, cancellationToken);

            if (photo == null)
            {
                return null;
            }

            var path = GetDerivativePath(photo, sizeKey);
            if (!File.Exists(path))
            {
                return null;
            }

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return (stream, photo.ContentType);
        }

        public string GetDerivativePath(ProjectPhoto photo, string sizeKey)
        {
            if (photo == null)
            {
                throw new ArgumentNullException(nameof(photo));
            }

            if (!_options.Derivatives.TryGetValue(sizeKey, out _))
            {
                throw new KeyNotFoundException($"Derivative size '{sizeKey}' is not configured.");
            }

            var extension = string.Equals(photo.ContentType, "image/png", StringComparison.OrdinalIgnoreCase)
                ? ".png"
                : ".webp";

            var directory = BuildProjectDirectory(photo.ProjectId);
            return Path.Combine(directory, $"{photo.StorageKey}-{sizeKey}{extension}");
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
                                                          CancellationToken cancellationToken)
        {
            var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                ?? throw new InvalidOperationException("Project not found.");

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

            var photo = new ProjectPhoto
            {
                ProjectId = projectId,
                StorageKey = storageKey,
                OriginalFileName = sanitizedName,
                ContentType = validation.OutputContentType,
                Width = validation.CroppedWidth,
                Height = validation.CroppedHeight,
                Ordinal = ordinal + 1,
                Caption = captionValue,
                IsCover = setAsCover || project.CoverPhotoId == null,
                CreatedUtc = now,
                UpdatedUtc = now,
                Version = 1
            };

            using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            await WriteImageFilesAsync(projectId, storageKey, validation, cancellationToken);

            _db.ProjectPhotos.Add(photo);
            await _db.SaveChangesAsync(cancellationToken);

            if (photo.IsCover)
            {
                await _db.ProjectPhotos
                    .Where(p => p.ProjectId == projectId && p.Id != photo.Id && p.IsCover)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.IsCover, false), cancellationToken);

                project.CoverPhotoId = photo.Id;
                project.CoverPhotoVersion = photo.Version;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

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

            using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            DeleteAllFiles(photo);
            await WriteImageFilesAsync(projectId, photo.StorageKey, validation, cancellationToken);

            photo.OriginalFileName = SanitizeFileName(originalFileName ?? photo.OriginalFileName);
            photo.ContentType = validation.OutputContentType;
            photo.Width = validation.CroppedWidth;
            photo.Height = validation.CroppedHeight;
            photo.Version += 1;
            photo.UpdatedUtc = now;

            await _db.SaveChangesAsync(cancellationToken);

            if (photo.Project?.CoverPhotoId == photo.Id)
            {
                photo.Project.CoverPhotoVersion = photo.Version;
                await _db.SaveChangesAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);

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

                if (image.Width < _options.MinWidth || image.Height < _options.MinHeight)
                {
                    throw new InvalidOperationException($"Images must be at least {_options.MinWidth}x{_options.MinHeight}.");
                }

                var cropRectangle = crop.HasValue
                    ? ValidateCrop(image.Width, image.Height, crop.Value)
                    : CalculateDefaultCrop(image.Width, image.Height);

                image.Mutate(ctx => ctx.Crop(cropRectangle));

                var hasTransparency = DetectTransparency(image);
                var outputContentType = hasTransparency ? "image/png" : "image/webp";

                var derivativeFiles = await GenerateDerivativesAsync(image, hasTransparency, cancellationToken);

                var result = new ImageValidationResult
                {
                    OutputContentType = outputContentType,
                    DerivativeFiles = derivativeFiles,
                    CroppedWidth = image.Width,
                    CroppedHeight = image.Height
                };

                return result;
            }
            finally
            {
                ProcessingSemaphore.Release();
            }
        }

        private async Task<Dictionary<string, InMemoryFile>> GenerateDerivativesAsync(Image<Rgba32> image,
                                                                                       bool hasTransparency,
                                                                                       CancellationToken cancellationToken)
        {
            var map = new Dictionary<string, InMemoryFile>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _options.Derivatives)
            {
                var sizeKey = kvp.Key;
                var derivative = kvp.Value;

                await EncodingSemaphore.WaitAsync(cancellationToken);
                try
                {
                    using var clone = image.Clone(ctx => ctx.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(derivative.Width, derivative.Height),
                        Sampler = KnownResamplers.Lanczos3
                    }));

                    var ms = new MemoryStream();
                    if (hasTransparency)
                    {
                        var encoder = new PngEncoder
                        {
                            ColorType = PngColorType.Rgb,
                            CompressionLevel = PngCompressionLevel.BestCompression
                        };

                        await clone.SaveAsync(ms, encoder, cancellationToken);
                        map[sizeKey] = new InMemoryFile(ms, ".png");
                    }
                    else
                    {
                        var encoder = new WebpEncoder
                        {
                            FileFormat = WebpFileFormatType.Lossy,
                            Quality = derivative.Quality
                        };

                        await clone.SaveAsync(ms, encoder, cancellationToken);
                        map[sizeKey] = new InMemoryFile(ms, ".webp");
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
                var path = Path.Combine(directory, $"{storageKey}-{kvp.Key}{kvp.Value.Extension}");
                kvp.Value.Stream.Position = 0;
                await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                await kvp.Value.Stream.CopyToAsync(fs, cancellationToken);
                kvp.Value.Stream.Dispose();
            }
        }

        private void DeleteAllFiles(ProjectPhoto photo)
        {
            foreach (var key in _options.Derivatives.Keys)
            {
                var path = GetDerivativePath(photo, key);
                if (File.Exists(path))
                {
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
            return Path.Combine(_basePath, "projects", projectId.ToString());
        }

        private static Rectangle ValidateCrop(int width, int height, ProjectPhotoCrop crop)
        {
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                throw new InvalidOperationException("Crop dimensions must be positive.");
            }

            if (crop.X < 0 || crop.Y < 0 || crop.X + crop.Width > width || crop.Y + crop.Height > height)
            {
                throw new InvalidOperationException("Crop rectangle must be within the image bounds.");
            }

            if (crop.Width * 3 != crop.Height * 4)
            {
                throw new InvalidOperationException("Crop rectangle must maintain a 4:3 aspect ratio.");
            }

            return new Rectangle(crop.X, crop.Y, crop.Width, crop.Height);
        }

        private static Rectangle CalculateDefaultCrop(int width, int height)
        {
            var desiredRatio = 4d / 3d;
            var currentRatio = width / (double)height;

            if (Math.Abs(currentRatio - desiredRatio) < 0.0001)
            {
                return new Rectangle(0, 0, width, height);
            }

            int cropWidth;
            int cropHeight;
            if (currentRatio > desiredRatio)
            {
                cropHeight = height;
                cropWidth = (int)Math.Round(height * desiredRatio);
            }
            else
            {
                cropWidth = width;
                cropHeight = (int)Math.Round(width / desiredRatio);
            }

            var x = (width - cropWidth) / 2;
            var y = (height - cropHeight) / 2;

            cropWidth = cropHeight * 4 / 3;
            cropHeight = cropWidth * 3 / 4;

            return new Rectangle(x, y, cropWidth, cropHeight);
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
            var path = GetDerivativePath(photo, "xl");
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
            public string OutputContentType { get; init; } = "image/webp";

            public Dictionary<string, InMemoryFile> DerivativeFiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);

            public int CroppedWidth { get; init; }

            public int CroppedHeight { get; init; }
        }

        private sealed record InMemoryFile(MemoryStream Stream, string Extension)
        {
            public MemoryStream Stream { get; } = Stream;

            public string Extension { get; } = Extension;
        }
    }
}
