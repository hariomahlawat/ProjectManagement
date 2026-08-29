import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');
const exists = (...parts) => fs.existsSync(path.join(root, ...parts));

const failures = [];
const expect = (condition, message) => { if (!condition) failures.push(message); };

expect(exists('Services', 'SearchV2', 'SearchV2ServiceCollectionExtensions.cs'), 'Search V2 DI extension is missing');
expect(exists('Services', 'SearchV2', 'Query', 'SearchEngine.cs'), 'Search V2 engine is missing');
expect(exists('Services', 'SearchV2', 'Indexing', 'SearchIndexWorker.cs'), 'Search V2 index worker is missing');
expect(exists('Migrations', '20261216200000_AddSearchV2Foundation.cs'), 'Search V2 migration is missing');
expect(exists('wwwroot', 'js', 'pages', 'search.js'), 'Search page JavaScript is missing');

const view = read('Areas', 'Common', 'Pages', 'Search', 'Index.cshtml');
expect(!view.includes('Html.Raw(hit.Snippet)'), 'Search view still renders raw snippets');
expect(!view.includes('Html.Raw(hit.Title)'), 'Search view still renders raw titles');
expect(view.includes('foreach (var segment in hit.TitleSegments)'), 'Search view must render structured title highlights');
expect(view.includes('Search PRISM'), 'Search page should use PRISM search identity');

const program = read('Program.cs');
expect(program.includes('AddSearchV2(builder.Configuration)'), 'Program.cs does not register Search V2');

const options = read('Services', 'SearchV2', 'SearchV2Options.cs');
const settings = JSON.parse(read('appsettings.json'));
const gateway = read('Services', 'SearchV2', 'Query', 'SearchGateway.cs');
const worker = read('Services', 'SearchV2', 'Indexing', 'SearchIndexWorker.cs');
expect(options.includes('public bool ServeV2 { get; set; } = false;'), 'Search V2 must default to safe shadow-only serving');
expect(settings?.Search?.V2?.Enabled === true, 'Search V2 must remain enabled in appsettings');
expect(typeof settings?.Search?.V2?.ServeV2 === 'boolean' && typeof settings?.Search?.V2?.ShadowMode === 'boolean', 'Search V2 rollout flags must be explicit booleans');
expect(gateway.includes('if (_options.ShadowMode && v2Response is { IsReady: true } && legacyResults is not null)'), 'Shadow comparison must run even while Legacy serves users');
expect(gateway.includes('var runV2 = _options.Enabled && (serveV2ToUser || _options.ShadowMode);'), 'Search V2 should run only for served users or shadow comparison');
expect(gateway.includes('var v2Task = runV2'), 'Search V2 and Legacy must be launched without serial shadow-mode latency');
expect(gateway.includes('ApplyLegacyFilters(legacyResults, request.Categories, request.Sources)'), 'Legacy fallback does not honour category and source filters');
expect(gateway.includes('!ShouldServeV2(user)'), 'V2 suggestions must remain hidden from users outside the V2 rollout');
expect(options.includes('WorkItemLeaseMinutes'), 'Search indexing work-item lease must be configurable');
expect(worker.includes('_options.WorkItemLeaseMinutes'), 'Search index worker does not use the configured work-item lease');
const healthService = read('Services', 'Dashboard', 'SearchHealthService.cs');
const healthWidget = read('Pages', 'Dashboard', 'Partials', '_SearchHealthWidget.cshtml');
expect(healthService.includes('ISearchIndexStore'), 'Dashboard search health does not consume Search V2 index health');
expect(healthWidget.includes('Search index'), 'Dashboard does not expose Search V2 index health separately from OCR');

