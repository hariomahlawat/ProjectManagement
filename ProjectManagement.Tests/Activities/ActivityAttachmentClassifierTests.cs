using ProjectManagement.Contracts.Activities;
using Xunit;

namespace ProjectManagement.Tests.Activities;

public class ActivityAttachmentClassifierTests
{
    // SECTION: PDF extension classification
    [Theory]
    [InlineData("brief.pdf")]
    [InlineData("brief.PDF")]
    [InlineData("brief.Pdf")]
    public void Classify_DetectsPdfExtensionsCaseInsensitively(string fileName)
    {
        var kind = ActivityAttachmentClassifier.Classify(fileName, "application/octet-stream");

        Assert.Equal(ActivityAttachmentKind.Pdf, kind);
    }

    // SECTION: Mixed-case content type classification
    [Theory]
    [InlineData("photo.JPG", "Image/JPEG", ActivityAttachmentKind.Photo)]
    [InlineData("clip.mov", "Video/QuickTime", ActivityAttachmentKind.Video)]
    [InlineData("report.bin", "Application/PDF", ActivityAttachmentKind.Pdf)]
    public void Classify_DetectsMixedCaseContentTypes(string fileName, string contentType, ActivityAttachmentKind expected)
    {
        var kind = ActivityAttachmentClassifier.Classify(fileName, contentType);

        Assert.Equal(expected, kind);
    }

    // SECTION: Office document MIME and extension classification
    [Theory]
    [InlineData("memo.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("slides.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("legacy.DOC", "application/octet-stream")]
    [InlineData("budget.XLSX", "application/octet-stream")]
    [InlineData("deck.PPTX", "application/octet-stream")]
    public void Classify_DetectsOfficeDocumentMimeTypesAndExtensions(string fileName, string contentType)
    {
        var kind = ActivityAttachmentClassifier.Classify(fileName, contentType);

        Assert.Equal(ActivityAttachmentKind.Document, kind);
    }

    // SECTION: Fallback classification
    [Fact]
    public void Classify_ReturnsOtherForUnknownAttachments()
    {
        var kind = ActivityAttachmentClassifier.Classify("archive.zip", "application/zip");

        Assert.Equal(ActivityAttachmentKind.Other, kind);
    }
}
