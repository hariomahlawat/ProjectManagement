namespace ProjectManagement.Features.PhotoTools;

/// <summary>
/// Settings for placing the face in Passport photo (<c>PhotoTools:PassportFace</c>). The defaults
/// are what the feature used before it had settings of its own (the upload and size limits it
/// shared with Remove background, and two at once), so a server without this section behaves as
/// before. It is on whenever the Photos library's face detector is installed.
/// </summary>
public sealed class PassportFaceOptions
{
    public const string SectionName = "PhotoTools:PassportFace";

    /// <summary>How many faces are found at once; any more are answered "busy" at once.</summary>
    public int MaximumConcurrent { get; set; } = 2;

    /// <summary>The largest upload accepted, in bytes. The page sends a 1600 px JPG.</summary>
    public long MaximumUploadBytes { get; set; } = 20L * 1024 * 1024;

    /// <summary>The longest side accepted, in pixels.</summary>
    public int MaximumSide { get; set; } = 2048;

    /// <summary>
    /// How long one face may take, from the photo arriving to the answer, in seconds. Longer is
    /// answered "busy", so the page tries once more and then lets the user place it by hand.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// How long to wait for the face detector when the Photos library is using it (People
    /// indexing), in seconds, before answering "busy" instead of holding a place.
    /// </summary>
    public int DetectorWaitSeconds { get; set; } = 5;

    public int EffectiveConcurrent => Math.Clamp(MaximumConcurrent, 1, 16);

    public long EffectiveUploadBytes => Math.Clamp(MaximumUploadBytes, 1L * 1024 * 1024, 64L * 1024 * 1024);

    public int EffectiveSide => Math.Clamp(MaximumSide, 256, 4096);

    public TimeSpan EffectiveTimeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 2, 120));

    public TimeSpan EffectiveDetectorWait => TimeSpan.FromSeconds(Math.Clamp(DetectorWaitSeconds, 1, 60));
}
