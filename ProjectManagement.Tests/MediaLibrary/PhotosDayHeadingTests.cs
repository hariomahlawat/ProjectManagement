using ProjectManagement.Pages.Photos;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// A day heading on the Photos timeline names what the day's pictures came from: up to two
/// names, then "and N more".
/// </summary>
public sealed class PhotosDayHeadingTests
{
    [Fact]
    public void One_source_is_named()
        => Assert.Equal("Raising day", IndexModel.DaySourceSummary(new[] { Item("Raising day"), Item("Raising day") }));

    [Fact]
    public void Two_sources_are_both_named()
        => Assert.Equal("Raising day · Visit of delegation",
            IndexModel.DaySourceSummary(new[] { Item("Raising day"), Item("Visit of delegation") }));

    [Fact]
    public void More_than_two_sources_are_counted()
        => Assert.Equal("A · B and 2 more",
            IndexModel.DaySourceSummary(new[] { Item("A"), Item("B"), Item("C"), Item("D"), Item("a") }));

    [Fact]
    public void A_day_without_named_sources_still_has_a_label()
        => Assert.Equal("Photos", IndexModel.DaySourceSummary(new[] { Item(" ") }));

    private static IndexModel.MediaItem Item(string context)
        => new()
        {
            Id = Guid.NewGuid().ToString(),
            Kind = IndexModel.MediaKind.Photo,
            Source = IndexModel.MediaSource.Project,
            SourceLabel = "Project",
            ContextKey = "project:1",
            CollectionKey = "project:1",
            ContextTitle = context,
            ContextSubtitle = "Project media",
            Title = "Picture",
            MediaDate = new DateTime(2026, 11, 6),
            ThumbnailUrl = string.Empty,
            DisplayUrl = string.Empty,
            OriginalUrl = string.Empty
        };
}
