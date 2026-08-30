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
  const analyticsJs = read('wwwroot/js/analytics-projects.js');

  assert.match(analyticsJs, /'DCD Projects':\s*'#3c68e8'/);
  assert.match(analyticsJs, /'CoE':\s*'#52c653'/);
  assert.match(analyticsJs, /'Other R&D Projects':\s*'#ef7a00'/);
  assert.match(analyticsJs, /semanticCategoryOrder/);
});
