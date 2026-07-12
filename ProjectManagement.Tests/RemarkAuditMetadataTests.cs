using System.Text.Json;
using ProjectManagement.Services.Remarks;

namespace ProjectManagement.Tests;

public sealed class RemarkAuditMetadataTests
{
    [Fact]
    public void ForOfficerConferenceReview_ReturnsValidStructuredJson()
    {
        var value = RemarkAuditMetadata.ForOfficerConferenceReview("officer-1");

        using var document = JsonDocument.Parse(value);
        Assert.Equal("officer-conference-review", document.RootElement.GetProperty("origin").GetString());
        Assert.Equal("officer-1", document.RootElement.GetProperty("officerUserId").GetString());
    }

    [Fact]
    public void Validate_RejectsPlainTextBeforeDatabasePersistence()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RemarkAuditMetadata.Validate("Officer conference review"));

        Assert.Equal(RemarkAuditMetadata.InvalidMetadataMessage, exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"origin\":\"test\"}")]
    [InlineData("[1,2,3]")]
    public void Validate_AcceptsNullEmptyOrValidJson(string? value)
        => RemarkAuditMetadata.Validate(value);
}
