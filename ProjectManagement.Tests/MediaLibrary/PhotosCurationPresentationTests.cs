using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Pages.Photos;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class PhotosCurationPresentationTests
{
    [Theory]
    [InlineData("Col", "Hari Om Ahlawat", "hari", "Col Hari Om Ahlawat")]
    [InlineData("Col", "Col Hari Om Ahlawat", "hari", "Col Hari Om Ahlawat")]
    [InlineData("", "Hari Om Ahlawat", "hari", "Hari Om Ahlawat")]
    [InlineData(null, null, "hari", "hari")]
    [InlineData(null, null, null, "PRISM user")]
    public void BuildCreatorDisplayName_ProducesStableNonDuplicatedLabel(
        string? rank,
        string? fullName,
        string? userName,
        string expected)
    {
        Assert.Equal(expected, PhotosCurationPresentation.BuildCreatorDisplayName(rank, fullName, userName));
    }


    [Theory]
    [InlineData(true, false, 0, true)]
    [InlineData(true, false, 249, true)]
    [InlineData(true, false, 250, false)]
    [InlineData(true, true, 1, false)]
    [InlineData(false, false, 1, false)]
    public void CanAddMedia_RequiresManageAuthorityActiveAlbumAndAvailableCapacity(
        bool canManage,
        bool archived,
        int itemCount,
        bool expected)
    {
        var details = new MediaAlbumDetails(
            Guid.NewGuid(),
            "Test album",
            null,
            "creator",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            archived,
            null,
            Guid.NewGuid(),
            canManage,
            Enumerable.Range(1, itemCount).Select(value => (long)value).ToArray(),
            itemCount,
            itemCount,
            itemCount,
            0);

        Assert.Equal(expected, PhotosCurationPresentation.CanAddMedia(details));
    }

    [Fact]
    public void CanAddMedia_UsesTotalMembershipCount_NotOnlyVisibleItems()
    {
        var details = new MediaAlbumDetails(
            Guid.NewGuid(),
            "Capacity test",
            null,
            "creator",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            false,
            null,
            Guid.NewGuid(),
            true,
            Enumerable.Range(1, 249).Select(value => (long)value).ToArray(),
            MediaAlbumService.MaximumAlbumItems,
            249,
            249,
            0);

        Assert.False(PhotosCurationPresentation.CanAddMedia(details));
    }

    [Theory]
    [InlineData(true, false, 2, true)]
    [InlineData(true, false, 1, false)]
    [InlineData(true, true, 5, false)]
    [InlineData(false, false, 5, false)]
    public void CanOrganize_RequiresManageAuthorityActiveAlbumAndTwoVisibleItems(
        bool canManage,
        bool archived,
        int itemCount,
        bool expected)
    {
        var details = new MediaAlbumDetails(
            Guid.NewGuid(),
            "Test album",
            null,
            "creator",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            archived,
            null,
            Guid.NewGuid(),
            canManage,
            Enumerable.Range(1, itemCount).Select(value => (long)value).ToArray(),
            itemCount,
            itemCount,
            itemCount,
            0);

        Assert.Equal(expected, PhotosCurationPresentation.CanOrganize(details));
    }
}