const engine = read('Services', 'SearchV2', 'Query', 'SearchEngine.cs');
expect(engine.includes('exact_identifier'), 'Deterministic exact-identifier ranking tier is missing');
expect(engine.includes('exact_title'), 'Deterministic exact-title ranking tier is missing');
expect(engine.includes('SUM(weight / (@rrfK + channel_rank))'), 'Reciprocal Rank Fusion is missing');
expect(engine.includes('PARTITION BY c."CanonicalEntityType", c."CanonicalEntityKey"'), 'Canonical entity clustering is missing');
expect(engine.includes('request.ProjectIds') && engine.includes('request.Statuses') && engine.includes('request.DateFrom'), 'Search cursor is not bound to active search filters');
expect(!engine.includes('facet_clustered'), 'Legacy representative-only facet clustering is still present');
expect(engine.includes('filtered_candidates'), 'Search filters are not applied before canonical clustering');
expect(!engine.includes('FROM authorised e\n            FROM authorised e'), 'Search SQL contains a duplicated FROM clause');
expect(engine.includes('DateOnly.MaxValue'), 'Date facet upper bound does not guard DateOnly.MaxValue');

// Search results must navigate to pages authorised at the same visibility level as retrieval.
const urlBuilder = read('Services', 'Navigation', 'UrlBuilder.cs');
expect(urlBuilder.includes('FfcRecordDetails'), 'FFC search result read-only URL is missing');
expect(urlBuilder.includes('ProjectOfficeTrainingView'), 'Training search result read-only URL is missing');
expect(urlBuilder.includes('ProjectOfficeProliferationProject'), 'Proliferation search result read-only URL is missing');
expect(urlBuilder.includes('Proliferation/Project/{projectId.ToString(CultureInfo.InvariantCulture)}'), 'Proliferation read-only URL must follow the @page {id:int} route');
expect(!urlBuilder.includes('Proliferation/Project?id='), 'Proliferation read-only URL still uses the invalid query-string route');
expect(urlBuilder.includes('ProjectOfficeArppDetails'), 'ARPP search result read-only URL is missing');

// Incremental indexing must not lose an update that arrives while an item is being processed,
// and abandoned processing leases must recover after an IIS/app restart.
const indexStore = read('Services', 'SearchV2', 'Indexing', 'SearchIndexStore.cs');
expect(indexStore.includes('RecoverStaleWorkItemsAsync'), 'Search index stale work-item recovery is missing');
expect(indexStore.includes('"RequestedAtUtc" > "StartedAtUtc"'), 'Search index completion does not preserve newer requests');
expect(indexStore.includes("\"EntityType\" = 'ProjectDocument' AND \"ParentProjectId\" = @projectId"), 'Project refresh does not replace dependent Project Document projections');

const projectionBuilder = read('Services', 'SearchV2', 'Indexing', 'SearchProjectionBuilder.cs');
expect(projectionBuilder.includes('row.ProjectId.HasValue ? "Project" : "IprRecord"'), 'Linked IPR results are not canonically grouped with their Project');

const migration = read('Migrations', '20261216200000_AddSearchV2Foundation.cs');
expect(migration.includes("search_v2_install_trigger('DocRepoDocumentTexts'"), 'DocRepo OCR invalidation trigger targets the wrong physical table');
expect(!migration.includes("search_v2_install_trigger('DocumentTexts'"), 'Search V2 migration still references the non-existent DocumentTexts table');

const legacyProjectSearch = read('Services', 'Search', 'GlobalProjectSearchService.cs');
expect(legacyProjectSearch.includes('.RankCoverDensity(searchQuery)'), 'Legacy Project search still discards PostgreSQL FTS rank');
expect(legacyProjectSearch.includes('.OrderByDescending(x => x.Rank)'), 'Legacy Project result ordering is not relevance-first');

const docRepoSearch = read('Services', 'DocRepo', 'DocumentSearchService.cs');
expect(docRepoSearch.includes('SearchLikePattern.Contains(preparedQuery)'), 'Document Repository subject matching still treats LIKE wildcard characters as operators');
expect(docRepoSearch.includes('EF.Functions.ILike(d.Subject, literalPattern, SearchLikePattern.EscapeCharacter)'), 'Document Repository subject matching is not using the escaped literal pattern');
expect(docRepoSearch.includes('d.DocumentText.OcrText ?? string.Empty'), 'Document Repository body-match hint is not query-aware');

