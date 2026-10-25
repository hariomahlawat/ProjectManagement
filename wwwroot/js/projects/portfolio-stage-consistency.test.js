const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = process.cwd();
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');

test('workspace and analytics use the canonical present-stage resolver', () => {
  const analytics = read('Pages/Analytics/Index.cshtml.cs');
  const workspace = read('Services/Workspace/CommandWorkspaceService.cs');

  assert.match(workspace, /PresentStageHelper\.ComputePresentStageAndAge\s*\(/);
  assert.match(analytics, /PresentStageHelper\.ComputePresentStageAndAge\s*\(/);
  assert.doesNotMatch(analytics, /private\s+static\s+StageSnapshot\?\s+DetermineCurrentStage\s*\(/);
  assert.match(analytics, /p\.WorkflowVersion/);
  assert.match(analytics, /s\.ActualStart/);
});

test('workspace and analytics resolve project categories through the same recursive hierarchy helper', () => {
  const analytics = read('Pages/Analytics/Index.cshtml.cs');
  const workspace = read('Services/Workspace/CommandWorkspaceService.cs');
  const hierarchy = read('Services/Projects/ProjectCategoryHierarchyResolver.cs');

  assert.match(hierarchy, /ResolveRoot\s*\(/);
  assert.match(workspace, /ProjectCategoryHierarchyResolver\.ResolveRoot\s*\(/);
  assert.match(analytics, /ProjectCategoryHierarchyResolver\.ResolveRoot\s*\(/);
});

test('workspace stage chart uses balanced dashboard geometry rather than the old thin-bar cap', () => {
  const workspaceJs = read('wwwroot/js/pages/command-workspace.js');

  assert.doesNotMatch(workspaceJs, /maxBarThickness:\s*44\b/);
  assert.match(workspaceJs, /categoryPercentage:\s*0\.72\b/);
  assert.match(workspaceJs, /barPercentage:\s*0\.88\b/);
  assert.match(workspaceJs, /maxBarThickness:\s*80\b/);
  assert.match(workspaceJs, /autoSkip:\s*false/);
  assert.match(workspaceJs, /maxRotation:\s*0/);
  assert.match(workspaceJs, /getWorkspaceStageAxisLabel/);
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

test('workspace and analytics keep compact EAS labeling and adaptive y-axis headroom', () => {
  const workspaceJs = read('wwwroot/js/pages/command-workspace.js');
  const analyticsJs = read('wwwroot/js/analytics-projects.js');

  assert.match(workspaceJs, /workspaceStageAxisCodeOverrides/);
  assert.match(workspaceJs, /EAS:\s*'EAS'/);
  assert.match(workspaceJs, /grace:\s*'5%'/);
  // Analytics takes its axis headroom from the shared chart defaults.
  assert.match(read('wwwroot/js/charts/pm-chart-defaults.js'), /grace:\s*'8%'/);
  assert.match(analyticsJs, /barScales\(/);
});

