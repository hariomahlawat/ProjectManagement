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
    string Title,
    string ProjectName,
    string IprType,
    string ApplicationNumber,
    string Status,
    string StatusChipClass,
    string? ExternalRemark,
    DateTime? FiledOn,
    DateTime? GrantedOn,
    IReadOnlyList<IprRecordAttachmentViewModel> Attachments,
    int AttachmentCount);
