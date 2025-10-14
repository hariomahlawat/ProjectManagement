using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.SocialMedia.ViewModels;

public sealed record SocialMediaEventPhotoItem(
    Guid Id,
    string? Caption,
    string? VersionStamp,
    bool IsCover,
    string RowVersion);

public sealed record SocialMediaEventPhotoGalleryModel(
    Guid EventId,
    IReadOnlyList<SocialMediaEventPhotoItem> Photos,
    bool CanManage);
