const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..', '..');
const exists = rel => fs.existsSync(path.join(root, rel));
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');

test('ToT has one canonical applicability policy that excludes repeat builds', () => {
  const rel = 'Services/Projects/ProjectTotApplicabilityPolicy.cs';
  assert.equal(exists(rel), true, `${rel} must exist`);
  const policy = read(rel);
  assert.match(policy, /!project\.IsBuild/);
  assert.match(policy, /!project\.IsDeleted/);
  assert.match(policy, /!project\.IsArchived/);
  assert.match(policy, /ProjectLifecycleStatus\.Completed/);
  assert.match(policy, /not applicable to Repeat Build projects/i);
});

test('tracker and new project creation honor ToT applicability', () => {
  const tracker = read('Areas/ProjectOfficeReports/Application/ProjectTotTrackerReadService.cs');
  const create = read('Pages/Projects/Create.cshtml.cs');
  assert.match(tracker, /ProjectTotApplicabilityPolicy\.EligibleProjectPredicate/);
  assert.match(create, /if \(!Input\.IsBuild\)/);
  assert.match(create, /project\.Tot = new ProjectTot/);
});

test('ToT writes, overview and standalone editor reject repeat builds', () => {
  const service = read('Services/Projects/ProjectTotService.cs');
  const overview = read('Pages/Projects/Overview.Tot.cs');
  const edit = read('Pages/Projects/Tot/Edit.cshtml.cs');
  assert.match(service, /ProjectTotApplicabilityPolicy\.GetIneligibilityReason/);
  assert.match(overview, /ProjectTotApplicabilityPolicy\.GetIneligibilityReason/);
  assert.match(edit, /ProjectTotApplicabilityPolicy\.GetIneligibilityReason/);
});

test('ToT summary and tracker contain no Repeat Build legacy-review subsystem', () => {
  const tracker = read('Areas/ProjectOfficeReports/Application/ProjectTotTrackerReadService.cs');
  const summaryModel = read('Areas/ProjectOfficeReports/Pages/Tot/Summary.cshtml.cs');
  const summaryView = read('Areas/ProjectOfficeReports/Pages/Tot/Summary.cshtml');
  const indexModel = read('Areas/ProjectOfficeReports/Pages/Tot/Index.cshtml.cs');
  const indexView = read('Areas/ProjectOfficeReports/Pages/Tot/Index.cshtml');
  for (const source of [tracker, summaryModel, summaryView, indexModel, indexView]) {
    assert.doesNotMatch(source, /LegacyRepeatBuild|Repeat Build review|historical ToT records require review/i);
  }
});

test('downstream ToT consumers exclude repeat builds', () => {
  const completedPolicy = read('Services/Projects/CompletedProjectPortfolioPolicy.cs');
  const dashboard = read('Services/Dashboard/OpsSignalsService.cs');
  const compendium = read('Services/Compendiums/CompendiumReadService.cs');
  const searchV1 = read('Services/Search/GlobalProjectReportsSearchService.cs');
  const searchV2 = read('Services/SearchV2/Indexing/SearchProjectionBuilder.cs');
  assert.match(completedPolicy, /item\.IsBuild/);
  assert.match(dashboard, /ProjectTotApplicabilityPolicy\.EligibleProjectPredicate/);
  assert.match(compendium, /ProjectTotApplicabilityPolicy\.EligibleTotPredicate/);
  assert.match(searchV1, /ProjectTotApplicabilityPolicy\.EligibleTotPredicate/);
  assert.match(searchV2, /ProjectTotApplicabilityPolicy\.EligibleTotPredicate/);
});


test('completed-project ToT metrics and filters treat Repeat Build as not applicable', () => {
  const summaryService = read('Services/Projects/CompletedProjectsSummaryService.cs');
  const overview = read('Services/Projects/CompletedProjectsPortfolioOverview.cs');
  assert.match(summaryService, /!r\.IsBuild\s*&&\s*r\.TotStatus == ProjectTotStatus\.Completed/);
  assert.match(overview, /TotCompletedCount = items\.Count\(x => !x\.IsBuild && x\.TotStatus == ProjectTotStatus\.Completed\)/);
});

test('new ToT-scoped documents cannot be attached to Repeat Build projects', () => {
  const requestService = read('Services/Documents/DocumentRequestService.cs');
  const documentService = read('Services/Documents/DocumentService.cs');
  assert.match(requestService, /ProjectTotApplicabilityPolicy\.EligibleTotPredicate/);
  assert.match(documentService, /ProjectTotApplicabilityPolicy\.GetIneligibilityReason/);
});


