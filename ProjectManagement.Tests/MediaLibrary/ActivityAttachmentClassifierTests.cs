using ProjectManagement.Contracts.Activities;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class ActivityAttachmentClassifierTests
{
    [Theory]
    [InlineData("photo.jpg", "application/octet-stream")]
    [InlineData("portrait.HEIC", null)]
    [InlineData("image.bin", "image/jpeg")]
    public void PhotoRecognitionUsesMimeTypeOrTrustedImageExtension(string fileName, string? contentType)
    {
        Assert.True(ActivityAttachmentClassifier.IsPhoto(fileName, contentType));
    }

    [Theory]
    [InlineData("notes.pdf", "application/pdf")]
    [InlineData("document.docx", "application/octet-stream")]
    public void NonImagesAreNotAdmittedAsPhotos(string fileName, string? contentType)
    {
        Assert.False(ActivityAttachmentClassifier.IsPhoto(fileName, contentType));
    }
}