// Search V2.2 relevance, faceting and operational-assurance gates.
const normalizer = read('Services', 'SearchV2', 'Query', 'SearchQueryNormalizer.cs');
const aliasProvider = read('Services', 'SearchV2', 'Query', 'SearchAliasProvider.cs');
const projectionModel = read('Services', 'SearchV2', 'Models', 'SearchProjection.cs');
const searchJs = read('wwwroot', 'js', 'pages', 'search.js');
const searchCss = read('wwwroot', 'css', 'pages', 'search.css');
const pageModel = read('Areas', 'Common', 'Pages', 'Search', 'Index.cshtml.cs');
const adminSearchIndex = read('Areas', 'Admin', 'Pages', 'Diagnostics', 'SearchIndex.cshtml.cs');

expect(options.includes('ProjectionVersion') && options.includes('= 4;'), 'Projection semantic version is missing or was not bumped to 4');
expect(worker.includes('IsReadyAsync(_options.ProjectionVersion'), 'Index worker does not force projection-version compatibility');
expect(worker.includes('ReplaceFullGenerationAsync(projections, _options.ProjectionVersion'), 'Full rebuild does not activate the configured projection version');
expect(indexStore.includes('RequestFullRebuildAsync') && indexStore.includes("'__FullRebuild__'"), 'Administrative full rebuild queue support is missing');
expect(indexStore.includes('GetFailedItemsAsync') && indexStore.includes('RetryFailedAsync'), 'Failed indexing job inspection/retry support is missing');
expect(indexStore.includes('RecordIndexErrorAsync') && worker.includes('RecordIndexErrorAsync(ex.Message'), 'Full rebuild failures are not persisted into Search Health');
expect(indexStore.includes('CASE WHEN "SearchIndexWorkItems"."Status" = 1 THEN 1 ELSE 0 END'), 'Full-rebuild requests can overwrite an in-flight lease');
expect(exists('Areas', 'Admin', 'Pages', 'Diagnostics', 'SearchIndex.cshtml'), 'Search index administration page is missing');
expect(adminSearchIndex.includes('OnPostRebuildAsync') && adminSearchIndex.includes('OnPostRetryAllAsync'), 'Search index administration handlers are incomplete');