test('repository ToT filters and document upload UI treat Repeat Build as outside ToT', () => {
  const searchFilters = read('Services/Projects/ProjectSearchFilters.cs');
  const uploadPage = read('Pages/Projects/Documents/UploadRequest.cshtml.cs');
  const remarksPanel = read('Services/Projects/ProjectRemarksPanelService.cs');
  assert.match(searchFilters, /filters\.TotStatus\.HasValue[\s\S]*ProjectTotApplicabilityPolicy\.EligibleProjectPredicate/);
  assert.match(uploadPage, /AllowTotLinking =>[\s\S]*Project is \{ IsBuild: false \}/);
  assert.match(uploadPage, /canLinkTot = Project is \{ IsBuild: false \}/);
  assert.match(remarksPanel, /!project\.IsBuild\s*&&\s*project\.Tot is/);
});

test('project overview manage permission is gated by the canonical ToT applicability policy', () => {
  const overviewModel = read('Pages/Projects/Overview.cshtml.cs');
  assert.match(
    overviewModel,
    /CanManageTot\s*=\s*ProjectTotApplicabilityPolicy\.IsApplicable\(project\)\s*&&\s*\(isAdmin \|\| isHoD \|\| isThisProjectsPo\)/
  );
});

test('ToT summary labels the denominator as ToT-applicable projects', () => {
  const summaryView = read('Areas/ProjectOfficeReports/Pages/Tot/Summary.cshtml');
  assert.match(summaryView, /tot-summary__metric-label">ToT-applicable projects</);
});



test('Repeat Build project surfaces never claim historical ToT retention or offer tracker management', () => {
  const commandCard = read('ViewComponents/ProjectTotCommandCardViewComponent.cs');
  const summaryVm = read('ViewModels/ProjectTotSummaryViewModel.cs');
  const drawer = read('Pages/Projects/_ProjectTotDrawer.cshtml');
  const overviewTot = read('Pages/Projects/Overview.Tot.cs');
  for (const source of [commandCard, summaryVm, drawer, overviewTot]) {
    assert.doesNotMatch(source, /legacy ToT|historical ToT|legacy data retained|BuildLegacyRepeatBuildSummaryPayload/i);
  }
  assert.match(drawer, /@if \(tot\.IsApplicable\)[\s\S]{0,500}Open tracker/);
});

test('approval queue does not surface ToT requests belonging to Repeat Build projects', () => {
  const queue = read('Services/Approvals/ApprovalQueueService.cs');
  const details = read('Pages/Approvals/Pending/Details.cshtml');
  assert.match(queue, /ProjectTotRequests[\s\S]{0,1200}!project\.IsBuild/);
  assert.doesNotMatch(queue, /Reject this legacy request to close it/i);
  assert.doesNotMatch(details, /legacy ToT request/i);
});

test('Repeat Build ToT cleanup migration is registered in the immutable migration manifest', () => {
  const manifest = read('Migrations/immutable-migration-ids.txt');
  assert.match(manifest, /^20261216210000_RemoveTotDataFromRepeatBuildProjects$/m);
});

test('one-time migration removes all ToT data from Repeat Build projects while preserving project assets', () => {
  const rel = 'Migrations/20261216210000_RemoveTotDataFromRepeatBuildProjects.cs';
  assert.equal(exists(rel), true, `${rel} must exist`);
  const migration = read(rel);
  assert.match(migration, /UPDATE "ProjectDocuments"[\s\S]*SET "TotId" = NULL/);
  assert.match(migration, /DELETE FROM "ProjectDocumentRequests"[\s\S]*"TotId"/);
  assert.match(migration, /UPDATE "ProjectPhotos"[\s\S]*SET "TotId" = NULL/);
  assert.match(migration, /DELETE FROM "Remarks"[\s\S]*"Scope" = 'TransferOfTechnology'/);
  assert.match(migration, /DELETE FROM "ProjectTotRequests"/);
  assert.match(migration, /DELETE FROM "ProjectTots"/);
  assert.doesNotMatch(migration, /DELETE FROM "ProjectDocuments"/);
  assert.doesNotMatch(migration, /DELETE FROM "ProjectPhotos"/);
});
