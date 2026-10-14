using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace ProjectManagement.Tests.Infrastructure;

internal static class RazorPageTestRouting
{
    public static void AddPageEndpoints(IServiceCollection services)
    {
        // Use the application's Razor Page endpoints so tag helpers generate real page URLs.
        services.AddSingleton<EndpointDataSource>(provider =>
        {
            var builder = new TestEndpointRouteBuilder(provider);
            builder.MapRazorPages();
            return new CompositeEndpointDataSource(builder.DataSources);
        });
    }

    public static void EnableEndpointRouting(HttpContext context)
    {
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse(""), 0, EndpointMetadataCollection.Empty, "Razor fixture"));
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider provider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = provider;
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
