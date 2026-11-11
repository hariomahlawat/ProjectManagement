using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Tests.Infrastructure;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Upload storage falls back to wwwroot/uploads when no storage root is configured, and
/// static files are served before authentication. Stored uploads must never be reachable
/// as static files; normal static assets must still be served.
/// </summary>
public sealed class UploadsStaticFileBlockTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UploadsStaticFileBlockTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UsePrismTestInfrastructure("uploads-static-block");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.RemoveAll(typeof(DbContextOptions<MediaLibraryDbContext>));
                services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase("uploads-static-block"));
                services.AddDbContext<MediaLibraryDbContext>(o => o.UseInMemoryDatabase("uploads-static-block-media"));
            });
        });
    }

    [Theory]
    [InlineData("/uploads/activities/3/20251029092855324-2e7f3005c9b245d88fa20aa9b0860cfe-RFI.pdf")]
    [InlineData("/Uploads/activities/3/20251029092855324-2e7f3005c9b245d88fa20aa9b0860cfe-RFI.pdf")]
    public async Task Files_under_uploads_are_not_served_statically(string path)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ordinary_static_assets_are_still_served()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/lib/bootstrap-icons/font/bootstrap-icons.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
