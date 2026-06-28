namespace ProjectManagement.Features.MediaLibrary.Domain;

public enum MediaLibrarySourceType
{
    Prism = 0,
    FileSystem = 1,

    [Obsolete("Use FileSystem. The value is retained for database and configuration compatibility.")]
    NetworkShare = FileSystem
}

public enum MediaAssetOrigin
{
    ProjectPhoto = 0,
    ProjectVideo = 1,
    VisitPhoto = 2,
    SocialMediaEventPhoto = 3,
    ExternalFile = 4,

    [Obsolete("Use ExternalFile. The value is retained for database compatibility.")]
    NetworkFile = ExternalFile
}

public enum MediaAssetKind
{
    Photo = 0,
    Video = 1
}

public enum MediaClassification
{
    Unknown = 0,
    Photograph = 1,
    Screenshot = 2,
    ScannedDocument = 3,
    Diagram = 4,
    PresentationSlide = 5,
    Graphic = 6
}

public enum MediaAvailabilityStatus
{
    Available = 0,
    TemporarilyUnavailable = 1,
    SourceMissing = 2,
    AccessDenied = 3,
    Unsupported = 4,
    Corrupt = 5
}

public enum MediaProcessingStatus
{
    NotRequested = 0,
    Pending = 1,
    Processing = 2,
    Ready = 3,
    Failed = 4,
    Unsupported = 5
}

public enum MediaProcessingJobType
{
    AnalyseAsset = 0,
    RebuildDerivatives = 1,
    ReclassifyAsset = 2,
    DetectFaces = 3
}

public enum MediaProcessingJobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    DeadLetter = 4
}

public enum FaceIdentityStatus
{
    Unidentified = 0,
    Suggested = 1,
    Confirmed = 2,
    Rejected = 3
}

public enum FaceClusterStatus
{
    Active = 0,
    Hidden = 1,
    Merged = 2,
    NeedsReview = 3
}
