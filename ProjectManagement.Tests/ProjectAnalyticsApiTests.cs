using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public class ProjectAnalyticsApiTests
{
    [Fact]
    public async Task CategoryShare_ParsesQueryParameters()
    {
        using var factory = new AnalyticsApiFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/api/analytics/projects/category-share?lifecycle=Completed&categoryId=5&technicalCategoryId=8");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var spy = factory.Services.GetRequiredService<SpyProjectAnalyticsService>();
        var call = Assert.Single(spy.CategoryShareRequests);
        Assert.Equal(ProjectLifecycleFilter.Completed, call.Lifecycle);
        Assert.Equal(5, call.CategoryId);
        Assert.Equal(8, call.TechnicalCategoryId);
    }

    [Fact]
    public async Task StageDistribution_ParsesQueryParameters()
    {
        using var factory = new AnalyticsApiFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/api/analytics/projects/stage-distribution?lifecycle=active&categoryId=12&technicalCategoryId=9");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var spy = factory.Services.GetRequiredService<SpyProjectAnalyticsService>();
        var call = Assert.Single(spy.StageDistributionRequests);
        Assert.Equal(ProjectLifecycleFilter.Active, call.Lifecycle);
        Assert.Equal(12, call.CategoryId);
        Assert.Equal(9, call.TechnicalCategoryId);
    }

    [Fact]
    public async Task CategoryShare_AllowsLegacyLifecycle()
    {
        using var factory = new AnalyticsApiFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/api/analytics/projects/category-share?lifecycle=Legacy");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var spy = factory.Services.GetRequiredService<SpyProjectAnalyticsService>();
        var call = Assert.Single(spy.CategoryShareRequests);
        Assert.Equal(ProjectLifecycleFilter.Legacy, call.Lifecycle);
    }

    [Fact]
    public async Task SlipBuckets_ParsesQueryParameters()
    {
        using var factory = new AnalyticsApiFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/api/analytics/projects/slip-buckets?lifecycle=completed&categoryId=2&technicalCategoryId=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var spy = factory.Services.GetRequiredService<SpyProjectAnalyticsService>();
        var call = Assert.Single(spy.SlipBucketRequests);
        Assert.Equal(ProjectLifecycleFilter.Completed, call.Lifecycle);
        Assert.Equal(2, call.CategoryId);
        Assert.Equal(5, call.TechnicalCategoryId);
    }

    [Fact]
    public async Task TopOverdue_ParsesQueryParameters()
    {
        using var factory = new AnalyticsApiFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/api/analytics/projects/top-overdue?lifecycle=active&categoryId=6&technicalCategoryId=10&take=7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var spy = factory.Services.GetRequiredService<SpyProjectAnalyticsService>();
        var call = Assert.Single(spy.TopOverdueRequests);
        Assert.Equal(ProjectLifecycleFilter.Active, call.Lifecycle);
        Assert.Equal(6, call.CategoryId);
        Assert.Equal(10, call.TechnicalCategoryId);
        Assert.Equal(7, call.Take);
    }

    private static HttpClient CreateAuthenticatedClient(AnalyticsApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private sealed class AnalyticsApiFactory : WebApplicationFactory<Program>
    {
        public SpyProjectAnalyticsService Spy { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IProjectAnalyticsService));
                services.AddSingleton<SpyProjectAnalyticsService>(_ => Spy);
                services.AddSingleton<IProjectAnalyticsService>(sp => sp.GetRequiredService<SpyProjectAnalyticsService>());
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                        options.DefaultScheme = "Test";
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "analytics-user"),
                new Claim(ClaimTypes.Name, "analytics-user")
            }, Scheme.Name);

            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    public sealed class SpyProjectAnalyticsService : IProjectAnalyticsService
    {
        public List<(ProjectLifecycleFilter Lifecycle, int? CategoryId, int? TechnicalCategoryId)> CategoryShareRequests { get; } = new();

        public List<(ProjectLifecycleFilter Lifecycle, int? CategoryId, int? TechnicalCategoryId)> StageDistributionRequests { get; } = new();

        public List<(ProjectLifecycleFilter Lifecycle, int? CategoryId, int? TechnicalCategoryId)> SlipBucketRequests { get; } = new();

        public List<(ProjectLifecycleFilter Lifecycle, int? CategoryId, int? TechnicalCategoryId, int Take)> TopOverdueRequests { get; } = new();

        public Task<CategoryShareResult> GetCategoryShareAsync(ProjectLifecycleFilter lifecycle, int? categoryId = null, int? technicalCategoryId = null, CancellationToken cancellationToken = default)
        {
            CategoryShareRequests.Add((lifecycle, categoryId, technicalCategoryId));
            return Task.FromResult(new CategoryShareResult(Array.Empty<CategoryShareSlice>(), 0));
        }

        public Task<StageDistributionResult> GetStageDistributionAsync(ProjectLifecycleFilter lifecycle, int? categoryId, int? technicalCategoryId, CancellationToken cancellationToken = default)
        {
            StageDistributionRequests.Add((lifecycle, categoryId, technicalCategoryId));
            return Task.FromResult(new StageDistributionResult(Array.Empty<StageDistributionItem>(), lifecycle));
        }

        public Task<SlipBucketResult> GetSlipBucketsAsync(ProjectLifecycleFilter lifecycle, int? categoryId, int? technicalCategoryId, CancellationToken cancellationToken = default)
        {
            SlipBucketRequests.Add((lifecycle, categoryId, technicalCategoryId));
            return Task.FromResult(new SlipBucketResult(Array.Empty<SlipBucketItem>()));
        }

        public Task<IReadOnlyCollection<int>> GetProjectIdsForSlipBucketAsync(ProjectLifecycleFilter lifecycle, int? categoryId, int? technicalCategoryId, string bucketKey, CancellationToken cancellationToken = default, IReadOnlyCollection<int>? expandedCategoryIds = null)
        {
            return Task.FromResult<IReadOnlyCollection<int>>(Array.Empty<int>());
        }

        public Task<TopOverdueProjectsResult> GetTopOverdueProjectsAsync(ProjectLifecycleFilter lifecycle, int? categoryId, int? technicalCategoryId, int take, CancellationToken cancellationToken = default)
        {
            TopOverdueRequests.Add((lifecycle, categoryId, technicalCategoryId, take));
            return Task.FromResult(new TopOverdueProjectsResult(Array.Empty<TopOverdueProject>()));
        }
    }
}
