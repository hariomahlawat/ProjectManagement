using System;
using System.IO;
using System.Net.Mime;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services.Security;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Controllers;

[Authorize]
[ApiController]
[Route("files")]
public sealed class FilesController : ControllerBase
{
    private readonly IFileAccessTokenService _tokenService;
    private readonly IUploadPathResolver _pathResolver;
    private readonly ILogger<FilesController>? _logger;
    private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

    public FilesController(
        IFileAccessTokenService tokenService,
        IUploadPathResolver pathResolver,
        ILogger<FilesController>? logger = null)
    {
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger;
    }

    [HttpGet("{token}")]
    public IActionResult Get(string token, [FromQuery] string? mode)
    {
        var errorResult = TryResolveFileRequest(token, out var resolvedRequest);
        if (errorResult is not null || resolvedRequest is null)
        {
            return errorResult ?? NotFound();
        }

        Response.Headers["Cache-Control"] = "private, no-store";

        var isInline = string.Equals(mode, "inline", StringComparison.OrdinalIgnoreCase);
        if (isInline)
        {
            return File(resolvedRequest.Stream, resolvedRequest.ContentType, enableRangeProcessing: true);
        }

        return File(resolvedRequest.Stream, resolvedRequest.ContentType, resolvedRequest.FileName, enableRangeProcessing: true);
    }

    // SECTION: Request resolution helpers
    private IActionResult? TryResolveFileRequest(string token, out ResolvedFileRequest? resolvedRequest)
    {
        resolvedRequest = null;

        if (!_tokenService.TryValidate(token, out var payload) || payload is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(payload.UserId))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.Equals(payload.UserId, userId, StringComparison.Ordinal))
            {
                return Forbid();
            }
        }

        string absolutePath;
        try
        {
            absolutePath = _pathResolver.ToAbsolute(payload.StorageKey);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Rejected file download for invalid path token.");
            return NotFound();
        }

        if (!System.IO.File.Exists(absolutePath))
        {
            return NotFound();
        }

        var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var contentType = payload.ContentType;
        if (string.IsNullOrWhiteSpace(contentType) && !_contentTypeProvider.TryGetContentType(absolutePath, out contentType))
        {
            contentType = MediaTypeNames.Application.Octet;
        }

        var fileName = payload.FileName;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = Path.GetFileName(absolutePath);
        }

        resolvedRequest = new ResolvedFileRequest(stream, contentType!, fileName);
        return null;
    }

    private sealed record ResolvedFileRequest(FileStream Stream, string ContentType, string FileName);
}
