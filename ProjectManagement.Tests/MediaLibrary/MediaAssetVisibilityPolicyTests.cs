using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaAssetVisibilityPolicyTests
{
    [Fact]
    public void IsVisible_AllowsAvailablePrismAsset()
    {
        var policy = CreatePolicy(externalEnabled: false);
        var asset = CreateAsset(MediaAssetOrigin.ProjectPhoto, MediaLibrarySourceType.Prism);

        Assert.True(policy.IsVisible(asset));
    }

    [Fact]
    public void IsVisible_RejectsArchivedAsset()
    {
        var policy = CreatePolicy(externalEnabled: false);
        var asset = CreateAsset(MediaAssetOrigin.ProjectPhoto, MediaLibrarySourceType.Prism);
        asset.IsArchived = true;

        Assert.False(policy.IsVisible(asset));
    }

    [Fact]
    public void IsVisible_RejectsExternalAssetWhenExternalLibraryIsDisabled()
    {
        var policy = CreatePolicy(externalEnabled: false);
        var asset = CreateAsset(MediaAssetOrigin.ExternalFile, MediaLibrarySourceType.FileSystem);

        Assert.False(policy.IsVisible(asset));
    }

    [Fact]
    public void IsVisible_RequiresEnabledVisibleFileSystemSourceForExternalAsset()
    {
        var policy = CreatePolicy(externalEnabled: true);
        var asset = CreateAsset(MediaAssetOrigin.ExternalFile, MediaLibrarySourceType.FileSystem);

        Assert.True(policy.IsVisible(asset));

        asset.Source.IsEnabled = false;
        Assert.False(policy.IsVisible(asset));

        asset.Source.IsEnabled = true;
        asset.Source.IsVisibleInLibrary = false;
        Assert.False(policy.IsVisible(asset));
    }

    private static MediaAssetVisibilityPolicy CreatePolicy(bool externalEnabled)
    {
        var options = new MediaLibraryOptions
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true },
            ExternalSources = new ExternalMediaSourcesOptions
            {
                Enabled = externalEnabled
            }
        };
        return new MediaAssetVisibilityPolicy(Options.Create(options));
    }

    private static MediaAsset CreateAsset(MediaAssetOrigin origin, MediaLibrarySourceType sourceType)
        => new()
        {
            Origin = origin,
            IsAvailable = true,
            AvailabilityStatus = MediaAvailabilityStatus.Available,
            Source = new MediaLibrarySource
            {
                SourceType = sourceType,
                IsEnabled = true,
                IsVisibleInLibrary = true
            }
        };
}
