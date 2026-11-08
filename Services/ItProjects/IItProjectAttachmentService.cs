using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ProjectManagement.Services.ItProjects;

/// <summary>One attachment as the project page lists it.</summary>
public sealed record ItProjectAttachmentView(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string UploadedByUserId,
    string UploadedByName,
    DateTime UploadedAtUtc);

/// <summary>An open attachment ready to be sent. The caller disposes the stream.</summary>
public sealed record ItProjectAttachmentDownload(Stream Content, string FileName, string ContentType);

public interface IItProjectAttachmentService
{
    /// <summary>The configured upload limit, for the page's hint.</summary>
    long MaxSizeBytes { get; }

    /// <summary>Live attachments of a project, newest first.</summary>
    Task<IReadOnlyList<ItProjectAttachmentView>> ListAsync(int itProjectId, CancellationToken cancellationToken = default);

    /// <summary>Validates, scans and stores the file, then writes its record and the audit row. Nothing is kept when any step fails.</summary>
    Task<(ItProjectCommandResult Result, Guid AttachmentId)> UploadAsync(int itProjectId, IFormFile file, ItProjectActor actor, CancellationToken cancellationToken = default);

    /// <summary>Opens a live attachment of the given, live project; null when there is no such attachment on that project.</summary>
    Task<ItProjectAttachmentDownload?> OpenAsync(int itProjectId, Guid attachmentId, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a live attachment of the given, live project. The file stays on disk for a later restore.</summary>
    Task<ItProjectCommandResult> DeleteAsync(int itProjectId, Guid attachmentId, ItProjectActor actor, CancellationToken cancellationToken = default);
}
