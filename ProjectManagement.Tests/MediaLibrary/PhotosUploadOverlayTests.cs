using ProjectManagement.Pages.Photos;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// While the catalogue catches up, the Photos page shows the catalogue plus recent PRISM
/// uploads it has not indexed yet, instead of re-reading every PRISM media row.
/// </summary>
public sealed class PhotosUploadOverlayTests
{
    [Theory]
    [InlineData(1, "newest", "all", 0, "all", true)]
    [InlineData(1, "newest", "all", 0, "projects", true)]
    [InlineData(2, "newest", "all", 0, "all", false)]
    [InlineData(1, "oldest", "all", 0, "all", false)]
    [InlineData(1, "newest", "photographs", 0, "all", false)]
    [InlineData(1, "newest", "all", 1, "all", false)]
    [InlineData(1, "newest", "all", 0, "external", false)]
    public void Applies_only_to_the_first_page_of_an_unfiltered_newest_timeline(
        int page, string sort, string classification, int people, string source, bool expected)
        => Assert.Equal(expected, PhotosUploadOverlay.Applies(page, sort, classification, people, source));

    [Fact]
    public void SelectPending_excludes_catalogued_media_and_orders_newest_first()
    {
        var recent = new[]
        {
            Item("project-photo:1", new DateTime(2026, 11, 10)),
            Item("project-photo:2", new DateTime(2026, 11, 11)),
            Item("activity-photo:9", new DateTime(2026, 11, 9))
        };

        var pending = PhotosUploadOverlay.SelectPending(
            recent,
            new HashSet<string>(StringComparer.Ordinal) { "project-photo:1" },
            take: 10);

        Assert.Equal(new[] { "project-photo:2", "activity-photo:9" }, pending.Select(item => item.Id));
    }

    [Fact]
    public void SelectPending_is_capped()
    {
        var recent = Enumerable.Range(1, 50)
            .Select(index => Item($"project-photo:{index}", new DateTime(2026, 1, 1).AddMinutes(index)));

        var pending = PhotosUploadOverlay.SelectPending(recent, new HashSet<string>(), take: 5);

        Assert.Equal(5, pending.Count);
        Assert.Equal("project-photo:50", pending[0].Id);
    }

    private static IndexModel.MediaItem Item(string id, DateTime date)
        => new()
        {
            Id = id,
            Kind = IndexModel.MediaKind.Photo,
            Source = IndexModel.MediaSource.Project,
            SourceLabel = "Project",
            ContextKey = "project:1",
            CollectionKey = "project:1",
            ContextTitle = "Project",
            ContextSubtitle = "Project media",
            Title = id,
            MediaDate = date,
            ThumbnailUrl = string.Empty,
            DisplayUrl = string.Empty,
            OriginalUrl = string.Empty
        };
}
