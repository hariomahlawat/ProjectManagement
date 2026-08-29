using Xunit;

namespace ProjectManagement.Tests.Search;

public sealed class SearchV2SourceContractTests
{
    [Fact]
    public void SearchPage_UsesStructuredHighlightingAndNeverRawResultHtml()
    {
        var view = ReadRepoFile("Areas", "Common", "Pages", "Search", "Index.cshtml");

        Assert.DoesNotContain("Html.Raw(hit.Snippet)", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Html.Raw(hit.Title)", view, StringComparison.Ordinal);
        Assert.Contains("foreach (var segment in hit.TitleSegments)", view, StringComparison.Ordinal);
        Assert.Contains("<mark>@segment.Text</mark>", view, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchV2_UsesDeterministicExactTiersAndRankFusion()
    {
        var engine = ReadRepoFile("Services", "SearchV2", "Query", "SearchEngine.cs");

        Assert.Contains("exact_identifier", engine, StringComparison.Ordinal);
        Assert.Contains("exact_title", engine, StringComparison.Ordinal);
        Assert.Contains("SUM(weight / (@rrfK + channel_rank))", engine, StringComparison.Ordinal);
        Assert.Contains("PARTITION BY c.\"CanonicalEntityType\", c.\"CanonicalEntityKey\"", engine, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchV2_MigrationCreatesFtsTrigramAclAndTelemetryInfrastructure()
    {
        var migration = ReadRepoFile("Migrations", "20261216200000_AddSearchV2Foundation.cs");

        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pg_trgm", migration, StringComparison.Ordinal);
        Assert.Contains("SearchVectorSimple", migration, StringComparison.Ordinal);
        Assert.Contains("SearchVectorEnglish", migration, StringComparison.Ordinal);
        Assert.Contains("SearchEntryPrincipals", migration, StringComparison.Ordinal);
        Assert.Contains("SearchIndexWorkItems", migration, StringComparison.Ordinal);
        Assert.Contains("SearchQueryLogs", migration, StringComparison.Ordinal);
        Assert.Contains("SearchShadowComparisons", migration, StringComparison.Ordinal);
        Assert.Contains("TR_SearchV2_ArppEntries", migration, StringComparison.Ordinal);
        Assert.Contains("search_v2_install_trigger('DocRepoDocumentTexts'", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("search_v2_install_trigger('DocumentTexts'", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchV2_SupportsShadowAndSelectedUserRolloutWithoutForcingAppSettingsState()
    {
        var options = ReadRepoFile("Services", "SearchV2", "SearchV2Options.cs");
        var gateway = ReadRepoFile("Services", "SearchV2", "Query", "SearchGateway.cs");
        var worker = ReadRepoFile("Services", "SearchV2", "Indexing", "SearchIndexWorker.cs");

        Assert.Contains("public bool ServeV2 { get; set; } = false;", options, StringComparison.Ordinal);
        Assert.Contains("ServeV2Users", options, StringComparison.Ordinal);
        Assert.Contains("ServeV2Roles", options, StringComparison.Ordinal);
        Assert.Contains("if (_options.ShadowMode && v2Response is { IsReady: true } && legacyResults is not null)", gateway, StringComparison.Ordinal);
        Assert.Contains("var runV2 = _options.Enabled && (serveV2ToUser || _options.ShadowMode);", gateway, StringComparison.Ordinal);
        Assert.Contains("!ShouldServeV2(user)", gateway, StringComparison.Ordinal);
        Assert.Contains("WorkItemLeaseMinutes", options, StringComparison.Ordinal);
        Assert.Contains("_options.WorkItemLeaseMinutes", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectReindex_ReplacesDependentProjectDocumentProjections()
    {
        var store = ReadRepoFile("Services", "SearchV2", "Indexing", "SearchIndexStore.cs");
        var builder = ReadRepoFile("Services", "SearchV2", "Indexing", "SearchProjectionBuilder.cs");

        Assert.Contains("includeProjectDocuments: true", builder, StringComparison.Ordinal);
        Assert.Contains("\"EntityType\" = 'ProjectDocument' AND \"ParentProjectId\" = @projectId", store, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyFallback_IsFailureIsolatedAndLiteralLikeSafe()
    {
        var orchestrator = ReadRepoFile("Services", "Search", "GlobalSearchService.cs");
        var pattern = ReadRepoFile("Services", "Search", "SearchLikePattern.cs");

        Assert.Contains("SafeSearchAsync", orchestrator, StringComparison.Ordinal);
        Assert.Contains("Other providers will continue", orchestrator, StringComparison.Ordinal);
        Assert.Contains("Replace(\"%\", \"\\\\%\"", pattern, StringComparison.Ordinal);
        Assert.Contains("Replace(\"_\", \"\\\\_\"", pattern, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyProjectSearch_PreservesPostgreSqlRelevanceOrdering()
    {
        var projectSearch = ReadRepoFile("Services", "Search", "GlobalProjectSearchService.cs");

        Assert.Contains(".RankCoverDensity(searchQuery)", projectSearch, StringComparison.Ordinal);
        Assert.Contains(".OrderByDescending(x => x.Rank)", projectSearch, StringComparison.Ordinal);
    }


    [Fact]
    public void SearchV2_GatewayAndCursorRespectShadowAndFilterSemantics()
    {
        var gateway = ReadRepoFile("Services", "SearchV2", "Query", "SearchGateway.cs");
        var engine = ReadRepoFile("Services", "SearchV2", "Query", "SearchEngine.cs");

        Assert.Contains("var v2Task = runV2", gateway, StringComparison.Ordinal);
        Assert.Contains("ApplyLegacyFilters(legacyResults, request.Categories, request.Sources)", gateway, StringComparison.Ordinal);
        Assert.Contains("!ShouldServeV2(user)", gateway, StringComparison.Ordinal);
        Assert.Contains("request.ProjectIds", engine, StringComparison.Ordinal);
        Assert.Contains("request.Statuses", engine, StringComparison.Ordinal);
        Assert.Contains("request.DateFrom", engine, StringComparison.Ordinal);
        Assert.Contains("indexHealth.ActiveGeneration", engine, StringComparison.Ordinal);
        Assert.Contains("facet_clustered", engine, StringComparison.Ordinal);
        Assert.Contains("filtered_candidates", engine, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchResultRoutesAndDocRepoHintsAreLiteralAndNavigable()
    {
        var urls = ReadRepoFile("Services", "Navigation", "UrlBuilder.cs");
        var documents = ReadRepoFile("Services", "DocRepo", "DocumentSearchService.cs");

        Assert.Contains("Proliferation/Project/{projectId.ToString(CultureInfo.InvariantCulture)}", urls, StringComparison.Ordinal);
        Assert.DoesNotContain("Proliferation/Project?id=", urls, StringComparison.Ordinal);
        Assert.Contains("SearchLikePattern.Contains(preparedQuery)", documents, StringComparison.Ordinal);
        Assert.Contains("EF.Functions.ILike(d.Subject, literalPattern, SearchLikePattern.EscapeCharacter)", documents, StringComparison.Ordinal);
        Assert.Contains("d.DocumentText.OcrText ?? string.Empty", documents, StringComparison.Ordinal);
    }
    private static string ReadRepoFile(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}