for (const kind of ['Name', 'Organisation', 'Location', 'Person', 'Context']) {
  expect(projectionModel.includes(`public const string ${kind} = "${kind}";`), `Typed search term ${kind} is missing`);
}
expect(exists('Services', 'SearchV2', 'Query', 'SearchAliasProvider.cs'), 'Database-backed search alias provider is missing');
expect(aliasProvider.includes('FROM "SearchAliases"') && aliasProvider.includes('SearchAliasQueryExpander'), 'SearchAliases is not the runtime terminology source');
expect(aliasProvider.includes('string.Join(" OR ", variants'), 'Compound alias expansion does not preserve mandatory non-alias terms through whole-query variants');
expect(normalizer.includes('Runtime terminology expansion is owned by SearchAliasProvider'), 'Query normalizer still owns a second terminology catalogue');
expect(!normalizer.includes('BuildExpansionVariants'), 'Hard-coded normalizer alias expansion is still present');
expect(engine.includes('HasStrongLexicalChannel'), 'Did-you-mean is not gated by strong lexical matches');
expect(engine.includes('rows.Count > 3'), 'Did-you-mean is not suppressed for healthy result sets');
expect(engine.includes('name_matches'), 'Name retrieval channel is missing');
expect(engine.includes(`t."TermType" = 'Alias' AND t."NormalizedTerm" = @exact`), 'Exact alias tier is missing');
expect(engine.includes('query.HighlightTerms.Any(term => row.Title.Contains'), 'Title-visible matched-field precedence is missing');
expect(engine.includes('MatchedFieldFromMetadata'), 'Precise metadata matched-field attribution is missing');
expect(engine.includes('CategoryFacetFilterClause') && engine.includes('SourceFacetFilterClause') && engine.includes('ProjectFacetFilterClause'), 'Disjunctive facet scopes are missing');
expect(engine.includes('COUNT(DISTINCT ("CanonicalEntityType", "CanonicalEntityKey"))'), 'Facet counts are not canonical-entity aware');
expect(!engine.includes('reader.NextResultAsync'), 'Committed Search V2 still depends on a second result set whose CTE scope cannot be shared');
expect(engine.includes('paged_results') && engine.includes('LEFT JOIN paged_results'), 'Committed Search V2 must return summary/facets and paged rows from one SQL statement');
expect(!/;\s*\n\s*SELECT r\."Id"[\s\S]{0,4000}FROM ranked r/.test(engine), 'Committed Search V2 still contains a second statement that references the ranked CTE');
expect(engine.includes('ProjectFacets') && engine.includes('StatusFacets') && engine.includes('FileTypeFacets') && engine.includes('StageFacets'), 'Advanced Search V2 facets are incomplete');
expect(engine.includes('ts_headline') && engine.includes('@maxSnippetSourceCharacters'), 'Search result query still transfers entire narrative/OCR bodies');
expect(engine.includes('indexHealth.ActiveGeneration') && read('Services', 'SearchV2', 'Query', 'SearchCursorCodec.cs').includes('ActiveGeneration'), 'Search cursor is not generation-aware');
expect(view.includes('CategoryOrder'), 'Category tabs are not rendered in stable product order');
expect(view.includes('pm-gs-active-filters'), 'Active filter chips are missing');
expect(view.includes('data-project-facet-search') && view.includes('data-project-facet-more'), 'Project facet search/show-more markup is missing');
expect(view.includes('Relevant date'), 'Date facet has not been clarified as Relevant date');
expect(view.includes('BuildRelatedUrl'), 'Related result chips are not navigable');
expect(searchCss.includes('position: sticky') && searchCss.includes('.pm-gs-active-filter'), 'Filter sticky actions/active-chip styling is incomplete');
expect(searchCss.includes('top: var(--pm-header-height, 52px);'), 'Search results sticky header does not clear the 52px PRISM application header');
expect(searchJs.includes('initProjectFacets'), 'Project facet search/show-more JavaScript is missing');
expect(searchJs.includes('data-project-facet-search') && searchJs.includes('data-project-facet-more'), 'Project facet JavaScript is not wired to the Razor data contract');
expect(pageModel.includes('if (DateFrom.HasValue && DateTo.HasValue && DateFrom.Value > DateTo.Value)'), 'Invalid date ranges are not normalized before search');
expect(/"Visits",\s*"Trackers"/.test(projectionBuilder), 'Visits are not classified under Trackers');
expect(projectionBuilder.includes('"Social media", "Trackers"'), 'Social Media is not classified under Trackers');
expect(projectionBuilder.includes('SearchTermKinds.Location') && projectionBuilder.includes('SearchTermKinds.Organisation'), 'Projection builder does not persist typed contextual terms');
expect(!/BuildTerms\([^)]*aliases:\s*Values\(row\.Location\)/s.test(projectionBuilder), 'Activity location is still promoted to an Alias');
expect(gateway.includes('gatewayLatencyMs') && gateway.includes('\"V2-Engine\"') && gateway.includes('\"V2-Suggest\"'), 'Engine/gateway/suggestion latency telemetry is incomplete');
expect(healthService.includes('\"V2-Engine\"') && healthService.includes('EngineP95LatencyMs') && healthService.includes('\"V2-Suggest\"') && healthService.includes('SuggestionP95LatencyMs'), 'Search Health engine/suggestion latency metrics are missing');
expect(exists('tools', 'search-v2-relevance-evaluator.mjs'), 'Search V2 relevance evaluator is missing');
expect(exists('tools', 'search-v2-relevance-dataset.schema.json'), 'Search V2 relevance dataset schema is missing');

