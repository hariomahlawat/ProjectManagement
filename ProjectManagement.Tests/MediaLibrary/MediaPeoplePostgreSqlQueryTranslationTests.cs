using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// Provider-level regression tests for the People queries. These tests do not connect to a
/// database: ToQueryString forces Npgsql to translate each expression tree and therefore
/// catches the runtime failure mode that EF Core InMemory/SQLite tests cannot detect.
/// </summary>
public sealed class MediaPeoplePostgreSqlQueryTranslationTests
{
    [Fact]
    public void Identity_grouping_query_translates_for_postgresql()
    {
        using var db = CreateContext();

        var sql = FaceIdentityGroupingService.BuildGroupingRowsQuery(
                db,
                "opencv-sface",
                "2021dec",
                128,
                0.45d,
                2_000)
            .ToQueryString();

        Assert.Contains("MediaFaceEmbeddings", sql);
        Assert.Contains("MediaFaces", sql);
        Assert.Contains("MediaAssets", sql);
        Assert.Contains("NOT EXISTS", sql.ToUpperInvariant());
    }

    [Theory]
    [InlineData("name")]
    [InlineData("photos")]
    [InlineData("recent")]
    public void People_directory_query_translates_for_postgresql(string sort)
    {
        using var db = CreateContext();
        var filteredPeople = MediaPeopleQueryService.BuildFilteredPeopleQuery(
            db,
            "officer_100%",
            includeHidden: false);

        var sql = MediaPeopleQueryService.BuildPeopleIndexRowsQuery(
                db,
                filteredPeople,
                sort,
                skip: 0,
                take: 36)
            .ToQueryString();

        Assert.Contains("MediaPersons", sql);
        Assert.Contains("MediaPersonFaces", sql);
        Assert.Contains("ORDER BY", sql.ToUpperInvariant());
        Assert.Contains("ILIKE", sql.ToUpperInvariant());
    }

    [Theory]
    [InlineData("all")]
    [InlineData("any")]
    public void People_photo_gallery_filter_translates_for_postgresql(string matchMode)
    {
        using var db = CreateContext();
        var selectedPeople = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var sql = MediaLibraryQueryService.ApplyPeopleFilter(
                db.Assets.AsNoTracking(),
                selectedPeople,
                matchMode)
            .ToQueryString();

        Assert.Contains("MediaAssets", sql);
        Assert.Contains("MediaFaces", sql);
        Assert.Contains("MediaPersonFaces", sql);
        Assert.Contains("EXISTS", sql.ToUpperInvariant());
        if (matchMode == "all")
        {
            Assert.True(sql.ToUpperInvariant().Split(
                new[] { "EXISTS" },
                StringSplitOptions.None).Length >= 3);
        }
    }

    [Fact]
    public void Candidate_reference_query_translates_for_postgresql()
    {
        using var db = CreateContext();

        var sql = FaceCandidateSearchService.BuildReferenceRowsQuery(
                db,
                Guid.NewGuid(),
                "opencv-sface",
                "2021dec",
                128,
                0.65d,
                10_000)
            .ToQueryString();

        Assert.Contains("MediaPersonFaces", sql);
        Assert.Contains("MediaFaceEmbeddings", sql);
        Assert.Contains("MediaAssets", sql);
        Assert.Contains("ORDER BY", sql.ToUpperInvariant());
        Assert.Contains("ReferenceStatus", sql);
        Assert.Contains("TrustedReference", sql);
    }

    [Fact]
    public void Individual_face_review_query_translates_for_postgresql()
    {
        using var db = CreateContext();

        var sql = MediaPeopleQueryService.BuildReviewableFacesQuery(db)
            .OrderByDescending(face => face.QualityScore)
            .Take(24)
            .ToQueryString();

        Assert.Contains("MediaFaces", sql);
        Assert.Contains("MediaAssets", sql);
        Assert.Contains("NOT EXISTS", sql.ToUpperInvariant());
    }

    [Fact]
    public void Candidate_queue_discovery_query_translates_for_postgresql()
    {
        using var db = CreateContext();

        var sql = FaceCandidateRefreshQueueService.BuildQueueableFacesQuery(
                db,
                "opencv-sface",
                "2021dec",
                128,
                0.55d)
            .OrderByDescending(face => face.QualityScore)
            .Take(250)
            .ToQueryString();

        Assert.Contains("MediaFaces", sql);
        Assert.Contains("MediaFaceEmbeddings", sql);
        Assert.Contains("MediaPersonFaces", sql);
        Assert.Contains("NOT EXISTS", sql.ToUpperInvariant());
    }

    private static MediaLibraryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseNpgsql("Host=localhost;Database=prism_query_translation;Username=prism;Password=not-used")
            .EnableDetailedErrors()
            .Options;
        return new MediaLibraryDbContext(options);
    }
}
