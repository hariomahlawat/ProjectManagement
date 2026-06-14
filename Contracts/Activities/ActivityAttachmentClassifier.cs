using System;
using System.Linq.Expressions;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Contracts.Activities;

public enum ActivityAttachmentKind
{
    Other,
    Photo,
    Video,
    Pdf,
    Document
}

public static class ActivityAttachmentClassifier
{
    public const string PhotoLabel = "Photo";
    public const string VideoLabel = "Video";
    public const string PdfLabel = "PDF";
    public const string DocumentLabel = "Document";
    public const string OtherLabel = "Other";

    // SECTION: EF-translatable normalized classification expressions
    public static readonly Expression<Func<ActivityAttachment, bool>> IsPhotoExpression = attachment =>
        attachment.ContentType != null && attachment.ContentType.ToLower().StartsWith("image/");

    public static readonly Expression<Func<ActivityAttachment, bool>> IsVideoExpression = attachment =>
        attachment.ContentType != null && attachment.ContentType.ToLower().StartsWith("video/");

    public static readonly Expression<Func<ActivityAttachment, bool>> IsPdfExpression = attachment =>
        (attachment.ContentType != null && attachment.ContentType.ToLower() == "application/pdf") ||
        (attachment.OriginalFileName != null && attachment.OriginalFileName.ToLower().EndsWith(".pdf"));

    public static readonly Expression<Func<ActivityAttachment, bool>> IsDocumentExpression = attachment =>
        (attachment.ContentType != null && attachment.ContentType.ToLower() == "application/pdf") ||
        (attachment.OriginalFileName != null && attachment.OriginalFileName.ToLower().EndsWith(".pdf")) ||
        (attachment.ContentType != null && attachment.ContentType.ToLower().Contains("document")) ||
        (attachment.ContentType != null && attachment.ContentType.ToLower().Contains("spreadsheet")) ||
        (attachment.ContentType != null && attachment.ContentType.ToLower().Contains("presentation")) ||
        (attachment.ContentType != null && attachment.ContentType.ToLower().Contains("wordprocessingml")) ||
        (attachment.ContentType != null && attachment.ContentType.ToLower().Contains("spreadsheetml")) ||
        (attachment.ContentType != null && attachment.ContentType.ToLower().Contains("presentationml")) ||
        (attachment.ContentType != null && attachment.ContentType.ToLower().Contains("officedocument")) ||
        (attachment.OriginalFileName != null && attachment.OriginalFileName.ToLower().EndsWith(".doc")) ||
        (attachment.OriginalFileName != null && attachment.OriginalFileName.ToLower().EndsWith(".docx")) ||
        (attachment.OriginalFileName != null && attachment.OriginalFileName.ToLower().EndsWith(".xls")) ||
        (attachment.OriginalFileName != null && attachment.OriginalFileName.ToLower().EndsWith(".xlsx")) ||
        (attachment.OriginalFileName != null && attachment.OriginalFileName.ToLower().EndsWith(".ppt")) ||
        (attachment.OriginalFileName != null && attachment.OriginalFileName.ToLower().EndsWith(".pptx"));

    // SECTION: In-memory classification helpers
    public static ActivityAttachmentKind Classify(string? fileName, string? contentType)
    {
        if (IsPhoto(fileName, contentType))
        {
            return ActivityAttachmentKind.Photo;
        }

        if (IsVideo(fileName, contentType))
        {
            return ActivityAttachmentKind.Video;
        }

        if (IsPdf(fileName, contentType))
        {
            return ActivityAttachmentKind.Pdf;
        }

        if (IsDocument(fileName, contentType))
        {
            return ActivityAttachmentKind.Document;
        }

        return ActivityAttachmentKind.Other;
    }

    public static bool IsPhoto(string? fileName, string? contentType) =>
        StartsWithNormalized(contentType, "image/");

    public static bool IsVideo(string? fileName, string? contentType) =>
        StartsWithNormalized(contentType, "video/");

    public static bool IsPdf(string? fileName, string? contentType) =>
        EqualsNormalized(contentType, "application/pdf") || EndsWithNormalized(fileName, ".pdf");

    public static bool IsDocument(string? fileName, string? contentType) =>
        IsPdf(fileName, contentType) ||
        ContainsNormalized(contentType, "document") ||
        ContainsNormalized(contentType, "spreadsheet") ||
        ContainsNormalized(contentType, "presentation") ||
        ContainsNormalized(contentType, "wordprocessingml") ||
        ContainsNormalized(contentType, "spreadsheetml") ||
        ContainsNormalized(contentType, "presentationml") ||
        ContainsNormalized(contentType, "officedocument") ||
        EndsWithNormalized(fileName, ".doc") ||
        EndsWithNormalized(fileName, ".docx") ||
        EndsWithNormalized(fileName, ".xls") ||
        EndsWithNormalized(fileName, ".xlsx") ||
        EndsWithNormalized(fileName, ".ppt") ||
        EndsWithNormalized(fileName, ".pptx");

    public static string GetDisplayLabel(string? fileName, string? contentType) => Classify(fileName, contentType) switch
    {
        ActivityAttachmentKind.Photo => PhotoLabel,
        ActivityAttachmentKind.Video => VideoLabel,
        ActivityAttachmentKind.Pdf => PdfLabel,
        ActivityAttachmentKind.Document => DocumentLabel,
        _ => OtherLabel
    };

    private static bool EqualsNormalized(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithNormalized(string? value, string expected) =>
        value?.Trim().StartsWith(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static bool EndsWithNormalized(string? value, string expected) =>
        value?.Trim().EndsWith(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static bool ContainsNormalized(string? value, string expected) =>
        value?.Trim().IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
}
