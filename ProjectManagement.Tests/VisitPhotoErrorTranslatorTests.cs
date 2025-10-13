using System;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using Xunit;

namespace ProjectManagement.Tests;

public class VisitPhotoErrorTranslatorTests
{
    [Fact]
    public void GetUserFacingMessage_NullException_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => VisitPhotoErrorTranslator.GetUserFacingMessage(null!));
    }

    [Fact]
    public void GetUserFacingMessage_PostgresMissingTable_ReturnsMigrationMessage()
    {
        var providerException = new FakePostgresException("42P01");
        var exception = new DbUpdateException("failed", providerException);

        var message = VisitPhotoErrorTranslator.GetUserFacingMessage(exception);

        Assert.Contains("migrations", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetUserFacingMessage_PostgresForeignKey_ReturnsVisitMissingMessage()
    {
        var providerException = new FakePostgresException("23503", "FK_VisitPhotos_Visits_VisitId");
        var exception = new DbUpdateException("failed", providerException);

        var message = VisitPhotoErrorTranslator.GetUserFacingMessage(exception);

        Assert.Contains("visit no longer exists", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetUserFacingMessage_SqlServerStringTruncation_ReturnsFriendlyCaptionMessage()
    {
        var providerException = new FakeSqlServerException(8152);
        var exception = new DbUpdateException("failed", providerException);

        var message = VisitPhotoErrorTranslator.GetUserFacingMessage(exception);

        Assert.Contains("too long", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetUserFacingMessage_DefaultsToGenericMessage()
    {
        var exception = new DbUpdateException("failed", new Exception("other error"));

        var message = VisitPhotoErrorTranslator.GetUserFacingMessage(exception);

        Assert.Equal("Unable to save photo metadata.", message);
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
        public FakeSqlServerException(int number) : base("sql error")
        {
            Number = number;
        }

        public int Number { get; }
    }
}
