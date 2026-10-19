using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ProjectManagement.Infrastructure;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class DocumentUploadRateLimitingTests
{
    private static readonly SharedRateLimiters Shared = new(
        Options.Create(new LoginRateLimitOptions()), Options.Create(new DocumentUploadRateLimitOptions()));

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
            Request("GET", "clerk-1"), new DocumentUploadRateLimitOptions { PermitLimit = 1 }, Shared.DocumentUpload);
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

        var clerk = DocumentUploadRateLimiting.GetPartition(Request("POST", "clerk-1"), options, Shared.DocumentUpload);
        var sameClerkOtherPc = DocumentUploadRateLimiting.GetPartition(Request("POST", "clerk-1", "10.0.0.9"), options, Shared.DocumentUpload);
        var otherClerkSamePc = DocumentUploadRateLimiting.GetPartition(Request("POST", "clerk-2"), options, Shared.DocumentUpload);

        Assert.Equal(clerk.PartitionKey, sameClerkOtherPc.PartitionKey);
        Assert.NotEqual(clerk.PartitionKey, otherClerkSamePc.PartitionKey);
    }

    [Fact]
    public void Each_user_gets_the_configured_allowance()
    {
        var options = new DocumentUploadRateLimitOptions { PermitLimit = 3, WindowMinutes = 1 };
        var partition = DocumentUploadRateLimiting.GetPartition(Request("POST", "clerk-1"), options, Shared.DocumentUpload);
        using var limiter = partition.Factory(partition.PartitionKey);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(limiter.AttemptAcquire().IsAcquired);
        }

        Assert.False(limiter.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public void Signed_out_requests_share_one_partition()
    {
        // Signed-out requests share one partition instead of one per (forgeable) address.
        Assert.Equal("anonymous", DocumentUploadRateLimiting.UploaderLabel(Request("POST", null)));
        Assert.Equal("anonymous", DocumentUploadRateLimiting.UploaderLabel(Request("POST", null, "10.9.9.9")));
        Assert.Equal("user:clerk-1", DocumentUploadRateLimiting.UploaderLabel(Request("POST", "clerk-1")));
    }

    [Fact]
    public void Defaults_allow_twenty_uploads_per_user_and_two_hundred_in_total_per_minute()
    {
        var options = new DocumentUploadRateLimitOptions();

        Assert.Equal(20, options.EffectivePermitLimit);
        Assert.Equal(200, options.EffectiveServerPermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), options.EffectiveWindow);
    }

    [Fact]
    public void Throttled_upload_returns_to_the_upload_page()
    {
        Assert.Equal(
            "/DocumentRepository/Documents/Upload?throttled=true",
            DocumentUploadRateLimiting.BuildThrottledRedirect(Request("POST", "clerk-1")));
    }

    [Fact]
    public void Gated_limiter_only_spends_the_shared_allowance_when_the_own_allowance_passes()
    {
        using var shared = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        using var clientA = new GatedRateLimiter(new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }), shared);
        using var clientB = new GatedRateLimiter(new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }), shared);

        Assert.True(clientA.AttemptAcquire().IsAcquired);
        for (var i = 0; i < 10; i++)
        {
            Assert.False(clientA.AttemptAcquire().IsAcquired); // refused by its own allowance only
        }

        Assert.True(clientB.AttemptAcquire().IsAcquired);
        Assert.True(clientB.AttemptAcquire().IsAcquired);
        Assert.False(clientB.AttemptAcquire().IsAcquired); // shared allowance (3) now spent
    }

    [Fact]
    public async Task Gated_limiter_never_queues_or_double_counts_a_rejection()
    {
        using var shared = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        using var limiter = new GatedRateLimiter(new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = 2, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }), shared);

        using var lease = await limiter.AcquireAsync();

        Assert.False(lease.IsAcquired);
        Assert.Equal(10, shared.GetStatistics()!.CurrentAvailablePermits);
        Assert.True(limiter.AttemptAcquire().IsAcquired);
        Assert.True(limiter.AttemptAcquire().IsAcquired);
    }
}
