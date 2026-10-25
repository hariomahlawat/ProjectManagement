using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.ViewModels;

public sealed record IprRecordAttachmentViewModel(
    int Id,
    string FileName,
    string FileSize,
    string UploadedBy,
    string UploadedAt);

public sealed record IprRecordRowViewModel(
    int Id,
    int? ProjectId,
    string Title,
    string ProjectName,
    string IprType,
    string ApplicationNumber,
    string Status,
    string StatusChipClass,
    string? ExternalRemark,
    string FiledBy,
    DateTime? FiledOn,
    DateTime? GrantedOn,
    IReadOnlyList<IprRecordAttachmentViewModel> Attachments,
    int AttachmentCount,
    // Register filtering facts. StatusKey and TypeKey are the stored enum names; IsCounted
    // and IsClosed come from IprStatusPolicy so the browser never re-derives the rule.
    string StatusKey = "",
    string TypeKey = "",
    bool IsCounted = true,
    bool IsClosed = false,
    bool IsPending = false,
    string? PendingAge = null,
    int? FiledYear = null,
    int? GrantedYear = null,
    int? PendingDays = null);
