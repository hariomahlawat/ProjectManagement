namespace ProjectManagement.Configuration;

public sealed class ProjectRetentionOptions
{
    public const int DefaultTrashRetentionDays = 30;

    public int TrashRetentionDays { get; set; } = DefaultTrashRetentionDays;

    public bool RemoveAssetsOnPurge { get; set; } = true;

    /// <summary>
    /// The retention period actually applied. A configured value below 1 would let the daily
    /// worker purge every trashed project (and its files) on its next run, so it falls back to
    /// the default instead. The app still starts; <see cref="HasInvalidTrashRetentionDays"/>
    /// lets the worker log a warning.
    /// </summary>
    public int EffectiveTrashRetentionDays =>
        TrashRetentionDays >= 1 ? TrashRetentionDays : DefaultTrashRetentionDays;

    public bool HasInvalidTrashRetentionDays => TrashRetentionDays < 1;
}
