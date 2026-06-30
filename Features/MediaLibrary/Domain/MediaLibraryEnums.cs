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
    ActivityPhoto = 5,

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

public enum MediaClassificationDecisionStatus
{
    NotProcessed = 0,
    AutomaticallyAccepted = 1,
    NeedsReview = 2,
    ManuallyConfirmed = 3,
    ManuallyCorrected = 4,
    ProcessingFailed = 5,
    NotApplicable = 6,
    ManualFaceProcessingApproved = 7
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
    DetectFaces = 3,
    BuildDerivatives = 4,
    ExtractMetadata = 5,
    ClassifyMedia = 6,
    GenerateFaceEmbeddings = 7,
    AssignFaceCluster = 8,
    RebuildIntelligence = 9
}

public enum MediaProcessingJobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    DeadLetter = 4
}


public enum FaceCandidateSearchStatus
{
    NotRequested = 0,
    Pending = 1,
    Processing = 2,
    Ready = 3,
    Failed = 4
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


public enum FaceQualityStatus
{
    Detected = 0,
    EmbeddingEligible = 1,
    LowResolution = 2,
    Blurred = 3,
    PoorExposure = 4,
    ExtremePose = 5,
    Occluded = 6,
    Suppressed = 7,
    ProcessingFailed = 8
}

public enum MediaPersonStatus { Unreviewed = 0, Confirmed = 1, Hidden = 2, Merged = 3, Archived = 4 }
public enum FaceAssignmentType { AutomaticCandidate = 0, HumanConfirmed = 1, HumanRejected = 2, ManualAssignment = 3 }

/// <summary>
/// Controls whether a confirmed appearance is allowed to influence future biometric matching.
/// Human confirmation and biometric reference trust are deliberately separate decisions.
/// </summary>
public enum FaceReferenceStatus
{
    NotReference = 0,
    TrustedReference = 1,
    Excluded = 2
}

/// <summary>
/// Human-readable review evidence band. This is not a probability and never confirms identity.
/// </summary>
public enum FaceCandidateConfidenceLevel
{
    None = 0,
    Possible = 1,
    Strong = 2
}

public enum FaceReviewDecisionType { Pending = 0, Confirmed = 1, Rejected = 2, Ignored = 3, NewPersonCreated = 4 }
