using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Infrastructure;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class DocumentUploadRateLimitingTests
{
    private static HttpContext Request(string method, string? userId, string ip = "10.0.0.5")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = DocumentUploadRateLimiting.UploadPath;
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (userId is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "test"));
        }

        return context;
    }

    [Fact]
    public void Opening_the_upload_page_is_never_throttled()
    {
        var partition = DocumentUploadRateLimiting.GetPartition(
            Request("GET", "clerk-1"), new DocumentUploadRateLimitOptions { PermitLimit = 1 });
        using var limiter = partition.Factory(partition.PartitionKey);

        for (var i = 0; i < 50; i++)
        {
            Assert.True(limiter.AttemptAcquire().IsAcquired);
        }
    }

    [Fact]
    public void Uploads_are_partitioned_per_user_even_from_one_client_address()
    {
        var options = new DocumentUploadRateLimitOptions();

        var clerk = DocumentUploadRateLimiting.GetPartition(Request("POST", "clerk-1"), options);
        var sameClerkOtherPc = DocumentUploadRateLimiting.GetPartition(Request("POST", "clerk-1", "10.0.0.9"), options);
        var otherClerkSamePc = DocumentUploadRateLimiting.GetPartition(Request("POST", "clerk-2"), options);

        Assert.Equal(clerk.PartitionKey, sameClerkOtherPc.PartitionKey);
        Assert.NotEqual(clerk.PartitionKey, otherClerkSamePc.PartitionKey);
    }

    [Fact]
    public void Each_user_gets_the_configured_allowance()
    {
        var options = new DocumentUploadRateLimitOptions { PermitLimit = 3, WindowMinutes = 1 };
        var partition = DocumentUploadRateLimiting.GetPartition(Request("POST", "clerk-1"), options);
        using var limiter = partition.Factory(partition.PartitionKey);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(limiter.AttemptAcquire().IsAcquired);
        }

        Assert.False(limiter.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public void Anonymous_requests_fall_back_to_the_client_address()
    {
        Assert.Equal("client:10.0.0.5", DocumentUploadRateLimiting.UploaderLabel(Request("POST", null)));
        Assert.Equal("user:clerk-1", DocumentUploadRateLimiting.UploaderLabel(Request("POST", "clerk-1")));
    }

    [Fact]
    public void Defaults_allow_twenty_uploads_per_minute()
    {
        var options = new DocumentUploadRateLimitOptions();

        Assert.Equal(20, options.EffectivePermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), options.EffectiveWindow);
    }

    [Fact]
    public void Throttled_upload_returns_to_the_upload_page()
    {
        Assert.Equal(
            "/DocumentRepository/Documents/Upload?throttled=true",
            DocumentUploadRateLimiting.BuildThrottledRedirect(Request("POST", "clerk-1")));
    }
}
