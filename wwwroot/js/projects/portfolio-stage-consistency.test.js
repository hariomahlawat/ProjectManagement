const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = process.cwd();
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');

test('analytics uses the canonical present-stage resolver', () => {
  const analytics = read('Services/Analytics/PortfolioAnalyticsService.cs');

  assert.match(analytics, /PresentStageHelper\.ComputePresentStageAndAge\s*\(/);
  assert.doesNotMatch(analytics, /private\s+static\s+StageSnapshot\?\s+DetermineCurrentStage\s*\(/);
  assert.match(analytics, /p\.WorkflowVersion/);
  assert.match(analytics, /s\.ActualStart/);
});

test('analytics resolves project categories through the recursive hierarchy helper', () => {
  const analytics = read('Services/Analytics/PortfolioAnalyticsService.cs');
  const hierarchy = read('Services/Projects/ProjectCategoryHierarchyResolver.cs');

  assert.match(hierarchy, /ResolveRoot\s*\(/);
  assert.match(analytics, /ProjectCategoryHierarchyResolver\.ResolveRoot\s*\(/);
});

test('analytics phase cards and chart brackets take the phases from the one server-side mapping', () => {
  const phases = read('Models/Stages/StagePhases.cs');
  const service = read('Services/Analytics/PortfolioAnalyticsService.cs');
  const partial = read('Pages/Analytics/Partials/_OngoingAnalytics.cshtml');
  const analyticsJs = read('wwwroot/js/analytics-projects.js');

  assert.match(phases, /StagePhase\.PreAon[\s\S]*StageCodes\.AON/);
  assert.match(service, /StagePhases\.Of\s*\(/);
  assert.match(partial, /data-phases=/);
  assert.match(analyticsJs, /readJson\(canvas, 'phases'/);
  // No stage-to-phase table in the script: it draws what the server sends.
  assert.doesNotMatch(analyticsJs, /['"]AON['"]\s*[,\]]\s*[,\]]?[\s\S]{0,40}phase/i);
});

test('the stage chart lives only in Analytics: the Command workspace links to it instead', () => {
  // Project portfolio drew a second copy of this chart; it was replaced by Project analytics.
  const workspaceJs = read('wwwroot/js/pages/command-workspace.js');
  const workspaceView = read('Pages/Workspace/_CommandWorkspace.cshtml');
  const workspaceService = read('Services/Workspace/CommandWorkspaceService.cs');
  const rail = read('Pages/Workspace/_CommandWorkspaceRail.cshtml');

  assert.doesNotMatch(workspaceJs, /command-stage-chart|initialiseStageChart/);
  assert.doesNotMatch(workspaceView, /command-stage-chart|cw-stage-board/);
  assert.doesNotMatch(workspaceService, /PresentStageHelper|BuildPortfolioAsync/);
  assert.match(rail, /asp-page="\/Analytics\/Index"[\s\S]{0,120}Project analytics/);
});

test('analytics assigns stable semantic colours to the three organisational project roots', () => {
  // Category colours are site tokens (one vocabulary for Light and Dark), resolved by meaning
  // in the shared chart defaults; the analytics script holds no colour literals.
  const defaultsJs = read('wwwroot/js/charts/pm-chart-defaults.js');
  const analyticsJs = read('wwwroot/js/analytics-projects.js');
  const siteCss = read('wwwroot/css/site.css');

  assert.match(defaultsJs, /dcd:\s*cssVar\('--pm-chart-cat-dcd'\)/);
  assert.match(defaultsJs, /coe:\s*cssVar\('--pm-chart-cat-coe'\)/);
  assert.match(defaultsJs, /other:\s*cssVar\('--pm-chart-cat-other'\)/);
  assert.match(defaultsJs, /export function categoryColor\(/);
  assert.match(analyticsJs, /categoryOrder\s*=\s*\['DCD Projects',\s*'CoE',\s*'Other R&D Projects'\]/);
  assert.doesNotMatch(analyticsJs, /#[0-9a-fA-F]{3,6}/);
  assert.equal((siteCss.match(/--pm-chart-cat-dcd:/g) || []).length, 2, 'light and dark values');
});

test('analytics keeps adaptive y-axis headroom', () => {
  const analyticsJs = read('wwwroot/js/analytics-projects.js');

  // Analytics takes its axis headroom from the shared chart defaults.
  assert.match(read('wwwroot/js/charts/pm-chart-defaults.js'), /grace:\s*'8%'/);
  assert.match(analyticsJs, /barScales\(/);
});

