using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class PersonPhotoDiscoveryPostgreSqlQueryTranslationTests
{
    [Fact]
    public void Person_candidate_query_translates_with_canonical_visibility()
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

        var sql = PersonPhotoDiscoveryQueryService.BuildCandidateRowsQuery(
                db,
                Guid.NewGuid(),
                "opencv-sface",
                "2021dec",
                visibleAssetIds)
            .ToQueryString();

        Assert.Contains("MediaFaceReviewDecisions", sql);
        Assert.Contains("MediaFaces", sql);
        Assert.Contains("MediaAssets", sql);
        Assert.Contains("MediaPersons", sql);
        Assert.Contains("MediaLibrarySources", sql);
        Assert.Contains("NOT EXISTS", sql.ToUpperInvariant());
    }

    [Fact]
    public void Trusted_reference_query_translates_with_current_embedding_contract()
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

        var sql = PersonPhotoDiscoveryQueryService.BuildValidTrustedReferenceFacesQuery(
                db,
                Guid.NewGuid(),
                "opencv-sface",
                "2021dec",
                128,
                visibleAssetIds)
            .ToQueryString();

        Assert.Contains("MediaPersonFaces", sql);
        Assert.Contains("MediaFaceEmbeddings", sql);
        Assert.Contains("TrustedReference", sql);
        Assert.Contains("MediaLibrarySources", sql);
    }

    private static MediaLibraryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseNpgsql("Host=localhost;Database=prism_person_profile_translation;Username=prism;Password=not-used")
            .EnableDetailedErrors()
            .Options;
        return new MediaLibraryDbContext(options);
    }
}