// Search V2 relevance & result-quality hardening gates.
expect(normalizer.includes('UnicodeCategory.DashPunctuation'), 'Unicode dash punctuation is not normalized consistently');
expect(exists('Services', 'SearchV2', 'Query', 'SearchTextQuality.cs'), 'Search text-quality utility is missing');
expect(engine.includes('title_phrase'), 'Normalized title-phrase ranking channel is missing');
expect(engine.includes('identifier_prefix'), 'Committed search does not cover autocomplete identifier-prefix candidates');
expect(engine.includes('alias_prefix'), 'Committed search does not cover autocomplete alias-prefix candidates');
expect(engine.includes('title_token_prefix'), 'Committed search does not share title-prefix semantics with autocomplete');
expect(engine.includes('title_fuzzy'), 'Committed search does not cover autocomplete title-fuzzy candidates');
expect(engine.includes(`STRPOS(' ' || e."NormalizedTitle" || ' ', ' ' || @exact || ' ') > 0`), 'Title phrase detection is missing');
expect(engine.includes('searchTextQuality'), 'Narrative/OCR ranking is not quality-aware');
expect(projectionBuilder.includes('searchTextQuality'), 'Projection metadata does not persist searchTextQuality');
expect(options.includes('public int SuggestionLimit { get; set; } = 6;'), 'Autocomplete default is not capped at six results');
expect(pageModel.includes('SuggestAsync(q, User, 6'), 'Search page does not request six autocomplete results');
expect(view.includes('CategoryLabel') && view.includes('Records'), 'Trackers is not mapped to the user-facing Records label');
expect(view.includes('pm-gs-tab__count">@Model.Search.TotalHits'), 'All tab does not show the total result count');
const searchContracts = read('Services', 'SearchV2', 'Models', 'SearchContracts.cs');
const searchDiagnosticsView = read('Areas', 'Admin', 'Pages', 'Diagnostics', 'SearchIndex.cshtml');
expect(adminSearchIndex.includes('ISearchV2Engine') && adminSearchIndex.includes('InspectQuery') && adminSearchIndex.includes('InspectionResults'), 'Authorised ranking inspector backend is missing');
expect(searchDiagnosticsView.includes('Ranking inspector'), 'Search diagnostics ranking inspector UI is missing');
expect(searchContracts.includes('MatchTier') && searchContracts.includes('MatchChannels'), 'Search results do not preserve rank tier/channel diagnostics');

expect(searchContracts.includes('FilteredHits'), 'Search response does not distinguish All count from current filtered count');
expect(searchContracts.includes('SearchV2ExecutionStatus'), 'Typed Search V2 execution status is missing');
expect(searchContracts.includes('FellBackToLegacy'), 'Gateway response does not expose safe fallback state');
expect(gateway.includes('Search V2 escaped its engine boundary') && gateway.includes('Legacy-Fallback'), 'Unexpected V2 failures are not safely observable at the gateway boundary');
expect(pageModel.includes('_environment.IsDevelopment()') && view.includes('ShowEngineDiagnostics') && view.includes('V2DiagnosticId'), 'Engine/fallback diagnostics are not development-gated');
expect(view.includes('@Model.Search.FilteredHits'), 'Search result heading does not use the current filtered result count');
const siteCss = read('wwwroot', 'css', 'site.css');
expect(!siteCss.includes('/* ---------- Global Search (Google-style) ---------- */'), 'Legacy Global Search CSS block still contaminates Search V2');
expect(!/\.pm-gs-[a-zA-Z0-9_-]+\s*\{/.test(siteCss), 'site.css still owns Search V2 selectors');


if (failures.length) {
  console.error(`Search V2 contract failed (${failures.length}):`);
  failures.forEach((failure) => console.error(` - ${failure}`));
  process.exit(1);
}

console.log('Search V2 source contract passed.');
