using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ApiAuthenticationResponseTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiAuthenticationResponseTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Unauthenticated_notebook_api_returns_401_not_redirect()
    {
        // SECTION: Notebook API challenges must not redirect unsafe fetch requests to Login.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/notebook/items/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { version = Guid.NewGuid(), title = "Test" })
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Unauthenticated_protected_page_redirects_to_login()
    {
        // SECTION: Browser pages retain normal Identity redirect behaviour.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Notebook");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Unauthenticated_signalr_negotiate_returns_401_not_redirect()
    {
        // SECTION: SignalR negotiate requests are realtime API traffic and should receive 401.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/hubs/notifications/negotiate", JsonContent.Create(new { }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}
