using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Features.MediaLibrary;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaLibraryDependencyRegistrationTests
{
    [Fact]
    public void Media_library_registers_album_person_discovery_and_identity_services()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddMediaLibrary(
            configuration,
            "Host=localhost;Database=prism_di_registration;Username=prism;Password=not-used");

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IMediaAlbumService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPersonPhotoDiscoveryQueryService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IFaceReviewService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IFaceCandidateRefreshQueueService));
    }
}
