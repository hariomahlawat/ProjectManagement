using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
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
    [InlineData(false)]
    [InlineData(true)]
    public void Collection_representative_rows_are_chosen_in_the_database(bool coverOnly)
    {
        using var db = CreateContext();

        var sql = MediaCollectionQueryService.BuildRepresentativeIdsQuery(
                db.Assets,
                new[] { "project:1", "external:nas:root" },
                coverOnly)
            .ToQueryString()
            .ToUpperInvariant();

        // One id per collection, chosen in the database (grouped, with a LIMIT 1 pick per
        // group), not every asset of each listed collection.
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("LIMIT 1", sql);
    }

    [Fact]
    public void Timeline_month_counts_are_grouped_in_the_database()
    {
        using var db = CreateContext();

        var sql = MediaLibraryQueryService.BuildMonthCountsQuery(db.Assets, new TimeSpan(5, 30, 0))
            .ToQueryString()
            .ToUpperInvariant();

        // The Photos month list needs one row per month, not every asset's date.
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("COUNT(", sql);
        // Months are those of the server's local time, as the timeline shows them: the
        // stored UTC date is shifted by the local offset before the month is taken.
        Assert.Contains("INTERVAL", sql);
    }

    [Fact]
    public void Year_filter_is_a_range_on_the_stored_date_and_translates_for_postgresql()
    {
        using var db = CreateContext();

        var sql = MediaLibraryQueryService.ApplyLocalYearFilter(db.Assets, 2026, new TimeSpan(5, 30, 0))
            .ToQueryString()
            .ToUpperInvariant();

        // A plain range on the column (index-friendly), not a function of the column.
        Assert.Contains("\"MEDIADATEUTC\" >=", sql);
        Assert.Contains("\"MEDIADATEUTC\" <", sql);
        Assert.DoesNotContain("DATE_PART", sql);
    }

    [Fact]
    public void Year_list_is_shifted_to_local_time_in_the_database()
    {
        using var db = CreateContext();

        var sql = MediaLibraryQueryService.BuildLocalYearsQuery(db.Assets, new TimeSpan(5, 30, 0))
            .OrderByDescending(year => year)
            .ToQueryString()
            .ToUpperInvariant();

        Assert.Contains("DISTINCT", sql);
        Assert.Contains("INTERVAL", sql);
    }

    [Fact]
    public void Picture_just_after_local_midnight_on_1_january_belongs_to_the_new_year()
    {
        // 1 Jan 2026 02:00 IST is 31 Dec 2025 20:30 UTC. The offset is passed in, so the
        // test does not depend on this machine's time zone.
        var ist = new TimeSpan(5, 30, 0);
        var newYearPicture = new MediaAsset
        {
            Id = 1,
            MediaDateUtc = new DateTimeOffset(2025, 12, 31, 20, 30, 0, TimeSpan.Zero)
        };
        var oldYearPicture = new MediaAsset
        {
            Id = 2,
            MediaDateUtc = new DateTimeOffset(2025, 12, 31, 18, 29, 0, TimeSpan.Zero)
        };
        var assets = new[] { newYearPicture, oldYearPicture }.AsQueryable();

        Assert.Equal(
            new[] { 2026, 2025 },
            MediaLibraryQueryService.BuildLocalYearsQuery(assets, ist).OrderByDescending(year => year).ToArray());
        Assert.Equal(
            new[] { newYearPicture.Id },
            MediaLibraryQueryService.ApplyLocalYearFilter(assets, 2026, ist).Select(asset => asset.Id).ToArray());
        Assert.Equal(
            new[] { oldYearPicture.Id },
            MediaLibraryQueryService.ApplyLocalYearFilter(assets, 2025, ist).Select(asset => asset.Id).ToArray());

        // Year, month and day agree: the month counts put the same picture in January 2026.
        var months = MediaLibraryQueryService.BuildMonthCountsQuery(assets, ist).ToList();
        Assert.Contains(months, month => month.Year == 2026 && month.Month == 1 && month.Count == 1);
        Assert.Contains(months, month => month.Year == 2025 && month.Month == 12 && month.Count == 1);
    }

    [Fact]
    public void Recently_seen_sort_places_people_without_photos_last()
    {
        using var db = CreateContext();
        var filteredPeople = MediaPeopleQueryService.BuildFilteredPeopleQuery(db, null, includeHidden: false);

        var sql = MediaPeopleQueryService.BuildPeopleIndexRowsQuery(db, filteredPeople, "recent", skip: 0, take: 36)
            .ToQueryString()
            .ToUpperInvariant();

        // PostgreSQL sorts NULL first in DESC order; the query orders by "has a date" first.
        var orderBy = sql[sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];
        Assert.Contains("IS NOT NULL", orderBy);
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
    [InlineData("linked")]
    [InlineData("unlinked")]
    [InlineData("reported")]
    public void People_account_link_filter_translates_for_postgresql(string accountLinkFilter)
    {
        using var db = CreateContext();

        var sql = MediaPeopleQueryService.BuildFilteredPeopleQuery(
                db,
                null,
                includeHidden: false,
                accountLinkFilter)
            .ToQueryString();

        Assert.Contains("MediaPersons", sql);
        Assert.Contains("MediaPersonUserLinks", sql);
        Assert.Contains("UnlinkedAtUtc", sql);
        if (accountLinkFilter == "reported")
        {
            Assert.Contains("ConcernRaisedAtUtc", sql);
            Assert.Contains("ConcernResolvedAtUtc", sql);
        }
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


    [Fact]
    public void Canonical_visibility_subquery_translates_across_people_workflows()
    {
        using var db = CreateContext();
        var options = new MediaLibraryOptions
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true },
            ExternalSources = new ExternalMediaSourcesOptions { Enabled = true }
        };
        var visibleAssetIds = new MediaAssetVisibilityPolicy(Options.Create(options))
            .Apply(db.Assets.AsNoTracking())
            .Select(asset => asset.Id);

        var peopleSql = MediaPeopleQueryService.BuildPeopleIndexRowsQuery(
                db,
                MediaPeopleQueryService.BuildFilteredPeopleQuery(db, null, includeHidden: false),
                "recent",
                0,
                36,
                visibleAssetIds)
            .ToQueryString();
        var groupingSql = FaceIdentityGroupingService.BuildGroupingRowsQuery(
                db,
                "opencv-sface",
                "2021dec",
                128,
                0.45d,
                2_000,
                visibleAssetIds)
            .ToQueryString();
        var referenceSql = FaceCandidateSearchService.BuildReferenceRowsQuery(
                db,
                Guid.Empty,
                "opencv-sface",
                "2021dec",
                128,
                0.65d,
                10_000,
                visibleAssetIds)
            .ToQueryString();
        var queueSql = FaceCandidateRefreshQueueService.BuildQueueableFacesQuery(
                db,
                "opencv-sface",
                "2021dec",
                128,
                0.55d,
                visibleAssetIds)
            .ToQueryString();
        var closedReviewSql = FaceReviewWorkloadService.BuildClosedUnidentifiedFacesQuery(
                db,
                visibleAssetIds)
            .ToQueryString();
        var activeKnownPersonSql = FaceReviewWorkloadService
            .BuildActivePendingKnownPersonDecisions(db)
            .ToQueryString();

        foreach (var sql in new[] { peopleSql, groupingSql, referenceSql, queueSql, closedReviewSql })
        {
            Assert.Contains("MediaAssets", sql);
            Assert.Contains("MediaLibrarySources", sql);
            Assert.Contains("IsVisibleInLibrary", sql);
            Assert.Contains("AvailabilityStatus", sql);
        }

        Assert.Contains("MediaPersons", activeKnownPersonSql);
        Assert.Contains("IsHidden", activeKnownPersonSql);
        Assert.Contains("Status", activeKnownPersonSql);
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
