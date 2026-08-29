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
        Assert.DoesNotContain("facet_clustered", engine, StringComparison.Ordinal);
        Assert.Contains("CategoryFacetFilterClause", engine, StringComparison.Ordinal);
        Assert.Contains("SourceFacetFilterClause", engine, StringComparison.Ordinal);
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

    [Fact]
    public void SearchV2_ProjectionVersionAndOperationalControlsPreventStaleIndexServing()
    {
        var options = ReadRepoFile("Services", "SearchV2", "SearchV2Options.cs");
        var worker = ReadRepoFile("Services", "SearchV2", "Indexing", "SearchIndexWorker.cs");
        var store = ReadRepoFile("Services", "SearchV2", "Indexing", "SearchIndexStore.cs");
        var adminPage = ReadRepoFile("Areas", "Admin", "Pages", "Diagnostics", "SearchIndex.cshtml.cs");

        Assert.Contains("ProjectionVersion", options, StringComparison.Ordinal);
        Assert.Contains("IsReadyAsync(_options.ProjectionVersion", worker, StringComparison.Ordinal);
        Assert.Contains("ReplaceFullGenerationAsync(projections, _options.ProjectionVersion", worker, StringComparison.Ordinal);
        Assert.Contains("RequestFullRebuildAsync", store, StringComparison.Ordinal);
        Assert.Contains("GetFailedItemsAsync", store, StringComparison.Ordinal);
        Assert.Contains("RetryFailedAsync", store, StringComparison.Ordinal);
        Assert.Contains("OnPostRebuildAsync", adminPage, StringComparison.Ordinal);
        Assert.Contains("OnPostRetryAllAsync", adminPage, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchV2_TypedTermsAndRuntimeAliasesDoNotPromoteLocationToAlias()
    {
        var projection = ReadRepoFile("Services", "SearchV2", "Models", "SearchProjection.cs");
        var builder = ReadRepoFile("Services", "SearchV2", "Indexing", "SearchProjectionBuilder.cs");
        var aliases = ReadRepoFile("Services", "SearchV2", "Query", "SearchAliasProvider.cs");
        var normalizer = ReadRepoFile("Services", "SearchV2", "Query", "SearchQueryNormalizer.cs");

        Assert.Contains("public const string Location = \"Location\";", projection, StringComparison.Ordinal);
        Assert.Contains("public const string Organisation = \"Organisation\";", projection, StringComparison.Ordinal);
        Assert.Contains("SearchTermKinds.Location", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("aliases: Values(row.Location)", builder, StringComparison.Ordinal);
        Assert.Contains("FROM \"SearchAliases\"", aliases, StringComparison.Ordinal);
        Assert.Contains("SearchAliasQueryExpander", aliases, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildExpansionVariants", normalizer, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchV2_FilterUxSupportsActiveChipsProjectSearchAndStickyActions()
    {
        var view = ReadRepoFile("Areas", "Common", "Pages", "Search", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "search.js");
        var css = ReadRepoFile("wwwroot", "css", "pages", "search.css");

        Assert.Contains("pm-gs-active-filters", view, StringComparison.Ordinal);
        Assert.Contains("data-project-facet-search", view, StringComparison.Ordinal);
        Assert.Contains("data-project-facet-more", view, StringComparison.Ordinal);
        Assert.Contains("Relevant date", view, StringComparison.Ordinal);
        Assert.Contains("initProjectFacets", script, StringComparison.Ordinal);
        Assert.Contains("data-project-facet-search", script, StringComparison.Ordinal);
        Assert.Contains("position: sticky", css, StringComparison.Ordinal);
        Assert.Contains("top: var(--pm-header-height, 52px);", css, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchV2_RelevanceHardeningIsTitleFirstAndQualityAware()
    {
        var engine = ReadRepoFile("Services", "SearchV2", "Query", "SearchEngine.cs");
        var projection = ReadRepoFile("Services", "SearchV2", "Indexing", "SearchProjectionBuilder.cs");
        var options = ReadRepoFile("Services", "SearchV2", "SearchV2Options.cs");

        Assert.Contains("title_phrase", engine, StringComparison.Ordinal);
        Assert.Contains("identifier_prefix", engine, StringComparison.Ordinal);
        Assert.Contains("alias_prefix", engine, StringComparison.Ordinal);
        Assert.Contains("title_token_prefix", engine, StringComparison.Ordinal);
        Assert.Contains("title_fuzzy", engine, StringComparison.Ordinal);
        Assert.Contains("@prefixTsQuery", engine, StringComparison.Ordinal);
        Assert.Contains("STRPOS(' ' || e.\"NormalizedTitle\" || ' ', ' ' || @exact || ' ') > 0", engine, StringComparison.Ordinal);
        Assert.Contains("'english_fts'::text AS channel", engine, StringComparison.Ordinal);
        Assert.Contains("5 AS tier", engine, StringComparison.Ordinal);
        Assert.Contains("searchTextQuality", engine, StringComparison.Ordinal);
        Assert.Contains("searchTextQuality", projection, StringComparison.Ordinal);
        Assert.Contains("public int ProjectionVersion { get; set; } = 4;", options, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchPage_UsesRecordsDisplayLabelAllCountAndSixSuggestions()
    {
        var view = ReadRepoFile("Areas", "Common", "Pages", "Search", "Index.cshtml");
        var model = ReadRepoFile("Areas", "Common", "Pages", "Search", "Index.cshtml.cs");
        var options = ReadRepoFile("Services", "SearchV2", "SearchV2Options.cs");

        Assert.Contains("CategoryLabel", view, StringComparison.Ordinal);
        Assert.Contains("Records", view, StringComparison.Ordinal);
        Assert.Contains("@Model.Search.TotalHits", view, StringComparison.Ordinal);
        Assert.Contains("SuggestAsync(q, User, 6", model, StringComparison.Ordinal);
        Assert.Contains("public int SuggestionLimit { get; set; } = 6;", options, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchDiagnostics_ExposeAuthorisedRankingInspectorWithTierAndChannels()
    {
        var model = ReadRepoFile("Areas", "Admin", "Pages", "Diagnostics", "SearchIndex.cshtml.cs");
        var view = ReadRepoFile("Areas", "Admin", "Pages", "Diagnostics", "SearchIndex.cshtml");
        var contracts = ReadRepoFile("Services", "SearchV2", "Models", "SearchContracts.cs");
        var engine = ReadRepoFile("Services", "SearchV2", "Query", "SearchEngine.cs");

        Assert.Contains("ISearchV2Engine", model, StringComparison.Ordinal);
        Assert.Contains("InspectQuery", model, StringComparison.Ordinal);
        Assert.Contains("InspectionResults", model, StringComparison.Ordinal);
        Assert.Contains("Ranking inspector", view, StringComparison.Ordinal);
        Assert.Contains("MatchTier", contracts, StringComparison.Ordinal);
        Assert.Contains("MatchChannels", contracts, StringComparison.Ordinal);
        Assert.Contains("row.Tier", engine, StringComparison.Ordinal);
        Assert.Contains("row.Channels", engine, StringComparison.Ordinal);
    }


    [Fact]
    public void SearchV2_CommittedQueryUsesSingleStatementAndExplicitExecutionState()
    {
        var engine = ReadRepoFile("Services", "SearchV2", "Query", "SearchEngine.cs");
        var contracts = ReadRepoFile("Services", "SearchV2", "Models", "SearchContracts.cs");

        Assert.DoesNotContain("reader.NextResultAsync", engine, StringComparison.Ordinal);
        Assert.Contains("paged_results", engine, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN paged_results", engine, StringComparison.Ordinal);
        Assert.Contains("FilteredHits", contracts, StringComparison.Ordinal);
        Assert.Contains("SearchV2ExecutionStatus", contracts, StringComparison.Ordinal);
        Assert.Contains("FellBackToLegacy", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchPage_UsesGlobalAllCountCurrentResultCountAndIsolatedSearchCss()
    {
        var view = ReadRepoFile("Areas", "Common", "Pages", "Search", "Index.cshtml");
        var siteCss = ReadRepoFile("wwwroot", "css", "site.css");

        Assert.Contains("@Model.Search.TotalHits", view, StringComparison.Ordinal);
        Assert.Contains("@Model.Search.FilteredHits", view, StringComparison.Ordinal);
        Assert.DoesNotContain("/* ---------- Global Search (Google-style) ---------- */", siteCss, StringComparison.Ordinal);
        Assert.DoesNotContain(".pm-gs-results", siteCss, StringComparison.Ordinal);
        Assert.DoesNotContain(".pm-gs-filter", siteCss, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchV2_FallbackIsObservableAndDevelopmentDiagnosticsDoNotLeakToProduction()
    {
        var gateway = ReadRepoFile("Services", "SearchV2", "Query", "SearchGateway.cs");
        var pageModel = ReadRepoFile("Areas", "Common", "Pages", "Search", "Index.cshtml.cs");
        var view = ReadRepoFile("Areas", "Common", "Pages", "Search", "Index.cshtml");

        Assert.Contains("Search V2 escaped its engine boundary", gateway, StringComparison.Ordinal);
        Assert.Contains("Legacy-Fallback", gateway, StringComparison.Ordinal);
        Assert.Contains("FellBackToLegacy", gateway, StringComparison.Ordinal);
        Assert.Contains("_environment.IsDevelopment()", pageModel, StringComparison.Ordinal);
        Assert.Contains("ShowEngineDiagnostics", view, StringComparison.Ordinal);
        Assert.Contains("V2DiagnosticId", view, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchV2_RankingEvidenceAndLazyFacetHardening_IsWiredEndToEnd()
    {
        var engine = ReadRepoFile("Services", "SearchV2", "Query", "SearchEngine.cs");
        var aliases = ReadRepoFile("Services", "SearchV2", "Query", "SearchAliasProvider.cs");
        var evidence = ReadRepoFile("Services", "SearchV2", "Query", "SearchMatchEvidence.cs");
        var contracts = ReadRepoFile("Services", "SearchV2", "Models", "SearchContracts.cs");
        var page = ReadRepoFile("Areas", "Common", "Pages", "Search", "Index.cshtml.cs");
        var view = ReadRepoFile("Areas", "Common", "Pages", "Search", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "search.js");

        Assert.Contains("title_tokens_exact", engine, StringComparison.Ordinal);
        Assert.Contains("alias_title_phrase", engine, StringComparison.Ordinal);
        Assert.Contains("strong_candidate_count", engine, StringComparison.Ordinal);
        Assert.Contains("@canonicalEntityBoost", engine, StringComparison.Ordinal);
        Assert.Contains("high tech", aliases, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hi tech", aliases, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SearchMatchEvidenceResolver", evidence, StringComparison.Ordinal);
        Assert.Contains("IncludeDetailedFacets", contracts, StringComparison.Ordinal);
        Assert.Contains("FacetsOnly", contracts, StringComparison.Ordinal);
        Assert.Contains("DetailedLoaded", contracts, StringComparison.Ordinal);
        Assert.Contains("OnGetFacetsAsync", page, StringComparison.Ordinal);
        Assert.Contains("data-search-dynamic-facets", view, StringComparison.Ordinal);
        Assert.Contains("pm-gs-filter__active-count", view, StringComparison.Ordinal);
        Assert.Contains("initLazyFacets", script, StringComparison.Ordinal);
        Assert.DoesNotContain("bi-check2-circle", view, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}
