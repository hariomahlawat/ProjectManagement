using System;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using Xunit;

namespace ProjectManagement.Tests;

public class SocialMediaEventPhotoErrorTranslatorTests
{
    [Fact]
    public void GetUserFacingMessage_NullException_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SocialMediaEventPhotoErrorTranslator.GetUserFacingMessage(null!));
    }

    [Fact]
    public void GetUserFacingMessage_PostgresForeignKey_ReturnsEventMissingMessage()
    {
        var providerException = new FakePostgresException("23503", "FK_SocialMediaEventPhotos_SocialMediaEvents_SocialMediaEventId");
        var exception = new DbUpdateException("failed", providerException);

        var message = SocialMediaEventPhotoErrorTranslator.GetUserFacingMessage(exception);

        Assert.Contains("event no longer exists", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetUserFacingMessage_SqlServerUniqueConstraint_ReturnsCoverConflictMessage()
    {
        var providerException = new FakeSqlServerException(2601, "UX_SocialMediaEventPhotos_IsCover");
        var exception = new DbUpdateException("failed", providerException);

        var message = SocialMediaEventPhotoErrorTranslator.GetUserFacingMessage(exception);

        Assert.Contains("cover", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetUserFacingMessage_DefaultsToGenericMessage()
    {
        var exception = new DbUpdateException("failed", new Exception("unknown"));

        var message = SocialMediaEventPhotoErrorTranslator.GetUserFacingMessage(exception);

        Assert.Equal("Unable to save social media photo metadata.", message);
    }

    private sealed class FakePostgresException : Exception
    {
        public FakePostgresException(string sqlState, string? constraintName = null) : base("postgres error")
        {
            SqlState = sqlState;
            ConstraintName = constraintName;
        }

        public string SqlState { get; }

        public string? ConstraintName { get; }
    }

    private sealed class FakeSqlServerException : Exception
    {
        public FakeSqlServerException(int number, string? constraint = null) : base("sql error")
        {
            Number = number;
            Constraint = constraint;
        }

        public int Number { get; }

        public string? Constraint { get; }
    }
}
